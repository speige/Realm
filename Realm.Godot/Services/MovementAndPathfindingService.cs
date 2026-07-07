using Arch.Core;
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
	private World _ecsWorld => _ecsWorldAccessor.Current;
	private readonly Entity _worldEntity;
	private Entity _resolvedWorldEntity = Entity.Null;
	private Entity ActiveWorldEntity
	{
		get
		{
			if (_resolvedWorldEntity == Entity.Null || !_ecsWorld.IsAlive(_resolvedWorldEntity))
			{
				if (_worldEntity != Entity.Null && _ecsWorld.IsAlive(_worldEntity))
				{
					_resolvedWorldEntity = _worldEntity;
				}
				else
				{
					var worldQuery = Realm.Ecs.Common.QueryCache.AllTerrainStateQuery;
					_ecsWorld.Query(in worldQuery, (Entity entity) => _resolvedWorldEntity = entity);
				}
			}
			return _resolvedWorldEntity;
		}
	}
	private readonly NavMeshPathfinder _pathfinder;
	private readonly TerrainNavMeshService _terrainNavMeshService;

	private float _fDelta;
	private const float CollisionCellSize = 10f;

	private readonly Dictionary<long, List<Entity>> _unitGrid = new();
	private readonly Dictionary<long, List<Entity>> _propGrid = new();
	private readonly List<List<Entity>> _listPool = new();

	private readonly List<Entity> _tickArrivedUnits = new();

	private readonly QueryDescription _movementQuery = Realm.Ecs.Common.QueryCache.AllPositionAndMoveToAndMovementStatsNoneDeadQuery;
	private readonly QueryDescription _spatialQuery = Realm.Ecs.Common.QueryCache.AllPositionQuery;
	private ForEachWithEntity<Position, MoveTo, MovementStats> _movementQueryDelegate = null!;

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

		_hasTerrainState = _ecsWorld.IsAlive(ActiveWorldEntity) && _ecsWorld.Has<TerrainState>(ActiveWorldEntity);
		if (_hasTerrainState)
		{
			_currentTerrainState = _ecsWorld.Get<TerrainState>(ActiveWorldEntity);
		}

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

		_ecsWorld.Query(in _spatialQuery, (Entity entity, ref Position p) =>
		{
			if (_ecsWorld.Has<Dead>(entity)) return;

			float x = p.Value.X;
			float z = p.Value.Z;
			long key = GetCellKey(x, z);

			if (_ecsWorld.Has<DefinitionId>(entity))
			{
				if (!_unitGrid.TryGetValue(key, out var list))
				{
					list = GetEntityListFromPool();
					_unitGrid[key] = list;
				}
				list.Add(entity);
			}
			else if (_ecsWorld.Has<PropIdentity>(entity))
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
		if (_ecsWorld.Has<Realm.Ecs.Components.Core.Buffs>(entity) && _ecsWorld.Get<Realm.Ecs.Components.Core.Buffs>(entity).Value.ContainsKey("stun"))
		{
			if (_ecsWorld.Has<Velocity>(entity))
			{
				_ecsWorld.Set(entity, new Velocity(System.Numerics.Vector3.Zero));
			}
			return;
		}

		int includeFlags = _ecsWorld.Has<PathingFlags>(entity)
			? _ecsWorld.Get<PathingFlags>(entity).Value
			: 8;

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
			if (_hasTerrainState)
			{
				_pathfinder.ComputePath(_currentTerrainState.NavMeshQuery, pos.Value, moveTo.Target, pathingFlags, ref pf);
			}
		}
		var current = pos.Value;
		var target = moveTo.Target;
		if (pf.CurrentWaypointIndex < pf.WaypointCount)
		{
			target = pf.Waypoints[pf.CurrentWaypointIndex];
		}
		float dist = System.Numerics.Vector3.Distance(current, target);
		if (dist < 0.2f)
		{
			pf.CurrentWaypointIndex++;
			if (pf.CurrentWaypointIndex < pf.WaypointCount)
			{
				target = pf.Waypoints[pf.CurrentWaypointIndex];
				dist = System.Numerics.Vector3.Distance(current, target);
			}
		}
		if (pf.CurrentWaypointIndex >= pf.WaypointCount)
		{
			_tickArrivedUnits.Add(entity);
			if (_ecsWorld.Has<Velocity>(entity))
			{
				_ecsWorld.Set(entity, new Velocity(System.Numerics.Vector3.Zero));
			}
			else
			{
				_ecsWorld.Add(entity, new Velocity(System.Numerics.Vector3.Zero));
			}
		}
		else
		{
			System.Numerics.Vector3 dir = System.Numerics.Vector3.Normalize(target - current);
			System.Numerics.Vector3 velocity = dir * stats.Speed;
			System.Numerics.Vector3 nextPos = current + dir * stats.Speed * _fDelta;

			float r1 = _ecsWorld.Has<CollisionRadius>(entity) 
				? _ecsWorld.Get<CollisionRadius>(entity).Value 
				: (_ecsWorld.Has<CollisionScale>(entity) ? _ecsWorld.Get<CollisionScale>(entity).Value : 1.0f) * 1.2f;

			int baseCx = (int)Math.Floor(nextPos.X / CollisionCellSize);
			int baseCz = (int)Math.Floor(nextPos.Z / CollisionCellSize);

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

							float r2 = _ecsWorld.Has<CollisionRadius>(otherEntity) 
								? _ecsWorld.Get<CollisionRadius>(otherEntity).Value 
								: (_ecsWorld.Has<CollisionScale>(otherEntity) ? _ecsWorld.Get<CollisionScale>(otherEntity).Value : 1.0f) * 1.2f;

							float minDist = (r1 + r2) * 0.85f;
							var otherPos = _ecsWorld.Get<Position>(otherEntity).Value;
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
							float r2 = _ecsWorld.Has<CollisionRadius>(propEntity) 
								? _ecsWorld.Get<CollisionRadius>(propEntity).Value 
								: (_ecsWorld.Has<CollisionScale>(propEntity) ? _ecsWorld.Get<CollisionScale>(propEntity).Value : 1.0f) * 1.5f;

							float minDist = (r1 + r2) * 0.85f;
							var propPos = _ecsWorld.Get<Position>(propEntity).Value;
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

			float groundHeight = nextPos.Y;
			System.Numerics.Vector3 normal = System.Numerics.Vector3.UnitY;
			if (_hasTerrainState)
			{
				_terrainNavMeshService.GetHeightAndNormal(in _currentTerrainState, nextPos.X, nextPos.Z, out groundHeight, out normal);
			}
			nextPos.Y = groundHeight;
			pos.Value = nextPos;
			if (_ecsWorld.Has<Velocity>(entity))
			{
				_ecsWorld.Set(entity, new Velocity(velocity));
			}
			else
			{
				_ecsWorld.Add(entity, new Velocity(velocity));
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
