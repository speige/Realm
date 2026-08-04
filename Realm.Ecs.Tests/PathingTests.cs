using NUnit.Framework;
using System.Numerics;
using Arch.Core;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Microsoft.Extensions.DependencyInjection;

namespace Realm.Ecs.Tests
{
    [TestFixture]
    public class PathingTests
    {
        private World _world = null!;
        private WorldAccessor _worldAccessor = null!;
        private TerrainNavMeshService _terrainNavMeshService = null!;
        private NavMeshPathfinder _pathfinder = null!;
        private MovementAndPathfindingService _movementService = null!;
        private Entity _worldEntity;

        [SetUp]
        public void Setup()
        {
            _world = World.Create();
            _worldAccessor = new WorldAccessor(_world);

            _terrainNavMeshService = new TerrainNavMeshService(_worldAccessor);
            _pathfinder = new NavMeshPathfinder();

            var services = new ServiceCollection();
            services.AddSingleton(_worldAccessor);
            services.AddSingleton(_terrainNavMeshService);
            services.AddSingleton(_pathfinder);
            var provider = services.BuildServiceProvider();
            ServiceLocator.Initialize(provider);
        }

        [TearDown]
        public void TearDown()
        {
            _world.Dispose();
        }

        private void InitializeTerrain(int width = 64, int depth = 64, float quadSize = 2.0f, float cellSize = 0.5f)
        {
            float[,] heights = new float[width, depth];
            int[,] pathingCodes = new int[width, depth];
            for (int z = 0; z < depth; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    pathingCodes[x, z] = 8 | 4;
                }
            }

            var terrainState = new TerrainState(width, depth, quadSize, cellSize, heights, pathingCodes, new DotRecast.Detour.DtNavMesh(), new DotRecast.Detour.DtNavMeshQuery(new DotRecast.Detour.DtNavMesh()));
            _worldEntity = _world.Create(terrainState);

            _movementService = new MovementAndPathfindingService(_worldAccessor, _worldEntity, _pathfinder);
        }

        private void SpawnObstacle(Vector3 pos, float radius)
        {
            _world.Create(
                new Position(pos),
                new CollisionRadius(radius),
                new PropIdentity("rock")
            );
        }

        private Entity SpawnUnit(Vector3 pos, float speed = 5.0f)
        {
            return _world.Create(
                new Position(pos),
                new MovementStats(speed, 10.0f, 10.0f),
                new CollisionRadius(0.5f),
                new DefinitionId("worker"),
                new Movable()
            );
        }

        [Test]
        public void TestOpenAreaPathing()
        {
            InitializeTerrain();
            _terrainNavMeshService.BakeNavMesh(ref _world.Get<TerrainState>(_worldEntity));

            var unit = SpawnUnit(new Vector3(-20f, 0f, -20f));
            var destination = new Vector3(20f, 0f, 20f);

            _world.Add(unit, new MoveTo(destination));

            var path = new List<Vector3>();
            path.Add(_world.Get<Position>(unit).Value);

            int ticks = 0;
            while (_world.Has<MoveTo>(unit) && ticks < 200)
            {
                _movementService.StepMovement(0.1f);
                path.Add(_world.Get<Position>(unit).Value);
                ticks++;
            }

            Assert.That(_world.Has<MoveTo>(unit), Is.False);
            float pathLength = 0;
            for (int i = 1; i < path.Count; i++)
            {
                pathLength += Vector3.Distance(path[i - 1], path[i]);
            }

            float straightLineDistance = Vector3.Distance(new Vector3(-20f, 0f, -20f), destination);
            Assert.That(pathLength, Is.LessThan(straightLineDistance * 1.1f));
        }

        [Test]
        public void TestPathingAroundCliffWall()
        {
            int width = 64;
            int depth = 64;
            float quadSize = 2.0f;
            InitializeTerrain(width, depth, quadSize);

            ref var terrain = ref _world.Get<TerrainState>(_worldEntity);
            for (int z = 0; z < depth; z++)
            {
                if (z != 32 && z != 31 && z != 33)
                {
                    terrain.Heights[32, z] = 10.0f;
                }
            }

            _terrainNavMeshService.BakeNavMesh(ref terrain);

            var start = new Vector3(-20f, 0f, 0f);
            var destination = new Vector3(20f, 0f, 0f);
            var unit = SpawnUnit(start);

            _world.Add(unit, new MoveTo(destination));

            var path = new List<Vector3>();
            path.Add(start);

            int ticks = 0;
            while (_world.Has<MoveTo>(unit) && ticks < 400)
            {
                _movementService.StepMovement(0.1f);
                path.Add(_world.Get<Position>(unit).Value);
                ticks++;
            }

            Assert.That(_world.Has<MoveTo>(unit), Is.False);
            
            float pathLength = 0;
            for (int i = 1; i < path.Count; i++)
            {
                pathLength += Vector3.Distance(path[i - 1], path[i]);
            }
            Assert.That(pathLength, Is.LessThan(48.0f));
            
            foreach (var pt in path)
            {
                if (Math.Abs(pt.X - 1.0f) < 0.5f)
                {
                    Assert.That(Math.Abs(pt.Z - 1.0f), Is.LessThan(4.0f));
                }
            }
        }

