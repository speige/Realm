using Realm.Ecs.Archetypes;

namespace Realm.Ecs.Definitions;

/// <summary>
///     Represents the complete structure of a game map definition, primarily containing
///     unit archetypes for a specific map.
///     Resource types, stat types, and capabilities are now discovered from code via managers.
/// </summary>
public class MapProperties
{
	public float? CameraBoundsLeft { get; set; }
	public float? CameraBoundsRight { get; set; }
	public float? CameraBoundsTop { get; set; }
	public float? CameraBoundsBottom { get; set; }
}

public class MapDefinition
{
	public List<UnitArchetype> Units { get; set; } = new();
	public MapProperties MapProperties { get; set; }
}