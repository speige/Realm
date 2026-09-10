using System.Text.Json;

namespace Realm.Ecs.AI.Policy;

public class BotProfile
{
	public string MapName { get; set; } = "GenericMap";
	public string Version { get; set; } = "1.0";
	public float[] Weights { get; set; } = Array.Empty<float>();

	public string ToJson()
	{
		return JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
	}

	public static BotProfile FromJson(string json)
	{
		return JsonSerializer.Deserialize<BotProfile>(json) ?? new BotProfile();
	}
}
