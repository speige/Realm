using Arch.Core;
using Realm.Ecs.AI.Affordances;
using Realm.Ecs.AI.Policy;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;

namespace Realm.Ecs.AI;

public class BotController
{
	private readonly AffordanceScanner _scanner = new();
	private readonly LinearUtilityPolicy _policy = new();
	private float[] _weights;
	private float _decisionInterval = 1.0f;
	private float _timer = 0.0f;

	public float[] Weights => _weights;

	public BotController(float[]? weights = null, float decisionInterval = 1.0f)
	{
		_decisionInterval = decisionInterval;
		if (weights != null && weights.Length >= AffordanceScanner.FeatureCount)
		{
			_weights = weights;
		}
		else
		{
			_weights = new float[AffordanceScanner.FeatureCount];
			_weights[0] = -0.5f; // Cost
			_weights[1] = 0.8f;  // Target Threat
			_weights[2] = 0.2f;  // Range
			_weights[3] = 0.5f;  // Health Ratio
			_weights[4] = 0.7f;  // Tempo
			_weights[5] = 0.9f;  // Cooldown
			_weights[6] = 0.4f;  // Retreat Urgency
			_weights[7] = 0.6f;  // Synergy
		}
	}

	public void LoadProfile(BotProfile profile)
	{
		if (profile != null && profile.Weights != null && profile.Weights.Length >= AffordanceScanner.FeatureCount)
		{
			_weights = profile.Weights;
		}
	}

	public void Tick(World world, int playerIndex, float delta)
	{
		_timer += delta;
		if (_timer < _decisionInterval)
		{
			return;
		}
		_timer = 0.0f;

		var affordances = _scanner.ScanAffordances(world, playerIndex);
		var action = _policy.SelectAction(affordances, _weights);

		if (action.HasValue)
		{
			ExecuteAction(world, action.Value);
		}
	}

	private void ExecuteAction(World world, GenericAffordance aff)
	{
		if (!world.IsAlive(aff.SourceEntity)) return;

		switch (aff.Intent)
		{
			case CommandIntent.Attack:
				if (world.IsAlive(aff.TargetEntity))
				{
					world.AddOrGet(aff.SourceEntity, new AttackTarget(aff.TargetEntity));
				}
				break;

			case CommandIntent.MoveTo:
				world.AddOrGet(aff.SourceEntity, new MoveTo(aff.TargetPosition));
				break;

			case CommandIntent.Train:
				if (world.Has<ProductionQueue>(aff.SourceEntity))
				{
					ref var q = ref world.Get<ProductionQueue>(aff.SourceEntity);
					if (q.UnitIds.Count < 5)
					{
						q.UnitIds.Add("grunt");
					}
				}
				break;
		}
	}
}
