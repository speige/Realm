using Godot;
using Arch.Core;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.MapAPI;
using System;
using System.Collections.Generic;
using static Realm.Ecs.Common.ResourceConstants;

public partial class GameHost
{
	private readonly Dictionary<int, UnitWrapper> _unitWrapperCache = new();

	public UnitWrapper GetUnitWrapper(Entity entity)
	{
		if (!EcsWorld.IsAlive(entity))
		{
			throw new ArgumentException("Entity is not alive", nameof(entity));
		}
		if (_unitWrapperCache.TryGetValue(entity.Id, out var wrapper))
		{
			return wrapper;
		}
		wrapper = new UnitWrapper(entity, EcsWorld);
		_unitWrapperCache[entity.Id] = wrapper;
		return wrapper;
	}

	private void KillUnit(Unit3D unit)
	{
		IUnit killer = null;
		if (EcsWorld.IsAlive(unit.Entity))
		{
			if (EcsWorld.Has<LastAttacker>(unit.Entity))
			{
				var killerEntity = EcsWorld.Get<LastAttacker>(unit.Entity).Value;
				if (EcsWorld.IsAlive(killerEntity))
				{
					killer = GetUnitWrapper(killerEntity);
				}
			}
			OnUnitDied?.Invoke(GetUnitWrapper(unit.Entity), killer);

			int id = unit.Entity.Id;
			_unitWrapperCache.Remove(id);
		}

		SelectedUnits.Remove(unit);
		AllUnits.Remove(unit);
		if (unit.UnitId == "castle")
		{
			_castlesList.Remove(unit);
		}
		if (unit.IsBuilding)
		{
			RebakeNavMesh();
		}

		if (unit.IsEnemy && UnitRegistry.TryGetValue(unit.UnitId, out var bountyMeta) && bountyMeta.GoldBounty > 0f)
		{
			if (EcsWorld.IsAlive(_playerEntity) && EcsWorld.Has<PlayerResources>(_playerEntity))
			{
				EcsWorld.Mutate<PlayerResources>(_playerEntity, (ref PlayerResources r) =>
				{
					if (r.Value.TryGetValue(_goldResourceId, out var currentGold))
						r.Value[_goldResourceId] = (int)Math.Min(ResourceCap, currentGold + bountyMeta.GoldBounty);
				});
				InGameHUD.Instance?.RefreshUI(SelectedUnits);
			}
		}

		if (!unit.IsEnemy && UnitRegistry.TryGetValue(unit.UnitId, out var killMeta))
		{
			if (unit.UnitId == "castle")
			{
				MaxPopulation = Math.Max(0, MaxPopulation - 20);
			}
			if (!EcsWorld.Has<BypassPopulationTag>(unit.Entity))
			{
				CurrentPopulation = Math.Max(0, CurrentPopulation - killMeta.PopCost);
			}
		}

		if (_multiplayerActive)
		{
			if (_clientToServerEntityMap.TryGetValue(unit.Entity.Id, out int serverId))
			{
				_serverToClientEntityMap.Remove(serverId);
			}
			_clientToServerEntityMap.Remove(unit.Entity.Id);
		}

		if (EcsWorld.IsAlive(unit.Entity))
		{
			EcsWorld.Destroy(unit.Entity);
		}

		var tween = CreateTween();
		tween.SetParallel(true);
		tween.TweenProperty(unit, "position:y", -3.0f, 1.0f);
		tween.TweenProperty(unit, "scale", Vector3.Zero, 1.0f);
		tween.Chain().TweenCallback(Callable.From(unit.QueueFree));

		if (unit.UnitId == "castle")
		{
			if (unit.IsEnemy)
			{
				GD.Print("[GameHost] Enemy Castle destroyed! Player wins!");
				Callable.From(() => UIManager.Instance?.TransitionTo(GameScreen.GameOver, true)).CallDeferred();
			}
			else
			{
				GD.Print("[GameHost] Player Castle destroyed! Player loses!");
				Callable.From(() => UIManager.Instance?.TransitionTo(GameScreen.GameOver, false)).CallDeferred();
			}
		}

		GD.Print($"Unit {unit.Name} died.");
	}

