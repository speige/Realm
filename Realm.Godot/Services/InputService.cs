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
	private readonly WorldAccessor EcsWorldAccessor;
	private World EcsWorld => EcsWorldAccessor.Current;
	private readonly TechTreeService _techTreeService;
	private int _buildingCycleIndex;

	public InputService(WorldAccessor ecsWorldAccessor, TechTreeService techTreeService)
	{
		EcsWorldAccessor = ecsWorldAccessor;
		_techTreeService = techTreeService;
	}

	private Entity GetWorldEntity()
	{
		Entity worldEntity = Entity.Null;
		var query = QueryCache.AllInputStateQuery;
		EcsWorld.Query(in query, entity => worldEntity = entity);
		return worldEntity;
	}

	private ref InputState GetInputState(Entity worldEntity)
	{
		return ref EcsWorld.Get<InputState>(worldEntity);
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
		if (EcsWorld.Has<MoveTo>(entity)) EcsWorld.Remove<MoveTo>(entity);
		if (EcsWorld.Has<PathFollow>(entity)) EcsWorld.Remove<PathFollow>(entity);
		if (EcsWorld.Has<AttackTarget>(entity)) EcsWorld.Remove<AttackTarget>(entity);
		if (EcsWorld.Has<Realm.Ecs.Components.Movement.AttackMove>(entity)) EcsWorld.Remove<Realm.Ecs.Components.Movement.AttackMove>(entity);
		if (EcsWorld.Has<Realm.Ecs.Components.Movement.HoldPosition>(entity)) EcsWorld.Remove<Realm.Ecs.Components.Movement.HoldPosition>(entity);
		if (EcsWorld.Has<Realm.Ecs.Components.Movement.Follow>(entity)) EcsWorld.Remove<Realm.Ecs.Components.Movement.Follow>(entity);
		if (EcsWorld.Has<Patrol>(entity)) EcsWorld.Remove<Patrol>(entity);
		if (EcsWorld.Has<HealingTarget>(entity)) EcsWorld.Remove<HealingTarget>(entity);
		if (EcsWorld.Has<WaypointQueue>(entity)) EcsWorld.Remove<WaypointQueue>(entity);
		if (EcsWorld.Has<Gatherer>(entity)) EcsWorld.Remove<Gatherer>(entity);
		if (EcsWorld.Has<Realm.Ecs.Components.Resources.BuildTask>(entity)) EcsWorld.Remove<Realm.Ecs.Components.Resources.BuildTask>(entity);
		if (EcsWorld.Has<Realm.Ecs.Components.Resources.BuildQueue>(entity)) EcsWorld.Remove<Realm.Ecs.Components.Resources.BuildQueue>(entity);
	}

	public void IssueMoveCommand(List<Entity> selectedEntities, System.Numerics.Vector3 targetPos)
	{
		int unitIndex = 0;
		int cols = (int)Math.Ceiling(Math.Sqrt(selectedEntities.Count));
		float spacing = 2.2f;

		System.Numerics.Vector3 groupCenter = System.Numerics.Vector3.Zero;
		int movableCount = 0;
		foreach (var entity in selectedEntities)
		{
			if (!EcsWorld.IsAlive(entity)) continue;
			if (EcsWorld.Has<Building>(entity)) continue;
			bool isEnemy = EcsWorld.Has<UnitFaction>(entity) && EcsWorld.Get<UnitFaction>(entity).IsEnemy;
			if (isEnemy) continue;
			if (EcsWorld.Has<Realm.Ecs.Components.Tags.Movable>(entity) && EcsWorld.Has<Position>(entity))
			{
				groupCenter += EcsWorld.Get<Position>(entity).Value;
				movableCount++;
			}
		}
		if (movableCount > 0)
		{
			groupCenter /= movableCount;
		}

		System.Numerics.Vector3 moveDir = targetPos - groupCenter;
		moveDir.Y = 0f;
		if (moveDir.LengthSquared() > 0.01f)
		{
			moveDir = System.Numerics.Vector3.Normalize(moveDir);
		}
		else
		{
			moveDir = new System.Numerics.Vector3(0f, 0f, -1f);
		}
		System.Numerics.Vector3 right = new System.Numerics.Vector3(-moveDir.Z, 0f, moveDir.X);

		foreach (var entity in selectedEntities)
		{
			if (!EcsWorld.IsAlive(entity)) continue;
			bool isBuilding = EcsWorld.Has<Building>(entity);
			bool isEnemy = EcsWorld.Has<UnitFaction>(entity) && EcsWorld.Get<UnitFaction>(entity).IsEnemy;
			if (isBuilding || isEnemy) continue;

			ClearUnitOrders(entity);

			if (EcsWorld.Has<Realm.Ecs.Components.Tags.Movable>(entity))
			{
				int row = unitIndex / cols;
				int col = unitIndex % cols;
				float offsetX = (col - cols * 0.5f + 0.5f) * spacing;
				float offsetZ = -row * spacing;
				var scattered = targetPos + right * offsetX + moveDir * offsetZ;

				var moveTo = new MoveTo(scattered);
				if (EcsWorld.Has<MoveTo>(entity))
					EcsWorld.Set(entity, moveTo);
				else
					EcsWorld.Add(entity, moveTo);

				unitIndex++;
			}
		}
	}

	public void IssueMoveCommandQueued(List<Entity> selectedEntities, System.Numerics.Vector3 targetPos)
	{
		int unitIndex = 0;
		int cols = (int)Math.Ceiling(Math.Sqrt(selectedEntities.Count));
		float spacing = 2.2f;

		System.Numerics.Vector3 groupCenter = System.Numerics.Vector3.Zero;
		int movableCount = 0;
		foreach (var entity in selectedEntities)
		{
			if (!EcsWorld.IsAlive(entity)) continue;
			if (EcsWorld.Has<Building>(entity)) continue;
			bool isEnemy = EcsWorld.Has<UnitFaction>(entity) && EcsWorld.Get<UnitFaction>(entity).IsEnemy;
			if (isEnemy) continue;
			if (EcsWorld.Has<Realm.Ecs.Components.Tags.Movable>(entity) && EcsWorld.Has<Position>(entity))
			{
				groupCenter += EcsWorld.Get<Position>(entity).Value;
				movableCount++;
			}
		}
		if (movableCount > 0)
		{
			groupCenter /= movableCount;
		}

		System.Numerics.Vector3 moveDir = targetPos - groupCenter;
		moveDir.Y = 0f;
		if (moveDir.LengthSquared() > 0.01f)
		{
			moveDir = System.Numerics.Vector3.Normalize(moveDir);
		}
		else
		{
			moveDir = new System.Numerics.Vector3(0f, 0f, -1f);
		}
		System.Numerics.Vector3 right = new System.Numerics.Vector3(-moveDir.Z, 0f, moveDir.X);

		foreach (var entity in selectedEntities)
		{
			if (!EcsWorld.IsAlive(entity)) continue;
			bool isBuilding = EcsWorld.Has<Building>(entity);
			bool isEnemy = EcsWorld.Has<UnitFaction>(entity) && EcsWorld.Get<UnitFaction>(entity).IsEnemy;
			if (isBuilding || isEnemy) continue;
			if (!EcsWorld.Has<Realm.Ecs.Components.Tags.Movable>(entity)) continue;

			bool alreadyMoving = EcsWorld.Has<MoveTo>(entity);
			if (!alreadyMoving)
			{
				ClearUnitOrders(entity);
			}

			int row = unitIndex / cols;
			int col = unitIndex % cols;
			float offsetX = (col - cols * 0.5f + 0.5f) * spacing;
			float offsetZ = -row * spacing;
			var scattered = targetPos + right * offsetX + moveDir * offsetZ;

			if (alreadyMoving)
			{
				if (EcsWorld.Has<WaypointQueue>(entity))
				{
					var q = EcsWorld.Get<WaypointQueue>(entity);
					q.Add(scattered);
					EcsWorld.Set(entity, q);
				}
				else
				{
					var q = new WaypointQueue(scattered);
					EcsWorld.Add(entity, q);
				}
			}
			else
			{
				var moveTo = new MoveTo(scattered);
				if (EcsWorld.Has<MoveTo>(entity))
					EcsWorld.Set(entity, moveTo);
				else
					EcsWorld.Add(entity, moveTo);
			}

			unitIndex++;
		}
	}

	public void IssueAttackCommand(List<Entity> selectedEntities, Entity targetEntity, bool isQueued = false)
	{
		foreach (var entity in selectedEntities)
		{
			if (!EcsWorld.IsAlive(entity)) continue;
			bool isBuilding = EcsWorld.Has<Building>(entity);
			bool isEnemy = EcsWorld.Has<UnitFaction>(entity) && EcsWorld.Get<UnitFaction>(entity).IsEnemy;
			if (isBuilding || isEnemy) continue;

			if (!isQueued)
			{
				ClearUnitOrders(entity);
			}

			var attackTarget = new AttackTarget(targetEntity);
			if (EcsWorld.Has<AttackTarget>(entity))
				EcsWorld.Set(entity, attackTarget);
			else
				EcsWorld.Add(entity, attackTarget);
		}
	}

	public void IssueFollowCommand(List<Entity> selectedEntities, Entity targetEntity, bool isQueued = false)
	{
		foreach (var entity in selectedEntities)
		{
			if (!EcsWorld.IsAlive(entity) || entity == targetEntity) continue;
			bool isBuilding = EcsWorld.Has<Building>(entity);
			bool isEnemy = EcsWorld.Has<UnitFaction>(entity) && EcsWorld.Get<UnitFaction>(entity).IsEnemy;
			if (isBuilding || isEnemy) continue;

			ClearUnitOrders(entity);

			if (EcsWorld.Has<DefinitionId>(entity) && EcsWorld.Get<DefinitionId>(entity).Value == "priest")
			{
				var healTarget = new HealingTarget(targetEntity);
				EcsWorld.Add(entity, healTarget);
			}
			else if (EcsWorld.Has<Realm.Ecs.Components.Tags.Movable>(entity))
			{
				var follow = new Follow(targetEntity);
				if (EcsWorld.Has<Follow>(entity))
					EcsWorld.Set(entity, follow);
				else
					EcsWorld.Add(entity, follow);
			}
		}
	}

	public void IssuePatrolCommand(List<Entity> selectedEntities, System.Numerics.Vector3 targetPos, bool isQueued = false)
	{
		int unitIndex = 0;
		int cols = (int)Math.Ceiling(Math.Sqrt(selectedEntities.Count));
		float spacing = 2.2f;

		foreach (var entity in selectedEntities)
		{
			if (!EcsWorld.IsAlive(entity)) continue;
			bool isBuilding = EcsWorld.Has<Building>(entity);
			bool isEnemy = EcsWorld.Has<UnitFaction>(entity) && EcsWorld.Get<UnitFaction>(entity).IsEnemy;
			if (isBuilding || isEnemy) continue;

			if (!isQueued)
			{
				ClearUnitOrders(entity);
			}

			if (EcsWorld.Has<Realm.Ecs.Components.Tags.Movable>(entity))
			{
				int row = unitIndex / cols;
				int col = unitIndex % cols;
				float offsetX = (col - cols * 0.5f + 0.5f) * spacing;
				float offsetZ = row * spacing;

				var unitPos = EcsWorld.Has<Position>(entity) ? EcsWorld.Get<Position>(entity).Value : System.Numerics.Vector3.Zero;
				var patrolA = new System.Numerics.Vector3(unitPos.X, unitPos.Y, unitPos.Z);
				var patrolB = new System.Numerics.Vector3(targetPos.X + offsetX, targetPos.Y, targetPos.Z + offsetZ);

				var patrol = new Patrol(patrolA, patrolB);
				if (EcsWorld.Has<Patrol>(entity)) EcsWorld.Set(entity, patrol);
				else EcsWorld.Add(entity, patrol);

				var moveTo = new MoveTo(patrolB);
				if (EcsWorld.Has<MoveTo>(entity)) EcsWorld.Set(entity, moveTo);
				else EcsWorld.Add(entity, moveTo);

				unitIndex++;
			}
		}
	}

	public void IssueAttackMoveCommand(List<Entity> selectedEntities, System.Numerics.Vector3 targetPos, bool isQueued = false)
	{
		foreach (var entity in selectedEntities)
		{
			if (!EcsWorld.IsAlive(entity)) continue;
			bool isBuilding = EcsWorld.Has<Building>(entity);
			bool isEnemy = EcsWorld.Has<UnitFaction>(entity) && EcsWorld.Get<UnitFaction>(entity).IsEnemy;
			if (isBuilding || isEnemy) continue;

			if (!isQueued)
			{
				ClearUnitOrders(entity);
			}

			var attackMove = new AttackMove(targetPos);
			if (EcsWorld.Has<AttackMove>(entity))
				EcsWorld.Set(entity, attackMove);
			else
				EcsWorld.Add(entity, attackMove);

			var moveTo = new MoveTo(targetPos);
			if (EcsWorld.Has<MoveTo>(entity))
				EcsWorld.Set(entity, moveTo);
			else
				EcsWorld.Add(entity, moveTo);
		}
	}

	public void HoldSelectedUnits(List<Entity> selectedEntities)
	{
		foreach (var entity in selectedEntities)
		{
			if (!EcsWorld.IsAlive(entity)) continue;
			bool isBuilding = EcsWorld.Has<Building>(entity);
			bool isEnemy = EcsWorld.Has<UnitFaction>(entity) && EcsWorld.Get<UnitFaction>(entity).IsEnemy;
			if (isBuilding || isEnemy) continue;

			ClearUnitOrders(entity);

			if (!EcsWorld.Has<HoldPosition>(entity))
				EcsWorld.Add<HoldPosition>(entity);
		}
	}

	public void StopSelectedUnits(List<Entity> selectedEntities)
	{
		foreach (var entity in selectedEntities)
		{
			if (!EcsWorld.IsAlive(entity)) continue;
			bool isEnemy = EcsWorld.Has<UnitFaction>(entity) && EcsWorld.Get<UnitFaction>(entity).IsEnemy;
			if (isEnemy) continue;

			ClearUnitOrders(entity);
		}
	}

	public int CycleSelectionFocus(Entity worldEntity, int selectedCount, bool reverse)
	{
		if (selectedCount <= 1 || worldEntity == Entity.Null) return 0;

		ref var state = ref EcsWorld.Get<InputState>(worldEntity);
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
		var query = Realm.Ecs.Common.QueryCache.AllBuildingAndOwnerAndPositionNoneDeadQuery;
		EcsWorld.Query(in query, (Entity entity, ref Owner owner) =>
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
			if (EcsWorld.IsAlive(entity) && !EcsWorld.Has<Dead>(entity))
			{
				EcsWorld.Add<Dead>(entity);
			}
		}
	}

	public List<Entity> GetIdleUnitEntities(Entity playerEntity)
	{
		var result = new List<Entity>();
		var query = Realm.Ecs.Common.QueryCache.AllOwnerAndMovableNoneDeadAndBuildingQuery;
		EcsWorld.Query(in query, (Entity entity, ref Owner owner) =>
		{
			if (owner.PlayerEntity.Value == playerEntity)
			{
				bool hasMoveTo = EcsWorld.Has<MoveTo>(entity);
				bool hasAttackTarget = EcsWorld.Has<AttackTarget>(entity);
				bool hasAttackMove = EcsWorld.Has<AttackMove>(entity);
				bool isGathering = EcsWorld.Has<Gatherer>(entity) && EcsWorld.Get<Gatherer>(entity).TargetEntity != Entity.Null;

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
		var query = Realm.Ecs.Common.QueryCache.AllOwnerAndMovableNoneDeadAndBuildingQuery;
		EcsWorld.Query(in query, (Entity entity, ref Owner owner) =>
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
		var query = Realm.Ecs.Common.QueryCache.AllOwnerAndBuildingNoneDeadQuery;
		EcsWorld.Query(in query, (Entity entity, ref Owner owner) =>
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
		var query = Realm.Ecs.Common.QueryCache.AllPositionNoneDeadQuery;
		EcsWorld.Query(in query, (Entity entity, ref Position pos) =>
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

		var query = Realm.Ecs.Common.QueryCache.AllPositionNoneDeadQuery;
		EcsWorld.Query(in query, (Entity entity, ref Position entityPos) =>
		{
			if (entity == ignoreEntity) return;

			float r1 = checkRadius;
			float r2 = 1.2f;

			if (EcsWorld.Has<DefinitionId>(entity))
			{
				string defId = EcsWorld.Get<DefinitionId>(entity).Value;
				float scale = EcsWorld.Has<ModelScale>(entity) ? EcsWorld.Get<ModelScale>(entity).Value : 1.0f;
				r2 = GetPlacementRadius(defId, scale);
			}
			else if (EcsWorld.Has<PropIdentity>(entity))
			{
				string propId = EcsWorld.Get<PropIdentity>(entity).PropId;
				float scale = EcsWorld.Has<ModelScale>(entity) ? EcsWorld.Get<ModelScale>(entity).Value : 1.0f;
				r2 = GetPlacementRadius(propId, scale);
			}
			else
			{
				float scale = EcsWorld.Has<ModelScale>(entity) ? EcsWorld.Get<ModelScale>(entity).Value : 1.0f;
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
		if (!EcsWorld.IsAlive(playerEntity) || !EcsWorld.Has<SpellCooldowns>(playerEntity))
			return false;

		ref var cd = ref EcsWorld.Get<SpellCooldowns>(playerEntity);

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

		if (target == Entity.Null && EcsWorld.IsAlive(selectedUnitEntity))
		{
			bool isBuilding = EcsWorld.Has<Building>(selectedUnitEntity);
			bool isEnemy = EcsWorld.Has<UnitFaction>(selectedUnitEntity) && EcsWorld.Get<UnitFaction>(selectedUnitEntity).IsEnemy;
			if (!isBuilding && !isEnemy && EcsWorld.Has<Inventory>(selectedUnitEntity))
			{
				target = selectedUnitEntity;
			}
		}

		if (target != Entity.Null)
		{
			targetUnitEntity = target;
			ref var inv = ref EcsWorld.Get<Inventory>(target);
			inv.Potions += 1;
			return true;
		}

		return false;
	}

	private Entity FindClosestFriendlyCombatUnit(System.Numerics.Vector3 castlePos, Entity playerEntity, float maxDistance)
	{
		Entity closestUnit = Entity.Null;
		float closestDist = maxDistance;

		var query = Realm.Ecs.Common.QueryCache.AllPositionAndOwnerAndInventoryNoneDeadAndBuildingQuery;
		EcsWorld.Query(in query, (Entity entity, ref Position pos, ref Owner owner) =>
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
		if (!EcsWorld.IsAlive(unitEntity) || !EcsWorld.Has<Inventory>(unitEntity) || !EcsWorld.Has<Health>(unitEntity))
			return false;

		ref var inv = ref EcsWorld.Get<Inventory>(unitEntity);
		if (inv.Potions <= 0) return false;

		ref var hp = ref EcsWorld.Get<Health>(unitEntity);
		if (hp.Current >= hp.Max) return false;

		inv.Potions--;
		float oldCurrent = hp.Current;
		hp.Current = Math.Min(hp.Max, hp.Current + 50f);
		healedAmount = hp.Current - oldCurrent;

		return true;
	}

	public bool TryQueueUnitAtCastle(Entity playerEntity, Entity castleEntity, string unitId, int popCost, float productionTime)
	{
		if (!EcsWorld.IsAlive(playerEntity) || !EcsWorld.IsAlive(castleEntity)) return false;

		if (EcsWorld.Has<PlayerPopulation>(playerEntity))
		{
			ref var pop = ref EcsWorld.Get<PlayerPopulation>(playerEntity);
			if (popCost > 0 && pop.Current + popCost > pop.Max)
			{
				return false;
			}
			pop.Current += popCost;
		}

		ref var prod = ref EcsWorld.Has<ProductionQueue>(castleEntity)
			? ref EcsWorld.Get<ProductionQueue>(castleEntity)
			: ref CreateProductionQueue(castleEntity);

		if (prod.UnitIds.Count >= 5) return false;

		if (prod.UnitIds.Count == 0)
		{
			prod.BuildTime = productionTime;
		}
		prod.UnitIds.Add(unitId);
		EcsWorld.Set(castleEntity, prod);

		return true;
	}

	private ref ProductionQueue CreateProductionQueue(Entity castleEntity)
	{
		EcsWorld.Add(castleEntity, new ProductionQueue());
		return ref EcsWorld.Get<ProductionQueue>(castleEntity);
	}

	public bool CancelQueuedUnitAt(Entity castleEntity, int index, out string? cancelledUnitId, out string? nextUnitId)
	{
		cancelledUnitId = null;
		nextUnitId = null;

		if (!EcsWorld.IsAlive(castleEntity) || !EcsWorld.Has<ProductionQueue>(castleEntity))
			return false;

		var prod = EcsWorld.Get<ProductionQueue>(castleEntity);
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

		EcsWorld.Set(castleEntity, prod);
		return true;
	}

	public void SetRallyPoint(Entity buildingEntity, System.Numerics.Vector3 position, bool queue)
	{
		if (!EcsWorld.IsAlive(buildingEntity)) return;

		if (queue)
		{
			if (EcsWorld.Has<RallyPoint>(buildingEntity))
			{
				var rp = EcsWorld.Get<RallyPoint>(buildingEntity);
				rp.Add(position);
				EcsWorld.Set(buildingEntity, rp);
			}
			else
			{
				var rp = new RallyPoint(position);
				EcsWorld.Add(buildingEntity, rp);
			}
		}
		else
		{
			var rp = new RallyPoint(position);
			if (EcsWorld.Has<RallyPoint>(buildingEntity))
				EcsWorld.Set(buildingEntity, rp);
			else
				EcsWorld.Add(buildingEntity, rp);
		}
	}

	public bool TryUpgradeTower(Entity towerEntity, out int newLevel, out string newName)
	{
		newLevel = 1;
		newName = "";
		if (!EcsWorld.IsAlive(towerEntity)) return false;

		int currentLevel = 1;
		if (EcsWorld.Has<TowerUpgradeLevel>(towerEntity))
		{
			currentLevel = EcsWorld.Get<TowerUpgradeLevel>(towerEntity).Value;
		}

		if (currentLevel >= 3) return false;

		newLevel = currentLevel + 1;
		EcsWorld.Set(towerEntity, new TowerUpgradeLevel(newLevel));

		string baseName = "Spell Tower";
		if (EcsWorld.Has<Name>(towerEntity))
		{
			var nameComp = EcsWorld.Get<Name>(towerEntity);
			if (nameComp.Value.Contains("Orc")) baseName = "Orc Totem Tower";
		}
		newName = $"{baseName} (Lvl {newLevel})";
		EcsWorld.Set(towerEntity, new Name(newName));

		if (EcsWorld.Has<Health>(towerEntity))
		{
			var hp = EcsWorld.Get<Health>(towerEntity);
			EcsWorld.Set(towerEntity, new Health(hp.Current + 250f, hp.Max + 250f));
		}
		if (EcsWorld.Has<Armor>(towerEntity))
		{
			var arm = EcsWorld.Get<Armor>(towerEntity);
			EcsWorld.Set(towerEntity, new Armor(arm.Value + 5f));
		}
		if (EcsWorld.Has<Attack>(towerEntity))
		{
			var atk = EcsWorld.Get<Attack>(towerEntity);
			EcsWorld.Set(towerEntity, new Attack(atk.Damage + 10f, atk.Range, atk.Cooldown));
		}

		return true;
	}

	public void SetEntityPosition(Entity entity, System.Numerics.Vector3 position)
	{
		if (EcsWorld.IsAlive(entity))
		{
			EcsWorld.Set(entity, new Position(position));
		}
	}

	public string? ActiveSpellTargeting
	{
		get
		{
			var worldEntity = GetWorldEntity();
			return worldEntity != Entity.Null && EcsWorld.Has<InputState>(worldEntity)
				? EcsWorld.Get<InputState>(worldEntity).ActiveSpellTargeting
				: null;
		}
		set
		{
			var worldEntity = GetWorldEntity();
			if (worldEntity == Entity.Null || !EcsWorld.Has<InputState>(worldEntity)) return;
			ref var state = ref EcsWorld.Get<InputState>(worldEntity);
			state.ActiveSpellTargeting = value;
		}
	}

	public string? ActiveCommandTargeting
	{
		get
		{
			var worldEntity = GetWorldEntity();
			return worldEntity != Entity.Null && EcsWorld.Has<InputState>(worldEntity)
				? EcsWorld.Get<InputState>(worldEntity).ActiveCommandTargeting
				: null;
		}
		set
		{
			var worldEntity = GetWorldEntity();
			if (worldEntity == Entity.Null || !EcsWorld.Has<InputState>(worldEntity)) return;
			ref var state = ref EcsWorld.Get<InputState>(worldEntity);
			state.ActiveCommandTargeting = value;
		}
	}

	public string? ActiveBuildingPlacementType
	{
		get
		{
			var worldEntity = GetWorldEntity();
			return worldEntity != Entity.Null && EcsWorld.Has<InputState>(worldEntity)
				? EcsWorld.Get<InputState>(worldEntity).ActiveBuildingPlacementType
				: null;
		}
		set
		{
			var worldEntity = GetWorldEntity();
			if (worldEntity == Entity.Null || !EcsWorld.Has<InputState>(worldEntity)) return;
			ref var state = ref EcsWorld.Get<InputState>(worldEntity);
			state.ActiveBuildingPlacementType = value;
		}
	}

	public bool ActivePingMode
	{
		get
		{
			var worldEntity = GetWorldEntity();
			return worldEntity != Entity.Null && EcsWorld.Has<InputState>(worldEntity)
				&& EcsWorld.Get<InputState>(worldEntity).ActivePingMode;
		}
		set
		{
			var worldEntity = GetWorldEntity();
			if (worldEntity == Entity.Null || !EcsWorld.Has<InputState>(worldEntity)) return;
			ref var state = ref EcsWorld.Get<InputState>(worldEntity);
			state.ActivePingMode = value;
		}
	}

	public int GetCycleSelectionIndex(int selectedCount)
	{
		if (selectedCount == 0) return 0;
		var worldEntity = GetWorldEntity();
		if (worldEntity == Entity.Null || !EcsWorld.Has<InputState>(worldEntity)) return 0;
		int val = EcsWorld.Get<InputState>(worldEntity).CycleSelectionIndex;
		return Math.Clamp(val, 0, selectedCount - 1);
	}

	public void SetCycleSelectionIndex(int value, int selectedCount)
	{
		var worldEntity = GetWorldEntity();
		if (worldEntity == Entity.Null || !EcsWorld.Has<InputState>(worldEntity)) return;
		ref var state = ref EcsWorld.Get<InputState>(worldEntity);
		state.CycleSelectionIndex = selectedCount > 0 ? Math.Clamp(value, 0, selectedCount - 1) : 0;
	}
}
