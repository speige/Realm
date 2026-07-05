using Arch.Core;
using Arch.Core.Extensions;
using DotRecast.Core.Numerics;
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

internal class SimulationService
{
	private readonly WorldAccessor _ecsWorldAccessor;
	private World _ecsWorld => _ecsWorldAccessor.Current;
	private readonly Entity _worldEntity;
	private Entity _resolvedWorldEntity = Entity.Null;
	private Entity ActiveWorldEntity
	{
		get
		{
			if (_resolvedWorldEntity == Entity.Null || !_ecsWorld.IsAlive(_resolvedWorldEntity))
			{
				if (_worldEntity != Entity.Null && _ecsWorld.IsAlive(_worldEntity))
				{
					_resolvedWorldEntity = _worldEntity;
				}
				else
				{
					var worldQuery = Realm.Ecs.Common.QueryCache.AllTerrainStateQuery;
					_ecsWorld.Query(in worldQuery, (Entity entity) => _resolvedWorldEntity = entity);
				}
			}
			return _resolvedWorldEntity;
		}
	}
	private readonly NavMeshPathfinder _pathfinder;

	private readonly MovementAndPathfindingService _movementService;
	private readonly CombatAndDamageService _combatService;
	private readonly ResourceEconomyService _economyService;

	private float _fDelta;

	private readonly List<string> _tickExpiredBuffs = new();
	private readonly List<string> _tickBuffKeys = new();
	private readonly List<(Entity Entity, Patrol Patrol)> _tickPatrolToFlip = new();
	private readonly List<Entity> _tickFollowToStop = new();
	private readonly List<(Entity Follower, System.Numerics.Vector3 TargetPos)> _tickFollowToMove = new();
	private readonly List<Entity> _tickArrivedUnits = new();
	private readonly List<(Entity Entity, PathFollow PathFollow)> _tickAddPathFollow = new();
	private readonly List<Entity> _tickEntitiesToClearOrders = new();
	private readonly List<Entity> _tickEntitiesToStopGathering = new();
	private readonly List<SpawningRequest> _tickSpawningRequests = new();
	private bool _tickNeedsUiRefresh = false;

	private readonly QueryDescription _buffQuery = Realm.Ecs.Common.QueryCache.AllBuffsNoneDeadQuery;
	private readonly QueryDescription _patrolArrivalQuery = Realm.Ecs.Common.QueryCache.AllPatrolAndPositionNoneDeadAndAttackTargetQuery;
	private readonly QueryDescription _followQuery = Realm.Ecs.Common.QueryCache.AllFollowAndPositionNoneDeadQuery;
	private readonly QueryDescription _attackCooldownQuery = Realm.Ecs.Common.QueryCache.AllAttackQuery;
	private readonly QueryDescription _prodQuery = Realm.Ecs.Common.QueryCache.AllProductionQueueQuery;
	private readonly QueryDescription _spellCooldownQuery = Realm.Ecs.Common.QueryCache.AllSpellCooldownsQuery;

	private ForEachWithEntity<Realm.Ecs.Components.Core.Buffs> _buffsQueryDelegate = null!;
	private ForEachWithEntity<Patrol, Position> _patrolArrivalQueryDelegate = null!;
	private ForEachWithEntity<Follow, Position> _followQueryDelegate = null!;
	private ForEachWithEntity<Attack> _attackCooldownQueryDelegate = null!;
	private ForEachWithEntity<Realm.Ecs.Components.Core.ProductionQueue> _prodQueryDelegate = null!;
	private ForEachWithEntity<InterpolationTarget> _interpolationQueryDelegate = null!;
	private ForEachWithEntity<SpellCooldowns> _spellCooldownQueryDelegate = null!;

	public Action<System.Numerics.Vector3, System.Numerics.Vector3> OnArrowProjectileRequested;
	public Action<Entity> OnDamageFlashRequested;
	public Action<System.Numerics.Vector3, System.Numerics.Vector3> OnHealEffectRequested;
	public Action<Entity> OnHealFlashRequested;
	public Action<Entity, Entity, float> OnUnitDamagedCallback;
	public Action<string> OnUnderAttackAlertRequested;
	public Action<Entity> OnKillUnitRequested;
	public Action<string, System.Numerics.Vector3, bool, System.Numerics.Vector3?, bool> OnSpawnUnitFromProductionRequested;
	public Action<Entity> OnClearUnitOrdersRequested;
	public Action<Entity> OnStopGatheringMovementRequested;
	public Action OnUiRefreshRequested;
	public Action<Entity> OnPropDepleted;
	public Action<string, float> OnResourceDepositedForPlayer;
	public Action<string> OnProductionCompleted;
	public Func<string, float> GetProductionBuildTime;

