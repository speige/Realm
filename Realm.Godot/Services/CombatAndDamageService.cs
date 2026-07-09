using Arch.Core;
using Realm.Ecs.Services;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using System;
using System.Collections.Generic;

internal class CombatAndDamageService
{
	private readonly WorldAccessor EcsWorldAccessor;
	private World EcsWorld => EcsWorldAccessor.Current;

	private const float UnderAttackAlertCooldown = 8f;

	private System.Numerics.Vector3 _scanAttackerPos;
	private PlayerEntity _scanAttackerOwner;
	private bool _scanIsAttackerEnemy;
	private float _scanClosestDist;
	private Entity _scanClosestEnemy;

	private System.Numerics.Vector3 _scanPriestPos;
	private PlayerEntity _scanFriendlyOwner;
	private float _scanFriendlyClosestDist;
	private Entity _scanClosestDamagedFriendly;

	private readonly List<(Entity Attacker, AttackTarget Target)> _tickNewAttackTargets = new();
	private readonly List<Entity> _tickActionsToRemoveTarget = new();
	private readonly List<(Entity Attacker, System.Numerics.Vector3 TargetPos)> _tickActionsToChase = new();
	private readonly List<Entity> _tickActionsToStopChasing = new();
	private readonly List<Entity> _tickUnitsToKill = new();
	private readonly List<(Entity Priest, HealingTarget Target)> _tickNewHealingTargets = new();
	private readonly List<Entity> _tickHealRemoveTargets = new();
	private readonly List<(Entity Priest, System.Numerics.Vector3 TargetPos)> _tickHealChaseTargets = new();
	private readonly List<Entity> _tickHealStopChasing = new();

	private readonly QueryDescription _enemyQuery = QueryCache.AllPositionAndOwnerNoneDeadQuery;
	private readonly QueryDescription _friendlyScanQuery = QueryCache.AllPositionAndHealthAndOwnerNoneDeadQuery;
	private readonly QueryDescription _targetAcquisitionQuery = QueryCache.AllPositionAndAttackAndOwnerNoneAttackTargetAndDeadQuery;
	private readonly QueryDescription _combatQuery = QueryCache.AllPositionAndAttackAndAttackTargetAndOwnerNoneDeadQuery;
	private readonly QueryDescription _priestScanQuery = QueryCache.AllPositionAndOwnerAndDefinitionIdNoneDeadAndHealingTargetQuery;
	private readonly QueryDescription _healingExecutionQuery = QueryCache.AllPositionAndAttackAndHealingTargetAndOwnerNoneDeadQuery;

	private ForEachWithEntity<Position, Attack, Owner> _targetAcquisitionQueryDelegate;
	private ForEachWithEntity<Position, Owner> _potentialEnemyQueryDelegate;
	private ForEachWithEntity<Position, Attack, AttackTarget, Owner> _combatQueryDelegate;
	private ForEachWithEntity<Position, Owner, DefinitionId> _priestScanQueryDelegate;
	private ForEachWithEntity<Position, Health, Owner> _friendlyScanQueryDelegate;
	private ForEachWithEntity<Position, Attack, HealingTarget, Owner> _healingExecutionQueryDelegate;

	public Action<System.Numerics.Vector3, System.Numerics.Vector3> OnArrowProjectileRequested;
	public Action<Entity> OnDamageFlashRequested;
	public Action<System.Numerics.Vector3, System.Numerics.Vector3> OnHealEffectRequested;
	public Action<Entity> OnHealFlashRequested;
	public Action<Entity, Entity, float> OnUnitDamagedCallback;
	public Action<Entity, Entity>? OnUnitAttackedCallback;
	public Action<string> OnUnderAttackAlertRequested;
	public Action<Entity> OnKillUnitRequested;

	public CombatAndDamageService(WorldAccessor ecsWorldAccessor)
	{
		EcsWorldAccessor = ecsWorldAccessor;
		_targetAcquisitionQueryDelegate = TargetAcquisitionQueryAction;
		_potentialEnemyQueryDelegate = ScanEnemyQueryAction;
		_combatQueryDelegate = CombatQueryAction;
		_priestScanQueryDelegate = PriestScanQueryAction;
		_friendlyScanQueryDelegate = ScanFriendlyQueryAction;
		_healingExecutionQueryDelegate = HealingExecutionQueryAction;
	}

	public void StepCombat(float delta)
	{
		TickCombatAlertTimer(delta);

		ProcessTargetAcquisition();
		ProcessCombatTicks();
		ProcessHealingTicks();
	}

