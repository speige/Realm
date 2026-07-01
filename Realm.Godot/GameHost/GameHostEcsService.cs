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

internal class GameHostEcsService
{
	private readonly World _ecsWorld;
	private readonly Entity _worldEntity;
	private readonly NavMeshPathfinder _pathfinder;

	private float _fDelta;

	private const float CollisionCellSize = 10f;
	private const float UnderAttackAlertCooldown = 8f;

	private readonly Dictionary<long, List<Unit3D>> _unitGrid = new();
	private readonly Dictionary<long, List<Prop3D>> _propGrid = new();
	private readonly List<List<Unit3D>> _unitListPool = new();
	private readonly List<List<Prop3D>> _propListPool = new();

	private Entity _scanAttackerEntity;
	private Vector3 _scanAttackerPos;
	private PlayerEntity _scanAttackerOwner;
	private bool _scanIsAttackerEnemy;
	private float _scanClosestDist;
	private Entity _scanClosestEnemy;

	private Vector3 _scanPriestPos;
	private PlayerEntity _scanFriendlyOwner;
	private float _scanFriendlyClosestDist;
	private Entity _scanClosestDamagedFriendly;

	private readonly List<string> _tickExpiredBuffs = new();
	private readonly List<string> _tickBuffKeys = new();
	private readonly List<(Entity Entity, Patrol Patrol)> _tickPatrolToFlip = new();
	private readonly List<Entity> _tickFollowToStop = new();
	private readonly List<(Entity Follower, Vector3 TargetPos)> _tickFollowToMove = new();
	private readonly List<(Entity Worker, Gatherer NewState, Vector3? NewDestination)> _tickGatherersToUpdate = new();
	private readonly List<Entity> _tickArrivedUnits = new();
	private readonly List<(Entity Entity, PathFollow PathFollow)> _tickAddPathFollow = new();
	private readonly List<(Entity Attacker, AttackTarget Target)> _tickNewAttackTargets = new();
	private readonly List<Entity> _tickActionsToRemoveTarget = new();
	private readonly List<(Entity Attacker, Vector3 TargetPos)> _tickActionsToChase = new();
	private readonly List<Entity> _tickActionsToStopChasing = new();
	private readonly List<(Entity Entity, Unit3D Unit)> _tickUnitsToKill = new();
	private readonly List<(Entity Priest, HealingTarget Target)> _tickNewHealingTargets = new();
	private readonly List<Entity> _tickHealRemoveTargets = new();
	private readonly List<(Entity Priest, Vector3 TargetPos)> _tickHealChaseTargets = new();
	private readonly List<Entity> _tickHealStopChasing = new();
	private readonly List<Entity> _tickEntitiesToClearOrders = new();
	private readonly List<Entity> _tickEntitiesToStopGathering = new();
	private readonly List<SpawningRequest> _tickSpawningRequests = new();
	private bool _tickNeedsUiRefresh = false;

	private readonly QueryDescription _enemyQuery = new QueryDescription().WithAll<Position, Owner>().WithNone<Dead>();
	private readonly QueryDescription _friendlyScanQuery = new QueryDescription().WithAll<Position, Health, Owner>().WithNone<Dead>();
	private readonly QueryDescription _passiveIncomeQuery = new QueryDescription().WithAll<PlayerResources>().WithNone<Dead>();
	private readonly QueryDescription _buffQuery = new QueryDescription().WithAll<Realm.Ecs.Components.Core.Buffs>().WithNone<Dead>();
	private readonly QueryDescription _patrolArrivalQuery = new QueryDescription().WithAll<Patrol, Position>().WithNone<Dead, AttackTarget>();
	private readonly QueryDescription _followQuery = new QueryDescription().WithAll<Follow, Position>().WithNone<Dead>();
	private readonly QueryDescription _gatherQuery = new QueryDescription().WithAll<Position, Gatherer>().WithNone<Dead>();
	private readonly QueryDescription _movementQuery = new QueryDescription().WithAll<Position, MoveTo, MovementStats>().WithNone<Dead>();
	private readonly QueryDescription _attackCooldownQuery = new QueryDescription().WithAll<Attack>();
	private readonly QueryDescription _targetAcquisitionQuery = new QueryDescription().WithAll<Position, Attack, Owner>().WithNone<AttackTarget, Dead>();
	private readonly QueryDescription _combatQuery = new QueryDescription().WithAll<Position, Attack, AttackTarget, Owner>().WithNone<Dead>();
	private readonly QueryDescription _priestScanQuery = new QueryDescription().WithAll<Position, Owner, DefinitionId>().WithNone<Dead, HealingTarget>();
	private readonly QueryDescription _healingExecutionQuery = new QueryDescription().WithAll<Position, Attack, HealingTarget, Owner>().WithNone<Dead>();
	private readonly QueryDescription _prodQuery = new QueryDescription().WithAll<Realm.Ecs.Components.Core.ProductionQueue>();
	private readonly QueryDescription _spellCooldownQuery = new QueryDescription().WithAll<SpellCooldowns>();