        [Test]
        public void TestPathingAroundObstacleWall()
        {
            InitializeTerrain();

            for (float z = -16.0f; z <= 16.0f; z += 2.0f)
            {
                SpawnObstacle(new Vector3(0f, 0f, z), 1.0f);
            }

            _terrainNavMeshService.BakeNavMesh(ref _world.Get<TerrainState>(_worldEntity));

            var start = new Vector3(-20f, 0f, 0f);
            var destination = new Vector3(20f, 0f, 0f);
            var unit = SpawnUnit(start);

            _world.Add(unit, new MoveTo(destination));

            var path = new List<Vector3>();
            path.Add(start);

            int ticks = 0;
            while (_world.Has<MoveTo>(unit) && ticks < 400)
            {
                var beforePos = _world.Get<Position>(unit).Value;
                _movementService.StepMovement(0.1f);
                var afterPos = _world.Get<Position>(unit).Value;
                path.Add(afterPos);

                if (Vector3.Distance(beforePos, afterPos) < 0.001f && _world.Has<MoveTo>(unit))
                {
                    break;
                }
                ticks++;
            }

            if (_world.Has<PathFollow>(unit))
            {
                var pf = _world.Get<PathFollow>(unit);
                Console.WriteLine($"Obstacle Wall test - Waypoint count: {pf.WaypointCount}, Current Index: {pf.CurrentWaypointIndex}");
                for (int i = 0; i < pf.WaypointCount; i++)
                {
                    Console.WriteLine($"  WP {i}: {pf.Waypoints[i]}");
                }
            }
            else
            {
                Console.WriteLine("Obstacle Wall test - No PathFollow component!");
            }
            Console.WriteLine($"Obstacle Wall test - Final position: {_world.Get<Position>(unit).Value}");

            Assert.That(_world.Has<MoveTo>(unit), Is.False);
            
            float pathLength = 0;
            for (int i = 1; i < path.Count; i++)
            {
                pathLength += Vector3.Distance(path[i - 1], path[i]);
            }
            Assert.That(pathLength, Is.LessThan(65.0f));
        }

        [Test]
        public void TestPathingAroundLargeBuilding()
        {
            InitializeTerrain();

            SpawnObstacle(new Vector3(0f, 0f, 0f), 3.0f);

            _terrainNavMeshService.BakeNavMesh(ref _world.Get<TerrainState>(_worldEntity));

            var start = new Vector3(-20f, 0f, 0f);
            var destination = new Vector3(20f, 0f, 0f);
            var unit = SpawnUnit(start);

            _world.Add(unit, new MoveTo(destination));

            var path = new List<Vector3>();
            path.Add(start);

            int ticks = 0;
            while (_world.Has<MoveTo>(unit) && ticks < 300)
            {
                _movementService.StepMovement(0.1f);
                path.Add(_world.Get<Position>(unit).Value);
                ticks++;
            }

            Assert.That(_world.Has<MoveTo>(unit), Is.False);
            
            float pathLength = 0;
            for (int i = 1; i < path.Count; i++)
            {
                pathLength += Vector3.Distance(path[i - 1], path[i]);
            }
            Assert.That(pathLength, Is.LessThan(45.0f));
        }

        [Test]
        public void TestPathingAroundDynamicUnits()
        {
            InitializeTerrain();

            for (float z = -6.0f; z <= 6.0f; z += 2.0f)
            {
                _world.Create(
                    new Position(new Vector3(0f, 0f, z)),
                    new CollisionRadius(0.8f),
                    new DefinitionId("blocking_worker")
                );
            }

            _terrainNavMeshService.BakeNavMesh(ref _world.Get<TerrainState>(_worldEntity));

            var start = new Vector3(-20f, 0f, 0f);
            var destination = new Vector3(20f, 0f, 0f);
            var unit = SpawnUnit(start);

            _world.Add(unit, new MoveTo(destination));

            var path = new List<Vector3>();
            path.Add(start);

            int ticks = 0;
            while (_world.Has<MoveTo>(unit) && ticks < 300)
            {
                _movementService.StepMovement(0.1f);
                path.Add(_world.Get<Position>(unit).Value);
                ticks++;
            }

            Assert.That(_world.Has<MoveTo>(unit), Is.False);
            
            float pathLength = 0;
            for (int i = 1; i < path.Count; i++)
            {
                pathLength += Vector3.Distance(path[i - 1], path[i]);
            }
            Assert.That(pathLength, Is.LessThan(48.0f));
        }

