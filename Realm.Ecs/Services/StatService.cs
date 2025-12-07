using Arch.Core;
using Realm.Ecs.Common; // For StatId
using Realm.Ecs.Components.Stats;
using System;

namespace Realm.Ecs.Services;

/// <summary>
/// Demonstrates how Stat and StatModifier components are used together.
/// </summary>
public class StatService
{
    private readonly World _world;
    private readonly DefinitionManager _definitionManager; // Needed for StatId validation

    public StatService(World world, DefinitionManager definitionManager)
    {
        _world = world;
        _definitionManager = definitionManager;
    }

    /// <summary>
    /// Gets the final calculated value of a stat after all modifiers are applied.
    /// An entity can have multiple StatModifier components.
    /// </summary>
    public float GetStatValue(Entity entity, StatId statId)
    {
        // 1. Get the base stat value from the 'Stats' component dictionary.
        if (!_world.Has<Stats>(entity)) return 0f;
        
        var baseStats = _world.Get<Stats>(entity).Value;
        if (!baseStats.TryGetValue(statId, out var currentValue)) return 0f;
        
        // 2. Query for all StatModifier components on this entity.
        // NOTE: This version of Arch does not have a World.GetAll<T>(entity) method.
        // The idiomatic way to handle this is to query all entities with the component
        // and check if the entity ID matches inside the loop. For a single-entity lookup,
        // this is less efficient than GetAll, but it is the correct approach for this library version.
        var query = new QueryDescription().WithAll<StatModifier>();
        var flatBonus = 0f;
        var percentBonus = 1.0f;

        _world.Query(in query, (ref Entity e, ref StatModifier mod) =>
        {
            if (e != entity || mod.StatTypeId != statId) return; // Compare StatId directly

            if (mod.Type == ModifierType.Flat)
            {
                flatBonus += mod.Value;
            }
            else if (mod.Type == ModifierType.Percentage)
            {
                percentBonus *= mod.Value;
            }
        });

        // Apply flat bonuses first, then percentage.
        return (currentValue + flatBonus) * percentBonus;
    }

    /// <summary>
    /// Adds a StatModifier to an entity. A 'BuffSystem' would be responsible
    /// for managing the duration and removing expired modifiers.
    /// </summary>
    public void ApplyStatModifier(Entity entity, StatModifier modifier)
    {
        _world.Add(entity, modifier);
    }
}
