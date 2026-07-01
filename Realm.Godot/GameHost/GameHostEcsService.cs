using Arch.Core;
using Arch.Core.Extensions;
using DotRecast.Core.Numerics;
using Godot;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Services;
using System;
using System.Collections.Generic;
using static Realm.Ecs.Common.WorldExtensions;

internal class GameHostEcsService
{
	private readonly World _ecsWorld;
	private readonly Entity _worldEntity;
	private readonly NavMeshPathfinder _pathfinder;

	private readonly MovementAndPathfindingService _movementService;
	private readonly CombatAndDamageService _combatService;
	private readonly ResourceEconomyService _economyService;

	private float _fDelta;

	private readonly List<string> _tickExpiredBuffs = new();
	private readonly List<string> _tickBuffKeys = new();
	private readonly List<(Entity Entity, Patrol Patrol)> _tickPatrolToFlip = new();
	private readonly List<Entity> _tickFollowToStop = new();
	private readonly List<(Entity Follower, Vector3 TargetPos)> _tickFollowToMove = new();
	private readonly List<Entity> _tickArrivedUnits = new();
	private readonly List<(Entity Entity, PathFollow PathFollow)> _tickAddPathFollow = new();
	private readonly List<Entity> _tickEntitiesToClearOrders = new();
	private readonly List<Entity> _tickEntitiesToStopGathering = new();
	private readonly List<SpawningRequest> _tickSpawningRequests = new();
	private bool _tickNeedsUiRefresh = false;

	private readonly QueryDescription _buffQuery = new QueryDescription().WithAll<Realm.Ecs.Components.Core.Buffs>().WithNone<Dead>();
	private readonly QueryDescription _patrolArrivalQuery = new QueryDescription().WithAll<Patrol, Position>().WithNone<Dead, AttackTarget>();
	private readonly QueryDescription _followQuery = new QueryDescription().WithAll<Follow, Position>().WithNone<Dead>();
	private readonly QueryDescription _attackCooldownQuery = new QueryDescription().WithAll<Attack>();
	private readonly QueryDescription _prodQuery = new QueryDescription().WithAll<Realm.Ecs.Components.Core.ProductionQueue>();
	private readonly QueryDescription _spellCooldownQuery = new QueryDescription().WithAll<SpellCooldowns>();

	private ForEachWithEntity<Realm.Ecs.Components.Core.Buffs> _buffsQueryDelegate = null!;
	private ForEachWithEntity<Patrol, Position> _patrolArrivalQueryDelegate = null!;
	private ForEachWithEntity<Follow, Position> _followQueryDelegate = null!;
	private ForEachWithEntity<Attack> _attackCooldownQueryDelegate = null!;
	private ForEachWithEntity<Realm.Ecs.Components.Core.ProductionQueue> _prodQueryDelegate = null!;
	private ForEachWithEntity<InterpolationTarget, Unit3D> _interpolationQueryDelegate = null!;
	private ForEachWithEntity<SpellCooldowns> _spellCooldownQueryDelegate = null!;

	public Action<Vector3, Vector3> OnArrowProjectileRequested;
	public Action<Unit3D> OnDamageFlashRequested;
	public Action<Vector3, Vector3> OnHealEffectRequested;
	public Action<Unit3D> OnHealFlashRequested;
	public Action<Unit3D, Unit3D, float> OnUnitDamagedCallback;
	public Action<string> OnUnderAttackAlertRequested;
	public Action<Entity> OnKillUnitRequested;
	public Action<string, Vector3, bool, Vector3?, bool> OnSpawnUnitFromProductionRequested;
	public Action<Entity> OnClearUnitOrdersRequested;
	public Action<Entity> OnStopGatheringMovementRequested;
	public Action OnUiRefreshRequested;
	public Action<string, float> OnResourceDepositedForPlayer;
	public Action<string> OnProductionCompleted;

	public struct SpawningRequest
	{
		public string UnitId;
		public Vector3 Position;
		public bool IsEnemy;
		public Vector3? RallyPoint;
		public bool IsFromQueue;
	}

