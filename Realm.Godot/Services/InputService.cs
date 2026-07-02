using Arch.Core;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Common;
using Realm.Ecs.Services;
using System;
using System.Collections.Generic;

internal class InputService
{
	private readonly WorldAccessor _ecsWorldAccessor;
	private World _ecsWorld => _ecsWorldAccessor.Current;
	private readonly TechTreeService _techTreeService;
	private int _buildingCycleIndex = 0;

	public InputService(WorldAccessor ecsWorldAccessor, TechTreeService techTreeService)
	{
		_ecsWorldAccessor = ecsWorldAccessor;
		_techTreeService = techTreeService;
	}

	private Entity GetWorldEntity()
	{
		Entity worldEntity = Entity.Null;
		var query = new QueryDescription().WithAll<InputState>();
		_ecsWorld.Query(in query, (Entity entity) => worldEntity = entity);
		return worldEntity;
	}

	private ref InputState GetInputState(Entity worldEntity)
	{
		return ref _ecsWorld.Get<InputState>(worldEntity);
	}

	public void ClearTargetingModes()
	{
		var worldEntity = GetWorldEntity();
		if (worldEntity != Entity.Null)
		{
			ref var state = ref GetInputState(worldEntity);
			state.ActiveSpellTargeting = null;
			state.ActiveCommandTargeting = null;
			state.ActiveBuildingPlacementType = null;
			state.ActivePingMode = false;
		}
	}

	public bool BuyWeaponsUpgrade(Entity playerEntity)
	{
		return _techTreeService.BuyWeaponsUpgrade(playerEntity);
	}

	public bool BuyShieldsUpgrade(Entity playerEntity)
	{
		return _techTreeService.BuyShieldsUpgrade(playerEntity);
	}

	public bool BuyHarvestingUpgrade(Entity playerEntity)
	{
		return _techTreeService.BuyHarvestingUpgrade(playerEntity);
	}

	public void ClearUnitOrders(Entity entity)
	{
		if (_ecsWorld.Has<MoveTo>(entity)) _ecsWorld.Remove<MoveTo>(entity);
		if (_ecsWorld.Has<PathFollow>(entity)) _ecsWorld.Remove<PathFollow>(entity);
		if (_ecsWorld.Has<AttackTarget>(entity)) _ecsWorld.Remove<AttackTarget>(entity);
		if (_ecsWorld.Has<Realm.Ecs.Components.Movement.AttackMove>(entity)) _ecsWorld.Remove<Realm.Ecs.Components.Movement.AttackMove>(entity);
		if (_ecsWorld.Has<Realm.Ecs.Components.Movement.HoldPosition>(entity)) _ecsWorld.Remove<Realm.Ecs.Components.Movement.HoldPosition>(entity);
		if (_ecsWorld.Has<Realm.Ecs.Components.Movement.Follow>(entity)) _ecsWorld.Remove<Realm.Ecs.Components.Movement.Follow>(entity);
		if (_ecsWorld.Has<Patrol>(entity)) _ecsWorld.Remove<Patrol>(entity);
		if (_ecsWorld.Has<HealingTarget>(entity)) _ecsWorld.Remove<HealingTarget>(entity);
		if (_ecsWorld.Has<WaypointQueue>(entity)) _ecsWorld.Remove<WaypointQueue>(entity);
		if (_ecsWorld.Has<Gatherer>(entity)) _ecsWorld.Remove<Gatherer>(entity);
	}