	private Entity FindWorldEntity()
	{
		Entity worldEntity = Entity.Null;
		var query = Realm.Ecs.Common.QueryCache.AllWorldStateQuery;
		EcsWorld.Query(in query, (Entity entity) => worldEntity = entity);
		return worldEntity;
	}

	private int GetTimeOfDayIndex()
	{
		var worldEntity = FindWorldEntity();
		if (worldEntity != Entity.Null && EcsWorld.Has<WorldState>(worldEntity))
		{
			return EcsWorld.Get<WorldState>(worldEntity).TimeOfDayIndex;
		}
		return 0;
	}

	private float GetCombatAlertTimer()
	{
		Entity worldEntity = Entity.Null;
		var query = Realm.Ecs.Common.QueryCache.AllCombatAlertStateQuery;
		EcsWorld.Query(in query, (Entity entity) => worldEntity = entity);

		if (worldEntity != Entity.Null && EcsWorld.Has<CombatAlertState>(worldEntity))
		{
			return EcsWorld.Get<CombatAlertState>(worldEntity).UnderAttackAlertTimer;
		}
		return 0f;
	}

	private void SetCombatAlertTimer(float value)
	{
		Entity worldEntity = Entity.Null;
		var query = Realm.Ecs.Common.QueryCache.AllCombatAlertStateQuery;
		EcsWorld.Query(in query, (Entity entity) => worldEntity = entity);

		if (worldEntity != Entity.Null && EcsWorld.Has<CombatAlertState>(worldEntity))
		{
			ref var state = ref EcsWorld.Get<CombatAlertState>(worldEntity);
			state.UnderAttackAlertTimer = value;
		}
	}

	private void TickCombatAlertTimer(float fDelta)
	{
		Entity worldEntity = Entity.Null;
		var query = Realm.Ecs.Common.QueryCache.AllCombatAlertStateQuery;
		EcsWorld.Query(in query, (Entity entity) => worldEntity = entity);

		if (worldEntity != Entity.Null && EcsWorld.Has<CombatAlertState>(worldEntity))
		{
			ref var state = ref EcsWorld.Get<CombatAlertState>(worldEntity);
			if (state.UnderAttackAlertTimer > 0f)
			{
				state.UnderAttackAlertTimer = Math.Max(0f, state.UnderAttackAlertTimer - fDelta);
			}
		}
	}

	private void ProcessTargetAcquisition()
	{
		_tickNewAttackTargets.Clear();
		EcsWorld.Query(in _targetAcquisitionQuery, _targetAcquisitionQueryDelegate);
		foreach (var (attacker, target) in _tickNewAttackTargets)
		{
			if (EcsWorld.IsAlive(attacker))
			{
				if (EcsWorld.Has<AttackTarget>(attacker))
					EcsWorld.Set(attacker, target);
				else
					EcsWorld.Add(attacker, target);
			}
		}
	}

	private void TargetAcquisitionQueryAction(Entity entity, ref Position pos, ref Attack atk, ref Owner owner)
	{
		if (EcsWorld.Has<DefinitionId>(entity) && EcsWorld.Get<DefinitionId>(entity).Value == "priest")
		{
			return;
		}

		bool isAttackMove = EcsWorld.Has<Realm.Ecs.Components.Movement.AttackMove>(entity);
		bool isPatrol     = EcsWorld.Has<Patrol>(entity);
		bool isIdle = !EcsWorld.Has<MoveTo>(entity) && !isAttackMove;

		if (isIdle || isAttackMove || isPatrol)
		{
			float scanRadius = EcsWorld.Has<ScanRadius>(entity)
				? EcsWorld.Get<ScanRadius>(entity).Value
				: 15.0f;

			if (GetTimeOfDayIndex() == 2)
			{
				scanRadius *= 0.7f;
			}

			_scanAttackerPos = pos.Value;
			_scanAttackerOwner = owner.PlayerEntity;
			_scanIsAttackerEnemy = false;
			if (EcsWorld.Has<UnitFaction>(entity))
			{
				_scanIsAttackerEnemy = EcsWorld.Get<UnitFaction>(entity).IsEnemy;
			}
			_scanClosestDist = scanRadius;
			_scanClosestEnemy = Entity.Null;

			EcsWorld.Query(in _enemyQuery, _potentialEnemyQueryDelegate);

			if (_scanClosestEnemy != Entity.Null)
			{
				_tickNewAttackTargets.Add((entity, new AttackTarget(_scanClosestEnemy)));
			}
		}
	}