	private void DepleteProp(Prop3D prop)
	{
		if (GodotObject.IsInstanceValid(prop))
		{
			AllProps.Remove(prop);
			EntityToProp3D.Remove(prop.Entity);
			if (EcsWorld.IsAlive(prop.Entity))
			{
				EcsWorld.Destroy(prop.Entity);
			}
			prop.QueueFree();
			RebakeNavMesh();
		}
	}

	public bool FastBuildEnabled { get; set; } = false;

	private const float BaseConstructionWorkRatePerSecond = 1f / 20f;
	private float ConstructionWorkRatePerSecond => FastBuildEnabled ? BaseConstructionWorkRatePerSecond * 10f : BaseConstructionWorkRatePerSecond;

	private readonly List<(Entity Worker, BuildTask UpdatedTask)> _pendingBuildTaskUpdates = new();
	private readonly List<Entity> _completedBuildings = new();

	private readonly Dictionary<int, List<MeshInstance3D>> _buildQueueGhosts = new();

	internal void TickConstructionSystem(float fDelta)
	{
		_pendingBuildTaskUpdates.Clear();
		_completedBuildings.Clear();

		var workerQuery = QueryCache.AllPositionAndBuildTaskNoneDeadQuery;
		EcsWorld.Query(in workerQuery, (Entity workerEntity, ref Position workerPos, ref BuildTask buildTask) =>
		{
			if (!EcsWorld.IsAlive(buildTask.BuildingEntity)) return;

			var buildingPos = EcsWorld.Has<Position>(buildTask.BuildingEntity)
				? EcsWorld.Get<Position>(buildTask.BuildingEntity).Value
				: workerPos.Value;

			float distSq = System.Numerics.Vector3.DistanceSquared(workerPos.Value, buildingPos);
			bool inRange = distSq <= 16f;

			if (!inRange)
			{
				if (!EcsWorld.Has<MoveTo>(workerEntity))
				{
					EcsWorld.Add(workerEntity, new MoveTo(buildingPos));
				}
				return;
			}

			if (EcsWorld.Has<MoveTo>(workerEntity))
			{
				EcsWorld.Remove<MoveTo>(workerEntity);
			}

			if (EcsWorld.Has<DefinitionId>(buildTask.BuildingEntity))
			{
				string bType = EcsWorld.Get<DefinitionId>(buildTask.BuildingEntity).Value;
				if (UnitRegistry.TryGetValue(bType, out var m) && !TryGetUnit3D(buildTask.BuildingEntity, out _))
				{
					string modelPath = !string.IsNullOrEmpty(m.ModelPath) ? m.ModelPath : GetFallbackModelPath(bType, true);
					SpawnUnit3D(buildTask.BuildingEntity, bType, modelPath, new Godot.Vector3(buildingPos.X, buildingPos.Y, buildingPos.Z), true, false);

					if (TryGetUnit3D(buildTask.BuildingEntity, out var bNode) && GodotObject.IsInstanceValid(bNode))
					{
						bNode.Modulate = new Godot.Color(1f, 1f, 1f, 0.4f);
					}
				}
			}

			float progressGain = ConstructionWorkRatePerSecond * fDelta;
			var updatedTask = new BuildTask(buildTask.BuildingEntity, buildTask.TotalBuildTime)
			{
				Progress = buildTask.Progress + progressGain
			};
			_pendingBuildTaskUpdates.Add((workerEntity, updatedTask));

			if (EcsWorld.Has<ConstructionState>(buildTask.BuildingEntity))
			{
				ref var constructionState = ref EcsWorld.Get<ConstructionState>(buildTask.BuildingEntity);
				constructionState.Progress = Mathf.Min(constructionState.Progress + progressGain, constructionState.TotalBuildTime);

				if (constructionState.Progress >= constructionState.TotalBuildTime && !_completedBuildings.Contains(buildTask.BuildingEntity))
				{
					_completedBuildings.Add(buildTask.BuildingEntity);
				}
			}
		});

		foreach (var (workerEntity, updatedTask) in _pendingBuildTaskUpdates)
		{
			if (EcsWorld.IsAlive(workerEntity))
			{
				EcsWorld.Set(workerEntity, updatedTask);

				if (updatedTask.Progress >= updatedTask.TotalBuildTime && EcsWorld.Has<BuildTask>(workerEntity))
				{
					EcsWorld.Remove<BuildTask>(workerEntity);

					if (EcsWorld.Has<BuildQueue>(workerEntity))
					{
						ref var buildQueue = ref EcsWorld.Get<BuildQueue>(workerEntity);
						if (buildQueue.TryDequeue(out string nextType, out var nextPos, out Arch.Core.Entity nextTarget))
						{
							if (nextTarget != Entity.Null && EcsWorld.IsAlive(nextTarget) && EcsWorld.Has<ConstructionState>(nextTarget))
							{
								var cState = EcsWorld.Get<ConstructionState>(nextTarget);
								var newTask = new BuildTask(nextTarget, cState.TotalBuildTime);
								newTask.Progress = cState.Progress;
								EcsWorld.Add(workerEntity, newTask);
								var moveTo = new MoveTo(new System.Numerics.Vector3(nextPos.X, nextPos.Y, nextPos.Z));
								if (EcsWorld.Has<MoveTo>(workerEntity)) EcsWorld.Set(workerEntity, moveTo);
								else EcsWorld.Add(workerEntity, moveTo);
							}
							else
							{
								AssignBuildTaskToWorker(workerEntity, nextType, nextPos);
							}
						}
						else
						{
							EcsWorld.Remove<BuildQueue>(workerEntity);
						}
					}
				}
			}
		}

		foreach (var buildingEntity in _completedBuildings)
		{
			if (!EcsWorld.IsAlive(buildingEntity)) continue;

			if (EcsWorld.Has<UnderConstruction>(buildingEntity))
			{
				EcsWorld.Remove<UnderConstruction>(buildingEntity);
			}

			if (TryGetUnit3D(buildingEntity, out var buildingNode) && GodotObject.IsInstanceValid(buildingNode))
			{
				buildingNode.Modulate = new Godot.Color(1f, 1f, 1f, 1f);
			}

			InGameHUD.Instance?.ShowFeedbackText("Construction complete!", new Godot.Color(0.3f, 0.9f, 0.4f));
			InGameHUD.Instance?.RefreshUI(SelectedUnits);
		}

		if (_pendingBuildTaskUpdates.Count > 0)
		{
			InGameHUD.Instance?.RefreshUI(SelectedUnits);
		}

		UpdateBuildQueueGhosts();
	}