	public void IssueMoveCommand(List<Entity> selectedEntities, System.Numerics.Vector3 targetPos)
	{
		int unitIndex = 0;
		int cols = (int)Math.Ceiling(Math.Sqrt(selectedEntities.Count));
		float spacing = 2.2f;

		foreach (var entity in selectedEntities)
		{
			if (!_ecsWorld.IsAlive(entity)) continue;
			bool isBuilding = _ecsWorld.Has<Building>(entity);
			bool isEnemy = _ecsWorld.Has<UnitFaction>(entity) && _ecsWorld.Get<UnitFaction>(entity).IsEnemy;
			if (isBuilding || isEnemy) continue;

			ClearUnitOrders(entity);

			if (_ecsWorld.Has<Realm.Ecs.Components.Tags.Movable>(entity))
			{
				int row = unitIndex / cols;
				int col = unitIndex % cols;
				float offsetX = (col - cols * 0.5f + 0.5f) * spacing;
				float offsetZ = row * spacing;
				var scattered = new System.Numerics.Vector3(targetPos.X + offsetX, targetPos.Y, targetPos.Z + offsetZ);

				var moveTo = new MoveTo(scattered);
				if (_ecsWorld.Has<MoveTo>(entity))
					_ecsWorld.Set(entity, moveTo);
				else
					_ecsWorld.Add(entity, moveTo);

				unitIndex++;
			}
		}
	}

	public void IssueMoveCommandQueued(List<Entity> selectedEntities, System.Numerics.Vector3 targetPos)
	{
		int unitIndex = 0;
		int cols = (int)Math.Ceiling(Math.Sqrt(selectedEntities.Count));
		float spacing = 2.2f;

		foreach (var entity in selectedEntities)
		{
			if (!_ecsWorld.IsAlive(entity)) continue;
			bool isBuilding = _ecsWorld.Has<Building>(entity);
			bool isEnemy = _ecsWorld.Has<UnitFaction>(entity) && _ecsWorld.Get<UnitFaction>(entity).IsEnemy;
			if (isBuilding || isEnemy) continue;
			if (!_ecsWorld.Has<Realm.Ecs.Components.Tags.Movable>(entity)) continue;

			bool alreadyMoving = _ecsWorld.Has<MoveTo>(entity);
			if (!alreadyMoving)
			{
				ClearUnitOrders(entity);
			}

			int row = unitIndex / cols;
			int col = unitIndex % cols;
			float offsetX = (col - cols * 0.5f + 0.5f) * spacing;
			float offsetZ = row * spacing;
			var scattered = new System.Numerics.Vector3(targetPos.X + offsetX, targetPos.Y, targetPos.Z + offsetZ);

			if (alreadyMoving)
			{
				if (_ecsWorld.Has<WaypointQueue>(entity))
				{
					var q = _ecsWorld.Get<WaypointQueue>(entity);
					q.Add(scattered);
					_ecsWorld.Set(entity, q);
				}
				else
				{
					var q = new WaypointQueue(scattered);
					_ecsWorld.Add(entity, q);
				}
			}
			else
			{
				var moveTo = new MoveTo(scattered);
				if (_ecsWorld.Has<MoveTo>(entity))
					_ecsWorld.Set(entity, moveTo);
				else
					_ecsWorld.Add(entity, moveTo);
			}

			unitIndex++;
		}
	}

	public void IssueAttackCommand(List<Entity> selectedEntities, Entity targetEntity)
	{
		foreach (var entity in selectedEntities)
		{
			if (!_ecsWorld.IsAlive(entity)) continue;
			bool isBuilding = _ecsWorld.Has<Building>(entity);
			bool isEnemy = _ecsWorld.Has<UnitFaction>(entity) && _ecsWorld.Get<UnitFaction>(entity).IsEnemy;
			if (isBuilding || isEnemy) continue;

			ClearUnitOrders(entity);

			var attackTarget = new AttackTarget(targetEntity);
			if (_ecsWorld.Has<AttackTarget>(entity))
				_ecsWorld.Set(entity, attackTarget);
			else
				_ecsWorld.Add(entity, attackTarget);
		}
	}

