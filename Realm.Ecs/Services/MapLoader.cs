using Realm.Ecs.Definitions;
using System.Text.Json;

namespace Realm.Ecs.Services;

/// <summary>
///     Responsible for loading the game's map definition (map.json) and
///     orchestrating the initialization of all data managers.
/// </summary>
internal class MapLoader
{
	private static readonly JsonSerializerOptions Options = new()
	{
		PropertyNameCaseInsensitive = true
	};

	public MapLoader(WorldAccessor ecsWorldAccessor, string definitionsBasePath)
	{
		DefinitionManager = new DefinitionManager(ecsWorldAccessor);

		var mapJsonPath = Path.Combine(definitionsBasePath, "map.json");
		var mapJson = File.ReadAllText(mapJsonPath);
		var mapDefinitionUnitsOnly = JsonSerializer.Deserialize<MapDefinition>(mapJson, Options) ?? new MapDefinition();

		ArchetypeManager = new ArchetypeManager(ecsWorldAccessor, mapDefinitionUnitsOnly.Units, DefinitionManager);
	}

	public DefinitionManager DefinitionManager { get; }
	public ArchetypeManager ArchetypeManager { get; private set; }
}