	private void UpdateBuildQueueGhosts()
	{
		var workerQuery = QueryCache.AllPositionAndBuildTaskNoneDeadQuery;

		var activeWorkerIds = new System.Collections.Generic.HashSet<int>();
		EcsWorld.Query(in workerQuery, (Entity workerEntity, ref BuildTask _) =>
		{
			if (!EcsWorld.IsAlive(workerEntity)) return;
			activeWorkerIds.Add(workerEntity.Id);

			int queueCount = EcsWorld.Has<BuildQueue>(workerEntity)
				? EcsWorld.Get<BuildQueue>(workerEntity).Count
				: 0;

			if (!_buildQueueGhosts.TryGetValue(workerEntity.Id, out var ghosts))
			{
				ghosts = new List<MeshInstance3D>();
				_buildQueueGhosts[workerEntity.Id] = ghosts;
			}

			while (ghosts.Count > queueCount)
			{
				int last = ghosts.Count - 1;
				if (GodotObject.IsInstanceValid(ghosts[last]))
					ghosts[last].QueueFree();
				ghosts.RemoveAt(last);
			}

			if (queueCount == 0) return;

			ref var queue = ref EcsWorld.Get<BuildQueue>(workerEntity);
			for (int slotIndex = 0; slotIndex < queueCount; slotIndex++)
			{
				queue.PeekAt(slotIndex, out string? buildType, out var queuedPos);
				if (buildType == null) continue;

				var worldPos = new Godot.Vector3(queuedPos.X, GetTerrainHeightAt(new Godot.Vector3(queuedPos.X, 0, queuedPos.Z)), queuedPos.Z);

				if (slotIndex >= ghosts.Count)
				{
					ghosts.Add(CreateGhostMesh(buildType));
				}
				else if (!GodotObject.IsInstanceValid(ghosts[slotIndex]))
				{
					ghosts[slotIndex] = CreateGhostMesh(buildType);
				}

				ghosts[slotIndex].GlobalPosition = worldPos;
			}
		});

		var toRemove = new List<int>();
		foreach (var kv in _buildQueueGhosts)
		{
			if (!activeWorkerIds.Contains(kv.Key))
			{
				foreach (var ghost in kv.Value)
					if (GodotObject.IsInstanceValid(ghost)) ghost.QueueFree();
				toRemove.Add(kv.Key);
			}
		}
		foreach (var id in toRemove)
			_buildQueueGhosts.Remove(id);
	}