	private void ScanEnemyQueryAction(Entity potentialEnemy, ref Position enemyPos, ref Owner enemyOwner)
	{
		if (enemyOwner.PlayerEntity != _scanAttackerOwner)
		{
			bool isEnemyEntity = EcsWorld.Has<UnitFaction>(potentialEnemy) && EcsWorld.Get<UnitFaction>(potentialEnemy).IsEnemy;
			if (isEnemyEntity != _scanIsAttackerEnemy)
			{
				float dist = System.Numerics.Vector3.Distance(_scanAttackerPos, enemyPos.Value);
				if (dist < _scanClosestDist)
				{
					_scanClosestDist = dist;
					_scanClosestEnemy = potentialEnemy;
				}
			}
		}
	}

	private void ProcessCombatTicks()
	{
		_tickActionsToRemoveTarget.Clear();
		_tickActionsToChase.Clear();
		_tickActionsToStopChasing.Clear();
		_tickUnitsToKill.Clear();

		EcsWorld.Query(in _combatQuery, _combatQueryDelegate);

		foreach (var targetEntity in _tickUnitsToKill)
		{
			if (EcsWorld.IsAlive(targetEntity))
			{
				if (!EcsWorld.Has<Dead>(targetEntity))
				{
					EcsWorld.Add<Dead>(targetEntity);
					OnKillUnitRequested?.Invoke(targetEntity);
				}
			}
		}

		foreach (var ent in _tickActionsToRemoveTarget)
		{
			if (EcsWorld.IsAlive(ent))
			{
				if (EcsWorld.Has<AttackTarget>(ent))
				{
					EcsWorld.Remove<AttackTarget>(ent);
				}

				if (EcsWorld.Has<Realm.Ecs.Components.Movement.AttackMove>(ent))
				{
					var am = EcsWorld.Get<Realm.Ecs.Components.Movement.AttackMove>(ent);
					var moveTo = new MoveTo(am.Target);
					if (EcsWorld.Has<MoveTo>(ent))
						EcsWorld.Set(ent, moveTo);
					else
						EcsWorld.Add(ent, moveTo);
				}
				else if (EcsWorld.Has<Patrol>(ent))
				{
					var patrol = EcsWorld.Get<Patrol>(ent);
					var destVec = patrol.GoingToB ? patrol.PointB : patrol.PointA;
					var moveTo = new MoveTo(destVec);
					if (EcsWorld.Has<MoveTo>(ent))
						EcsWorld.Set(ent, moveTo);
					else
						EcsWorld.Add(ent, moveTo);
				}
			}
		}

		foreach (var (attacker, targetPos) in _tickActionsToChase)
		{
			if (EcsWorld.IsAlive(attacker))
			{
				var moveTo = new MoveTo(targetPos);
				if (EcsWorld.Has<MoveTo>(attacker))
					EcsWorld.Set(attacker, moveTo);
				else
					EcsWorld.Add(attacker, moveTo);
			}
		}

		foreach (var attacker in _tickActionsToStopChasing)
		{
			if (EcsWorld.IsAlive(attacker))
			{
				if (EcsWorld.Has<MoveTo>(attacker))
				{
					EcsWorld.Remove<MoveTo>(attacker);
				}
				if (EcsWorld.Has<Velocity>(attacker))
				{
					EcsWorld.Set(attacker, new Velocity(System.Numerics.Vector3.Zero));
				}
			}
		}
	}

