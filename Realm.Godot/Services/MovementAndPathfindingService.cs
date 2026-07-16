using Arch.Core;
using DotRecast.Core.Numerics;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using System;
using System.Collections.Generic;

internal class MovementAndPathfindingService
{
	private readonly WorldAccessor _ecsWorldAccessor;
	private World EcsWorld => _ecsWorldAccessor.Current;
	private readonly Entity _worldEntity;
	private Entity _resolvedWorldEntity = Entity.Null;
	private Entity ActiveWorldEntity
	{
		get
		{
			if (_resolvedWorldEntity == Entity.Null || !EcsWorld.IsAlive(_resolvedWorldEntity))
			{
				if (_worldEntity != Entity.Null && EcsWorld.IsAlive(_worldEntity))
				{
					_resolvedWorldEntity = _worldEntity;
				}
				else
				{
					var worldQuery = Realm.Ecs.Common.QueryCache.AllTerrainStateQuery;
					EcsWorld.Query(in worldQuery, entity => _resolvedWorldEntity = entity);
				}
			}
			return _resolvedWorldEntity;
		}
	}
	private readonly NavMeshPathfinder _pathfinder;
	private readonly TerrainNavMeshService _terrainNavMeshService;
	private static readonly Random Random = new();

	private float _fDelta;
	private readonly float _collisionCellSize = Realm.Ecs.Common.GameplayConstants.PathfindingGridSize;

	private readonly Dictionary<long, List<Entity>> _unitGrid = new();
	private readonly Dictionary<long, List<Entity>> _propGrid = new();
	private readonly List<List<Entity>> _listPool = new();

	private readonly List<Entity> _tickArrivedUnits = new();

	private readonly QueryDescription _movementQuery = Realm.Ecs.Common.QueryCache.AllPositionAndMoveToAndMovementStatsNoneDeadQuery;
	private readonly QueryDescription _spatialQuery = Realm.Ecs.Common.QueryCache.AllPositionQuery;
	private ForEachWithEntity<Position, MoveTo, MovementStats> _movementQueryDelegate;

	private TerrainState _currentTerrainState;
	private bool _hasTerrainState;

	public MovementAndPathfindingService(WorldAccessor ecsWorldAccessor, Entity worldEntity, NavMeshPathfinder pathfinder)
	{
		_ecsWorldAccessor = ecsWorldAccessor;
		_worldEntity = worldEntity;
		_pathfinder = pathfinder;
		_terrainNavMeshService = ServiceLocator.Get<TerrainNavMeshService>();
		_movementQueryDelegate = MovementQueryAction;
	}

	public void StepMovement(float delta)
	{
		_fDelta = delta;
		_tickArrivedUnits.Clear();

		_hasTerrainState = EcsWorld.IsAlive(ActiveWorldEntity) && EcsWorld.Has<TerrainState>(ActiveWorldEntity);
		if (_hasTerrainState)
		{
			_currentTerrainState = EcsWorld.Get<TerrainState>(ActiveWorldEntity);
		}

		RebuildSpatialGrid();

		EcsWorld.Query(in _movementQuery, _movementQueryDelegate);

		foreach (var entity in _tickArrivedUnits)
		{
			if (EcsWorld.IsAlive(entity) && EcsWorld.Has<MoveTo>(entity))
			{
				if (EcsWorld.Has<PathFollow>(entity))
				{
					EcsWorld.Remove<PathFollow>(entity);
				}
				if (EcsWorld.Has<WaypointQueue>(entity))
				{
					var q = EcsWorld.Get<WaypointQueue>(entity);
					if (q.Count > 0)
					{
						var nextWaypoint = q.Dequeue();
						EcsWorld.Set(entity, q);
						EcsWorld.Set(entity, new MoveTo(nextWaypoint));
						continue;
					}
					else
					{
						EcsWorld.Remove<WaypointQueue>(entity);
					}
				}
				EcsWorld.Remove<MoveTo>(entity);
			}
		}
	}

	public ForEachWithEntity<Position, MoveTo, MovementStats> EditorMovementQueryDelegate => _movementQueryDelegate;

	private long GetCellKey(float x, float z)
	{
		int cx = (int)Math.Floor(x / _collisionCellSize);
		int cz = (int)Math.Floor(z / _collisionCellSize);
		return ((long)cx << 32) | (uint)cz;
	}

