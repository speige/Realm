using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using System;
using System.Collections.Generic;

namespace Realm.Ecs.Archetypes;

/// <summary>
/// Defines the serializable blueprint for a unit archetype.
/// This class is used to deserialize unit definitions from JSON files.
/// </summary>
public class UnitArchetype
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public Health Health { get; set; }
    public Attack Attack { get; set; }
    public Armor Armor { get; set; }
    public MovementStats MovementStats { get; set; }
    public ResourceCost[] ResourceCosts { get; set; } = Array.Empty<ResourceCost>();
    
    // A unit can have multiple tags
    public List<string> Capabilities { get; set; } = new();

    // In the future, this could point to ability archetype guids
    public List<string> Abilities { get; set; } = new(); 
}