	public struct SpawningRequest
	{
		public string UnitId;
		public System.Numerics.Vector3 Position;
		public bool IsEnemy;
		public System.Numerics.Vector3? RallyPoint;
		public bool IsFromQueue;
	}

	public SimulationService(WorldAccessor ecsWorldAccessor, Entity worldEntity, NavMeshPathfinder pathfinder)
	{
		_ecsWorldAccessor = ecsWorldAccessor;
		_worldEntity = worldEntity;
		_pathfinder = pathfinder;

		_movementService = new MovementAndPathfindingService(ecsWorldAccessor, worldEntity, pathfinder);
		_combatService = new CombatAndDamageService(ecsWorldAccessor);
		_economyService = new ResourceEconomyService(ecsWorldAccessor);

		_combatService.OnArrowProjectileRequested = (p1, p2) => OnArrowProjectileRequested?.Invoke(p1, p2);
		_combatService.OnDamageFlashRequested = ent => OnDamageFlashRequested?.Invoke(ent);
		_combatService.OnHealEffectRequested = (p1, p2) => OnHealEffectRequested?.Invoke(p1, p2);
		_combatService.OnHealFlashRequested = ent => OnHealFlashRequested?.Invoke(ent);
		_combatService.OnUnitDamagedCallback = (e1, e2, d) => OnUnitDamagedCallback?.Invoke(e1, e2, d);
		_combatService.OnUnderAttackAlertRequested = id => OnUnderAttackAlertRequested?.Invoke(id);
		_combatService.OnKillUnitRequested = ent => OnKillUnitRequested?.Invoke(ent);

		_economyService.OnResourceDepositedForPlayer = (res, amount) => OnResourceDepositedForPlayer?.Invoke(res, amount);
		_economyService.OnClearUnitOrdersRequested = ent => OnClearUnitOrdersRequested?.Invoke(ent);
		_economyService.OnStopGatheringMovementRequested = ent => OnStopGatheringMovementRequested?.Invoke(ent);
		_economyService.OnPropDepleted = ent => OnPropDepleted?.Invoke(ent);
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

		if (ActiveWorldEntity != default && _ecsWorld.IsAlive(ActiveWorldEntity))
		{
			if (_ecsWorld.Has<WorldState>(ActiveWorldEntity))
			{
				var state = _ecsWorld.Get<WorldState>(ActiveWorldEntity);
				float elapsed = state.GameElapsedTime + fDelta;
				float timer = state.TimeOfDayTimer;
				int index = state.TimeOfDayIndex;

				if (state.DayNightCycleEnabled)
				{
					timer += fDelta;
					const float cycleDuration = 90f;
					if (timer >= cycleDuration)
					{
						timer -= cycleDuration;
					}

					float progress = timer / cycleDuration;
					float currentHour = progress * 24f;
					if (currentHour >= 5f && currentHour < 6f) index = 3;
					else if (currentHour >= 6f && currentHour < 18f) index = 0;
					else if (currentHour >= 18f && currentHour < 20f) index = 1;
					else index = 2;
				}
				_ecsWorld.Set(ActiveWorldEntity, new WorldState(elapsed, index, timer, state.DayNightCycleEnabled));
			}

			if (_ecsWorld.Has<CountdownState>(ActiveWorldEntity))
			{
				var countdown = _ecsWorld.Get<CountdownState>(ActiveWorldEntity);
				if (countdown.Active)
				{
					float newDuration = countdown.Duration - fDelta;
					if (newDuration <= 0f)
					{
						_ecsWorld.Set(ActiveWorldEntity, new CountdownState(false, 0f, countdown.Text));
					}
					else
					{
						_ecsWorld.Set(ActiveWorldEntity, new CountdownState(true, newDuration, countdown.Text));
					}
				}
			}
		}

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
	public ForEachWithEntity<InterpolationTarget> InterpolationQueryDelegate => _interpolationQueryDelegate;

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
		_economyService.SetRuntimeReferences(definitionManager, goldResourceId, woodResourceId, stoneResourceId);
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
				var moveTo = new MoveTo(targetPos);
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
		var current = pos.Value;
		var dest = patrol.GoingToB ? patrol.PointB : patrol.PointA;
		if (System.Numerics.Vector3.Distance(current, dest) < 1.5f)
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

		var currentPos = pos.Value;
		var targetPos = _ecsWorld.Get<Position>(follow.Target).Value;

		float dist = System.Numerics.Vector3.Distance(currentPos, targetPos);
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
					prod.BuildTime = GetProductionBuildTime != null
						? GetProductionBuildTime(nextUnitId)
						: 5f;
				}

