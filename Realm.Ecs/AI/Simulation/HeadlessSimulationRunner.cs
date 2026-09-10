using Arch.Core;
using Realm.Ecs.AI.Affordances;
using Realm.Ecs.AI.Policy;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using System.Numerics;

namespace Realm.Ecs.AI.Simulation;

public class SimulationMatchResult
{
	public int WinnerPlayerIndex { get; set; } = -1; // -1 for draw
	public int TotalTicksExecuted { get; set; }
	public float MatchDurationSeconds { get; set; }
	public int Player0UnitsBuilt { get; set; }
	public int Player1UnitsBuilt { get; set; }
}

public class HeadlessSimulationRunner
{
	private World _world = null!;
	private readonly AffordanceScanner _scanner = new();
	private readonly LinearUtilityPolicy _policy = new();

	public World World => _world;

	public HeadlessSimulationRunner()
	{
		ResetGame();
	}

	public void ResetGame()
	{
		_world?.Dispose();
		_world = World.Create();

		var p0 = _world.Create(
			new Player(),
			new UnitOwnerPlayer(0),
			new PlayerResources(new Dictionary<Common.ResourceId, int> { { new Common.ResourceId("Gold"), 1000 } })
		);

		var p1 = _world.Create(
			new Player(),
			new UnitOwnerPlayer(1),
			new PlayerResources(new Dictionary<Common.ResourceId, int> { { new Common.ResourceId("Gold"), 1000 } })
		);

		SpawnStartingBase(0, new Vector3(-20, 0, 0));
		SpawnStartingBase(1, new Vector3(20, 0, 0));
	}

	private void SpawnStartingBase(int playerIndex, Vector3 pos)
	{
		var baseEntity = _world.Create(
			new Position(pos),
			new Health(1000f, 1000f),
			new UnitOwnerPlayer(playerIndex),
			new ProductionQueue()
		);

		for (int i = 0; i < 3; i++)
		{
			var unitEntity = _world.Create(
				new Position(pos + new Vector3((i - 1) * 2f, 0, 3f)),
				new Health(100f, 100f),
				new Attack(15f, 3f, 1f),
				new MovementStats(5f, 10f, 10f),
				new UnitOwnerPlayer(playerIndex)
			);
		}
	}

	public void Tick(float delta, float[] p0Weights, float[] p1Weights, float epsilon = 0.0f)
	{
		ExecuteAgentDecisions(0, p0Weights, epsilon);
		ExecuteAgentDecisions(1, p1Weights, epsilon);

		StepMovement(delta);
		StepCombat(delta);
		StepProduction(delta);
	}

	private void ExecuteAgentDecisions(int playerIndex, float[] weights, float epsilon)
	{
		if (weights == null || weights.Length == 0) return;

		var affordances = _scanner.ScanAffordances(_world, playerIndex);
		var action = _policy.SelectAction(affordances, weights, epsilon);

		if (action.HasValue)
		{
			var aff = action.Value;
			if (aff.Intent == CommandIntent.Attack && _world.IsAlive(aff.TargetEntity))
			{
				_world.AddOrGet(aff.SourceEntity, new AttackTarget(aff.TargetEntity));
			}
			else if (aff.Intent == CommandIntent.MoveTo)
			{
				_world.AddOrGet(aff.SourceEntity, new MoveTo(aff.TargetPosition));
			}
			else if (aff.Intent == CommandIntent.Train)
			{
				if (_world.Has<ProductionQueue>(aff.SourceEntity))
				{
					ref var q = ref _world.Get<ProductionQueue>(aff.SourceEntity);
					if (q.UnitIds.Count < 5)
					{
						q.UnitIds.Add("grunt");
					}
				}
			}
		}
	}

	private void StepMovement(float delta)
	{
		var moveQuery = new QueryDescription().WithAll<Position, MoveTo>().WithNone<Dead>();
		_world.Query(in moveQuery, (Entity e, ref Position pos, ref MoveTo move) =>
		{
			Vector3 dir = move.Target - pos.Value;
			float dist = dir.Length();
			if (dist < 0.5f)
			{
				_world.Remove<MoveTo>(e);
			}
			else
			{
				float speed = _world.Has<MovementStats>(e) ? _world.Get<MovementStats>(e).Speed : 5.0f;
				pos.Value += Vector3.Normalize(dir) * MathF.Min(dist, speed * delta);
			}
		});
	}