	public void IssueFollowCommand(List<Entity> selectedEntities, Entity targetEntity)
	{
		foreach (var entity in selectedEntities)
		{
			if (!_ecsWorld.IsAlive(entity) || entity == targetEntity) continue;
			bool isBuilding = _ecsWorld.Has<Building>(entity);
			bool isEnemy = _ecsWorld.Has<UnitFaction>(entity) && _ecsWorld.Get<UnitFaction>(entity).IsEnemy;
			if (isBuilding || isEnemy) continue;

			ClearUnitOrders(entity);

			if (_ecsWorld.Has<DefinitionId>(entity) && _ecsWorld.Get<DefinitionId>(entity).Value == "priest")
			{
				var healTarget = new HealingTarget(targetEntity);
				_ecsWorld.Add(entity, healTarget);
			}
			else if (_ecsWorld.Has<Realm.Ecs.Components.Tags.Movable>(entity))
			{
				var follow = new Follow(targetEntity);
				if (_ecsWorld.Has<Follow>(entity))
					_ecsWorld.Set(entity, follow);
				else
					_ecsWorld.Add(entity, follow);
			}
		}
	}

	public void IssuePatrolCommand(List<Entity> selectedEntities, System.Numerics.Vector3 targetPos)
	{
		int unitIndex = 0;
		int cols = (int)Math.Ceiling(Math.Sqrt(selectedEntities.Count));
		float spacing = 2.2f;

		foreach (var entity in selectedEntities)
		{
			if (!_ecsWorld.IsAlive(entity)) continue;
			bool isBuilding = _ecsWorld.Has<Building>(entity);
			bool isEnemy = _ecsWorld.Has<UnitFaction>(entity) && _ecsWorld.Get<UnitFaction>(entity).IsEnemy;
			if (isBuilding || isEnemy) continue;

			ClearUnitOrders(entity);

			if (_ecsWorld.Has<Realm.Ecs.Components.Tags.Movable>(entity))
			{
				int row = unitIndex / cols;
				int col = unitIndex % cols;
				float offsetX = (col - cols * 0.5f + 0.5f) * spacing;
				float offsetZ = row * spacing;

				var unitPos = _ecsWorld.Has<Position>(entity) ? _ecsWorld.Get<Position>(entity).Value : System.Numerics.Vector3.Zero;
				var patrolA = new System.Numerics.Vector3(unitPos.X, unitPos.Y, unitPos.Z);
				var patrolB = new System.Numerics.Vector3(targetPos.X + offsetX, targetPos.Y, targetPos.Z + offsetZ);

				var patrol = new Patrol(patrolA, patrolB);
				if (_ecsWorld.Has<Patrol>(entity)) _ecsWorld.Set(entity, patrol);
				else _ecsWorld.Add(entity, patrol);

				var moveTo = new MoveTo(patrolB);
				if (_ecsWorld.Has<MoveTo>(entity)) _ecsWorld.Set(entity, moveTo);
				else _ecsWorld.Add(entity, moveTo);

				unitIndex++;
			}
		}
	}

	public void IssueAttackMoveCommand(List<Entity> selectedEntities, System.Numerics.Vector3 targetPos)
	{
		foreach (var entity in selectedEntities)
		{
			if (!_ecsWorld.IsAlive(entity)) continue;
			bool isBuilding = _ecsWorld.Has<Building>(entity);
			bool isEnemy = _ecsWorld.Has<UnitFaction>(entity) && _ecsWorld.Get<UnitFaction>(entity).IsEnemy;
			if (isBuilding || isEnemy) continue;

			ClearUnitOrders(entity);

			var attackMove = new AttackMove(targetPos);
			if (_ecsWorld.Has<AttackMove>(entity))
				_ecsWorld.Set(entity, attackMove);
			else
				_ecsWorld.Add(entity, attackMove);

			var moveTo = new MoveTo(targetPos);
			if (_ecsWorld.Has<MoveTo>(entity))
				_ecsWorld.Set(entity, moveTo);
			else
				_ecsWorld.Add(entity, moveTo);
		}
	}

	public void HoldSelectedUnits(List<Entity> selectedEntities)
	{
		foreach (var entity in selectedEntities)
		{
			if (!_ecsWorld.IsAlive(entity)) continue;
			bool isBuilding = _ecsWorld.Has<Building>(entity);
			bool isEnemy = _ecsWorld.Has<UnitFaction>(entity) && _ecsWorld.Get<UnitFaction>(entity).IsEnemy;
			if (isBuilding || isEnemy) continue;

			ClearUnitOrders(entity);

			if (!_ecsWorld.Has<HoldPosition>(entity))
				_ecsWorld.Add<HoldPosition>(entity);
		}
	}

