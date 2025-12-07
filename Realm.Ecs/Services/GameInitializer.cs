using Arch.Core;
using Realm.Ecs.Archetypes;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags; // Added for Player tag
using System;
using System.Collections.Generic;
using System.Numerics;

namespace Realm.Ecs.Services;

/// <summary>
/// A conceptual service that initializes the game state, including players and their starting units.
/// This demonstrates how PlayerEntity and Owner components would be used in practice.
/// </summary>
public class GameInitializer
{
    private readonly World _world;
    private readonly EntityFactory _entityFactory;
    private readonly ArchetypeManager _archetypeManager;
    private readonly DefinitionManager _definitionManager; // Inject consolidated DefinitionManager

    public GameInitializer(World world, MapLoader mapLoader)
    {
        _world = world;
        _definitionManager = mapLoader.DefinitionManager; // Get from mapLoader
        _archetypeManager = mapLoader.ArchetypeManager;
        _entityFactory = new EntityFactory(_world, _archetypeManager, mapLoader.DefinitionManager); // Use TagManager
    }

    /// <summary>
    /// Creates a player entity and spawns a starting unit for them, assigning ownership.
    /// </summary>
    public void InitializePlayer(string playerName, string startingUnitArchetypeId)
    {
        // 1. Create the Player Entity
        var playerEntity = _world.Create();
        _world.Set(playerEntity, new Player()); // Tag it as a player (struct exists)
        _world.Set(playerEntity, new Name(playerName));
        // Add starting resources for the player
        _world.Set(playerEntity, new PlayerResources(new Dictionary<ResourceId, int>
        {
            { "Gold".AsResourceId(_definitionManager), 500 } // Example: 500 Gold
        }));

        // 2. Create a type-safe PlayerEntity wrapper
        var typedPlayerEntity = playerEntity.AsPlayerEntity(_world);

        // 3. Spawn a unit using the EntityFactory
        var unitEntity = _entityFactory.SpawnUnit(startingUnitArchetypeId, new Vector3(0, 0, 0)); // Spawn at (0,0,0) for now

        // 4. Assign ownership to the unit
        _world.Set(unitEntity, new Owner(typedPlayerEntity));

        System.Console.WriteLine($"{playerName} initialized with a {startingUnitArchetypeId} unit.");
    }
}