	private void CombatQueryAction(Entity entity, ref Position pos, ref Attack atk, ref AttackTarget target, ref Owner owner)
	{
		if (!EcsWorld.IsAlive(target.Target) || EcsWorld.Has<Dead>(target.Target))
		{
			_tickActionsToRemoveTarget.Add(entity);
			return;
		}

		var targetPosComp = EcsWorld.Get<Position>(target.Target);
		var currentPos = pos.Value;
		var targetPos = targetPosComp.Value;

		float dist = System.Numerics.Vector3.Distance(currentPos, targetPos);
		if (dist <= atk.Range)
		{
			_tickActionsToStopChasing.Add(entity);

			if (atk.CurrentCooldown <= 0)
			{
				if (EcsWorld.Has<Realm.Ecs.Components.Tags.Invulnerable>(target.Target))
				{
					atk.CurrentCooldown = atk.Cooldown;
					return;
				}

				var targetHealth = EcsWorld.Get<Health>(target.Target);
				var targetArmor = EcsWorld.Has<Armor>(target.Target) ? EcsWorld.Get<Armor>(target.Target) : new Armor(0);

				float damage = atk.Damage - targetArmor.Value;
				if (damage < 1f) damage = 1f;

				if (EcsWorld.Has<LastAttacker>(target.Target))
				{
					EcsWorld.Set(target.Target, new LastAttacker(entity));
				}
				else
				{
					EcsWorld.Add(target.Target, new LastAttacker(entity));
				}

				OnUnitAttackedCallback?.Invoke(entity, target.Target);
				OnUnitDamagedCallback?.Invoke(target.Target, entity, damage);

				float newHp = Math.Max(0, targetHealth.Current - damage);
				EcsWorld.Set(target.Target, new Health(newHp, targetHealth.Max));

				if (EcsWorld.Has<DefinitionId>(target.Target))
				{
					string targetUnitId = EcsWorld.Get<DefinitionId>(target.Target).Value;
					bool targetIsEnemy = EcsWorld.Has<UnitFaction>(target.Target) && EcsWorld.Get<UnitFaction>(target.Target).IsEnemy;
					if (!targetIsEnemy)
					{
						float currentTimer = GetCombatAlertTimer();
						if (currentTimer <= 0f)
						{
							SetCombatAlertTimer(UnderAttackAlertCooldown);
							OnUnderAttackAlertRequested?.Invoke(targetUnitId);
						}
					}
				}

				if (EcsWorld.IsAlive(target.Target) && !EcsWorld.Has<Dead>(target.Target) && !EcsWorld.Has<AttackTarget>(target.Target))
				{
					if (EcsWorld.Has<Attack>(target.Target))
					{
						bool hasMoveTo = EcsWorld.Has<MoveTo>(target.Target);
						if (!hasMoveTo || EcsWorld.Has<Realm.Ecs.Components.Movement.AttackMove>(target.Target))
						{
							_tickNewAttackTargets.Add((target.Target, new AttackTarget(entity)));
						}
					}
				}

				atk.CurrentCooldown = atk.Cooldown;

				if (atk.Range > 3f)
				{
					OnArrowProjectileRequested?.Invoke(currentPos, targetPos);
				}

				if (newHp <= 0)
				{
					_tickUnitsToKill.Add(target.Target);
				}
				else
				{
					OnDamageFlashRequested?.Invoke(target.Target);
				}
			}
		}
		else
		{
			if (!EcsWorld.Has<Realm.Ecs.Components.Movement.HoldPosition>(entity) && EcsWorld.Has<Realm.Ecs.Components.Tags.Movable>(entity) && !EcsWorld.Has<Building>(entity))
			{
				_tickActionsToChase.Add((entity, targetPos));
			}
			else
			{
				_tickActionsToRemoveTarget.Add(entity);
			}
		}
	}

	private void ProcessHealingTicks()
	{
		_tickNewHealingTargets.Clear();
		EcsWorld.Query(in _priestScanQuery, _priestScanQueryDelegate);
		foreach (var (priest, target) in _tickNewHealingTargets)
		{
			if (EcsWorld.IsAlive(priest))
			{
				if (EcsWorld.Has<HealingTarget>(priest)) EcsWorld.Set(priest, target);
				else EcsWorld.Add(priest, target);
			}
		}

		_tickHealRemoveTargets.Clear();
		_tickHealChaseTargets.Clear();
		_tickHealStopChasing.Clear();

		EcsWorld.Query(in _healingExecutionQuery, _healingExecutionQueryDelegate);

		foreach (var ent in _tickHealRemoveTargets)
		{
			if (EcsWorld.IsAlive(ent) && EcsWorld.Has<HealingTarget>(ent))
			{
				EcsWorld.Remove<HealingTarget>(ent);
			}
		}

		foreach (var (priest, targetPos) in _tickHealChaseTargets)
		{
			if (EcsWorld.IsAlive(priest))
			{
				var moveTo = new MoveTo(targetPos);
				if (EcsWorld.Has<MoveTo>(priest)) EcsWorld.Set(priest, moveTo);
				else EcsWorld.Add(priest, moveTo);
			}
		}

		foreach (var priest in _tickHealStopChasing)
		{
			if (EcsWorld.IsAlive(priest))
			{
				if (EcsWorld.Has<MoveTo>(priest)) EcsWorld.Remove<MoveTo>(priest);
				if (EcsWorld.Has<Velocity>(priest))
				{
					EcsWorld.Set(priest, new Velocity(System.Numerics.Vector3.Zero));
				}
			}
		}
	}