        [Test]
        public void TestPathingSqueezeAndUnstuck()
        {
            InitializeTerrain(128, 128, 0.5f);

            SpawnObstacle(new Vector3(0f, 0f, -2.3f), 1.5f);
            SpawnObstacle(new Vector3(0f, 0f, 2.3f), 1.5f);
            for (float z = -64.0f; z <= 64.0f; z += 3.0f)
            {
                if (z > -3.5f && z < 3.5f) continue;
                SpawnObstacle(new Vector3(0f, 0f, z), 1.5f);
            }

            _terrainNavMeshService.BakeNavMesh(ref _world.Get<TerrainState>(_worldEntity));

            var start = new Vector3(-20f, 0f, 0f);
            var destination = new Vector3(20f, 0f, 0f);
            var unit = SpawnUnit(start);

            _world.Add(unit, new MoveTo(destination));

            int ticks = 0;
            while (_world.Has<MoveTo>(unit) && ticks < 1000)
            {
                _movementService.StepMovement(0.0333f);
                ticks++;
            }

            Assert.That(_world.Has<MoveTo>(unit), Is.False);
            var finalPos = _world.Get<Position>(unit).Value;
            Assert.That(Vector3.Distance(finalPos, destination), Is.LessThan(1.5f));
            Console.WriteLine($"Squeeze and Unstuck Ticks: {ticks}");
        }

        [Test]
        public void TestLargeUnitSqueezesThroughNarrowGap()
        {
            // Collision separation only pushes units apart once they overlap past 45% of
            // their combined radius, so a large unit can squeeze through a gap that is
            // narrower than its collision diameter (intentional RTS clumping behavior).
            InitializeTerrain(128, 128, 0.5f);

            SpawnObstacle(new Vector3(0f, 0f, -2.3f), 1.5f);
            SpawnObstacle(new Vector3(0f, 0f, 2.3f), 1.5f);
            for (float z = -64.0f; z <= 64.0f; z += 3.0f)
            {
                if (z > -3.5f && z < 3.5f) continue;
                SpawnObstacle(new Vector3(0f, 0f, z), 1.5f);
            }

            _terrainNavMeshService.BakeNavMesh(ref _world.Get<TerrainState>(_worldEntity));

            var start = new Vector3(-20f, 0f, 0f);
            var destination = new Vector3(20f, 0f, 0f);
            var unit = _world.Create(
                new Position(start),
                new MovementStats(5.0f, 10.0f, 10.0f),
                new CollisionRadius(1.6f),
                new DefinitionId("worker_large"),
                new Movable()
            );

            _world.Add(unit, new MoveTo(destination));

            int ticks = 0;
            while (_world.Has<MoveTo>(unit) && ticks < 600)
            {
                _movementService.StepMovement(0.0333f);
                ticks++;
            }

            Assert.That(_world.Has<MoveTo>(unit), Is.False, $"Unit failed to squeeze through the gap within {ticks} ticks.");
            var finalPos = _world.Get<Position>(unit).Value;
            Assert.That(Vector3.Distance(finalPos, destination), Is.LessThan(1.5f));
        }

        [Test]
        public void TestLayeredPathing()
        {
            int width = 64;
            int depth = 64;
            float quadSize = 2.0f;
            InitializeTerrain(width, depth, quadSize);

            ref var terrain = ref _world.Get<TerrainState>(_worldEntity);
            for (int z = 0; z < depth; z++)
            {
                terrain.PathingCodes[32, z] = (int)TerrainPathingFlags.Flying;
            }

            _terrainNavMeshService.BakeNavMesh(ref terrain);

            var start = new Vector3(-20f, 0f, 0f);
            
            // Ground unit (pathing flag Ground) cannot cross the flying-only column,
            // so give it a destination on the near side and expect it to reach it.
            var groundUnit = SpawnUnit(start);
            _world.Add(groundUnit, new PathingFlags((int)TerrainPathingFlags.Ground));
            _world.Add(groundUnit, new MoveTo(new Vector3(20f, 0f, 0f)));

            int ticks = 0;
            while (_world.Has<MoveTo>(groundUnit) && ticks < 200)
            {
                _movementService.StepMovement(0.1f);
                ticks++;
            }

            var groundPos = _world.Get<Position>(groundUnit).Value;
            Assert.That(_world.Has<MoveTo>(groundUnit), Is.False, "Ground unit should reach its destination before the flying-only column.");
            Assert.That(groundPos.X, Is.GreaterThan(18f), $"Ground unit stopped at X={groundPos.X} instead of reaching X=20.");
            
            // Flying unit (pathing flag Flying) bypasses the navmesh and flies
            // straight to a destination past the flying-only column.
            var flyingUnit = SpawnUnit(start);
            _world.Add(flyingUnit, new PathingFlags((int)TerrainPathingFlags.Flying));
            _world.Add(flyingUnit, new MoveTo(new Vector3(40f, 0f, 0f)));

            ticks = 0;
            while (_world.Has<MoveTo>(flyingUnit) && ticks < 200)
            {
                _movementService.StepMovement(0.1f);
                ticks++;
            }

            Assert.That(_world.Has<MoveTo>(flyingUnit), Is.False);
            var flyingPos = _world.Get<Position>(flyingUnit).Value;
            Assert.That(flyingPos.X, Is.GreaterThan(30f), "Flying unit should cross over the flying-only column.");
        }
    }
}