	private ForEachWithEntity<Realm.Ecs.Components.Core.Buffs> _buffsQueryDelegate = null!;
	private ForEachWithEntity<Patrol, Position> _patrolArrivalQueryDelegate = null!;
	private ForEachWithEntity<Follow, Position> _followQueryDelegate = null!;
	private ForEachWithEntity<Position, Gatherer> _gatherQueryDelegate = null!;
	private ForEachWithEntity<Position, MoveTo, MovementStats> _movementQueryDelegate = null!;
	private ForEachWithEntity<Attack> _attackCooldownQueryDelegate = null!;
	private ForEachWithEntity<Position, Attack, Owner> _targetAcquisitionQueryDelegate = null!;
	private ForEachWithEntity<Position, Owner> _potentialEnemyQueryDelegate = null!;
	private ForEachWithEntity<Position, Attack, AttackTarget, Owner> _combatQueryDelegate = null!;
	private ForEachWithEntity<Position, Owner, DefinitionId> _priestScanQueryDelegate = null!;
	private ForEachWithEntity<Position, Health, Owner> _friendlyScanQueryDelegate = null!;
	private ForEachWithEntity<Position, Attack, HealingTarget, Owner> _healingExecutionQueryDelegate = null!;
	private ForEachWithEntity<Realm.Ecs.Components.Core.ProductionQueue> _prodQueryDelegate = null!;
	private ForEachWithEntity<Position, MoveTo, MovementStats> _editorMovementQueryDelegate = null!;
	private ForEachWithEntity<InterpolationTarget, Unit3D> _interpolationQueryDelegate = null!;
	private ForEachWithEntity<PlayerResources> _passiveIncomeQueryDelegate = null!;
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
	}

	public void Initialize()
	{
		_buffsQueryDelegate = UpdateBuffsQueryAction;
		_patrolArrivalQueryDelegate = PatrolArrivalQueryAction;
		_followQueryDelegate = FollowQueryAction;
		_gatherQueryDelegate = GatherQueryAction;
		_movementQueryDelegate = MovementQueryAction;
		_attackCooldownQueryDelegate = AttackCooldownQueryAction;
		_targetAcquisitionQueryDelegate = TargetAcquisitionQueryAction;
		_potentialEnemyQueryDelegate = ScanEnemyQueryAction;
		_combatQueryDelegate = CombatQueryAction;
		_priestScanQueryDelegate = PriestScanQueryAction;
		_friendlyScanQueryDelegate = ScanFriendlyQueryAction;
		_healingExecutionQueryDelegate = HealingExecutionQueryAction;
		_prodQueryDelegate = ProdQueryAction;
		_editorMovementQueryDelegate = ProcessMapEditorPhysicsQueryAction;
		_interpolationQueryDelegate = InterpolationQueryAction;
		_passiveIncomeQueryDelegate = UpdatePassiveIncomeQueryAction;
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

		TickCombatAlertTimer(fDelta);

		_ecsWorld.Query(in _passiveIncomeQuery, _passiveIncomeQueryDelegate);
		_ecsWorld.Query(in _buffQuery, _buffsQueryDelegate);

		ProcessPatrolArrivals();
		ProcessFollowMovements();
		ProcessGatheringTicks();
		ProcessMovementTicks();

		_ecsWorld.Query(in _attackCooldownQuery, _attackCooldownQueryDelegate);
		ProcessTargetAcquisition();
		ProcessCombatTicks();
		ProcessHealingTicks();

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

	public ForEachWithEntity<Position, MoveTo, MovementStats> EditorMovementQueryDelegate => _editorMovementQueryDelegate;
	public ForEachWithEntity<InterpolationTarget, Unit3D> InterpolationQueryDelegate => _interpolationQueryDelegate;

	private void TickCombatAlertTimer(float fDelta)
	{
		if (_ecsWorld.IsAlive(_worldEntity) && _ecsWorld.Has<CombatAlertState>(_worldEntity))
		{
			ref var alertState = ref _ecsWorld.Get<CombatAlertState>(_worldEntity);
			if (alertState.UnderAttackAlertTimer > 0f)
			{
				_ecsWorld.Set(_worldEntity, new CombatAlertState(alertState.UnderAttackAlertTimer - fDelta));
			}
		}
	}

	private void ProcessPatrolArrivals()
	{
		_tickPatrolToFlip.Clear();
		_ecsWorld.Query(in _patrolArrivalQuery, _patrolArrivalQueryDelegate);
		foreach (var (entity, patrol) in _tickPatrolToFlip)
		{
			var flipped = new Patrol(patrol.PointA, patrol.PointB) { GoingToB = !patrol.GoingToB };
			_ecsWorld.Set(entity, flipped);
			var newDest = flipped.GoingToB ? flipped.PointB : flipped.PointA;
			var moveTo = new MoveTo(new System.Numerics.Vector3(newDest.X, newDest.Y, newDest.Z));
			if (_ecsWorld.Has<MoveTo>(entity)) _ecsWorld.Set(entity, moveTo);
			else _ecsWorld.Add(entity, moveTo);
		}
	}

	private void ProcessFollowMovements()
	{
		_tickFollowToStop.Clear();
		_tickFollowToMove.Clear();
		_ecsWorld.Query(in _followQuery, _followQueryDelegate);

		foreach (var ent in _tickFollowToStop)
		{
			if (_ecsWorld.IsAlive(ent))
			{
				if (!_ecsWorld.IsAlive(_ecsWorld.Get<Follow>(ent).Target) || _ecsWorld.Has<Dead>(_ecsWorld.Get<Follow>(ent).Target))
				{
					_ecsWorld.Remove<Follow>(ent);
				}
				if (_ecsWorld.Has<MoveTo>(ent))
				{
					_ecsWorld.Remove<MoveTo>(ent);
				}
				if (_ecsWorld.Has<Unit3D>(ent))
				{
					var unit3D = _ecsWorld.Get<Unit3D>(ent);
					unit3D.Velocity = Vector3.Zero;
				}
			}
		}

		foreach (var (ent, targetPos) in _tickFollowToMove)
		{
			if (_ecsWorld.IsAlive(ent))
			{
				var moveTo = new MoveTo(new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z));
				if (_ecsWorld.Has<MoveTo>(ent)) _ecsWorld.Set(ent, moveTo);
				else _ecsWorld.Add(ent, moveTo);
			}
		}
	}

	private void ProcessGatheringTicks()
	{
		_tickGatherersToUpdate.Clear();
		_ecsWorld.Query(in _gatherQuery, _gatherQueryDelegate);

		foreach (var (worker, newState, dest) in _tickGatherersToUpdate)
		{
			if (_ecsWorld.IsAlive(worker))
			{
				_ecsWorld.Set(worker, newState);
				if (dest.HasValue)
				{
					var moveTo = new MoveTo(new System.Numerics.Vector3(dest.Value.X, dest.Value.Y, dest.Value.Z));
					if (_ecsWorld.Has<MoveTo>(worker)) _ecsWorld.Set(worker, moveTo);
					else _ecsWorld.Add(worker, moveTo);
				}
			}
		}
	}

	private void ProcessMovementTicks()
	{
		_tickArrivedUnits.Clear();
		RebuildSpatialGrid();
		_ecsWorld.Query(in _movementQuery, _movementQueryDelegate);
		foreach (var entity in _tickArrivedUnits)
		{
			if (_ecsWorld.IsAlive(entity) && _ecsWorld.Has<MoveTo>(entity))
			{
				if (_ecsWorld.Has<PathFollow>(entity))
				{
					_ecsWorld.Remove<PathFollow>(entity);
				}
				if (_ecsWorld.Has<WaypointQueue>(entity))
				{
					var q = _ecsWorld.Get<WaypointQueue>(entity);
					if (q.Count > 0)
					{
						var nextWaypoint = q.Dequeue();
						_ecsWorld.Set(entity, q);
						_ecsWorld.Set(entity, new MoveTo(nextWaypoint));
						continue;
					}
					else
					{
						_ecsWorld.Remove<WaypointQueue>(entity);
					}
				}
				_ecsWorld.Remove<MoveTo>(entity);
			}
		}
	}

	private void ProcessTargetAcquisition()
	{
		_tickNewAttackTargets.Clear();
		_ecsWorld.Query(in _targetAcquisitionQuery, _targetAcquisitionQueryDelegate);
		foreach (var (attacker, target) in _tickNewAttackTargets)
		{
			if (_ecsWorld.IsAlive(attacker))
			{
				if (_ecsWorld.Has<AttackTarget>(attacker))
					_ecsWorld.Set(attacker, target);
				else
					_ecsWorld.Add(attacker, target);
			}
		}
	}

	private void ProcessCombatTicks()
	{
		_tickActionsToRemoveTarget.Clear();
		_tickActionsToChase.Clear();
		_tickActionsToStopChasing.Clear();
		_tickUnitsToKill.Clear();

		_ecsWorld.Query(in _combatQuery, _combatQueryDelegate);

		foreach (var (targetEntity, target3D) in _tickUnitsToKill)
		{
			if (_ecsWorld.IsAlive(targetEntity))
			{
				if (!_ecsWorld.Has<Dead>(targetEntity))
				{
					_ecsWorld.Add<Dead>(targetEntity);
					OnKillUnitRequested?.Invoke(targetEntity);
				}
			}
		}

		foreach (var ent in _tickActionsToRemoveTarget)
		{
			if (_ecsWorld.IsAlive(ent))
			{
				if (_ecsWorld.Has<AttackTarget>(ent))
				{
					_ecsWorld.Remove<AttackTarget>(ent);
				}

				if (_ecsWorld.Has<Realm.Ecs.Components.Movement.AttackMove>(ent))
				{
					var am = _ecsWorld.Get<Realm.Ecs.Components.Movement.AttackMove>(ent);
					var moveTo = new MoveTo(am.Target);
					if (_ecsWorld.Has<MoveTo>(ent))
						_ecsWorld.Set(ent, moveTo);
					else
						_ecsWorld.Add(ent, moveTo);
				}
				else if (_ecsWorld.Has<Patrol>(ent))
				{
					var patrol = _ecsWorld.Get<Patrol>(ent);
					var destVec = patrol.GoingToB ? patrol.PointB : patrol.PointA;
					var moveTo = new MoveTo(destVec);
					if (_ecsWorld.Has<MoveTo>(ent))
						_ecsWorld.Set(ent, moveTo);
					else
						_ecsWorld.Add(ent, moveTo);
				}
			}
		}

		foreach (var (attacker, targetPos) in _tickActionsToChase)
		{
			if (_ecsWorld.IsAlive(attacker))
			{
				var moveTo = new MoveTo(new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z));
				if (_ecsWorld.Has<MoveTo>(attacker))
					_ecsWorld.Set(attacker, moveTo);
				else
					_ecsWorld.Add(attacker, moveTo);
			}
		}

		foreach (var attacker in _tickActionsToStopChasing)
		{
			if (_ecsWorld.IsAlive(attacker))
			{
				if (_ecsWorld.Has<MoveTo>(attacker))
				{
					_ecsWorld.Remove<MoveTo>(attacker);
				}
				if (_ecsWorld.Has<Unit3D>(attacker))
				{
					var unit3D = _ecsWorld.Get<Unit3D>(attacker);
					unit3D.Velocity = Vector3.Zero;
				}
			}
		}
	}

	private void ProcessHealingTicks()
	{
		_tickNewHealingTargets.Clear();
		_ecsWorld.Query(in _priestScanQuery, _priestScanQueryDelegate);
		foreach (var (priest, target) in _tickNewHealingTargets)
		{
			if (_ecsWorld.IsAlive(priest))
			{
				if (_ecsWorld.Has<HealingTarget>(priest)) _ecsWorld.Set(priest, target);
				else _ecsWorld.Add(priest, target);
			}
		}

		_tickHealRemoveTargets.Clear();
		_tickHealChaseTargets.Clear();
		_tickHealStopChasing.Clear();

		_ecsWorld.Query(in _healingExecutionQuery, _healingExecutionQueryDelegate);

		foreach (var ent in _tickHealRemoveTargets)
		{
			if (_ecsWorld.IsAlive(ent) && _ecsWorld.Has<HealingTarget>(ent))
			{
				_ecsWorld.Remove<HealingTarget>(ent);
			}
		}

		foreach (var (priest, targetPos) in _tickHealChaseTargets)
		{
			if (_ecsWorld.IsAlive(priest))
			{
				var moveTo = new MoveTo(new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z));
				if (_ecsWorld.Has<MoveTo>(priest)) _ecsWorld.Set(priest, moveTo);
				else _ecsWorld.Add(priest, moveTo);
			}
		}

		foreach (var priest in _tickHealStopChasing)
		{
			if (_ecsWorld.IsAlive(priest))
			{
				if (_ecsWorld.Has<MoveTo>(priest)) _ecsWorld.Remove<MoveTo>(priest);
				if (_ecsWorld.Has<Unit3D>(priest))
				{
					var unit3D = _ecsWorld.Get<Unit3D>(priest);
					unit3D.Velocity = Vector3.Zero;
				}
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

	private static long GetCellKey(float x, float z)
	{
		int cx = (int)Math.Floor(x / CollisionCellSize);
		int cz = (int)Math.Floor(z / CollisionCellSize);
		return ((long)cx << 32) | (uint)cz;
	}

	private void RebuildSpatialGrid(List<Unit3D> allUnits, List<Prop3D> allProps)
	{
		foreach (var list in _unitGrid.Values)
		{
			list.Clear();
			_unitListPool.Add(list);
		}
		_unitGrid.Clear();

		foreach (var list in _propGrid.Values)
		{
			list.Clear();
			_propListPool.Add(list);
		}
		_propGrid.Clear();

		foreach (var u in allUnits)
		{
			if (!GodotObject.IsInstanceValid(u) || _ecsWorld.Has<Dead>(u.Entity)) continue;
			long key = GetCellKey(u.GlobalPosition.X, u.GlobalPosition.Z);
			if (!_unitGrid.TryGetValue(key, out var list))
			{
				if (_unitListPool.Count > 0)
				{
					int lastIdx = _unitListPool.Count - 1;
					list = _unitListPool[lastIdx];
					_unitListPool.RemoveAt(lastIdx);
				}
				else
				{
					list = new List<Unit3D>(16);
				}
				_unitGrid[key] = list;
			}
			list.Add(u);
		}

		foreach (var p in allProps)
		{
			if (!GodotObject.IsInstanceValid(p)) continue;
			long key = GetCellKey(p.GlobalPosition.X, p.GlobalPosition.Z);
			if (!_propGrid.TryGetValue(key, out var list))
			{
				if (_propListPool.Count > 0)
				{
					int lastIdx = _propListPool.Count - 1;
					list = _propListPool[lastIdx];
					_propListPool.RemoveAt(lastIdx);
				}
				else
				{
					list = new List<Prop3D>(16);
				}
				_propGrid[key] = list;
			}
			list.Add(p);
		}
	}

	private List<Unit3D> _allUnitsRef;
	private List<Prop3D> _allPropsRef;
	private List<Unit3D> _castlesListRef;
	private DefinitionManager _definitionManagerRef;
	private ResourceId _goldResourceId;
	private ResourceId _woodResourceId;
	private ResourceId _stoneResourceId;
	private EditableTerrain _groundTerrainRef;

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
		_allUnitsRef = allUnits;
		_allPropsRef = allProps;
		_castlesListRef = castlesList;
		_definitionManagerRef = definitionManager;
		_goldResourceId = goldResourceId;
		_woodResourceId = woodResourceId;
		_stoneResourceId = stoneResourceId;
		_groundTerrainRef = groundTerrain;
	}

	private void RebuildSpatialGrid() => RebuildSpatialGrid(_allUnitsRef, _allPropsRef);

	private void UpdateBuffsQueryAction(Entity entity, ref Realm.Ecs.Components.Core.Buffs buffs)
	{
		var buffsDict = buffs.Value;
		if (buffsDict == null || buffsDict.Count == 0) return;

		_tickExpiredBuffs.Clear();
		_tickBuffKeys.Clear();
		foreach (var kvp in buffsDict)
		{
			_tickBuffKeys.Add(kvp.Key);
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

	private void GatherQueryAction(Entity entity, ref Position pos, ref Gatherer gather)
	{
		var currentPos = new Vector3(pos.Value.X, pos.Value.Y, pos.Value.Z);

		if (gather.ReturningToBase)
		{
			Unit3D nearestCastle = null;
			float nearestDist = float.MaxValue;
			foreach (var u in _castlesListRef)
			{
				var wOwner = _ecsWorld.Get<Owner>(entity).PlayerEntity;
				var uOwner = _ecsWorld.Get<Owner>(u.Entity).PlayerEntity;
				if (uOwner == wOwner && GodotObject.IsInstanceValid(u))
				{
					float dist = currentPos.DistanceTo(u.GlobalPosition);
					if (dist < nearestDist)
					{
						nearestDist = dist;
						nearestCastle = u;
					}
				}
			}

			if (nearestCastle == null)
			{
				var newState = gather;
				newState.ReturningToBase = false;
				newState.CarriedAmount = 0;
				_tickGatherersToUpdate.Add((entity, newState, null));
				return;
			}

			float castleRadius = 6.0f;
			if (currentPos.DistanceTo(nearestCastle.GlobalPosition) <= castleRadius)
			{
				float carry = gather.CarriedAmount;
				var ownerEntity = _ecsWorld.Get<Owner>(entity).PlayerEntity.Value;
				if (_ecsWorld.Has<PlayerResources>(ownerEntity))
				{
					ref var playerRes = ref _ecsWorld.Get<PlayerResources>(ownerEntity);
					var resId = gather.ResourceType.AsResourceId(_definitionManagerRef);
					if (playerRes.Value.ContainsKey(resId))
					{
						playerRes.Value[resId] = (int)Math.Min(GameHost.ResourceCap, playerRes.Value[resId] + carry);
					}
				}

				var playerEntityForAlert = _ecsWorld.Get<NetworkMappingState>(_worldEntity).PlayerEntity;
				if (ownerEntity == playerEntityForAlert)
				{
					OnResourceDepositedForPlayer?.Invoke(gather.ResourceType, carry);
				}

				Prop3D targetNode = null;
				if (_ecsWorld.IsAlive(gather.TargetEntity) && _ecsWorld.Has<Prop3D>(gather.TargetEntity))
				{
					targetNode = _ecsWorld.Get<Prop3D>(gather.TargetEntity);
				}

				if (GodotObject.IsInstanceValid(targetNode))
				{
					var newState = gather;
					newState.ReturningToBase = false;
					newState.CarriedAmount = 0f;
					var dest = targetNode.GlobalPosition;
					_tickGatherersToUpdate.Add((entity, newState, dest));
				}
				else
				{
					var newState = gather;
					newState.ReturningToBase = false;
					newState.CarriedAmount = 0f;
					newState.TargetEntity = Entity.Null;
					_tickGatherersToUpdate.Add((entity, newState, null));
				}
			}
			else
			{
				if (!_ecsWorld.Has<MoveTo>(entity))
				{
					var dest = nearestCastle.GlobalPosition;
					_tickGatherersToUpdate.Add((entity, gather, dest));
				}
			}
		}
		else
		{
			Prop3D targetNode = null;
			if (_ecsWorld.IsAlive(gather.TargetEntity) && _ecsWorld.Has<Prop3D>(gather.TargetEntity))
			{
				targetNode = _ecsWorld.Get<Prop3D>(gather.TargetEntity);
			}

			if (!GodotObject.IsInstanceValid(targetNode))
			{
				Prop3D alternate = FindNearbyResourceNode(currentPos, gather.ResourceType, 25.0f);
				if (alternate != null)
				{
					var newState = gather;
					newState.TargetEntity = alternate.Entity;
					var dest = alternate.GlobalPosition;
					_tickGatherersToUpdate.Add((entity, newState, dest));
				}
				else
				{
					_tickEntitiesToClearOrders.Add(entity);
				}
				return;
			}

			float dist = currentPos.DistanceTo(targetNode.GlobalPosition);
			float gatherRange = 3.5f;
			if (dist <= gatherRange)
			{
				if (_ecsWorld.Has<MoveTo>(entity))
				{
					_tickEntitiesToStopGathering.Add(entity);
				}

				var newState = gather;
				float mineRate = 4.0f * _fDelta;

				var enemyPlayerEntity = _ecsWorld.Get<NetworkMappingState>(_worldEntity).EnemyPlayerEntity;
				bool isEnemy = _ecsWorld.Get<Owner>(entity).PlayerEntity == enemyPlayerEntity.AsPlayerEntity(_ecsWorld);
				if (!isEnemy && GetHarvestingUpgrade()) mineRate *= 1.5f;

				float nodeRemaining = targetNode.ResourceAmount;
				if (mineRate > nodeRemaining)
				{
					mineRate = nodeRemaining;
				}

				targetNode.ResourceAmount -= mineRate;
				newState.CarriedAmount = Math.Min(gather.MaxCapacity, gather.CarriedAmount + mineRate);

				if (_ecsWorld.Has<Unit3D>(entity))
				{
					var worker3D = _ecsWorld.Get<Unit3D>(entity);
					float gameElapsed = GetGameElapsedTime();
					float pulse = 1.0f + Mathf.Sin(gameElapsed * 10f) * 0.1f;
					worker3D.Scale = new Vector3(pulse * 0.9f, (2.0f - pulse) * 0.9f, pulse * 0.9f);
				}

				if (targetNode.ResourceAmount <= 0f)
				{
					var depletedNode = targetNode;
					_allPropsRef.Remove(depletedNode);
					if (_ecsWorld.IsAlive(depletedNode.Entity))
					{
						_ecsWorld.Destroy(depletedNode.Entity);
					}
					depletedNode.QueueFree();
				}

				if (newState.CarriedAmount >= gather.MaxCapacity)
				{
					newState.ReturningToBase = true;
					Unit3D nearestCastle = null;
					float nearestDist = float.MaxValue;
					foreach (var u in _castlesListRef)
					{
						var wOwner = _ecsWorld.Get<Owner>(entity).PlayerEntity;
						var uOwner = _ecsWorld.Get<Owner>(u.Entity).PlayerEntity;
						if (uOwner == wOwner && GodotObject.IsInstanceValid(u))
						{
							float d = currentPos.DistanceTo(u.GlobalPosition);
							if (d < nearestDist)
							{
								nearestDist = d;
								nearestCastle = u;
							}
						}
					}

					if (nearestCastle != null)
					{
						var dest = nearestCastle.GlobalPosition;
						_tickGatherersToUpdate.Add((entity, newState, dest));
					}
					else
					{
						_tickGatherersToUpdate.Add((entity, newState, null));
					}
				}
				else
				{
					_tickGatherersToUpdate.Add((entity, newState, null));
				}
			}
			else
			{
				if (!_ecsWorld.Has<MoveTo>(entity))
				{
					var dest = targetNode.GlobalPosition;
					_tickGatherersToUpdate.Add((entity, gather, dest));
				}
			}
		}
	}

	private void MovementQueryAction(Entity entity, ref Position pos, ref MoveTo moveTo, ref MovementStats stats)
	{
		if (_ecsWorld.Has<Realm.Ecs.Components.Core.Buffs>(entity) && _ecsWorld.Get<Realm.Ecs.Components.Core.Buffs>(entity).Value.ContainsKey("stun"))
		{
			if (_ecsWorld.Has<Unit3D>(entity))
			{
				var u3d = _ecsWorld.Get<Unit3D>(entity);
				if (GodotObject.IsInstanceValid(u3d))
				{
					u3d.Velocity = Vector3.Zero;
				}
			}
			return;
		}

		string unitId = "worker";
		if (_ecsWorld.Has<Unit3D>(entity))
		{
			unitId = _ecsWorld.Get<Unit3D>(entity).UnitId;
		}
		int includeFlags = 8;
		if (GameHost.UnitRegistry.TryGetValue(unitId, out var meta))
		{
			includeFlags = GameHost.GetUnitPathingFlags(meta);
		}

		ushort pathingFlags = (ushort)includeFlags;

		PathFollow pf;
		bool hasPf = _ecsWorld.Has<PathFollow>(entity);
		if (hasPf)
		{
			pf = _ecsWorld.Get<PathFollow>(entity);
		}
		else
		{
			pf = new PathFollow { WaypointCount = 0, CurrentWaypointIndex = 0, Target = moveTo.Target };
		}

		if (pf.Target != moveTo.Target || pf.WaypointCount == 0)
		{
			_pathfinder.ComputePath(_groundTerrainRef?.NavMeshQuery!, new System.Numerics.Vector3(pos.Value.X, pos.Value.Y, pos.Value.Z), moveTo.Target, pathingFlags, ref pf);
		}
		var current = new Vector3(pos.Value.X, pos.Value.Y, pos.Value.Z);
		var target = new Vector3(moveTo.Target.X, moveTo.Target.Y, moveTo.Target.Z);
		if (pf.CurrentWaypointIndex < pf.WaypointCount)
		{
			var wp = pf.Waypoints[pf.CurrentWaypointIndex];
			target = new Vector3(wp.X, wp.Y, wp.Z);
		}
		float dist = current.DistanceTo(target);
		if (dist < 0.2f)
		{
			pf.CurrentWaypointIndex++;
			if (pf.CurrentWaypointIndex < pf.WaypointCount)
			{
				var nextWp = pf.Waypoints[pf.CurrentWaypointIndex];
				target = new Vector3(nextWp.X, nextWp.Y, nextWp.Z);
				dist = current.DistanceTo(target);
			}
		}
		if (pf.CurrentWaypointIndex >= pf.WaypointCount)
		{
			_tickArrivedUnits.Add(entity);
			if (_ecsWorld.Has<Unit3D>(entity))
			{
				var unit3D = _ecsWorld.Get<Unit3D>(entity);
				unit3D.Velocity = Vector3.Zero;
			}
		}
		else
		{
			Vector3 dir = (target - current).Normalized();
			Vector3 velocity = dir * stats.Speed;
			if (_ecsWorld.Has<Unit3D>(entity))
			{
				var unit3D = _ecsWorld.Get<Unit3D>(entity);
				var nextPos = current + dir * stats.Speed * _fDelta;
				float r1 = unit3D.Scale.X * 1.2f;
				if (unit3D.UnitId == "castle") r1 = unit3D.Scale.X * 5.0f;
				else if (unit3D.UnitId == "tower") r1 = unit3D.Scale.X * 2.5f;

				int baseCx = (int)Math.Floor(nextPos.X / CollisionCellSize);
				int baseCz = (int)Math.Floor(nextPos.Z / CollisionCellSize);

				for (int dx = -1; dx <= 1; dx++)
				{
					for (int dz = -1; dz <= 1; dz++)
					{
						long key = ((long)(baseCx + dx) << 32) | (uint)(baseCz + dz);
						if (_unitGrid.TryGetValue(key, out var list))
						{
							foreach (var other in list)
							{
								if (other == unit3D || !GodotObject.IsInstanceValid(other)) continue;
								if (_ecsWorld.Has<Dead>(other.Entity)) continue;

								float r2 = other.Scale.X * 1.2f;
								if (other.UnitId == "castle") r2 = other.Scale.X * 5.0f;
								else if (other.UnitId == "tower") r2 = other.Scale.X * 2.5f;

								float minDist = (r1 + r2) * 0.85f;
								float ox = nextPos.X - other.GlobalPosition.X;
								float oz = nextPos.Z - other.GlobalPosition.Z;
								float distSq = ox * ox + oz * oz;
								if (distSq < minDist * minDist)
								{
									float otherDist = Mathf.Sqrt(distSq);
									Vector3 pushDir;
									if (otherDist < 0.001f)
									{
										pushDir = new Vector3(1f, 0f, 0f);
										otherDist = 1f;
									}
									else
									{
										pushDir = new Vector3(ox / otherDist, 0f, oz / otherDist);
									}
									float overlap = minDist - otherDist;
									nextPos += pushDir * overlap;
								}
							}
						}
					}
				}

				for (int dx = -1; dx <= 1; dx++)
				{
					for (int dz = -1; dz <= 1; dz++)
					{
						long key = ((long)(baseCx + dx) << 32) | (uint)(baseCz + dz);
						if (_propGrid.TryGetValue(key, out var list))
						{
							foreach (var prop in list)
							{
								if (!GodotObject.IsInstanceValid(prop)) continue;

								float r2 = prop.Scale.X * 1.5f;
								if (prop.PropId == "goldmine") r2 = prop.Scale.X * 4.0f;

								float minDist = (r1 + r2) * 0.85f;
								float ox = nextPos.X - prop.GlobalPosition.X;
								float oz = nextPos.Z - prop.GlobalPosition.Z;
								float distSq = ox * ox + oz * oz;
								if (distSq < minDist * minDist)
								{
									float otherDist = Mathf.Sqrt(distSq);
									Vector3 pushDir;
									if (otherDist < 0.001f)
									{
										pushDir = new Vector3(1f, 0f, 0f);
										otherDist = 1f;
									}
									else
									{
										pushDir = new Vector3(ox / otherDist, 0f, oz / otherDist);
									}
									float overlap = minDist - otherDist;
									nextPos += pushDir * overlap;
								}
							}
						}
					}
				}
				float groundHeight = nextPos.Y;
				Vector3 normal = Vector3.Up;
				if (_groundTerrainRef != null)
				{
					_groundTerrainRef.GetHeightAndNormal(nextPos.X, nextPos.Z, out groundHeight, out normal);
				}
				nextPos.Y = groundHeight;
				unit3D.Velocity = velocity;
				unit3D.GlobalPosition = nextPos;
				pos.Value = new System.Numerics.Vector3(nextPos.X, nextPos.Y, nextPos.Z);
				if (dir.LengthSquared() > 0.01f)
				{
					float angle = Mathf.Atan2(-dir.X, -dir.Z);
					var rot = unit3D.Rotation;
					rot.Y = Mathf.LerpAngle(rot.Y, angle, 10f * _fDelta);
					unit3D.Rotation = rot;
					Vector3 forwardDir = new Vector3(-Mathf.Sin(unit3D.Rotation.Y), 0f, -Mathf.Cos(unit3D.Rotation.Y));
					Vector3 up = normal.Normalized();
					Vector3 right = forwardDir.Cross(up).Normalized();
					Vector3 forwardPerp = right.Cross(up).Normalized();
					Basis targetBasis = new Basis(right, up, forwardPerp);
					var qTarget = targetBasis.GetRotationQuaternion();
					var qCurrent = unit3D.Basis.GetRotationQuaternion();
					var qLerp = qCurrent.Slerp(qTarget, 10f * _fDelta);
					unit3D.Basis = new Basis(qLerp);
				}
			}
			else
			{
				var nextPos = current + dir * stats.Speed * _fDelta;
				if (_groundTerrainRef != null && _groundTerrainRef.NavMeshQuery != null)
				{
					var nextRc = new RcVec3f(nextPos.X, nextPos.Y, nextPos.Z);
					_groundTerrainRef.NavMeshQuery.FindNearestPoly(nextRc, NavMeshPathfinder.PathfindingExtents, _pathfinder.Filter, out long nearestRef, out var nearestPt, out _);
					if (nearestRef != 0)
					{
						nextPos = new Vector3(nearestPt.X, nearestPt.Y, nearestPt.Z);
					}
				}
				float groundHeight = nextPos.Y;
				if (_groundTerrainRef != null)
				{
					_groundTerrainRef.GetHeightAndNormal(nextPos.X, nextPos.Z, out groundHeight, out _);
				}
				nextPos.Y = groundHeight;
				pos.Value = new System.Numerics.Vector3(nextPos.X, nextPos.Y, nextPos.Z);
			}
		}

		if (hasPf)
		{
			_ecsWorld.Set(entity, pf);
		}
		else
		{
			_tickAddPathFollow.Add((entity, pf));
		}
	}

	private void UpdatePassiveIncomeQueryAction(Entity ent, ref PlayerResources res)
	{
		float goldPerSec = 1.5f;
		float woodPerSec = 1.0f;
		float stonePerSec = 0.8f;

		var playerEntityForUpgrade = _ecsWorld.Get<NetworkMappingState>(_worldEntity).PlayerEntity;
		if (ent == playerEntityForUpgrade && GetHarvestingUpgrade())
		{
			goldPerSec *= 1.5f;
			woodPerSec *= 1.5f;
			stonePerSec *= 1.5f;
		}

		if (res.Value.ContainsKey(_goldResourceId)) res.Value[_goldResourceId] = (int)Math.Min(GameHost.ResourceCap, res.Value[_goldResourceId] + _fDelta * goldPerSec);
		if (res.Value.ContainsKey(_woodResourceId)) res.Value[_woodResourceId] = (int)Math.Min(GameHost.ResourceCap, res.Value[_woodResourceId] + _fDelta * woodPerSec);
		if (res.Value.ContainsKey(_stoneResourceId)) res.Value[_stoneResourceId] = (int)Math.Min(GameHost.ResourceCap, res.Value[_stoneResourceId] + _fDelta * stonePerSec);
	}

	private void AttackCooldownQueryAction(Entity entity, ref Attack atk)
	{
		if (atk.CurrentCooldown > 0)
		{
			atk.CurrentCooldown = Math.Max(0, atk.CurrentCooldown - _fDelta);
		}
	}

	private void SpellCooldownQueryAction(Entity entity, ref SpellCooldowns cd)
	{
		float fCo = cd.FireballCooldown > 0 ? Math.Max(0, cd.FireballCooldown - _fDelta) : 0f;
		float lCo = cd.LightningCooldown > 0 ? Math.Max(0, cd.LightningCooldown - _fDelta) : 0f;
		float hCo = cd.HolyLightCooldown > 0 ? Math.Max(0, cd.HolyLightCooldown - _fDelta) : 0f;
		cd = new SpellCooldowns(fCo, lCo, hCo);
	}

	private void ScanEnemyQueryAction(Entity potentialEnemy, ref Position enemyPosComp, ref Owner enemyOwnerComp)
	{
		if (_scanAttackerOwner != enemyOwnerComp.PlayerEntity)
		{
			if (!_scanIsAttackerEnemy && _ecsWorld.Has<Unit3D>(potentialEnemy))
			{
				var enemyUnit3D = _ecsWorld.Get<Unit3D>(potentialEnemy);
				if (enemyUnit3D != null && !enemyUnit3D.Visible) return;
			}
			var enemyPos = new Vector3(enemyPosComp.Value.X, enemyPosComp.Value.Y, enemyPosComp.Value.Z);
			float dist = _scanAttackerPos.DistanceTo(enemyPos);
			if (dist < _scanClosestDist)
			{
				_scanClosestDist = dist;
				_scanClosestEnemy = potentialEnemy;
			}
		}
	}

	private void TargetAcquisitionQueryAction(Entity entity, ref Position pos, ref Attack atk, ref Owner owner)
	{
		if (_ecsWorld.Has<DefinitionId>(entity) && _ecsWorld.Get<DefinitionId>(entity).Value == "priest")
		{
			return;
		}

		bool isAttackMove = _ecsWorld.Has<Realm.Ecs.Components.Movement.AttackMove>(entity);
		bool isPatrol     = _ecsWorld.Has<Patrol>(entity);
		bool isIdle = !_ecsWorld.Has<MoveTo>(entity) && !isAttackMove;

		if (isIdle || isAttackMove || isPatrol)
		{
			float scanRadius = 15.0f;
			if (_ecsWorld.Has<DefinitionId>(entity))
			{
				string defId = _ecsWorld.Get<DefinitionId>(entity).Value;
				if (GameHost.UnitRegistry.TryGetValue(defId, out var metaReg) && metaReg.ScanRadius > 0)
					scanRadius = metaReg.ScanRadius;
			}

			if (GetTimeOfDayIndex() == 2)
			{
				scanRadius *= 0.7f;
			}

			_scanAttackerEntity = entity;
			_scanAttackerPos = new Vector3(pos.Value.X, pos.Value.Y, pos.Value.Z);
			_scanAttackerOwner = owner.PlayerEntity;
			_scanIsAttackerEnemy = false;
			if (_ecsWorld.Has<Unit3D>(entity))
			{
				_scanIsAttackerEnemy = _ecsWorld.Get<Unit3D>(entity).IsEnemy;
			}
			_scanClosestDist = scanRadius;
			_scanClosestEnemy = Entity.Null;

			_ecsWorld.Query(in _enemyQuery, _potentialEnemyQueryDelegate);

			if (_scanClosestEnemy != Entity.Null)
			{
				_tickNewAttackTargets.Add((entity, new AttackTarget(_scanClosestEnemy)));
			}
		}
	}

	private void CombatQueryAction(Entity entity, ref Position pos, ref Attack atk, ref AttackTarget target, ref Owner owner)
	{
		if (!_ecsWorld.IsAlive(target.Target) || _ecsWorld.Has<Dead>(target.Target))
		{
			_tickActionsToRemoveTarget.Add(entity);
			return;
		}

		var targetPosComp = _ecsWorld.Get<Position>(target.Target);
		var currentPos = new Vector3(pos.Value.X, pos.Value.Y, pos.Value.Z);
		var targetPos = new Vector3(targetPosComp.Value.X, targetPosComp.Value.Y, targetPosComp.Value.Z);

		float dist = currentPos.DistanceTo(targetPos);
		if (dist <= atk.Range)
		{
			_tickActionsToStopChasing.Add(entity);

			if (_ecsWorld.Has<Unit3D>(entity))
			{
				var unit3D = _ecsWorld.Get<Unit3D>(entity);
				Vector3 dir = (targetPos - currentPos).Normalized();
				if (dir.LengthSquared() > 0.01f)
				{
					float angle = Mathf.Atan2(-dir.X, -dir.Z);
					var rot = unit3D.Rotation;
					rot.Y = Mathf.LerpAngle(rot.Y, angle, 10f * _fDelta);
					unit3D.Rotation = rot;
				}
			}

			if (atk.CurrentCooldown <= 0)
			{
				if (_ecsWorld.Has<Realm.Ecs.Components.Tags.Invulnerable>(target.Target))
				{
					atk.CurrentCooldown = atk.Cooldown;
					return;
				}

				var targetHealth = _ecsWorld.Get<Health>(target.Target);
				var targetArmor = _ecsWorld.Has<Armor>(target.Target) ? _ecsWorld.Get<Armor>(target.Target) : new Armor(0);

				float damage = atk.Damage - targetArmor.Value;
				if (damage < 1f) damage = 1f;

				if (_ecsWorld.Has<LastAttacker>(target.Target))
				{
					_ecsWorld.Set(target.Target, new LastAttacker(entity));
				}
				else
				{
					_ecsWorld.Add(target.Target, new LastAttacker(entity));
				}

				OnUnitDamagedCallback?.Invoke(
					_ecsWorld.Has<Unit3D>(target.Target) ? _ecsWorld.Get<Unit3D>(target.Target) : null,
					_ecsWorld.Has<Unit3D>(entity) ? _ecsWorld.Get<Unit3D>(entity) : null,
					damage);

				float newHp = Math.Max(0, targetHealth.Current - damage);
				_ecsWorld.Set(target.Target, new Health(newHp, targetHealth.Max));

				if (_ecsWorld.Has<Unit3D>(target.Target))
				{
					var targetUnit3D_alert = _ecsWorld.Get<Unit3D>(target.Target);
					if (!targetUnit3D_alert.IsEnemy)
					{
						float currentTimer = GetCombatAlertTimer();
						if (currentTimer <= 0f)
						{
							SetCombatAlertTimer(UnderAttackAlertCooldown);
							OnUnderAttackAlertRequested?.Invoke(targetUnit3D_alert.UnitId);
						}
					}
				}

				if (_ecsWorld.IsAlive(target.Target) && !_ecsWorld.Has<Dead>(target.Target) && !_ecsWorld.Has<AttackTarget>(target.Target))
				{
					if (_ecsWorld.Has<Attack>(target.Target))
					{
						bool hasMoveTo = _ecsWorld.Has<MoveTo>(target.Target);
						if (!hasMoveTo || _ecsWorld.Has<Realm.Ecs.Components.Movement.AttackMove>(target.Target))
						{
							_tickNewAttackTargets.Add((target.Target, new AttackTarget(entity)));
						}
					}
				}

				atk.CurrentCooldown = atk.Cooldown;

				if (_ecsWorld.Has<Unit3D>(target.Target))
				{
					var target3D = _ecsWorld.Get<Unit3D>(target.Target);

					if (atk.Range > 3f && _ecsWorld.Has<Unit3D>(entity))
					{
						var attacker3D = _ecsWorld.Get<Unit3D>(entity);
						OnArrowProjectileRequested?.Invoke(attacker3D.GlobalPosition, target3D.GlobalPosition);
					}

					if (newHp <= 0)
					{
						_tickUnitsToKill.Add((target.Target, target3D));
					}
					else
					{
						OnDamageFlashRequested?.Invoke(target3D);
					}
				}
			}
		}
		else
		{
			if (!_ecsWorld.Has<Realm.Ecs.Components.Movement.HoldPosition>(entity) && _ecsWorld.Has<Realm.Ecs.Components.Tags.Movable>(entity) && !_ecsWorld.Has<Building>(entity))
			{
				_tickActionsToChase.Add((entity, targetPos));
			}
			else
			{
				_tickActionsToRemoveTarget.Add(entity);
			}
		}
	}

	private void ScanFriendlyQueryAction(Entity potentialFriendly, ref Position fPosComp, ref Health fHealth, ref Owner fOwner)
	{
		if (fOwner.PlayerEntity == _scanFriendlyOwner && fHealth.Current < fHealth.Max)
		{
			var fPos = new Vector3(fPosComp.Value.X, fPosComp.Value.Y, fPosComp.Value.Z);
			float dist = _scanPriestPos.DistanceTo(fPos);
			if (dist < _scanFriendlyClosestDist)
			{
				_scanFriendlyClosestDist = dist;
				_scanClosestDamagedFriendly = potentialFriendly;
			}
		}
	}

	private void PriestScanQueryAction(Entity entity, ref Position pos, ref Owner owner, ref DefinitionId defId)
	{
		if (defId.Value == "priest")
		{
			bool isIdle = !_ecsWorld.Has<MoveTo>(entity);
			if (isIdle)
			{
				_scanClosestDamagedFriendly = Entity.Null;
				_scanFriendlyClosestDist = 15.0f;
				_scanPriestPos = new Vector3(pos.Value.X, pos.Value.Y, pos.Value.Z);
				_scanFriendlyOwner = owner.PlayerEntity;

				_ecsWorld.Query(in _friendlyScanQuery, _friendlyScanQueryDelegate);

				if (_scanClosestDamagedFriendly != Entity.Null)
				{
					_tickNewHealingTargets.Add((entity, new HealingTarget(_scanClosestDamagedFriendly)));
				}
			}
		}
	}

	private void HealingExecutionQueryAction(Entity entity, ref Position pos, ref Attack atk, ref HealingTarget target, ref Owner owner)
	{
		if (!_ecsWorld.IsAlive(target.Target) || _ecsWorld.Has<Dead>(target.Target))
		{
			_tickHealRemoveTargets.Add(entity);
			return;
		}

		var targetHealth = _ecsWorld.Get<Health>(target.Target);
		if (targetHealth.Current >= targetHealth.Max)
		{
			_tickHealRemoveTargets.Add(entity);
			return;
		}

		var targetPosComp = _ecsWorld.Get<Position>(target.Target);
		var currentPos = new Vector3(pos.Value.X, pos.Value.Y, pos.Value.Z);
		var targetPos = new Vector3(targetPosComp.Value.X, targetPosComp.Value.Y, targetPosComp.Value.Z);

		float dist = currentPos.DistanceTo(targetPos);
		if (dist <= atk.Range)
		{
			_tickHealStopChasing.Add(entity);

			if (_ecsWorld.Has<Unit3D>(entity))
			{
				var unit3D = _ecsWorld.Get<Unit3D>(entity);
				Vector3 dir = (targetPos - currentPos).Normalized();
				if (dir.LengthSquared() > 0.01f)
				{
					float angle = Mathf.Atan2(-dir.X, -dir.Z);
					var rot = unit3D.Rotation;
					rot.Y = Mathf.LerpAngle(rot.Y, angle, 10f * _fDelta);
					unit3D.Rotation = rot;
				}
			}

			if (atk.CurrentCooldown <= 0)
			{
				float healAmount = atk.Damage;
				float newHp = Math.Min(targetHealth.Max, targetHealth.Current + healAmount);
				_ecsWorld.Set(target.Target, new Health(newHp, targetHealth.Max));

				atk.CurrentCooldown = atk.Cooldown;

				if (_ecsWorld.Has<Unit3D>(target.Target))
				{
					var target3D = _ecsWorld.Get<Unit3D>(target.Target);
					var priest3D = _ecsWorld.Get<Unit3D>(entity);

					OnHealEffectRequested?.Invoke(priest3D.GlobalPosition, target3D.GlobalPosition);
					OnHealFlashRequested?.Invoke(target3D);
				}
			}
		}
		else
		{
			if (!_ecsWorld.Has<Realm.Ecs.Components.Movement.HoldPosition>(entity) && _ecsWorld.Has<Realm.Ecs.Components.Tags.Movable>(entity))
			{
				_tickHealChaseTargets.Add((entity, targetPos));
			}
		}
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

	private void ProcessMapEditorPhysicsQueryAction(Entity entity, ref Position pos, ref MoveTo moveTo, ref MovementStats stats)
	{
		if (_ecsWorld.Has<Realm.Ecs.Components.Core.Buffs>(entity) && _ecsWorld.Get<Realm.Ecs.Components.Core.Buffs>(entity).Value.ContainsKey("stun"))
		{
			if (_ecsWorld.Has<Unit3D>(entity))
			{
				var u3d = _ecsWorld.Get<Unit3D>(entity);
				if (GodotObject.IsInstanceValid(u3d))
				{
					u3d.Velocity = Vector3.Zero;
				}
			}
			return;
		}
		var current = new Vector3(pos.Value.X, pos.Value.Y, pos.Value.Z);
		var target = new Vector3(moveTo.Target.X, moveTo.Target.Y, moveTo.Target.Z);

		float dist = current.DistanceTo(target);
		if (dist < 0.2f)
		{
			_tickArrivedUnits.Add(entity);
			if (_ecsWorld.Has<Unit3D>(entity))
			{
				var unit3D = _ecsWorld.Get<Unit3D>(entity);
				unit3D.Velocity = Vector3.Zero;
			}
		}
		else
		{
			Vector3 dir = (target - current).Normalized();
			Vector3 velocity = dir * stats.Speed;

			if (_ecsWorld.Has<Unit3D>(entity))
			{
				var unit3D = _ecsWorld.Get<Unit3D>(entity);
				unit3D.Velocity = velocity;
				unit3D.MoveAndSlide();

				var finalPos = unit3D.GlobalPosition;
				pos.Value = new System.Numerics.Vector3(finalPos.X, finalPos.Y, finalPos.Z);

				if (unit3D.Velocity.LengthSquared() > 0.01f)
				{
					float angle = Mathf.Atan2(-unit3D.Velocity.X, -unit3D.Velocity.Z);
					var rot = unit3D.Rotation;
					rot.Y = Mathf.LerpAngle(rot.Y, angle, 10f * _fDelta);
					unit3D.Rotation = rot;
				}
			}
			else
			{
				var nextPos = current + dir * stats.Speed * _fDelta;
				pos.Value = new System.Numerics.Vector3(nextPos.X, nextPos.Y, nextPos.Z);
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

	public Action<string, float> OnResourceDepositedForPlayer;
	public Action<string> OnProductionCompleted;

	private Prop3D FindNearbyResourceNode(Vector3 pos, string type, float radius)
	{
		Prop3D closest = null;
		float closestDist = radius;
		foreach (var prop in _allPropsRef)
		{
			if (GodotObject.IsInstanceValid(prop))
			{
				string pType = prop.PropId switch
				{
					"goldmine" => "gold",
					"tree" => "wood",
					"rock" => "stone",
					_ => null
				};

				if (pType == type)
				{
					float d = pos.DistanceTo(prop.GlobalPosition);
					if (d < closestDist)
					{
						closestDist = d;
						closest = prop;
					}
				}
			}
		}
		return closest;
	}

	private bool GetHarvestingUpgrade()
	{
		var playerEntity = _ecsWorld.Get<NetworkMappingState>(_worldEntity).PlayerEntity;
		if (_ecsWorld.IsAlive(playerEntity) && _ecsWorld.Has<PlayerUpgrades>(playerEntity))
		{
			return _ecsWorld.Get<PlayerUpgrades>(playerEntity).HarvestingUpgrade;
		}
		return false;
	}

	private int GetTimeOfDayIndex()
	{
		if (_ecsWorld.IsAlive(_worldEntity) && _ecsWorld.Has<WorldState>(_worldEntity))
			return _ecsWorld.Get<WorldState>(_worldEntity).TimeOfDayIndex;
		return 0;
	}

	private float GetGameElapsedTime()
	{
		if (_ecsWorld.IsAlive(_worldEntity) && _ecsWorld.Has<WorldState>(_worldEntity))
			return _ecsWorld.Get<WorldState>(_worldEntity).GameElapsedTime;
		return 0f;
	}

	private float GetDynamicInterpolationFactor()
	{
		if (_ecsWorld.IsAlive(_worldEntity) && _ecsWorld.Has<NetworkState>(_worldEntity))
			return _ecsWorld.Get<NetworkState>(_worldEntity).DynamicInterpolationFactor;
		return 10f;
	}

	private float GetCombatAlertTimer()
	{
		if (_ecsWorld.IsAlive(_worldEntity) && _ecsWorld.Has<CombatAlertState>(_worldEntity))
			return _ecsWorld.Get<CombatAlertState>(_worldEntity).UnderAttackAlertTimer;
		return 0f;
	}

	private void SetCombatAlertTimer(float value)
	{
		if (_ecsWorld.IsAlive(_worldEntity) && _ecsWorld.Has<CombatAlertState>(_worldEntity))
		{
			_ecsWorld.Set(_worldEntity, new CombatAlertState(value));
		}
	}

	public List<Entity> GetEditorArrivedUnits() => _tickArrivedUnits;
}