	public void StopSelectedUnits(List<Entity> selectedEntities)
	{
		foreach (var entity in selectedEntities)
		{
			if (!_ecsWorld.IsAlive(entity)) continue;
			bool isEnemy = _ecsWorld.Has<UnitFaction>(entity) && _ecsWorld.Get<UnitFaction>(entity).IsEnemy;
			if (isEnemy) continue;

			ClearUnitOrders(entity);
		}
	}

	public int CycleSelectionFocus(Entity worldEntity, int selectedCount, bool reverse)
	{
		if (selectedCount <= 1 || worldEntity == Entity.Null) return 0;

		ref var state = ref _ecsWorld.Get<InputState>(worldEntity);
		if (reverse)
		{
			state.CycleSelectionIndex = (state.CycleSelectionIndex - 1 + selectedCount) % selectedCount;
		}
		else
		{
			state.CycleSelectionIndex = (state.CycleSelectionIndex + 1) % selectedCount;
		}

		return state.CycleSelectionIndex;
	}

	public Entity CycleThroughBuildings(Entity playerEntity)
	{
		var buildings = new List<Entity>();
		var query = new QueryDescription().WithAll<Building, Owner, Position>().WithNone<Dead>();
		_ecsWorld.Query(in query, (Entity entity, ref Owner owner) =>
		{
			if (owner.PlayerEntity.Value == playerEntity)
			{
				buildings.Add(entity);
			}
		});

		if (buildings.Count == 0) return Entity.Null;
		_buildingCycleIndex = (_buildingCycleIndex + 1) % buildings.Count;
		return buildings[_buildingCycleIndex];
	}

	public void MarkEntitiesAsDead(List<Entity> entities)
	{
		foreach (var entity in entities)
		{
			if (_ecsWorld.IsAlive(entity) && !_ecsWorld.Has<Dead>(entity))
			{
				_ecsWorld.Add<Dead>(entity);
			}
		}
	}

	public List<Entity> GetIdleUnitEntities(Entity playerEntity)
	{
		var result = new List<Entity>();
		var query = new QueryDescription().WithAll<Owner, Movable>().WithNone<Dead, Building>();
		_ecsWorld.Query(in query, (Entity entity, ref Owner owner) =>
		{
			if (owner.PlayerEntity.Value == playerEntity)
			{
				bool hasMoveTo = _ecsWorld.Has<MoveTo>(entity);
				bool hasAttackTarget = _ecsWorld.Has<AttackTarget>(entity);
				bool hasAttackMove = _ecsWorld.Has<AttackMove>(entity);
				bool isGathering = _ecsWorld.Has<Gatherer>(entity) && _ecsWorld.Get<Gatherer>(entity).TargetEntity != Entity.Null;

				if (!hasMoveTo && !hasAttackTarget && !hasAttackMove && !isGathering)
				{
					result.Add(entity);
				}
			}
		});
		return result;
	}

	public List<Entity> GetMilitaryUnitEntities(Entity playerEntity)
	{
		var result = new List<Entity>();
		var query = new QueryDescription().WithAll<Owner, Movable>().WithNone<Dead, Building>();
		_ecsWorld.Query(in query, (Entity entity, ref Owner owner) =>
		{
			if (owner.PlayerEntity.Value == playerEntity)
			{
				result.Add(entity);
			}
		});
		return result;
	}

	public List<Entity> GetBuildingEntities(Entity playerEntity)
	{
		var result = new List<Entity>();
		var query = new QueryDescription().WithAll<Owner, Building>().WithNone<Dead>();
		_ecsWorld.Query(in query, (Entity entity, ref Owner owner) =>
		{
			if (owner.PlayerEntity.Value == playerEntity)
			{
				result.Add(entity);
			}
		});
		return result;
	}

