using Godot;
using Arch.Core;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Services;
using Realm.Ecs.Common;
using Realm.MapAPI;
using System;
using System.Collections.Generic;
using DotRecast.Core.Numerics;

public partial class GameHost
{


	private static long GetCellKey(float x, float z)
	{
		int cx = (int)Math.Floor(x / CollisionCellSize);
		int cz = (int)Math.Floor(z / CollisionCellSize);
		return ((long)cx << 32) | (uint)cz;
	}

	private void RebuildSpatialGrid()
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

		foreach (var u in AllUnits)
		{
			if (!GodotObject.IsInstanceValid(u) || EcsWorld.Has<Dead>(u.Entity)) continue;
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

		foreach (var p in AllProps)
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
		if (!EcsWorld.IsAlive(follow.Target) || EcsWorld.Has<Dead>(follow.Target))
		{
			_tickFollowToStop.Add(entity);
			return;
		}

		var targetPosComp = EcsWorld.Get<Position>(follow.Target);
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
			Unit3D? nearestCastle = null;
			float nearestDist = float.MaxValue;
			foreach (var u in _castlesList)
			{
				var wOwner = EcsWorld.Get<Owner>(entity).PlayerEntity;
				var uOwner = EcsWorld.Get<Owner>(u.Entity).PlayerEntity;
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
				var ownerEntity = EcsWorld.Get<Owner>(entity).PlayerEntity.Value;
				if (EcsWorld.Has<PlayerResources>(ownerEntity))
				{
					ref var playerRes = ref EcsWorld.Get<PlayerResources>(ownerEntity);
					var resId = gather.ResourceType.AsResourceId(_definitionManager);
					if (playerRes.Value.ContainsKey(resId))
					{
						playerRes.Value[resId] = (int)Math.Min(ResourceCap, playerRes.Value[resId] + carry);
					}
				}
				
				if (ownerEntity == _playerEntity && InGameHUD.Instance != null)
				{
					string resType = gather.ResourceType;
					string resTypeUpper = resType.ToUpper();
					InGameHUD.Instance.CallDeferred(nameof(InGameHUD.ShowFeedbackText), $"+{carry:F0} {resTypeUpper} deposited", new Color(0.2f, 0.9f, 0.4f));
				}
				
				Prop3D? targetNode = null;
				if (EcsWorld.IsAlive(gather.TargetEntity) && EcsWorld.Has<Prop3D>(gather.TargetEntity))
				{
					targetNode = EcsWorld.Get<Prop3D>(gather.TargetEntity);
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
				if (!EcsWorld.Has<MoveTo>(entity))
				{
					var dest = nearestCastle.GlobalPosition;
					_tickGatherersToUpdate.Add((entity, gather, dest));
				}
			}
		}
		else
		{
			Prop3D? targetNode = null;
			if (EcsWorld.IsAlive(gather.TargetEntity) && EcsWorld.Has<Prop3D>(gather.TargetEntity))
			{
				targetNode = EcsWorld.Get<Prop3D>(gather.TargetEntity);
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
				if (EcsWorld.Has<MoveTo>(entity))
				{
					_tickEntitiesToStopGathering.Add(entity);
				}
				
				var newState = gather;
				float mineRate = 4.0f * _fDelta;
				
				bool isEnemy = EcsWorld.Get<Owner>(entity).PlayerEntity == _enemyPlayerEntity.AsPlayerEntity(EcsWorld);
				if (!isEnemy && HasHarvestingUpgrade) mineRate *= 1.5f;
				
				float nodeRemaining = targetNode.ResourceAmount;
				if (mineRate > nodeRemaining)
				{
					mineRate = nodeRemaining;
				}
				
				targetNode.ResourceAmount -= mineRate;
				newState.CarriedAmount = Math.Min(gather.MaxCapacity, gather.CarriedAmount + mineRate);
				
				if (EcsWorld.Has<Unit3D>(entity))
				{
					var worker3D = EcsWorld.Get<Unit3D>(entity);
					float pulse = 1.0f + Mathf.Sin(GameElapsedTime * 10f) * 0.1f;
					worker3D.Scale = new Vector3(pulse * 0.9f, (2.0f - pulse) * 0.9f, pulse * 0.9f);
				}
				
				if (targetNode.ResourceAmount <= 0f)
				{
					var depletedNode = targetNode;
					AllProps.Remove(depletedNode);
					if (EcsWorld.IsAlive(depletedNode.Entity))
					{
						EcsWorld.Destroy(depletedNode.Entity);
					}
					depletedNode.QueueFree();
				}
				
				if (newState.CarriedAmount >= gather.MaxCapacity)
				{
					newState.ReturningToBase = true;
					Unit3D? nearestCastle = null;
					float nearestDist = float.MaxValue;
					foreach (var u in _castlesList)
					{
						var wOwner = EcsWorld.Get<Owner>(entity).PlayerEntity;
						var uOwner = EcsWorld.Get<Owner>(u.Entity).PlayerEntity;
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
				if (!EcsWorld.Has<MoveTo>(entity))
				{
					var dest = targetNode.GlobalPosition;
					_tickGatherersToUpdate.Add((entity, gather, dest));
				}
			}
		}
	}

	private void MovementQueryAction(Entity entity, ref Position pos, ref MoveTo moveTo, ref MovementStats stats)
	{
		if (EcsWorld.Has<Realm.Ecs.Components.Core.Buffs>(entity) && EcsWorld.Get<Realm.Ecs.Components.Core.Buffs>(entity).Value.ContainsKey("stun"))
		{
			if (EcsWorld.Has<Unit3D>(entity))
			{
				var u3d = EcsWorld.Get<Unit3D>(entity);
				if (GodotObject.IsInstanceValid(u3d))
				{
					u3d.Velocity = Vector3.Zero;
				}
			}
			return;
		}

		string unitId = "worker";
		if (EcsWorld.Has<Unit3D>(entity))
		{
			unitId = EcsWorld.Get<Unit3D>(entity).UnitId;
		}
		int includeFlags = 8;
		if (UnitRegistry.TryGetValue(unitId, out var meta))
		{
			includeFlags = GetUnitPathingFlags(meta);
		}
		
		ushort pathingFlags = (ushort)includeFlags;

		PathFollow pf;
		bool hasPf = EcsWorld.Has<PathFollow>(entity);
		if (hasPf)
		{
			pf = EcsWorld.Get<PathFollow>(entity);
		}
		else
		{
			pf = new PathFollow { WaypointCount = 0, CurrentWaypointIndex = 0, Target = moveTo.Target };
		}

		if (pf.Target != moveTo.Target || pf.WaypointCount == 0)
		{
			_pathfinder.ComputePath(GroundTerrain?.NavMeshQuery!, new System.Numerics.Vector3(pos.Value.X, pos.Value.Y, pos.Value.Z), moveTo.Target, pathingFlags, ref pf);
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
			if (EcsWorld.Has<Unit3D>(entity))
			{
				var unit3D = EcsWorld.Get<Unit3D>(entity);
				unit3D.Velocity = Vector3.Zero;
			}
		}
		else
		{
			Vector3 dir = (target - current).Normalized();
			Vector3 velocity = dir * stats.Speed;
			if (EcsWorld.Has<Unit3D>(entity))
			{
				var unit3D = EcsWorld.Get<Unit3D>(entity);
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
								if (EcsWorld.Has<Dead>(other.Entity)) continue;

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
				if (GroundTerrain != null)
				{
					GroundTerrain.GetHeightAndNormal(nextPos.X, nextPos.Z, out groundHeight, out normal);
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
				if (GroundTerrain != null && GroundTerrain.NavMeshQuery != null)
				{
					var nextRc = new RcVec3f(nextPos.X, nextPos.Y, nextPos.Z);
					GroundTerrain.NavMeshQuery.FindNearestPoly(nextRc, NavMeshPathfinder.PathfindingExtents, _pathfinder.Filter, out long nearestRef, out var nearestPt, out _);
					if (nearestRef != 0)
					{
						nextPos = new Vector3(nearestPt.X, nearestPt.Y, nearestPt.Z);
					}
				}
				float groundHeight = nextPos.Y;
				if (GroundTerrain != null)
				{
					GroundTerrain.GetHeightAndNormal(nextPos.X, nextPos.Z, out groundHeight, out _);
				}
				nextPos.Y = groundHeight;
				pos.Value = new System.Numerics.Vector3(nextPos.X, nextPos.Y, nextPos.Z);
			}
		}

		if (hasPf)
		{
			EcsWorld.Set(entity, pf);
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

		if (ent == _playerEntity && HasHarvestingUpgrade)
		{
			goldPerSec *= 1.5f;
			woodPerSec *= 1.5f;
			stonePerSec *= 1.5f;
		}

		if (res.Value.ContainsKey(_goldResourceId)) res.Value[_goldResourceId] = (int)Math.Min(ResourceCap, res.Value[_goldResourceId] + _fDelta * goldPerSec);
		if (res.Value.ContainsKey(_woodResourceId)) res.Value[_woodResourceId] = (int)Math.Min(ResourceCap, res.Value[_woodResourceId] + _fDelta * woodPerSec);
		if (res.Value.ContainsKey(_stoneResourceId)) res.Value[_stoneResourceId] = (int)Math.Min(ResourceCap, res.Value[_stoneResourceId] + _fDelta * stonePerSec);
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
			if (!_scanIsAttackerEnemy && EcsWorld.Has<Unit3D>(potentialEnemy))
			{
				var enemyUnit3D = EcsWorld.Get<Unit3D>(potentialEnemy);
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
		if (EcsWorld.Has<DefinitionId>(entity) && EcsWorld.Get<DefinitionId>(entity).Value == "priest")
		{
			return;
		}

		bool isAttackMove = EcsWorld.Has<Realm.Ecs.Components.Movement.AttackMove>(entity);
		bool isPatrol     = EcsWorld.Has<Patrol>(entity);
		bool isIdle = !EcsWorld.Has<MoveTo>(entity) && !isAttackMove;

		if (isIdle || isAttackMove || isPatrol)
		{
			float scanRadius = 15.0f;
			if (EcsWorld.Has<DefinitionId>(entity))
			{
				string defId = EcsWorld.Get<DefinitionId>(entity).Value;
				if (UnitRegistry.TryGetValue(defId, out var metaReg) && metaReg.ScanRadius > 0)
					scanRadius = metaReg.ScanRadius;
			}

			if (TimeOfDayIndex == 2)
			{
				scanRadius *= 0.7f;
			}

			_scanAttackerEntity = entity;
			_scanAttackerPos = new Vector3(pos.Value.X, pos.Value.Y, pos.Value.Z);
			_scanAttackerOwner = owner.PlayerEntity;
			_scanIsAttackerEnemy = false;
			if (EcsWorld.Has<Unit3D>(entity))
			{
				_scanIsAttackerEnemy = EcsWorld.Get<Unit3D>(entity).IsEnemy;
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

	private void CombatQueryAction(Entity entity, ref Position pos, ref Attack atk, ref AttackTarget target, ref Owner owner)
	{
		if (!EcsWorld.IsAlive(target.Target) || EcsWorld.Has<Dead>(target.Target))
		{
			_tickActionsToRemoveTarget.Add(entity);
			return;
		}

		var targetPosComp = EcsWorld.Get<Position>(target.Target);
		var currentPos = new Vector3(pos.Value.X, pos.Value.Y, pos.Value.Z);
		var targetPos = new Vector3(targetPosComp.Value.X, targetPosComp.Value.Y, targetPosComp.Value.Z);

		float dist = currentPos.DistanceTo(targetPos);
		if (dist <= atk.Range)
		{
			_tickActionsToStopChasing.Add(entity);

			if (EcsWorld.Has<Unit3D>(entity))
			{
				var unit3D = EcsWorld.Get<Unit3D>(entity);
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
				OnUnitDamaged?.Invoke(GetUnitWrapper(target.Target), GetUnitWrapper(entity), damage);

				float newHp = Math.Max(0, targetHealth.Current - damage);
				EcsWorld.Set(target.Target, new Health(newHp, targetHealth.Max));

				if (EcsWorld.Has<Unit3D>(target.Target))
				{
					var targetUnit3D_alert = EcsWorld.Get<Unit3D>(target.Target);
					if (!targetUnit3D_alert.IsEnemy && _underAttackAlertTimer <= 0f)
					{
						_underAttackAlertTimer = UnderAttackAlertCooldown;
						string alertMsg = targetUnit3D_alert.UnitId == "castle"
							? "⚠️ YOUR CASTLE IS UNDER ATTACK!"
							: $"⚠️ {targetUnit3D_alert.UnitId.ToUpper()} is under attack!";
						InGameHUD.Instance?.CallDeferred(nameof(InGameHUD.ShowFeedbackText), alertMsg, new Color(1.0f, 0.2f, 0.1f));
						UIManager.Instance?.CallDeferred(nameof(UIManager.PlayWarningSound));
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

				if (EcsWorld.Has<Unit3D>(target.Target))
				{
					var target3D = EcsWorld.Get<Unit3D>(target.Target);

					if (atk.Range > 3f && EcsWorld.Has<Unit3D>(entity))
					{
						var attacker3D = EcsWorld.Get<Unit3D>(entity);
						SpawnArrowProjectile(attacker3D.GlobalPosition, target3D.GlobalPosition);
					}

					if (newHp <= 0)
					{
						_tickUnitsToKill.Add((target.Target, target3D));
					}
					else
					{
						this.CallDeferred(nameof(FlashDamageUnit), target3D);
					}
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
			bool isIdle = !EcsWorld.Has<MoveTo>(entity);
			if (isIdle)
			{
				_scanClosestDamagedFriendly = Entity.Null;
				_scanFriendlyClosestDist = 15.0f;
				_scanPriestPos = new Vector3(pos.Value.X, pos.Value.Y, pos.Value.Z);
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
		var currentPos = new Vector3(pos.Value.X, pos.Value.Y, pos.Value.Z);
		var targetPos = new Vector3(targetPosComp.Value.X, targetPosComp.Value.Y, targetPosComp.Value.Z);

		float dist = currentPos.DistanceTo(targetPos);
		if (dist <= atk.Range)
		{
			_tickHealStopChasing.Add(entity);

			if (EcsWorld.Has<Unit3D>(entity))
			{
				var unit3D = EcsWorld.Get<Unit3D>(entity);
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
				EcsWorld.Set(target.Target, new Health(newHp, targetHealth.Max));

				atk.CurrentCooldown = atk.Cooldown;

				if (EcsWorld.Has<Unit3D>(target.Target))
				{
					var target3D = EcsWorld.Get<Unit3D>(target.Target);
					var priest3D = EcsWorld.Get<Unit3D>(entity);

					SpawnHealVisualEffect(priest3D.GlobalPosition, target3D.GlobalPosition);
					this.CallDeferred(nameof(FlashHealUnit), target3D);
				}
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
					prod.BuildTime = UnitRegistry[nextUnitId].ProductionTime;
				}

				if (EcsWorld.Has<Unit3D>(entity))
				{
					var building3D = EcsWorld.Get<Unit3D>(entity);
					var spawnPos = building3D.GlobalPosition + new Vector3(0, 0, 8);
					
					var ownerComp = EcsWorld.Get<Owner>(entity);
					bool isEnemy = ownerComp.PlayerEntity != _playerEntity.AsPlayerEntity(EcsWorld);

					Vector3? rallyPoint = null;
					if (EcsWorld.Has<RallyPoint>(entity))
					{
						var rp = EcsWorld.Get<RallyPoint>(entity);
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
						string displayName = UnitRegistry.TryGetValue(unitToSpawn, out var nm) ? nm.Name : unitToSpawn.ToUpper();
						InGameHUD.Instance?.CallDeferred(nameof(InGameHUD.ShowFeedbackText), $"✓ {displayName} training complete!", new Color(0.3f, 0.9f, 0.4f));
					}
				}

				_tickNeedsUiRefresh = true;
			}
		}
	}

	private void ProcessMapEditorPhysicsQueryAction(Entity entity, ref Position pos, ref MoveTo moveTo, ref MovementStats stats)
	{
		if (EcsWorld.Has<Realm.Ecs.Components.Core.Buffs>(entity) && EcsWorld.Get<Realm.Ecs.Components.Core.Buffs>(entity).Value.ContainsKey("stun"))
		{
			if (EcsWorld.Has<Unit3D>(entity))
			{
				var u3d = EcsWorld.Get<Unit3D>(entity);
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
			_tickEditorArrivedUnits.Add(entity);
			if (EcsWorld.Has<Unit3D>(entity))
			{
				var unit3D = EcsWorld.Get<Unit3D>(entity);
				unit3D.Velocity = Vector3.Zero;
			}
		}
		else
		{
			Vector3 dir = (target - current).Normalized();
			Vector3 velocity = dir * stats.Speed;

			if (EcsWorld.Has<Unit3D>(entity))
			{
				var unit3D = EcsWorld.Get<Unit3D>(entity);
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
		if (!unit.IsEnemy)
		{
			if (EcsWorld.Has<MoveTo>(entity) && EcsWorld.Has<MovementStats>(entity))
			{
				var moveTo = EcsWorld.Get<MoveTo>(entity);
				var stats = EcsWorld.Get<MovementStats>(entity);
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
					EcsWorld.Remove<MoveTo>(entity);
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
			if (EcsWorld.Has<Position>(entity))
			{
				var finalPos = unit.GlobalPosition;
				EcsWorld.Set(entity, new Position(new System.Numerics.Vector3(finalPos.X, finalPos.Y, finalPos.Z)));
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
			float factor = _dynamicInterpolationFactor;
			unit.GlobalPosition = unit.GlobalPosition.Lerp(targetPos, factor * _fDelta);
			unit.GlobalRotation = new Vector3(0, Mathf.LerpAngle(unit.GlobalRotation.Y, target.RotationY, factor * _fDelta), 0);
			unit.Velocity = targetVel;
			if (EcsWorld.Has<Position>(entity))
			{
				EcsWorld.Set(entity, new Position(new System.Numerics.Vector3(unit.GlobalPosition.X, unit.GlobalPosition.Y, unit.GlobalPosition.Z)));
			}
		}
	}

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


	private void DealSpellDamageAOE(Vector3 position, float radius, float damage, bool enemyOnly = true)
	{
		var unitsCopy = new List<Unit3D>(AllUnits);
		foreach (var unit in unitsCopy)
		{
			if (enemyOnly && !unit.IsEnemy) continue;
			if (unit.GlobalPosition.DistanceTo(position) <= radius)
			{
				if (EcsWorld.IsAlive(unit.Entity) && EcsWorld.Has<Health>(unit.Entity))
				{
					if (EcsWorld.Has<Realm.Ecs.Components.Tags.Invulnerable>(unit.Entity)) continue;

					IUnit? caster = null;
					if (SelectedUnits.Count > 0 && EcsWorld.IsAlive(SelectedUnits[0].Entity))
					{
						caster = GetUnitWrapper(SelectedUnits[0].Entity);
					}
					if (caster != null)
					{
						var casterEntity = ((IEcsEntityWrapper)caster).Entity;
						if (EcsWorld.Has<LastAttacker>(unit.Entity))
						{
							EcsWorld.Set(unit.Entity, new LastAttacker(casterEntity));
						}
						else
						{
							EcsWorld.Add(unit.Entity, new LastAttacker(casterEntity));
						}
					}
					OnUnitDamaged?.Invoke(GetUnitWrapper(unit.Entity), caster ?? GetUnitWrapper(unit.Entity), damage);

					var hp = EcsWorld.Get<Health>(unit.Entity);
					float newHp = Math.Max(0, hp.Current - damage);
					EcsWorld.Set(unit.Entity, new Health(newHp, hp.Max));

					if (newHp <= 0)
					{
						KillUnit(unit);
					}
					else
					{
						FlashDamageUnit(unit);
					}
				}
			}
		}

		InGameHUD.Instance?.RefreshUI(SelectedUnits);
	}

	private void HealAOE(Vector3 position, float radius, float healAmount)
	{
		foreach (var unit in AllUnits)
		{
			if (!unit.IsEnemy && unit.GlobalPosition.DistanceTo(position) <= radius)
			{
				if (EcsWorld.Has<Health>(unit.Entity))
				{
					var hp = EcsWorld.Get<Health>(unit.Entity);
					float newHp = Math.Min(hp.Max, hp.Current + healAmount);
					EcsWorld.Set(unit.Entity, new Health(newHp, hp.Max));

					if (EcsWorld.Has<Unit3D>(unit.Entity))
					{
						FlashHealUnit(unit);
					}
				}
			}
		}
		InGameHUD.Instance?.RefreshUI(SelectedUnits);
	}

	private void KillUnit(Unit3D unit)
	{
		IUnit? killer = null;
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

		if (unit.IsEnemy && UnitRegistry.TryGetValue(unit.UnitId, out var bountyMeta) && bountyMeta.GoldBounty > 0f)
		{
			if (InGameHUD.Instance != null)
			{
				InGameHUD.Instance.Gold = Math.Min(ResourceCap, InGameHUD.Instance.Gold + bountyMeta.GoldBounty);
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
}
