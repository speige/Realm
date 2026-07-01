using Arch.Core;
using Godot;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Services;
using System;
using System.Collections.Generic;

internal class MovementAndPathfindingService
{
	private readonly World _ecsWorld;
	private readonly NavMeshPathfinder _pathfinder;

	private float _fDelta;
	private const float CollisionCellSize = 10f;

	private readonly Dictionary<long, List<Unit3D>> _unitGrid = new();
	private readonly Dictionary<long, List<Prop3D>> _propGrid = new();
	private readonly List<List<Unit3D>> _unitListPool = new();
	private readonly List<List<Prop3D>> _propListPool = new();

	private readonly List<Entity> _tickArrivedUnits = new();

	private readonly QueryDescription _movementQuery = new QueryDescription().WithAll<Position, MoveTo, MovementStats>().WithNone<Dead>();
	private ForEachWithEntity<Position, MoveTo, MovementStats> _movementQueryDelegate = null!;

	private List<Unit3D> _allUnitsRef;
	private List<Prop3D> _allPropsRef;
	private EditableTerrain _groundTerrainRef;

	public MovementAndPathfindingService(World ecsWorld, NavMeshPathfinder pathfinder)
	{
		_ecsWorld = ecsWorld;
		_pathfinder = pathfinder;
		_movementQueryDelegate = MovementQueryAction;
	}

	public void SetRuntimeReferences(List<Unit3D> allUnits, List<Prop3D> allProps, EditableTerrain groundTerrain)
	{
		_allUnitsRef = allUnits;
		_allPropsRef = allProps;
		_groundTerrainRef = groundTerrain;
	}

	public void StepMovement(float delta)
	{
		_fDelta = delta;
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

	public ForEachWithEntity<Position, MoveTo, MovementStats> EditorMovementQueryDelegate => _movementQueryDelegate;

	private long GetCellKey(float x, float z)
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

		if (_allUnitsRef != null)
		{
			foreach (var u in _allUnitsRef)
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
		}

		if (_allPropsRef != null)
		{
			foreach (var p in _allPropsRef)
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
					pos.Value = new System.Numerics.Vector3(nextPos.X, nextPos.Y, nextPos.Z);
				}
			}
		}

		if (hasPf)
		{
			_ecsWorld.Set(entity, pf);
		}
		else
		{
			_ecsWorld.Add(entity, pf);
		}
	}
}