	public bool TryPlaceBuilding(System.Numerics.Vector3 position, float clearance)
	{
		return !IsAreaObstructed(position, clearance);
	}

	public bool IsAreaObstructed(System.Numerics.Vector3 position, float clearance)
	{
		bool obstructed = false;
		var query = new QueryDescription().WithAll<Position>().WithNone<Dead>();
		_ecsWorld.Query(in query, (Entity entity, ref Position pos) =>
		{
			if (System.Numerics.Vector3.Distance(position, pos.Value) < clearance)
			{
				obstructed = true;
			}
		});
		return obstructed;
	}

	public bool IsPositionBlocked(System.Numerics.Vector3 pos, float checkRadius, Entity ignoreEntity)
	{
		bool blocked = false;

		var query = new QueryDescription().WithAll<Position>().WithNone<Dead>();
		_ecsWorld.Query(in query, (Entity entity, ref Position entityPos) =>
		{
			if (entity == ignoreEntity) return;

			float r1 = checkRadius;
			float r2 = 1.2f;

			if (_ecsWorld.Has<DefinitionId>(entity))
			{
				string defId = _ecsWorld.Get<DefinitionId>(entity).Value;
				float scale = _ecsWorld.Has<ModelScale>(entity) ? _ecsWorld.Get<ModelScale>(entity).Value : 1.0f;
				r2 = GetPlacementRadius(defId, scale);
			}
			else if (_ecsWorld.Has<PropIdentity>(entity))
			{
				string propId = _ecsWorld.Get<PropIdentity>(entity).PropId;
				float scale = _ecsWorld.Has<ModelScale>(entity) ? _ecsWorld.Get<ModelScale>(entity).Value : 1.0f;
				r2 = GetPlacementRadius(propId, scale);
			}
			else
			{
				float scale = _ecsWorld.Has<ModelScale>(entity) ? _ecsWorld.Get<ModelScale>(entity).Value : 1.0f;
				r2 = scale * 1.2f;
			}

			float dx = entityPos.Value.X - pos.X;
			float dz = entityPos.Value.Z - pos.Z;
			float distXZ = (float)Math.Sqrt(dx * dx + dz * dz);

			if (distXZ < (r1 + r2) * 0.85f)
			{
				blocked = true;
			}
		});

		return blocked;
	}

	public float GetPlacementRadius(string placeId, float scale = 1.0f)
	{
		if (string.IsNullOrEmpty(placeId)) return 1.2f * scale;
		string lowerId = placeId.ToLower();
		float baseRadius = 1.2f;
		if (lowerId.Contains("castle")) baseRadius = 5.0f;
		else if (lowerId.Contains("tower")) baseRadius = 2.5f;
		else if (lowerId.Contains("goldmine")) baseRadius = 4.0f;
		else if (lowerId.Contains("logo") || lowerId.Contains("flag") || lowerId.Contains("rune")) baseRadius = 1.0f;
		return baseRadius * scale;
	}

	public System.Numerics.Vector3? FindNearestFreePosition(System.Numerics.Vector3 startPos, float checkRadius, float maxSearchDist = 20.0f, float terrainSpacing = 2.0f, int terrainWidth = 256, int terrainDepth = 256)
	{
		if (!IsPositionBlocked(startPos, checkRadius, Entity.Null))
		{
			return startPos;
		}

		float stepDist = 1.0f;
		int numSteps = (int)Math.Ceiling(maxSearchDist / stepDist);

		for (int i = 1; i <= numSteps; i++)
		{
			float dist = i * stepDist;
			int numAngles = 8 + i * 4;
			for (int a = 0; a < numAngles; a++)
			{
				float angle = a * ((float)Math.PI * 2.0f) / numAngles;
				var testPos = new System.Numerics.Vector3(
					startPos.X + dist * (float)Math.Cos(angle),
					startPos.Y,
					startPos.Z + dist * (float)Math.Sin(angle)
				);

				float halfW = (terrainWidth - 1) / 2.0f * terrainSpacing;
				float halfD = (terrainDepth - 1) / 2.0f * terrainSpacing;
				if (Math.Abs(testPos.X) > halfW || Math.Abs(testPos.Z) > halfD) continue;

				if (!IsPositionBlocked(testPos, checkRadius, Entity.Null))
				{
					return testPos;
				}
			}
		}

		return null;
	}