	private void ScanFriendlyQueryAction(Entity potentialFriendly, ref Position fPosComp, ref Health fHealth, ref Owner fOwner)
	{
		if (fOwner.PlayerEntity == _scanFriendlyOwner && fHealth.Current < fHealth.Max)
		{
			float dist = System.Numerics.Vector3.Distance(_scanPriestPos, fPosComp.Value);
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
			bool isIdle = !EcsWorld.Has<MoveTo>(entity);
			if (isIdle)
			{
				_scanClosestDamagedFriendly = Entity.Null;
				_scanFriendlyClosestDist = 15.0f;
				_scanPriestPos = pos.Value;
				_scanFriendlyOwner = owner.PlayerEntity;

				EcsWorld.Query(in _friendlyScanQuery, _friendlyScanQueryDelegate);

				if (_scanClosestDamagedFriendly != Entity.Null)
				{
					_tickNewHealingTargets.Add((entity, new HealingTarget(_scanClosestDamagedFriendly)));
				}
			}
		}
	}

	private void HealingExecutionQueryAction(Entity entity, ref Position pos, ref Attack atk, ref HealingTarget target, ref Owner owner)
	{
		if (!EcsWorld.IsAlive(target.Target) || EcsWorld.Has<Dead>(target.Target))
		{
			_tickHealRemoveTargets.Add(entity);
			return;
		}

		var targetHealth = EcsWorld.Get<Health>(target.Target);
		if (targetHealth.Current >= targetHealth.Max)
		{
			_tickHealRemoveTargets.Add(entity);
			return;
		}

		var targetPosComp = EcsWorld.Get<Position>(target.Target);
		var currentPos = pos.Value;
		var targetPos = targetPosComp.Value;

		float dist = System.Numerics.Vector3.Distance(currentPos, targetPos);
		if (dist <= atk.Range)
		{
			_tickHealStopChasing.Add(entity);

			if (atk.CurrentCooldown <= 0)
			{
				float healAmount = atk.Damage;
				float newHp = Math.Min(targetHealth.Max, targetHealth.Current + healAmount);
				EcsWorld.Set(target.Target, new Health(newHp, targetHealth.Max));

				atk.CurrentCooldown = atk.Cooldown;

				OnHealEffectRequested?.Invoke(currentPos, targetPos);
				OnHealFlashRequested?.Invoke(target.Target);
			}
		}
		else
		{
			if (!EcsWorld.Has<Realm.Ecs.Components.Movement.HoldPosition>(entity) && EcsWorld.Has<Realm.Ecs.Components.Tags.Movable>(entity))
			{
				_tickHealChaseTargets.Add((entity, targetPos));
			}
		}
	}

	public void DealSpellDamageAOE(System.Numerics.Vector3 position, float radius, float damage, Entity casterEntity, bool enemyOnly = true)
	{
		var query = Realm.Ecs.Common.QueryCache.AllPositionAndHealthNoneDeadQuery;
		EcsWorld.Query(in query, (Entity entity, ref Position pos, ref Health hp) =>
		{
			if (EcsWorld.Has<Realm.Ecs.Components.Tags.Invulnerable>(entity)) return;

			if (enemyOnly)
			{
				bool isEnemy = EcsWorld.Has<UnitFaction>(entity) && EcsWorld.Get<UnitFaction>(entity).IsEnemy;
				if (!isEnemy) return;
			}

			if (System.Numerics.Vector3.Distance(pos.Value, position) <= radius)
			{
				if (casterEntity != Entity.Null && EcsWorld.IsAlive(casterEntity))
				{
					if (EcsWorld.Has<LastAttacker>(entity))
					{
						EcsWorld.Set(entity, new LastAttacker(casterEntity));
					}
					else
					{
						EcsWorld.Add(entity, new LastAttacker(casterEntity));
					}
				}

				float newHp = Math.Max(0, hp.Current - damage);
				hp.Current = newHp;

				OnUnitDamagedCallback?.Invoke(entity, casterEntity, damage);

				if (newHp <= 0)
				{
					if (!EcsWorld.Has<Dead>(entity))
					{
						EcsWorld.Add<Dead>(entity);
					}
					OnKillUnitRequested?.Invoke(entity);
				}
				else
				{
					OnDamageFlashRequested?.Invoke(entity);
				}
			}
		});
	}

	public void HealAOE(System.Numerics.Vector3 position, float radius, float healAmount)
	{
		var query = Realm.Ecs.Common.QueryCache.AllPositionAndHealthNoneDeadQuery;
		EcsWorld.Query(in query, (Entity entity, ref Position pos, ref Health hp) =>
		{
			bool isEnemy = EcsWorld.Has<UnitFaction>(entity) && EcsWorld.Get<UnitFaction>(entity).IsEnemy;
			if (isEnemy) return;

			if (System.Numerics.Vector3.Distance(pos.Value, position) <= radius)
			{
				float newHp = Math.Min(hp.Max, hp.Current + healAmount);
				hp.Current = newHp;
				OnHealFlashRequested?.Invoke(entity);
			}
		});
	}
}
