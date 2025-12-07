using Realm.Ecs.Archetypes;
using Realm.Ecs.Common; // For ResourceId and TagId
using System;
using System.Collections.Generic;

namespace Realm.Ecs.Services;

/// <summary>
/// Loads and manages all game archetypes from definition files.
/// </summary>
public class ArchetypeManager
{
    private readonly Dictionary<string, UnitArchetype> _unitArchetypes = new();

    private readonly DefinitionManager _definitionManager; // Consolidated manager

    public ArchetypeManager(List<UnitArchetype> units, DefinitionManager definitionManager)
    {
        _definitionManager = definitionManager;

        foreach (var archetype in units)
        {
            // Validate ResourceCosts
            foreach (var cost in archetype.ResourceCosts)
            {
                if (!_definitionManager.IsValidResource(cost.ResourceTypeId.Value)) // Use .Value for comparison
                {
                    throw new ArgumentException($"Unit Archetype '{archetype.Id}' references an invalid ResourceTypeId: '{cost.ResourceTypeId.Value}'");
                }
            }

            // Validate Capabilities
            foreach (var capabilityId in archetype.Capabilities)
            {
                if (!_definitionManager.IsValidTag(capabilityId))
                {
                    throw new ArgumentException($"Unit Archetype '{archetype.Id}' references an invalid CapabilityId: '{capabilityId}'");
                }
            }

            // In a full game, you'd also validate StatTypeIds if the archetype had a 'Stats' component
            // For example:
            // if (archetype.Stats != null) {
            //     foreach (var stat in archetype.Stats.Value) {
            //         if (!_definitionManager.IsValidStat(stat.Key.Value)) { // Use .Value for comparison
            //             throw new ArgumentException($"Unit Archetype '{archetype.Id}' references an invalid StatTypeId: '{stat.Key.Value}'");
            //         }
            //     }
            // }

            _unitArchetypes[archetype.Id] = archetype;
        }
    }

    public UnitArchetype? GetUnitArchetype(string id)
    {
        return _unitArchetypes.TryGetValue(id, out var archetype) ? archetype : null;
    }

    public IEnumerable<UnitArchetype> GetAllUnitArchetypes()
    {
        return _unitArchetypes.Values;
    }
}