	public bool TryExecuteSpellCast(Entity playerEntity, string spellId, out float cooldownMax)
	{
		cooldownMax = 0f;
		if (!_ecsWorld.IsAlive(playerEntity) || !_ecsWorld.Has<SpellCooldowns>(playerEntity))
			return false;

		ref var cd = ref _ecsWorld.Get<SpellCooldowns>(playerEntity);

		if (spellId == "fireball")
		{
			if (cd.FireballCooldown > 0f) return false;
			cooldownMax = 8.0f;
			cd.FireballCooldown = cooldownMax;
		}
		else if (spellId == "lightning")
		{
			if (cd.LightningCooldown > 0f) return false;
			cooldownMax = 12.0f;
			cd.LightningCooldown = cooldownMax;
		}
		else if (spellId == "holylight")
		{
			if (cd.HolyLightCooldown > 0f) return false;
			cooldownMax = 15.0f;
			cd.HolyLightCooldown = cooldownMax;
		}

		return true;
	}

	public bool BuyHealingPotion(Entity playerEntity, System.Numerics.Vector3 castlePos, Entity selectedUnitEntity, out Entity targetUnitEntity)
	{
		targetUnitEntity = Entity.Null;

		Entity target = FindClosestFriendlyCombatUnit(castlePos, playerEntity, 20.0f);

		if (target == Entity.Null && _ecsWorld.IsAlive(selectedUnitEntity))
		{
			bool isBuilding = _ecsWorld.Has<Building>(selectedUnitEntity);
			bool isEnemy = _ecsWorld.Has<UnitFaction>(selectedUnitEntity) && _ecsWorld.Get<UnitFaction>(selectedUnitEntity).IsEnemy;
			if (!isBuilding && !isEnemy && _ecsWorld.Has<Inventory>(selectedUnitEntity))
			{
				target = selectedUnitEntity;
			}
		}

		if (target != Entity.Null)
		{
			targetUnitEntity = target;
			ref var inv = ref _ecsWorld.Get<Inventory>(target);
			inv.Potions += 1;
			return true;
		}

		return false;
	}

	private Entity FindClosestFriendlyCombatUnit(System.Numerics.Vector3 castlePos, Entity playerEntity, float maxDistance)
	{
		Entity closestUnit = Entity.Null;
		float closestDist = maxDistance;

		var query = new QueryDescription().WithAll<Position, Owner, Inventory>().WithNone<Dead, Building>();
		_ecsWorld.Query(in query, (Entity entity, ref Position pos, ref Owner owner) =>
		{
			if (owner.PlayerEntity.Value == playerEntity)
			{
				float dist = System.Numerics.Vector3.Distance(castlePos, pos.Value);
				if (dist < closestDist)
				{
					closestDist = dist;
					closestUnit = entity;
				}
			}
		});

		return closestUnit;
	}

	public bool UseHealingPotion(Entity unitEntity, out float healedAmount)
	{
		healedAmount = 0f;
		if (!_ecsWorld.IsAlive(unitEntity) || !_ecsWorld.Has<Inventory>(unitEntity) || !_ecsWorld.Has<Health>(unitEntity))
			return false;

		ref var inv = ref _ecsWorld.Get<Inventory>(unitEntity);
		if (inv.Potions <= 0) return false;

		ref var hp = ref _ecsWorld.Get<Health>(unitEntity);
		if (hp.Current >= hp.Max) return false;

		inv.Potions--;
		float oldCurrent = hp.Current;
		hp.Current = Math.Min(hp.Max, hp.Current + 50f);
		healedAmount = hp.Current - oldCurrent;

		return true;
	}