	public GameHostEcsService(World ecsWorld, Entity worldEntity, NavMeshPathfinder pathfinder)
	{
		_ecsWorld = ecsWorld;
		_worldEntity = worldEntity;
		_pathfinder = pathfinder;

		_movementService = new MovementAndPathfindingService(ecsWorld, pathfinder);
		_combatService = new CombatAndDamageService(ecsWorld);
		_economyService = new ResourceEconomyService(ecsWorld);

		_combatService.OnArrowProjectileRequested = (p1, p2) => OnArrowProjectileRequested?.Invoke(p1, p2);
		_combatService.OnDamageFlashRequested = u => OnDamageFlashRequested?.Invoke(u);
		_combatService.OnHealEffectRequested = (p1, p2) => OnHealEffectRequested?.Invoke(p1, p2);
		_combatService.OnHealFlashRequested = u => OnHealFlashRequested?.Invoke(u);
		_combatService.OnUnitDamagedCallback = (u1, u2, d) => OnUnitDamagedCallback?.Invoke(u1, u2, d);
		_combatService.OnUnderAttackAlertRequested = id => OnUnderAttackAlertRequested?.Invoke(id);
		_combatService.OnKillUnitRequested = (ent, u) => OnKillUnitRequested?.Invoke(ent);

		_economyService.OnResourceDepositedForPlayer = (res, amount) => OnResourceDepositedForPlayer?.Invoke(res, amount);
		_economyService.OnClearUnitOrdersRequested = ent => OnClearUnitOrdersRequested?.Invoke(ent);
		_economyService.OnStopGatheringMovementRequested = ent => OnStopGatheringMovementRequested?.Invoke(ent);
	}

	public void Initialize()
	{
		_buffsQueryDelegate = UpdateBuffsQueryAction;
		_patrolArrivalQueryDelegate = PatrolArrivalQueryAction;
		_followQueryDelegate = FollowQueryAction;
		_attackCooldownQueryDelegate = AttackCooldownQueryAction;
		_prodQueryDelegate = ProdQueryAction;
		_interpolationQueryDelegate = InterpolationQueryAction;
		_spellCooldownQueryDelegate = SpellCooldownQueryAction;
	}

	public void TickEcs(float fDelta)
	{
		_fDelta = fDelta;
		_tickEntitiesToClearOrders.Clear();
		_tickEntitiesToStopGathering.Clear();
		_tickSpawningRequests.Clear();
		_tickAddPathFollow.Clear();
		_tickNeedsUiRefresh = false;

		_ecsWorld.Query(in _spellCooldownQuery, _spellCooldownQueryDelegate);

		_movementService.StepMovement(fDelta);
		_combatService.StepCombat(fDelta);
		_economyService.StepEconomy(fDelta);

		_ecsWorld.Query(in _buffQuery, _buffsQueryDelegate);

		ProcessPatrolArrivals();
		ProcessFollowMovements();

		_ecsWorld.Query(in _attackCooldownQuery, _attackCooldownQueryDelegate);
		_ecsWorld.Query(in _prodQuery, _prodQueryDelegate);

		foreach (var (entity, pf) in _tickAddPathFollow)
		{
			if (_ecsWorld.IsAlive(entity))
			{
				_ecsWorld.Add(entity, pf);
			}
		}

		ApplyDeferredTickCommands();
	}

	public void TickEditorPhysics(float fDelta)
	{
		_fDelta = fDelta;
		_tickArrivedUnits.Clear();
		_tickAddPathFollow.Clear();
	}

	public ForEachWithEntity<Position, MoveTo, MovementStats> EditorMovementQueryDelegate => _movementService.EditorMovementQueryDelegate;
	public ForEachWithEntity<InterpolationTarget, Unit3D> InterpolationQueryDelegate => _interpolationQueryDelegate;

