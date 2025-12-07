using Arch.Core;
using Realm.Ecs.Common; // For IdExtensions
using Realm.Ecs.Services;
using System;
using System.Linq; 
using System.Numerics;
using Realm.Ecs.Components.Core; 
using Realm.Ecs.Components.Meta;

namespace Realm.CustomMap;

internal class Program
{
    static void Main(string[] args)
    {
        Console.WriteLine("--- Loading Custom Map ---");

        var world = World.Create();
        // The MapLoader now uses its default path or needs to be provided with the base path to the Definitions folder
        // For custom map, we'll indicate the path to its Definitions folder.
        var customMapLoader = new MapLoader("Realm.CustomMap"); 

        // Get the single consolidated DefinitionManager
        var definitionManager = customMapLoader.DefinitionManager;

        // Verify custom resource type
        Console.WriteLine($"Is Mana a valid resource type? {definitionManager.IsValidResource("Mana")}");
        var manaDefinition = definitionManager.GetResource("Mana");
        Console.WriteLine($"Mana DisplayName: {manaDefinition?.Definition.DisplayName}, Description: {manaDefinition?.Definition.Description}");

        // Now, we create the ArchetypeManager, EntityFactory, GameInitializer from mapLoader's properties
        var archetypeManager = customMapLoader.ArchetypeManager;
        var entityFactory = new EntityFactory(world, archetypeManager, definitionManager); 
        var gameInitializer = new GameInitializer(world, customMapLoader);

        Console.WriteLine("\n--- Initializing Player 1 ---");
        gameInitializer.InitializePlayer("Player 1", "orc_grunt");
        gameInitializer.InitializePlayer("Player 1", "orc_shaman");

        // --- Demonstrate fetching properties of the custom units ---
        Entity customGruntEntity = default;
        Entity orcShamanEntity = default;

        var queryDescription = new QueryDescription().WithAll<DefinitionId, Name>();
        world.Query(in queryDescription, (ref Entity entity, ref DefinitionId defId) =>
        {
            if (defId.Value == "orc_grunt")
            {
                customGruntEntity = entity;
            }
            else if (defId.Value == "orc_shaman")
            {
                orcShamanEntity = entity;
            }
        });

        if (customGruntEntity == default || orcShamanEntity == default)
        {
            Console.WriteLine("Error: Could not find spawned custom units in the world.");
            world.Dispose();
            return;
        }

        var gruntHealth = world.Get<Realm.Ecs.Components.Core.Health>(customGruntEntity);
        var gruntName = world.Get<Realm.Ecs.Components.Meta.Name>(customGruntEntity);
        Console.WriteLine($"\nSpawned Unit: {gruntName.Value} (ID: {world.Get<DefinitionId>(customGruntEntity).Value})");
        Console.WriteLine($"  Health: {gruntHealth.Current}/{gruntHealth.Max}");

        var shamanName = world.Get<Realm.Ecs.Components.Meta.Name>(orcShamanEntity);
        Console.WriteLine($"Spawned Unit: {shamanName.Value} (ID: {world.Get<DefinitionId>(orcShamanEntity).Value})");

        world.Dispose();

        Console.WriteLine("\n--- Custom Map Demo Complete ---");
    }
}