	private MeshInstance3D CreateGhostMesh(string buildType)
	{
		var mesh = new MeshInstance3D();
		var box = new BoxMesh();
		box.Size = buildType == "castle" ? new Godot.Vector3(10f, 6f, 10f) : new Godot.Vector3(3.2f, 4f, 3.2f);
		mesh.Mesh = box;

		var mat = new StandardMaterial3D();
		mat.AlbedoColor = new Godot.Color(1.0f, 0.65f, 0.1f, 0.35f);
		mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		mat.EmissionEnabled = true;
		mat.Emission = new Godot.Color(1.0f, 0.5f, 0.05f) * 0.25f;
		mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
		mesh.MaterialOverride = mat;

		AddChild(mesh);
		return mesh;
	}

	internal void ClearAllBuildQueueGhosts()
	{
		foreach (var kv in _buildQueueGhosts)
			foreach (var ghost in kv.Value)
				if (GodotObject.IsInstanceValid(ghost)) ghost.QueueFree();
		_buildQueueGhosts.Clear();
	}

	internal void AssignBuildTaskToWorker(Entity workerEntity, string buildType, System.Numerics.Vector3 targetPos)
	{
		if (!UnitRegistry.TryGetValue(buildType, out var meta)) return;
		float buildTime = meta.ProductionTime > 0f ? meta.ProductionTime : 30f;

		var playerOwner = _playerEntity.AsPlayerEntity(EcsWorld);
		string modelPath = !string.IsNullOrEmpty(meta.ModelPath) ? meta.ModelPath : GetFallbackModelPath(buildType, true);

		var bldEntity = CreateEcsUnit(buildType, meta.Name, meta.MaxHp, meta.Damage, meta.Range, meta.Armor, 0f,
			new Godot.Vector3(targetPos.X, targetPos.Y, targetPos.Z), playerOwner);

		EcsWorld.Add(bldEntity, new ConstructionState(buildTime));
		EcsWorld.Add(bldEntity, new UnderConstruction());

		// Removing immediate SpawnUnit3D so it spawns when worker arrives

		var buildTask = new BuildTask(bldEntity, buildTime);
		if (EcsWorld.Has<BuildTask>(workerEntity))
			EcsWorld.Set(workerEntity, buildTask);
		else
			EcsWorld.Add(workerEntity, buildTask);

		var buildingPos = new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z);
		if (!EcsWorld.Has<MoveTo>(workerEntity))
			EcsWorld.Add(workerEntity, new MoveTo(buildingPos));
		else
			EcsWorld.Set(workerEntity, new MoveTo(buildingPos));
	}

	private void UpdateVisualNodesFromEcs(float fDelta)
	{
		var query = Realm.Ecs.Common.QueryCache.AllPositionAndDefinitionIdQuery;
		EcsWorld.Query(in query, (Entity entity, ref Position pos) =>
		{
			if (TryGetUnit3D(entity, out var unit3D) && GodotObject.IsInstanceValid(unit3D))
			{
				Vector3 nextPos = new Vector3(pos.Value.X, pos.Value.Y, pos.Value.Z);
				unit3D.GlobalPosition = nextPos;

				Vector3 velVec = Vector3.Zero;
				if (EcsWorld.Has<Velocity>(entity))
				{
					var vel = EcsWorld.Get<Velocity>(entity);
					velVec = new Vector3(vel.Value.X, vel.Value.Y, vel.Value.Z);
				}

				if (!EcsWorld.Has<MoveTo>(entity))
				{
					velVec = Vector3.Zero;
					if (EcsWorld.Has<Velocity>(entity))
					{
						EcsWorld.Set(entity, new Velocity(System.Numerics.Vector3.Zero));
					}
				}

				unit3D.Velocity = velVec;

				Vector3 dir = unit3D.Velocity;
				bool hasDir = dir.LengthSquared() > 0.01f;
				if (!hasDir)
				{
					Vector3 lookTargetPos = Vector3.Zero;
					bool hasLookTarget = false;

					if (EcsWorld.Has<AttackTarget>(entity))
					{
						var targetEnt = EcsWorld.Get<AttackTarget>(entity).Target;
						if (EcsWorld.IsAlive(targetEnt) && EcsWorld.Has<Position>(targetEnt))
						{
							var tPosComp = EcsWorld.Get<Position>(targetEnt);
							lookTargetPos = new Vector3(tPosComp.Value.X, tPosComp.Value.Y, tPosComp.Value.Z);
							hasLookTarget = true;
						}
					}
					else if (EcsWorld.Has<HealingTarget>(entity))
					{
						var targetEnt = EcsWorld.Get<HealingTarget>(entity).Target;
						if (EcsWorld.IsAlive(targetEnt) && EcsWorld.Has<Position>(targetEnt))
						{
							var tPosComp = EcsWorld.Get<Position>(targetEnt);
							lookTargetPos = new Vector3(tPosComp.Value.X, tPosComp.Value.Y, tPosComp.Value.Z);
							hasLookTarget = true;
						}
					}
					else if (EcsWorld.Has<BuildTask>(entity))
					{
						var buildTask = EcsWorld.Get<BuildTask>(entity);
						if (EcsWorld.IsAlive(buildTask.BuildingEntity) && EcsWorld.Has<Position>(buildTask.BuildingEntity))
						{
							var bPos = EcsWorld.Get<Position>(buildTask.BuildingEntity);
							lookTargetPos = new Vector3(bPos.Value.X, bPos.Value.Y, bPos.Value.Z);
							hasLookTarget = true;
						}
					}

					if (hasLookTarget)
					{
						dir = (lookTargetPos - nextPos).Normalized();
						hasDir = dir.LengthSquared() > 0.01f;
					}
				}

				if (hasDir)
				{
					dir = dir.Normalized();
					float angle = Mathf.Atan2(-dir.X, -dir.Z) + Mathf.Pi;
					var rot = unit3D.Rotation;
					rot.Y = Mathf.LerpAngle(rot.Y, angle, 10f * fDelta);
					unit3D.Rotation = rot;

					Vector3 normal = Vector3.Up;
					if (GroundTerrain != null)
					{
						GroundTerrain.GetHeightAndNormal(nextPos.X, nextPos.Z, out _, out normal);
					}

					Vector3 forwardDir = new Vector3(-Mathf.Sin(unit3D.Rotation.Y), 0f, -Mathf.Cos(unit3D.Rotation.Y));
					Vector3 up = normal.Normalized();
					Vector3 right = forwardDir.Cross(up).Normalized();
					Vector3 forwardPerp = right.Cross(up).Normalized();
					Basis targetBasis = new Basis(right, up, forwardPerp);
					var qTarget = targetBasis.GetRotationQuaternion();
					var qCurrent = unit3D.Basis.GetRotationQuaternion();
					var qLerp = qCurrent.Slerp(qTarget, 10f * fDelta);
					unit3D.Basis = new Basis(qLerp);
				}

				bool isLaborAnimating = (EcsWorld.Has<Gatherer>(entity) && !EcsWorld.Get<Gatherer>(entity).ReturningToBase)
					|| (EcsWorld.Has<BuildTask>(entity) && !EcsWorld.Has<MoveTo>(entity));

				if (isLaborAnimating)
				{
					var state = EcsWorld.Get<WorldState>(_worldEntity);
					float gameElapsed = state.GameElapsedTime;
					float pulse = 1.0f + Mathf.Sin(gameElapsed * 10f) * 0.1f;
					unit3D.Scale = new Vector3(pulse * 0.9f, (2.0f - pulse) * 0.9f, pulse * 0.9f);
				}
				else
				{
					float scaleVal = EcsWorld.Has<CollisionScale>(entity) ? EcsWorld.Get<CollisionScale>(entity).Value : 1.0f;
					unit3D.Scale = Vector3.One * scaleVal;
				}

				unit3D.PlayAnimation(DetermineUnitAnimation(entity));
			}
		});

		var buildingQuery = QueryCache.AllBuildingAndConstructionStateAndOwnerNoneDeadQuery;
		EcsWorld.Query(in buildingQuery, (Entity entity, ref ConstructionState construction) =>
		{
			if (TryGetUnit3D(entity, out var buildingNode) && GodotObject.IsInstanceValid(buildingNode))
			{
				float alpha = Mathf.Lerp(0.3f, 1.0f, construction.Progress / Mathf.Max(construction.TotalBuildTime, 0.001f));
				buildingNode.Modulate = new Color(1f, 1f, 1f, alpha);
			}
		});
	}

	private string DetermineUnitAnimation(Entity entity)
	{
		if (Realm.Godot.ReplaySystem.ReplayPlaybackManager.Instance.IsPlayingReplay)
		{
			if (EcsWorld.Has<Realm.Ecs.Components.Meta.ReplayAnimationState>(entity))
			{
				return EcsWorld.Get<Realm.Ecs.Components.Meta.ReplayAnimationState>(entity).Animation;
			}
			return "Idle";
		}

		if (EcsWorld.Has<Dead>(entity))
			return "Death";

		bool isMoving = EcsWorld.Has<MoveTo>(entity) && EcsWorld.Has<Velocity>(entity)
			&& EcsWorld.Get<Velocity>(entity).Value.LengthSquared() > 0.01f;

		if (isMoving)
			return "Walk";

		if (EcsWorld.Has<AttackTarget>(entity))
			return "Attack";

		if (EcsWorld.Has<HealingTarget>(entity))
			return "Spell_Cast";

		if (EcsWorld.Has<Gatherer>(entity) && !EcsWorld.Get<Gatherer>(entity).ReturningToBase)
			return "Labor";

		if (EcsWorld.Has<BuildTask>(entity))
		{
			var task = EcsWorld.Get<BuildTask>(entity);
			var target = task.BuildingEntity;
			if (EcsWorld.IsAlive(target) && EcsWorld.Has<Position>(target))
			{
				var tPos = EcsWorld.Get<Position>(target).Value;
				var wPos = EcsWorld.Has<Position>(entity) ? EcsWorld.Get<Position>(entity).Value : System.Numerics.Vector3.Zero;
				if (System.Numerics.Vector3.Distance(wPos, tPos) < 4.0f)
				{
					return "Labor";
				}
				return "Walk";
			}
			return "Labor";
		}

		return "Idle";
	}
}