	public void SetRuntimeReferences(
		List<Unit3D> allUnits,
		List<Prop3D> allProps,
		List<Unit3D> castlesList,
		DefinitionManager definitionManager,
		ResourceId goldResourceId,
		ResourceId woodResourceId,
		ResourceId stoneResourceId,
		EditableTerrain groundTerrain)
	{
		_movementService.SetRuntimeReferences(allUnits, allProps, groundTerrain);
		_economyService.SetRuntimeReferences(allProps, castlesList, definitionManager, goldResourceId, woodResourceId, stoneResourceId);
	}

	private void ProcessPatrolArrivals()
	{
		_tickPatrolToFlip.Clear();
		_ecsWorld.Query(in _patrolArrivalQuery, _patrolArrivalQueryDelegate);
		foreach (var (entity, patrol) in _tickPatrolToFlip)
		{
			if (_ecsWorld.IsAlive(entity))
			{
				var newPatrol = patrol;
				newPatrol.GoingToB = !patrol.GoingToB;
				_ecsWorld.Set(entity, newPatrol);

				var dest = newPatrol.GoingToB ? newPatrol.PointB : newPatrol.PointA;
				var moveTo = new MoveTo(dest);
				if (_ecsWorld.Has<MoveTo>(entity))
					_ecsWorld.Set(entity, moveTo);
				else
					_ecsWorld.Add(entity, moveTo);
			}
		}
	}

