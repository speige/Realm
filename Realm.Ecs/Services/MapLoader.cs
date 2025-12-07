using Realm.Ecs.Definitions;
using System.IO;
using System.Text.Json;
using System.Collections.Generic; // For List<UnitArchetype>

namespace Realm.Ecs.Services;

/// <summary>
/// Responsible for loading the game's map definition (map.json) and
/// orchestrating the initialization of all data managers.
/// </summary>
public class MapLoader
{
    public DefinitionManager DefinitionManager { get; private set; } // Consolidated manager
    public ArchetypeManager ArchetypeManager { get; private set; }

    public MapLoader(string definitionsBasePath) // Changed constructor parameter
    {
        // 1. Initialize DefinitionManager that discovers types from code
        DefinitionManager = new DefinitionManager();

        // 2. Load and deserialize only the UnitArchetypes from the map definition file
        var mapJsonPath = Path.Combine(definitionsBasePath, "map.json"); // Construct path
        var mapJson = File.ReadAllText(mapJsonPath);
        var mapDefinitionUnitsOnly = JsonSerializer.Deserialize<MapDefinition>(mapJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? new MapDefinition();

        // 3. Instantiate ArchetypeManager with the loaded units and the discovered type managers
        ArchetypeManager = new ArchetypeManager(mapDefinitionUnitsOnly.Units, DefinitionManager);
    }
}