	private List<Entity> GetEntityListFromPool()
	{
		if (_listPool.Count > 0)
		{
			int lastIdx = _listPool.Count - 1;
			var list = _listPool[lastIdx];
			_listPool.RemoveAt(lastIdx);
			return list;
		}
		return new List<Entity>(16);
	}

	private void RebuildSpatialGrid()
	{
		foreach (var list in _unitGrid.Values)
		{
			list.Clear();
			_listPool.Add(list);
		}
		_unitGrid.Clear();

		foreach (var list in _propGrid.Values)
		{
			list.Clear();
			_listPool.Add(list);
		}
		_propGrid.Clear();

		EcsWorld.Query(in _spatialQuery, (Entity entity, ref Position p) =>
		{
			if (EcsWorld.Has<Dead>(entity)) return;

			float x = p.Value.X;
			float z = p.Value.Z;
			long key = GetCellKey(x, z);

			if (EcsWorld.Has<DefinitionId>(entity))
			{
				if (!_unitGrid.TryGetValue(key, out var list))
				{
					list = GetEntityListFromPool();
					_unitGrid[key] = list;
				}
				list.Add(entity);
			}
			else if (EcsWorld.Has<PropIdentity>(entity))
			{
				if (!_propGrid.TryGetValue(key, out var list))
				{
					list = GetEntityListFromPool();
					_propGrid[key] = list;
				}
				list.Add(entity);
			}
		});
	}

