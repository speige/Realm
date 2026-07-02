using Arch.Core;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using System;
using System.Collections.Generic;

internal class CombatAndDamageService
{
	private readonly World _ecsWorld;

	private float _fDelta;
	private const float UnderAttackAlertCooldown = 8f;

	private Entity _scanAttackerEntity;
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

	private readonly QueryDescription _enemyQuery = new QueryDescription().WithAll<Position, Owner>().WithNone<Dead>();
	private readonly QueryDescription _friendlyScanQuery = new QueryDescription().WithAll<Position, Health, Owner>().WithNone<Dead>();
	private readonly QueryDescription _targetAcquisitionQuery = new QueryDescription().WithAll<Position, Attack, Owner>().WithNone<AttackTarget, Dead>();
	private readonly QueryDescription _combatQuery = new QueryDescription().WithAll<Position, Attack, AttackTarget, Owner>().WithNone<Dead>();
	private readonly QueryDescription _priestScanQuery = new QueryDescription().WithAll<Position, Owner, DefinitionId>().WithNone<Dead, HealingTarget>();
	private readonly QueryDescription _healingExecutionQuery = new QueryDescription().WithAll<Position, Attack, HealingTarget, Owner>().WithNone<Dead>();

	private ForEachWithEntity<Position, Attack, Owner> _targetAcquisitionQueryDelegate = null!;
	private ForEachWithEntity<Position, Owner> _potentialEnemyQueryDelegate = null!;
	private ForEachWithEntity<Position, Attack, AttackTarget, Owner> _combatQueryDelegate = null!;
	private ForEachWithEntity<Position, Owner, DefinitionId> _priestScanQueryDelegate = null!;
	private ForEachWithEntity<Position, Health, Owner> _friendlyScanQueryDelegate = null!;
	private ForEachWithEntity<Position, Attack, HealingTarget, Owner> _healingExecutionQueryDelegate = null!;

	public Action<System.Numerics.Vector3, System.Numerics.Vector3> OnArrowProjectileRequested;
	public Action<Entity> OnDamageFlashRequested;
	public Action<System.Numerics.Vector3, System.Numerics.Vector3> OnHealEffectRequested;
	public Action<Entity> OnHealFlashRequested;
	public Action<Entity, Entity, float> OnUnitDamagedCallback;
	public Action<string> OnUnderAttackAlertRequested;
	public Action<Entity> OnKillUnitRequested;

	public CombatAndDamageService(World ecsWorld)
	{
		_ecsWorld = ecsWorld;
		_targetAcquisitionQueryDelegate = TargetAcquisitionQueryAction;
		_potentialEnemyQueryDelegate = ScanEnemyQueryAction;
		_combatQueryDelegate = CombatQueryAction;
		_priestScanQueryDelegate = PriestScanQueryAction;
		_friendlyScanQueryDelegate = ScanFriendlyQueryAction;
		_healingExecutionQueryDelegate = HealingExecutionQueryAction;
	}

	public void StepCombat(float delta)
	{
		_fDelta = delta;

		TickCombatAlertTimer(delta);

		ProcessTargetAcquisition();
		ProcessCombatTicks();
		ProcessHealingTicks();
	}

	private Entity FindWorldEntity()
	{
		Entity worldEntity = Entity.Null;
		var query = new QueryDescription().WithAll<WorldState>();
		_ecsWorld.Query(in query, (Entity entity) => worldEntity = entity);
		return worldEntity;
	}

	private int GetTimeOfDayIndex()
	{
		var worldEntity = FindWorldEntity();
		if (worldEntity != Entity.Null && _ecsWorld.Has<WorldState>(worldEntity))
		{
			return _ecsWorld.Get<WorldState>(worldEntity).TimeOfDayIndex;
		}
		return 0;
	}

	private float GetCombatAlertTimer()
	{
		Entity worldEntity = Entity.Null;
		var query = new QueryDescription().WithAll<CombatAlertState>();
		_ecsWorld.Query(in query, (Entity entity) => worldEntity = entity);

		if (worldEntity != Entity.Null && _ecsWorld.Has<CombatAlertState>(worldEntity))
		{
			return _ecsWorld.Get<CombatAlertState>(worldEntity).UnderAttackAlertTimer;
		}
		return 0f;
	}

	private void SetCombatAlertTimer(float value)
	{
		Entity worldEntity = Entity.Null;
		var query = new QueryDescription().WithAll<CombatAlertState>();
		_ecsWorld.Query(in query, (Entity entity) => worldEntity = entity);

		if (worldEntity != Entity.Null && _ecsWorld.Has<CombatAlertState>(worldEntity))
		{
			ref var state = ref _ecsWorld.Get<CombatAlertState>(worldEntity);
			state.UnderAttackAlertTimer = value;
		}
	}