	private void StepCombat(float delta)
	{
		var attackQuery = new QueryDescription().WithAll<Position, AttackTarget, Attack>().WithNone<Dead>();
		var deadEntities = new List<Entity>();

		_world.Query(in attackQuery, (Entity attacker, ref Position aPos, ref AttackTarget target, ref Attack attack) =>
		{
			if (!_world.IsAlive(target.Target) || _world.Has<Dead>(target.Target))
			{
				_world.Remove<AttackTarget>(attacker);
				return;
			}

			if (_world.Has<Position>(target.Target) && _world.Has<Health>(target.Target))
			{
				var tPos = _world.Get<Position>(target.Target).Value;
				float dist = Vector3.Distance(aPos.Value, tPos);
				if (dist <= 3.0f)
				{
					ref var hp = ref _world.Get<Health>(target.Target);
					hp.Current -= attack.Damage * delta;
					if (hp.Current <= 0.0f)
					{
						deadEntities.Add(target.Target);
					}
				}
				else
				{
					_world.AddOrGet(attacker, new MoveTo(tPos));
				}
			}
		});

		foreach (var d in deadEntities)
		{
			if (_world.IsAlive(d))
			{
				_world.AddOrGet(d, new Dead());
				_world.Destroy(d);
			}
		}
	}

	private void StepProduction(float delta)
	{
		var prodQuery = new QueryDescription().WithAll<ProductionQueue, UnitOwnerPlayer, Position>().WithNone<Dead>();
		_world.Query(in prodQuery, (Entity bld, ref ProductionQueue q, ref UnitOwnerPlayer owner, ref Position pos) =>
		{
			if (q.UnitIds.Count > 0)
			{
				q.CurrentProgress += delta;
				if (q.CurrentProgress >= q.BuildTime)
				{
					q.CurrentProgress = 0.0f;
					q.UnitIds.RemoveAt(0);

					var spawnedUnit = _world.Create(
						new Position(pos.Value + new Vector3(0, 0, 3f)),
						new Health(100f, 100f),
						new Attack(15f, 3f, 1f),
						new MovementStats(5f, 10f, 10f),
						new UnitOwnerPlayer(owner.PlayerIndex)
					);
				}
			}
		});
	}

	public SimulationMatchResult RunMatch(float[] p0Weights, float[] p1Weights, int maxTicks = 1000, float fixedDelta = 0.1f)
	{
		ResetGame();

		for (int tick = 0; tick < maxTicks; tick++)
		{
			Tick(fixedDelta, p0Weights, p1Weights);

			int p0Alive = CountAliveUnits(0);
			int p1Alive = CountAliveUnits(1);

			if (p0Alive == 0 || p1Alive == 0)
			{
				int winner = p0Alive > 0 ? 0 : (p1Alive > 0 ? 1 : -1);
				return new SimulationMatchResult
				{
					WinnerPlayerIndex = winner,
					TotalTicksExecuted = tick + 1,
					MatchDurationSeconds = (tick + 1) * fixedDelta,
					Player0UnitsBuilt = 0,
					Player1UnitsBuilt = 0
				};
			}
		}

		int p0Count = CountAliveUnits(0);
		int p1Count = CountAliveUnits(1);
		int finalWinner = p0Count > p1Count ? 0 : (p1Count > p0Count ? 1 : -1);

		return new SimulationMatchResult
		{
			WinnerPlayerIndex = finalWinner,
			TotalTicksExecuted = maxTicks,
			MatchDurationSeconds = maxTicks * fixedDelta,
			Player0UnitsBuilt = 0,
			Player1UnitsBuilt = 0
		};
	}

	private int CountAliveUnits(int playerIndex)
	{
		int count = 0;
		var query = new QueryDescription().WithAll<UnitOwnerPlayer, Health>().WithNone<Dead>();
		_world.Query(in query, (Entity e, ref UnitOwnerPlayer owner) =>
		{
			if (owner.PlayerIndex == playerIndex)
			{
				count++;
			}
		});
		return count;
	}
}