	private void MovementQueryAction(Entity entity, ref Position pos, ref MoveTo moveTo, ref MovementStats stats)
	{
		if (EcsWorld.Has<Buffs>(entity) && EcsWorld.Get<Buffs>(entity).Value.ContainsKey("stun"))
		{
			if (EcsWorld.Has<Velocity>(entity))
			{
				EcsWorld.Set(entity, new Velocity(System.Numerics.Vector3.Zero));
			}
			return;
		}

		int includeFlags = EcsWorld.Has<PathingFlags>(entity)
			? EcsWorld.Get<PathingFlags>(entity).Value
			: 8;

		ushort pathingFlags = (ushort)includeFlags;

		PathFollow pf;
		bool hasPf = EcsWorld.Has<PathFollow>(entity);
		if (hasPf)
		{
			pf = EcsWorld.Get<PathFollow>(entity);
		}
		else
		{
			pf = new PathFollow
			{
				WaypointCount = 0,
				CurrentWaypointIndex = 0,
				Target = moveTo.Target,
				LastPosition = pos.Value,
				StuckTime = 0f,
				TimeSinceLastReplan = 0f,
				IsJitterReplanned = false
			};
		}

		bool forceReplan = false;
		System.Numerics.Vector3 pathfindStart = pos.Value;
		System.Numerics.Vector3 pathfindEnd = moveTo.Target;

		if (hasPf)
		{
			float distMoved = System.Numerics.Vector3.Distance(pos.Value, pf.LastPosition);
			pf.LastPosition = pos.Value;

			float expectedDist = stats.Speed * _fDelta;
			if (distMoved < expectedDist * 0.1f)
			{
				pf.StuckTime += _fDelta;
				if (pf.StuckTime >= 0.1f)
				{
					pf.TimeSinceLastReplan += _fDelta;
					if (pf.TimeSinceLastReplan >= 0.1f)
					{
						pf.TimeSinceLastReplan = 0f;
						forceReplan = true;

						if (!pf.IsJitterReplanned)
						{
							float offsetX1 = (float)(Random.NextDouble() * 0.4 - 0.2);
							float offsetZ1 = (float)(Random.NextDouble() * 0.4 - 0.2);
							float offsetX2 = (float)(Random.NextDouble() * 0.4 - 0.2);
							float offsetZ2 = (float)(Random.NextDouble() * 0.4 - 0.2);
							pathfindStart += new System.Numerics.Vector3(offsetX1, 0f, offsetZ1);
							pathfindEnd += new System.Numerics.Vector3(offsetX2, 0f, offsetZ2);
							pf.IsJitterReplanned = true;
						}
						else
						{
							pf.IsJitterReplanned = false;
						}
					}
				}
			}
			else
			{
				pf.StuckTime = 0f;
				pf.TimeSinceLastReplan = 0f;
				pf.IsJitterReplanned = false;
			}
		}
		else
		{
			pf.LastPosition = pos.Value;
			pf.StuckTime = 0f;
			pf.TimeSinceLastReplan = 0f;
			pf.IsJitterReplanned = false;
		}

		if (pf.Target != moveTo.Target || pf.WaypointCount == 0 || forceReplan)
		{
			if (_hasTerrainState)
			{
				_pathfinder.ComputePath(_currentTerrainState.NavMeshQuery, pathfindStart, pathfindEnd, pathingFlags, ref pf);
				pf.Target = moveTo.Target;
			}
		}
		var current = pos.Value;
		var target = moveTo.Target;
		if (pf.CurrentWaypointIndex < pf.WaypointCount)
		{
			target = pf.Waypoints[pf.CurrentWaypointIndex];
		}
		float dist = System.Numerics.Vector3.Distance(current, target);
		float arrivalThreshold = Math.Max(0.5f, stats.Speed * _fDelta * 1.2f);
		if (dist < arrivalThreshold)
		{
			pf.CurrentWaypointIndex++;
			if (pf.CurrentWaypointIndex < pf.WaypointCount)
			{
				target = pf.Waypoints[pf.CurrentWaypointIndex];
			}
		}
		if (pf.CurrentWaypointIndex >= pf.WaypointCount)
		{
			_tickArrivedUnits.Add(entity);
			if (EcsWorld.Has<Velocity>(entity))
			{
				EcsWorld.Set(entity, new Velocity(System.Numerics.Vector3.Zero));
			}
			else
			{
				EcsWorld.Add(entity, new Velocity(System.Numerics.Vector3.Zero));
			}
		}
		else
		{
			System.Numerics.Vector3 desiredVelocity = System.Numerics.Vector3.Normalize(target - current) * stats.Speed;
			System.Numerics.Vector3 cohesion = System.Numerics.Vector3.Zero;
			System.Numerics.Vector3 alignment = System.Numerics.Vector3.Zero;
			System.Numerics.Vector3 separation = System.Numerics.Vector3.Zero;
			int neighborCount = 0;

			int currentCellX = (int)Math.Floor(current.X / _collisionCellSize);
			int currentCellZ = (int)Math.Floor(current.Z / _collisionCellSize);

			for (int dx = -1; dx <= 1; dx++)
			{
				for (int dz = -1; dz <= 1; dz++)
				{
					long key = ((long)(currentCellX + dx) << 32) | (uint)(currentCellZ + dz);
					if (_unitGrid.TryGetValue(key, out var list))
					{
						foreach (var other in list)
						{
							if (other == entity) continue;
							if (!EcsWorld.Has<Position>(other)) continue;

							var otherPos = EcsWorld.Get<Position>(other).Value;
							float neighborDist = System.Numerics.Vector3.Distance(current, otherPos);
							if (neighborDist > 0f && neighborDist < 8.0f)
							{
								cohesion += otherPos;
								if (EcsWorld.Has<Velocity>(other))
								{
									alignment += EcsWorld.Get<Velocity>(other).Value;
								}
								separation += System.Numerics.Vector3.Normalize(current - otherPos) / neighborDist;
								neighborCount++;
							}
						}
					}
				}
			}

			System.Numerics.Vector3 steering = desiredVelocity;
			if (neighborCount > 0)
			{
				cohesion = (cohesion / neighborCount) - current;
				if (cohesion.LengthSquared() > 0.001f) cohesion = System.Numerics.Vector3.Normalize(cohesion) * stats.Speed;

				alignment = alignment / neighborCount;
				if (alignment.LengthSquared() > 0.001f) alignment = System.Numerics.Vector3.Normalize(alignment) * stats.Speed;

				if (separation.LengthSquared() > 0.001f) separation = System.Numerics.Vector3.Normalize(separation) * stats.Speed;

				steering = desiredVelocity * 0.50f + separation * 0.35f + cohesion * 0.08f + alignment * 0.07f;
				if (steering.LengthSquared() > 0.001f)
				{
					steering = System.Numerics.Vector3.Normalize(steering) * stats.Speed;
				}
			}

			System.Numerics.Vector3 velocity = steering;
			System.Numerics.Vector3 nextPos = current + velocity * _fDelta;

			float r1 = EcsWorld.Has<CollisionRadius>(entity) 
				? EcsWorld.Get<CollisionRadius>(entity).Value 
				: (EcsWorld.Has<CollisionScale>(entity) ? EcsWorld.Get<CollisionScale>(entity).Value : 1.0f) * 1.2f;

			int baseCx = (int)Math.Floor(nextPos.X / _collisionCellSize);
			int baseCz = (int)Math.Floor(nextPos.Z / _collisionCellSize);

			for (int dx = -1; dx <= 1; dx++)
			{
				for (int dz = -1; dz <= 1; dz++)
				{
					long key = ((long)(baseCx + dx) << 32) | (uint)(baseCz + dz);
					if (_unitGrid.TryGetValue(key, out var list))
					{
						foreach (var otherEntity in list)
						{
							if (otherEntity == entity) continue;

							float r2 = EcsWorld.Has<CollisionRadius>(otherEntity) 
								? EcsWorld.Get<CollisionRadius>(otherEntity).Value 
								: (EcsWorld.Has<CollisionScale>(otherEntity) ? EcsWorld.Get<CollisionScale>(otherEntity).Value : 1.0f) * 1.2f;

							float minDist = (r1 + r2) * 0.85f;
							var otherPos = EcsWorld.Get<Position>(otherEntity).Value;
							float ox = nextPos.X - otherPos.X;
							float oz = nextPos.Z - otherPos.Z;
							float distSq = ox * ox + oz * oz;
							if (distSq < minDist * minDist)
							{
								float otherDist = (float)Math.Sqrt(distSq);
								System.Numerics.Vector3 pushDir;
								if (otherDist < 0.001f)
								{
									pushDir = new System.Numerics.Vector3(1f, 0f, 0f);
									otherDist = 1f;
								}
								else
								{
									pushDir = new System.Numerics.Vector3(ox / otherDist, 0f, oz / otherDist);
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
						foreach (var propEntity in list)
						{
							float r2 = EcsWorld.Has<CollisionRadius>(propEntity) 
								? EcsWorld.Get<CollisionRadius>(propEntity).Value 
								: (EcsWorld.Has<CollisionScale>(propEntity) ? EcsWorld.Get<CollisionScale>(propEntity).Value : 1.0f) * 1.5f;

							float minDist = (r1 + r2) * 0.85f;
							var propPos = EcsWorld.Get<Position>(propEntity).Value;
							float ox = nextPos.X - propPos.X;
							float oz = nextPos.Z - propPos.Z;
							float distSq = ox * ox + oz * oz;
							if (distSq < minDist * minDist)
							{
								float otherDist = (float)Math.Sqrt(distSq);
								System.Numerics.Vector3 pushDir;
								if (otherDist < 0.001f)
								{
									pushDir = new System.Numerics.Vector3(1f, 0f, 0f);
									otherDist = 1f;
								}
								else
								{
									pushDir = new System.Numerics.Vector3(ox / otherDist, 0f, oz / otherDist);
								}
								float overlap = minDist - otherDist;
								nextPos += pushDir * overlap;
							}
						}
					}
				}
			}

			if (_hasTerrainState && _currentTerrainState.NavMeshQuery != null)
			{
				var snapPos = new RcVec3f(nextPos.X, nextPos.Y, nextPos.Z);
				_currentTerrainState.NavMeshQuery.FindNearestPoly(snapPos,
					NavMeshPathfinder.PathfindingExtents, _pathfinder.Filter,
					out long snapRef, out var snappedPt, out _);
				if (snapRef != 0)
				{
					nextPos.X = snappedPt.X;
					nextPos.Z = snappedPt.Z;
				}
			}

			float groundHeight = nextPos.Y;
			if (_hasTerrainState)
			{
				_terrainNavMeshService.GetHeightAndNormal(in _currentTerrainState, nextPos.X, nextPos.Z, out groundHeight, out _);
			}
			nextPos.Y = groundHeight;
			pos.Value = nextPos;
			if (EcsWorld.Has<Velocity>(entity))
			{
				EcsWorld.Set(entity, new Velocity(velocity));
			}
			else
			{
				EcsWorld.Add(entity, new Velocity(velocity));
			}
		}

		if (hasPf)
		{
			EcsWorld.Set(entity, pf);
		}
		else
		{
			EcsWorld.Add(entity, pf);
		}
	}
}