	private void TickCombatAlertTimer(float fDelta)
	{
		Entity worldEntity = Entity.Null;
		var query = new QueryDescription().WithAll<CombatAlertState>();
		_ecsWorld.Query(in query, (Entity entity) => worldEntity = entity);

		if (worldEntity != Entity.Null && _ecsWorld.Has<CombatAlertState>(worldEntity))
		{
			ref var state = ref _ecsWorld.Get<CombatAlertState>(worldEntity);
			if (state.UnderAttackAlertTimer > 0f)
			{
				state.UnderAttackAlertTimer = Math.Max(0f, state.UnderAttackAlertTimer - fDelta);
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
			float scanRadius = _ecsWorld.Has<ScanRadius>(entity)
				? _ecsWorld.Get<ScanRadius>(entity).Value
				: 15.0f;

			if (GetTimeOfDayIndex() == 2)
			{
				scanRadius *= 0.7f;
			}

			_scanAttackerEntity = entity;
			_scanAttackerPos = pos.Value;
			_scanAttackerOwner = owner.PlayerEntity;
			_scanIsAttackerEnemy = false;
			if (_ecsWorld.Has<UnitFaction>(entity))
			{
				_scanIsAttackerEnemy = _ecsWorld.Get<UnitFaction>(entity).IsEnemy;
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

	private void ScanEnemyQueryAction(Entity potentialEnemy, ref Position enemyPos, ref Owner enemyOwner)
	{
		if (enemyOwner.PlayerEntity != _scanAttackerOwner)
		{
			bool isEnemyEntity = _ecsWorld.Has<UnitFaction>(potentialEnemy) && _ecsWorld.Get<UnitFaction>(potentialEnemy).IsEnemy;
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

		_ecsWorld.Query(in _combatQuery, _combatQueryDelegate);

		foreach (var targetEntity in _tickUnitsToKill)
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
				var moveTo = new MoveTo(targetPos);
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
				if (_ecsWorld.Has<Velocity>(attacker))
				{
					_ecsWorld.Set(attacker, new Velocity(System.Numerics.Vector3.Zero));
				}
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
		var currentPos = pos.Value;
		var targetPos = targetPosComp.Value;

		float dist = System.Numerics.Vector3.Distance(currentPos, targetPos);
		if (dist <= atk.Range)
		{
			_tickActionsToStopChasing.Add(entity);

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

				OnUnitDamagedCallback?.Invoke(target.Target, entity, damage);

				float newHp = Math.Max(0, targetHealth.Current - damage);
				_ecsWorld.Set(target.Target, new Health(newHp, targetHealth.Max));

				if (_ecsWorld.Has<DefinitionId>(target.Target))
				{
					string targetUnitId = _ecsWorld.Get<DefinitionId>(target.Target).Value;
					bool targetIsEnemy = _ecsWorld.Has<UnitFaction>(target.Target) && _ecsWorld.Get<UnitFaction>(target.Target).IsEnemy;
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
				var moveTo = new MoveTo(targetPos);
				if (_ecsWorld.Has<MoveTo>(priest)) _ecsWorld.Set(priest, moveTo);
				else _ecsWorld.Add(priest, moveTo);
			}
		}

		foreach (var priest in _tickHealStopChasing)
		{
			if (_ecsWorld.IsAlive(priest))
			{
				if (_ecsWorld.Has<MoveTo>(priest)) _ecsWorld.Remove<MoveTo>(priest);
				if (_ecsWorld.Has<Velocity>(priest))
				{
					_ecsWorld.Set(priest, new Velocity(System.Numerics.Vector3.Zero));
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
			bool isIdle = !_ecsWorld.Has<MoveTo>(entity);
			if (isIdle)
			{
				_scanClosestDamagedFriendly = Entity.Null;
				_scanFriendlyClosestDist = 15.0f;
				_scanPriestPos = pos.Value;
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
				_ecsWorld.Set(target.Target, new Health(newHp, targetHealth.Max));

				atk.CurrentCooldown = atk.Cooldown;

				OnHealEffectRequested?.Invoke(currentPos, targetPos);
				OnHealFlashRequested?.Invoke(target.Target);
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

	public void DealSpellDamageAOE(System.Numerics.Vector3 position, float radius, float damage, Entity casterEntity, bool enemyOnly = true)
	{
		var query = new QueryDescription().WithAll<Position, Health>().WithNone<Dead>();
		_ecsWorld.Query(in query, (Entity entity, ref Position pos, ref Health hp) =>
		{
			if (_ecsWorld.Has<Realm.Ecs.Components.Tags.Invulnerable>(entity)) return;

			if (enemyOnly)
			{
				bool isEnemy = _ecsWorld.Has<UnitFaction>(entity) && _ecsWorld.Get<UnitFaction>(entity).IsEnemy;
				if (!isEnemy) return;
			}

			if (System.Numerics.Vector3.Distance(pos.Value, position) <= radius)
			{
				if (casterEntity != Entity.Null && _ecsWorld.IsAlive(casterEntity))
				{
					if (_ecsWorld.Has<LastAttacker>(entity))
					{
						_ecsWorld.Set(entity, new LastAttacker(casterEntity));
					}
					else
					{
						_ecsWorld.Add(entity, new LastAttacker(casterEntity));
					}
				}

				float newHp = Math.Max(0, hp.Current - damage);
				hp.Current = newHp;

				OnUnitDamagedCallback?.Invoke(entity, casterEntity, damage);

				if (newHp <= 0)
				{
					if (!_ecsWorld.Has<Dead>(entity))
					{
						_ecsWorld.Add<Dead>(entity);
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
		var query = new QueryDescription().WithAll<Position, Health>().WithNone<Dead>();
		_ecsWorld.Query(in query, (Entity entity, ref Position pos, ref Health hp) =>
		{
			bool isEnemy = _ecsWorld.Has<UnitFaction>(entity) && _ecsWorld.Get<UnitFaction>(entity).IsEnemy;
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