	public bool TryQueueUnitAtCastle(Entity playerEntity, Entity castleEntity, string unitId, int popCost, float productionTime)
	{
		if (!_ecsWorld.IsAlive(playerEntity) || !_ecsWorld.IsAlive(castleEntity)) return false;

		if (_ecsWorld.Has<PlayerPopulation>(playerEntity))
		{
			ref var pop = ref _ecsWorld.Get<PlayerPopulation>(playerEntity);
			if (popCost > 0 && pop.Current + popCost > pop.Max)
			{
				return false;
			}
			pop.Current += popCost;
		}

		ref var prod = ref _ecsWorld.Has<ProductionQueue>(castleEntity)
			? ref _ecsWorld.Get<ProductionQueue>(castleEntity)
			: ref CreateProductionQueue(castleEntity);

		if (prod.UnitIds.Count >= 5) return false;

		if (prod.UnitIds.Count == 0)
		{
			prod.BuildTime = productionTime;
		}
		prod.UnitIds.Add(unitId);
		_ecsWorld.Set(castleEntity, prod);

		return true;
	}

	private ref ProductionQueue CreateProductionQueue(Entity castleEntity)
	{
		_ecsWorld.Add(castleEntity, new ProductionQueue());
		return ref _ecsWorld.Get<ProductionQueue>(castleEntity);
	}

	public bool CancelQueuedUnitAt(Entity castleEntity, int index, out string? cancelledUnitId, out string? nextUnitId)
	{
		cancelledUnitId = null;
		nextUnitId = null;

		if (!_ecsWorld.IsAlive(castleEntity) || !_ecsWorld.Has<ProductionQueue>(castleEntity))
			return false;

		var prod = _ecsWorld.Get<ProductionQueue>(castleEntity);
		if (index < 0 || index >= prod.UnitIds.Count)
			return false;

		cancelledUnitId = prod.UnitIds[index];
		prod.UnitIds.RemoveAt(index);

		if (index == 0)
		{
			prod.CurrentProgress = 0f;
			if (prod.UnitIds.Count > 0)
			{
				nextUnitId = prod.UnitIds[0];
			}
		}

		_ecsWorld.Set(castleEntity, prod);
		return true;
	}

	public void SetRallyPoint(Entity buildingEntity, System.Numerics.Vector3 position)
	{
		if (!_ecsWorld.IsAlive(buildingEntity)) return;

		var rp = new RallyPoint(position);
		if (_ecsWorld.Has<RallyPoint>(buildingEntity))
			_ecsWorld.Set(buildingEntity, rp);
		else
			_ecsWorld.Add(buildingEntity, rp);
	}

	public bool TryUpgradeTower(Entity towerEntity, out int newLevel, out string newName)
	{
		newLevel = 1;
		newName = "";
		if (!_ecsWorld.IsAlive(towerEntity)) return false;

		int currentLevel = 1;
		if (_ecsWorld.Has<TowerUpgradeLevel>(towerEntity))
		{
			currentLevel = _ecsWorld.Get<TowerUpgradeLevel>(towerEntity).Value;
		}

		if (currentLevel >= 3) return false;

		newLevel = currentLevel + 1;
		_ecsWorld.Set(towerEntity, new TowerUpgradeLevel(newLevel));

		string baseName = "Spell Tower";
		if (_ecsWorld.Has<Name>(towerEntity))
		{
			var nameComp = _ecsWorld.Get<Name>(towerEntity);
			if (nameComp.Value.Contains("Orc")) baseName = "Orc Totem Tower";
		}
		newName = $"{baseName} (Lvl {newLevel})";
		_ecsWorld.Set(towerEntity, new Name(newName));

		if (_ecsWorld.Has<Health>(towerEntity))
		{
			var hp = _ecsWorld.Get<Health>(towerEntity);
			_ecsWorld.Set(towerEntity, new Health(hp.Current + 250f, hp.Max + 250f));
		}
		if (_ecsWorld.Has<Armor>(towerEntity))
		{
			var arm = _ecsWorld.Get<Armor>(towerEntity);
			_ecsWorld.Set(towerEntity, new Armor(arm.Value + 5f));
		}
		if (_ecsWorld.Has<Attack>(towerEntity))
		{
			var atk = _ecsWorld.Get<Attack>(towerEntity);
			_ecsWorld.Set(towerEntity, new Attack(atk.Damage + 10f, atk.Range, atk.Cooldown));
		}

		return true;
	}

