using Realm.Ecs.AI.Affordances;

namespace Realm.Ecs.AI.Policy;

public class LinearUtilityPolicy
{
	private static readonly Random Random = new Random();

	public GenericAffordance? SelectAction(List<GenericAffordance> affordances, float[] weights, float epsilon = 0.0f, bool useSoftmax = false)
	{
		if (affordances == null || affordances.Count == 0)
		{
			return null;
		}

		if (epsilon > 0.0f && Random.NextDouble() < epsilon)
		{
			return affordances[Random.Next(affordances.Count)];
		}

		var scores = new float[affordances.Count];
		float maxScore = float.MinValue;

		for (int i = 0; i < affordances.Count; i++)
		{
			float score = ComputeUtility(affordances[i].FeatureVector, weights);
			scores[i] = score;
			if (score > maxScore)
			{
				maxScore = score;
			}
		}

		if (useSoftmax)
		{
			float sumExp = 0.0f;
			var expScores = new float[affordances.Count];
			for (int i = 0; i < affordances.Count; i++)
			{
				expScores[i] = MathF.Exp(scores[i] - maxScore);
				sumExp += expScores[i];
			}

			float randVal = (float)Random.NextDouble() * sumExp;
			float cumulative = 0.0f;
			for (int i = 0; i < affordances.Count; i++)
			{
				cumulative += expScores[i];
				if (randVal <= cumulative)
				{
					return affordances[i];
				}
			}
		}

		int bestIndex = 0;
		for (int i = 1; i < affordances.Count; i++)
		{
			if (scores[i] > scores[bestIndex])
			{
				bestIndex = i;
			}
		}

		return affordances[bestIndex];
	}

	public float ComputeUtility(float[] featureVector, float[] weights)
	{
		float dotProduct = 0.0f;
		int len = Math.Min(featureVector.Length, weights.Length);
		for (int i = 0; i < len; i++)
		{
			dotProduct += featureVector[i] * weights[i];
		}
		return dotProduct;
	}
}
