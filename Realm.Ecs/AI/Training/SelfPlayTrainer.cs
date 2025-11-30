using Realm.Ecs.AI.Affordances;
using Realm.Ecs.AI.Policy;
using Realm.Ecs.AI.Simulation;

namespace Realm.Ecs.AI.Training;

public class SelfPlayTrainer
{
	private static readonly Random Random = new Random();

	public BotProfile TrainSelfPlay(string mapName, int generations = 5, int populationSize = 8, int matchesPerEvaluation = 4)
	{
		int featureCount = AffordanceScanner.FeatureCount;
		var population = new List<float[]>();

		for (int i = 0; i < populationSize; i++)
		{
			var weights = new float[featureCount];
			for (int f = 0; f < featureCount; f++)
			{
				weights[f] = (float)(Random.NextDouble() * 2.0 - 1.0);
			}
			population.Add(weights);
		}

		var historicalPool = new List<float[]>();
		historicalPool.Add(population[0]);

		float[] bestGenome = population[0];
		float maxFitness = float.MinValue;

		for (int gen = 0; gen < generations; gen++)
		{
			var fitnessScores = new float[populationSize];

			Parallel.For(0, populationSize, popIdx =>
			{
				float totalScore = 0.0f;
				var candidate = population[popIdx];

				for (int m = 0; m < matchesPerEvaluation; m++)
				{
					float[] opponent = historicalPool[Random.Next(historicalPool.Count)];
					var runner = new HeadlessSimulationRunner();
					var result = runner.RunMatch(candidate, opponent, maxTicks: 400);

					if (result.WinnerPlayerIndex == 0) totalScore += 1.0f;
					else if (result.WinnerPlayerIndex == -1) totalScore += 0.5f;
				}

				fitnessScores[popIdx] = totalScore / matchesPerEvaluation;
			});

			for (int i = 0; i < populationSize; i++)
			{
				if (fitnessScores[i] > maxFitness)
				{
					maxFitness = fitnessScores[i];
					bestGenome = (float[])population[i].Clone();
				}
			}

			historicalPool.Add((float[])bestGenome.Clone());
			if (historicalPool.Count > 10)
			{
				historicalPool.RemoveAt(0);
			}

			var newPopulation = new List<float[]>();
			newPopulation.Add((float[])bestGenome.Clone());

			while (newPopulation.Count < populationSize)
			{
				float[] parent = population[Random.Next(populationSize)];
				float[] child = MutateGenome(parent);
				newPopulation.Add(child);
			}

			population = newPopulation;
		}

		return new BotProfile
		{
			MapName = mapName,
			Version = "1.0",
			Weights = bestGenome
		};
	}

	private static float[] MutateGenome(float[] parent)
	{
		var child = new float[parent.Length];
		for (int i = 0; i < parent.Length; i++)
		{
			float mutation = (float)(Random.NextDouble() * 0.4 - 0.2);
			child[i] = Math.Clamp(parent[i] + mutation, -2.0f, 2.0f);
		}
		return child;
	}
}