	public void SetEntityPosition(Entity entity, System.Numerics.Vector3 position)
	{
		if (_ecsWorld.IsAlive(entity))
		{
			_ecsWorld.Set(entity, new Position(position));
		}
	}

	public string? ActiveSpellTargeting
	{
		get
		{
			var worldEntity = GetWorldEntity();
			return worldEntity != Entity.Null && _ecsWorld.Has<InputState>(worldEntity)
				? _ecsWorld.Get<InputState>(worldEntity).ActiveSpellTargeting
				: null;
		}
		set
		{
			var worldEntity = GetWorldEntity();
			if (worldEntity == Entity.Null || !_ecsWorld.Has<InputState>(worldEntity)) return;
			ref var state = ref _ecsWorld.Get<InputState>(worldEntity);
			state.ActiveSpellTargeting = value;
		}
	}

	public string? ActiveCommandTargeting
	{
		get
		{
			var worldEntity = GetWorldEntity();
			return worldEntity != Entity.Null && _ecsWorld.Has<InputState>(worldEntity)
				? _ecsWorld.Get<InputState>(worldEntity).ActiveCommandTargeting
				: null;
		}
		set
		{
			var worldEntity = GetWorldEntity();
			if (worldEntity == Entity.Null || !_ecsWorld.Has<InputState>(worldEntity)) return;
			ref var state = ref _ecsWorld.Get<InputState>(worldEntity);
			state.ActiveCommandTargeting = value;
		}
	}

	public string? ActiveBuildingPlacementType
	{
		get
		{
			var worldEntity = GetWorldEntity();
			return worldEntity != Entity.Null && _ecsWorld.Has<InputState>(worldEntity)
				? _ecsWorld.Get<InputState>(worldEntity).ActiveBuildingPlacementType
				: null;
		}
		set
		{
			var worldEntity = GetWorldEntity();
			if (worldEntity == Entity.Null || !_ecsWorld.Has<InputState>(worldEntity)) return;
			ref var state = ref _ecsWorld.Get<InputState>(worldEntity);
			state.ActiveBuildingPlacementType = value;
		}
	}

	public bool ActivePingMode
	{
		get
		{
			var worldEntity = GetWorldEntity();
			return worldEntity != Entity.Null && _ecsWorld.Has<InputState>(worldEntity)
				&& _ecsWorld.Get<InputState>(worldEntity).ActivePingMode;
		}
		set
		{
			var worldEntity = GetWorldEntity();
			if (worldEntity == Entity.Null || !_ecsWorld.Has<InputState>(worldEntity)) return;
			ref var state = ref _ecsWorld.Get<InputState>(worldEntity);
			state.ActivePingMode = value;
		}
	}

	public int GetCycleSelectionIndex(int selectedCount)
	{
		if (selectedCount == 0) return 0;
		var worldEntity = GetWorldEntity();
		if (worldEntity == Entity.Null || !_ecsWorld.Has<InputState>(worldEntity)) return 0;
		int val = _ecsWorld.Get<InputState>(worldEntity).CycleSelectionIndex;
		return Math.Clamp(val, 0, selectedCount - 1);
	}

	public void SetCycleSelectionIndex(int value, int selectedCount)
	{
		var worldEntity = GetWorldEntity();
		if (worldEntity == Entity.Null || !_ecsWorld.Has<InputState>(worldEntity)) return;
		ref var state = ref _ecsWorld.Get<InputState>(worldEntity);
		state.CycleSelectionIndex = selectedCount > 0 ? Math.Clamp(value, 0, selectedCount - 1) : 0;
	}
}
