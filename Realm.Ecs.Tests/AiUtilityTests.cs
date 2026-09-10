using NUnit.Framework;
using Realm.Ecs.AI.Affordances;
using Realm.Ecs.AI.Balancer;
using Realm.Ecs.AI.Policy;
using Realm.Ecs.AI.Simulation;
using Realm.Ecs.AI.Training;
using System.Collections.Generic;

namespace Realm.Ecs.Tests;

[TestFixture]
public class AiUtilityTests
{
	[Test]
	public void TestAffordanceScannerGeneratesAffordances()
	{
		var runner = new HeadlessSimulationRunner();
		var scanner = new AffordanceScanner();

		var affordances = scanner.ScanAffordances(runner.World, 0);

		Assert.That(affordances, Is.Not.Null);
		Assert.That(affordances.Count, Is.GreaterThan(0), "Scanner should discover legal candidate affordances for Player 0");

		var first = affordances[0];
		Assert.That(first.FeatureVector, Is.Not.Null);
		Assert.That(first.FeatureVector.Length, Is.EqualTo(AffordanceScanner.FeatureCount));
	}

	[Test]
	public void TestLinearUtilityPolicySelection()
	{
		var policy = new LinearUtilityPolicy();
		var affordances = new List<GenericAffordance>
		{
			new GenericAffordance(Arch.Core.Entity.Null, CommandIntent.MoveTo, Arch.Core.Entity.Null, System.Numerics.Vector3.Zero, "move1", new float[] { 0.1f, 0.2f, 0.1f, 0.5f, 0.2f, 0.0f, 0.1f, 0.1f }),
			new GenericAffordance(Arch.Core.Entity.Null, CommandIntent.Attack, Arch.Core.Entity.Null, System.Numerics.Vector3.Zero, "attack1", new float[] { 0.0f, 0.9f, 0.8f, 1.0f, 0.9f, 0.0f, 0.1f, 0.8f })
		};

		var weights = new float[] { -0.5f, 1.0f, 0.5f, 0.5f, 0.5f, 0.5f, -0.5f, 0.5f };

		var bestAction = policy.SelectAction(affordances, weights);

		Assert.That(bestAction.HasValue, Is.True);
		Assert.That(bestAction.Value.PayloadId, Is.EqualTo("attack1"), "Policy should pick the highest scoring affordance");
	}

	[Test]
	public void TestHeadlessSimulationRunnerRunsMatch()
	{
		var runner = new HeadlessSimulationRunner();
		var p0Weights = new float[] { -0.5f, 0.8f, 0.2f, 0.5f, 0.7f, 0.9f, 0.4f, 0.6f };
		var p1Weights = new float[] { -0.5f, 0.8f, 0.2f, 0.5f, 0.7f, 0.9f, 0.4f, 0.6f };

		var result = runner.RunMatch(p0Weights, p1Weights, maxTicks: 200);

		Assert.That(result, Is.Not.Null);
		Assert.That(result.TotalTicksExecuted, Is.GreaterThan(0), "Simulation match should execute ticks");
	}

	[Test]
	public void TestSelfPlayTrainerGeneratesAndSerializesProfile()
	{
		var trainer = new SelfPlayTrainer();
		var profile = trainer.TrainSelfPlay("TestMap", generations: 2, populationSize: 4, matchesPerEvaluation: 2);

		Assert.That(profile, Is.Not.Null);
		Assert.That(profile.MapName, Is.EqualTo("TestMap"));
		Assert.That(profile.Weights.Length, Is.EqualTo(AffordanceScanner.FeatureCount));

		string json = profile.ToJson();
		Assert.That(string.IsNullOrWhiteSpace(json), Is.False);

		var restored = BotProfile.FromJson(json);
		Assert.That(restored.MapName, Is.EqualTo(profile.MapName));
		Assert.That(restored.Weights.Length, Is.EqualTo(profile.Weights.Length));
	}

	[Test]
	public void TestStrategicAutoBalancerMutatesAndOutputsGenome()
	{
		var balancer = new StrategicAutoBalancer();
		var initialGenome = new BalanceGenome();
		var agentWeights = new float[] { -0.5f, 0.8f, 0.2f, 0.5f, 0.7f, 0.9f, 0.4f, 0.6f };

		var optimized = balancer.OptimizeBalance(initialGenome, agentWeights, generations: 2, matchesPerGeneration: 2);

		Assert.That(optimized, Is.Not.Null);
		Assert.That(optimized.Unit0Cost, Is.GreaterThan(0f));

		string json = optimized.ToJson();
		Assert.That(string.IsNullOrWhiteSpace(json), Is.False);

		var restored = BalanceGenome.FromJson(json);
		Assert.That(restored.Unit0Cost, Is.EqualTo(optimized.Unit0Cost));
	}
}
