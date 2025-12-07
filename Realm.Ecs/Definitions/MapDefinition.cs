using Realm.Ecs.Archetypes;
using System.Collections.Generic;

namespace Realm.Ecs.Definitions;

/// <summary>
/// Represents the complete structure of a game map definition, primarily containing
/// unit archetypes for a specific map.
/// Resource types, stat types, and capabilities are now discovered from code via managers.
/// </summary>
public class MapDefinition
{
    public List<UnitArchetype> Units { get; set; } = new();
}