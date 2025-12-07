using Arch.Core;
using Realm.Ecs.Common; // For ResourceId
using Realm.Ecs.Components.Resources;
using System;
using System.Collections.Generic; // For Dictionary

namespace Realm.Ecs.Services;

/// <summary>
/// Demonstrates how player-specific resource data is used with archetype definition data.
/// </summary>
public class PlayerResourceService
{
    private readonly World _world;
    private readonly ArchetypeManager _archetypeManager; // Still needed to get archetype
    private readonly DefinitionManager _definitionManager; // To validate / convert string IDs

    public PlayerResourceService(World world, ArchetypeManager archetypeManager, DefinitionManager definitionManager)
    {
        _world = world;
        _archetypeManager = archetypeManager;
        _definitionManager = definitionManager;
    }

    /// <summary>
    /// Checks if a player can afford to build a unit defined by an archetype.
    /// </summary>
    public bool CanAfford(Entity playerEntity, string unitArchetypeId)
    {
        var archetype = _archetypeManager.GetUnitArchetype(unitArchetypeId);
        if (archetype?.ResourceCosts == null || archetype.ResourceCosts.Length == 0) return true; // No cost

        if (!_world.Has<PlayerResources>(playerEntity)) return false; // Player has no resources at all

        var playerResources = _world.Get<PlayerResources>(playerEntity).Value;
        
        foreach (var cost in archetype.ResourceCosts)
        {
            if (!playerResources.TryGetValue(cost.ResourceTypeId, out var playerAmount) || playerAmount < cost.Amount)
            {
                return false; // Player has insufficient funds for this resource type
            }
        }

        return true;
    }

    /// <summary>
    /// Deducts the cost of a unit from a player's resources.
    /// </summary>
    public void DeductCost(Entity playerEntity, string unitArchetypeId)
    {
        var archetype = _archetypeManager.GetUnitArchetype(unitArchetypeId);
        if (archetype?.ResourceCosts == null || !_world.Has<PlayerResources>(playerEntity)) return;

        // Get the component by reference to modify it directly
        ref var playerResources = ref _world.Get<PlayerResources>(playerEntity);

        foreach (var cost in archetype.ResourceCosts)
        {
            if (playerResources.Value.ContainsKey(cost.ResourceTypeId))
            {
                playerResources.Value[cost.ResourceTypeId] -= cost.Amount;
            }
        }
    }
}