	private void ProcessFollowMovements()
	{
		_tickFollowToStop.Clear();
		_tickFollowToMove.Clear();

		_ecsWorld.Query(in _followQuery, _followQueryDelegate);

		foreach (var entity in _tickFollowToStop)
		{
			if (_ecsWorld.IsAlive(entity))
			{
				if (_ecsWorld.Has<MoveTo>(entity))
				{
					_ecsWorld.Remove<MoveTo>(entity);
				}
				if (_ecsWorld.Has<Follow>(entity))
				{
					_ecsWorld.Remove<Follow>(entity);
				}
			}
		}

		foreach (var (follower, targetPos) in _tickFollowToMove)
		{
			if (_ecsWorld.IsAlive(follower))
			{
				var moveTo = new MoveTo(new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z));
				if (_ecsWorld.Has<MoveTo>(follower))
					_ecsWorld.Set(follower, moveTo);
				else
					_ecsWorld.Add(follower, moveTo);
			}
		}
	}

	private void ApplyDeferredTickCommands()
	{
		foreach (var ent in _tickEntitiesToClearOrders)
		{
			if (_ecsWorld.IsAlive(ent))
			{
				OnClearUnitOrdersRequested?.Invoke(ent);
			}
		}

		foreach (var ent in _tickEntitiesToStopGathering)
		{
			if (_ecsWorld.IsAlive(ent))
			{
				OnStopGatheringMovementRequested?.Invoke(ent);
			}
		}

		foreach (var req in _tickSpawningRequests)
		{
			OnSpawnUnitFromProductionRequested?.Invoke(req.UnitId, req.Position, req.IsEnemy, req.RallyPoint, req.IsFromQueue);
		}

		if (_tickNeedsUiRefresh)
		{
			OnUiRefreshRequested?.Invoke();
		}
	}

	private void UpdateBuffsQueryAction(Entity entity, ref Realm.Ecs.Components.Core.Buffs buffs)
	{
		var buffsDict = buffs.Value;
		_tickBuffKeys.Clear();
		_tickExpiredBuffs.Clear();
		foreach (var key in buffsDict.Keys)
		{
			_tickBuffKeys.Add(key);
		}
		for (int i = 0; i < _tickBuffKeys.Count; i++)
		{
			string key = _tickBuffKeys[i];
			float newTime = buffsDict[key] - _fDelta;
			if (newTime <= 0)
			{
				_tickExpiredBuffs.Add(key);
			}
			else
			{
				buffsDict[key] = newTime;
			}
		}
		for (int i = 0; i < _tickExpiredBuffs.Count; i++)
		{
			buffsDict.Remove(_tickExpiredBuffs[i]);
		}
	}

	private void PatrolArrivalQueryAction(Entity entity, ref Patrol patrol, ref Position pos)
	{
		var current = new Vector3(pos.Value.X, pos.Value.Y, pos.Value.Z);
		var dest = patrol.GoingToB
			? new Vector3(patrol.PointB.X, patrol.PointB.Y, patrol.PointB.Z)
			: new Vector3(patrol.PointA.X, patrol.PointA.Y, patrol.PointA.Z);
		if (current.DistanceTo(dest) < 1.5f)
		{
			_tickPatrolToFlip.Add((entity, patrol));
		}
	}

	private void FollowQueryAction(Entity entity, ref Follow follow, ref Position pos)
	{
		if (!_ecsWorld.IsAlive(follow.Target) || _ecsWorld.Has<Dead>(follow.Target))
		{
			_tickFollowToStop.Add(entity);
			return;
		}

		var targetPosComp = _ecsWorld.Get<Position>(follow.Target);
		var currentPos = new Vector3(pos.Value.X, pos.Value.Y, pos.Value.Z);
		var targetPos = new Vector3(targetPosComp.Value.X, targetPosComp.Value.Y, targetPosComp.Value.Z);

		float dist = currentPos.DistanceTo(targetPos);
		if (dist <= 3.0f)
		{
			_tickFollowToStop.Add(entity);
		}
		else
		{
			_tickFollowToMove.Add((entity, targetPos));
		}
	}

	private void AttackCooldownQueryAction(Entity entity, ref Attack atk)
	{
		if (atk.CurrentCooldown > 0)
		{
			atk.CurrentCooldown = Math.Max(0, atk.CurrentCooldown - _fDelta);
		}
	}

	private void SpellCooldownQueryAction(Entity entity, ref SpellCooldowns spellCooldowns)
	{
		if (spellCooldowns.FireballCooldown > 0f) spellCooldowns.FireballCooldown = Math.Max(0f, spellCooldowns.FireballCooldown - _fDelta);
		if (spellCooldowns.LightningCooldown > 0f) spellCooldowns.LightningCooldown = Math.Max(0f, spellCooldowns.LightningCooldown - _fDelta);
		if (spellCooldowns.HolyLightCooldown > 0f) spellCooldowns.HolyLightCooldown = Math.Max(0f, spellCooldowns.HolyLightCooldown - _fDelta);
	}

	private void ProdQueryAction(Entity entity, ref Realm.Ecs.Components.Core.ProductionQueue prod)
	{
		if (prod.UnitIds.Count > 0)
		{
			prod.CurrentProgress += _fDelta;
			if (prod.CurrentProgress >= prod.BuildTime)
			{
				string unitToSpawn = prod.UnitIds[0];
				prod.UnitIds.RemoveAt(0);
				prod.CurrentProgress = 0f;

				if (prod.UnitIds.Count > 0)
				{
					string nextUnitId = prod.UnitIds[0];
					prod.BuildTime = GameHost.UnitRegistry[nextUnitId].ProductionTime;
				}

				if (_ecsWorld.Has<Unit3D>(entity))
				{
					var building3D = _ecsWorld.Get<Unit3D>(entity);
					var spawnPos = building3D.GlobalPosition + new Vector3(0, 0, 8);

					var ownerComp = _ecsWorld.Get<Owner>(entity);
					var playerEntity = _ecsWorld.Get<NetworkMappingState>(_worldEntity).PlayerEntity;
					bool isEnemy = ownerComp.PlayerEntity != playerEntity.AsPlayerEntity(_ecsWorld);

					Vector3? rallyPoint = null;
					if (_ecsWorld.Has<RallyPoint>(entity))
					{
						var rp = _ecsWorld.Get<RallyPoint>(entity);
						rallyPoint = new Vector3(rp.Value.X, rp.Value.Y, rp.Value.Z);
					}
					else
					{
						rallyPoint = building3D.ToGlobal(new Vector3(0, 0, 8));
					}

					_tickSpawningRequests.Add(new SpawningRequest
					{
						UnitId = unitToSpawn,
						Position = spawnPos,
						IsEnemy = isEnemy,
						RallyPoint = rallyPoint,
						IsFromQueue = true
					});

					if (!isEnemy)
					{
						OnProductionCompleted?.Invoke(unitToSpawn);
					}
				}

				_tickNeedsUiRefresh = true;
			}
		}
	}

	private void InterpolationQueryAction(Entity entity, ref InterpolationTarget target, ref Unit3D unit)
	{
		if (!GodotObject.IsInstanceValid(unit)) return;
		Vector3 targetPos = new Vector3(target.Position.X, target.Position.Y, target.Position.Z);
		Vector3 targetVel = new Vector3(target.Velocity.X, target.Velocity.Y, target.Velocity.Z);
		float dynamicInterpolationFactor = GetDynamicInterpolationFactor();
		if (!unit.IsEnemy)
		{
			if (_ecsWorld.Has<MoveTo>(entity) && _ecsWorld.Has<MovementStats>(entity))
			{
				var moveTo = _ecsWorld.Get<MoveTo>(entity);
				var stats = _ecsWorld.Get<MovementStats>(entity);
				Vector3 dest = new Vector3(moveTo.Target.X, moveTo.Target.Y, moveTo.Target.Z);
				float distToDest = unit.GlobalPosition.DistanceTo(dest);
				if (distToDest > 0.05f)
				{
					Vector3 dir = (dest - unit.GlobalPosition).Normalized();
					float step = stats.Speed * _fDelta;
					if (step > distToDest) step = distToDest;
					unit.GlobalPosition += dir * step;
					unit.Velocity = dir * stats.Speed;
				}
				else
				{
					unit.GlobalPosition = dest;
					unit.Velocity = Vector3.Zero;
					_ecsWorld.Remove<MoveTo>(entity);
				}
				GD.Print($"[CLIENT_ESTIMATED] Unit={entity.Id} Pos={unit.GlobalPosition} Target={moveTo.Target}");
			}
			else
			{
				float dist = unit.GlobalPosition.DistanceTo(targetPos);
				if (dist > 2.0f)
				{
					unit.GlobalPosition = targetPos;
					unit.Velocity = targetVel;
				}
				else if (dist > 0.5f)
				{
					Vector3 diff = targetPos - unit.GlobalPosition;
					unit.GlobalPosition += diff * (_fDelta / 0.2f);
				}
				else if (dist > 0.01f)
				{
					Vector3 diff = targetPos - unit.GlobalPosition;
					unit.GlobalPosition += diff * (_fDelta / 0.5f);
				}
			}
			if (_ecsWorld.Has<Position>(entity))
			{
				var finalPos = unit.GlobalPosition;
				_ecsWorld.Set(entity, new Position(new System.Numerics.Vector3(finalPos.X, finalPos.Y, finalPos.Z)));
			}
			if (unit.Velocity.LengthSquared() > 0.01f)
			{
				float angle = Mathf.Atan2(-unit.Velocity.X, -unit.Velocity.Z);
				var rot = unit.Rotation;
				rot.Y = Mathf.LerpAngle(rot.Y, angle, 10f * _fDelta);
				unit.Rotation = rot;
			}
		}
		else
		{
			unit.GlobalPosition = unit.GlobalPosition.Lerp(targetPos, dynamicInterpolationFactor * _fDelta);
			unit.GlobalRotation = new Vector3(0, Mathf.LerpAngle(unit.GlobalRotation.Y, target.RotationY, dynamicInterpolationFactor * _fDelta), 0);
			unit.Velocity = targetVel;
			if (_ecsWorld.Has<Position>(entity))
			{
				_ecsWorld.Set(entity, new Position(new System.Numerics.Vector3(unit.GlobalPosition.X, unit.GlobalPosition.Y, unit.GlobalPosition.Z)));
			}
		}
	}

	private int GetTimeOfDayIndex()
		=> _ecsWorld.GetFieldOrDefault<WorldState, int>(_worldEntity, s => s.TimeOfDayIndex);

	private float GetDynamicInterpolationFactor()
		=> _ecsWorld.GetFieldOrDefault<NetworkState, float>(_worldEntity, s => s.DynamicInterpolationFactor, 10f);

	public List<Entity> GetEditorArrivedUnits() => _tickArrivedUnits;
}