				if (_ecsWorld.Has<Position>(entity))
				{
					var buildingPos = _ecsWorld.Get<Position>(entity).Value;

					System.Numerics.Vector3 spawnOffset = _ecsWorld.Has<BuildingSpawnOffset>(entity)
						? _ecsWorld.Get<BuildingSpawnOffset>(entity).Value
						: new System.Numerics.Vector3(0f, 0f, 8f);

					var spawnPos = buildingPos + spawnOffset;

					var ownerComp = _ecsWorld.Get<Owner>(entity);
					var playerEntity = _ecsWorld.Get<NetworkMappingState>(ActiveWorldEntity).PlayerEntity;
					bool isEnemy = ownerComp.PlayerEntity != playerEntity.AsPlayerEntity(_ecsWorld);

					System.Numerics.Vector3? rallyPoint = null;
					if (_ecsWorld.Has<RallyPoint>(entity))
					{
						rallyPoint = _ecsWorld.Get<RallyPoint>(entity).Value;
					}
					else
					{
						rallyPoint = buildingPos + spawnOffset;
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

	private void InterpolationQueryAction(Entity entity, ref InterpolationTarget target)
	{
		if (!_ecsWorld.Has<Position>(entity)) return;
		var pos = _ecsWorld.Get<Position>(entity);
		System.Numerics.Vector3 currentPos = pos.Value;
		System.Numerics.Vector3 targetPos = target.Position;
		System.Numerics.Vector3 targetVel = target.Velocity;

		System.Numerics.Vector3 finalPos = currentPos;
		System.Numerics.Vector3 finalVel = targetVel;

		bool isEnemy = _ecsWorld.Has<UnitFaction>(entity) && _ecsWorld.Get<UnitFaction>(entity).IsEnemy;
		float dynamicInterpolationFactor = GetDynamicInterpolationFactor();

		if (!isEnemy)
		{
			if (_ecsWorld.Has<MoveTo>(entity) && _ecsWorld.Has<MovementStats>(entity))
			{
				var moveTo = _ecsWorld.Get<MoveTo>(entity);
				var stats = _ecsWorld.Get<MovementStats>(entity);
				System.Numerics.Vector3 dest = moveTo.Target;
				float distToDest = System.Numerics.Vector3.Distance(currentPos, dest);
				if (distToDest > 0.05f)
				{
					System.Numerics.Vector3 dir = System.Numerics.Vector3.Normalize(dest - currentPos);
					float step = stats.Speed * _fDelta;
					if (step > distToDest) step = distToDest;
					finalPos = currentPos + dir * step;
					finalVel = dir * stats.Speed;
				}
				else
				{
					finalPos = dest;
					finalVel = System.Numerics.Vector3.Zero;
					_ecsWorld.Remove<MoveTo>(entity);
				}
				Console.WriteLine($"[CLIENT_ESTIMATED] Unit={entity.Id} Pos={finalPos} Target={moveTo.Target}");
			}
			else
			{
				float dist = System.Numerics.Vector3.Distance(currentPos, targetPos);
				if (dist > 2.0f)
				{
					finalPos = targetPos;
					finalVel = targetVel;
				}
				else if (dist > 0.5f)
				{
					System.Numerics.Vector3 diff = targetPos - currentPos;
					finalPos = currentPos + diff * (_fDelta / 0.2f);
				}
				else if (dist > 0.01f)
				{
					System.Numerics.Vector3 diff = targetPos - currentPos;
					finalPos = currentPos + diff * (_fDelta / 0.5f);
				}
			}
		}
		else
		{
			finalPos = System.Numerics.Vector3.Lerp(currentPos, targetPos, Math.Min(1f, dynamicInterpolationFactor * _fDelta));
			finalVel = targetVel;
		}

		_ecsWorld.Set(entity, new Position(finalPos));
		if (_ecsWorld.Has<Velocity>(entity))
		{
			_ecsWorld.Set(entity, new Velocity(finalVel));
		}
		else
		{
			_ecsWorld.Add(entity, new Velocity(finalVel));
		}
	}

	private int GetTimeOfDayIndex()
		=> _ecsWorld.GetFieldOrDefault<WorldState, int>(ActiveWorldEntity, s => s.TimeOfDayIndex);

	private float GetDynamicInterpolationFactor()
		=> _ecsWorld.GetFieldOrDefault<NetworkState, float>(ActiveWorldEntity, s => s.DynamicInterpolationFactor, 10f);

	public List<Entity> GetEditorArrivedUnits() => _tickArrivedUnits;

	public void DealSpellDamageAOE(System.Numerics.Vector3 position, float radius, float damage, Entity casterEntity, bool enemyOnly = true)
	{
		_combatService.DealSpellDamageAOE(position, radius, damage, casterEntity, enemyOnly);
	}

	public void HealAOE(System.Numerics.Vector3 position, float radius, float healAmount)
	{
		_combatService.HealAOE(position, radius, healAmount);
	}
}
