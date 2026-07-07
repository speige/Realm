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

		if (SelectedUnits.Contains(unit))
		{
			SelectedUnits.Remove(unit);
		}
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
					if (r.Value.ContainsKey(_goldResourceId))
						r.Value[_goldResourceId] = (int)Math.Min(ResourceCap, r.Value[_goldResourceId] + bountyMeta.GoldBounty);
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

					if (hasLookTarget)
					{
						dir = (lookTargetPos - nextPos).Normalized();
						hasDir = dir.LengthSquared() > 0.01f;
					}
				}

				if (hasDir)
				{
					dir = dir.Normalized();
					float angle = Mathf.Atan2(-dir.X, -dir.Z);
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

				if (EcsWorld.Has<Gatherer>(entity) && !EcsWorld.Get<Gatherer>(entity).ReturningToBase)
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
			}
		});
	}
}
