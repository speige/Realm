using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Tags;

// These are empty structs that act as "tag components" for Arch ECS.
// Their presence on an entity indicates a specific capability or classification.
// Their definitions are managed in capabilityTypes.json for modding and validation.

/// <summary>
/// Tag indicating the entity represents a player.
/// </summary>
[TagDefinition("Player", "Player", "Marks the entity as a player in the game.")]
public readonly record struct Player;

/// <summary>
/// Tag indicating the entity is capable of movement.
/// </summary>
[TagDefinition("Movable", "Movable", "Grants the entity the ability to move across the map.")]
public readonly record struct Movable;

/// <summary>
/// Tag indicating the entity can be attacked.
/// </summary>
[TagDefinition("Attackable", "Attackable", "Allows the entity to be targeted and damaged by attacks.")]
public readonly record struct Attackable;

/// <summary>
/// Tag indicating the entity can cast spells or abilities.
/// </summary>
[TagDefinition("Caster", "Caster", "Indicates the entity can cast spells or abilities.")]
public readonly record struct Caster;

/// <summary>
/// Tag indicating the entity is a primary game unit.
/// </summary>
[TagDefinition("Unit", "Unit", "Marks the entity as a primary game unit.")]
public readonly record struct Unit;

/// <summary>
/// Tag indicating the entity is a static building.
/// </summary>
[TagDefinition("Building", "Building", "Marks the entity as a static structure.")]
public readonly record struct Building;

/// <summary>
/// Tag indicating the entity is a unique, powerful hero unit.
/// </summary>
[TagDefinition("Hero", "Hero", "Marks the entity as a unique, powerful hero unit.")]
public readonly record struct Hero;

/// <summary>
/// Tag indicating the entity is a static or destructible world object.
/// </summary>
[TagDefinition("Prop", "Prop", "Marks the entity as a static or destructible world object.")]
public readonly record struct Prop;

/// <summary>
/// Tag indicating the entity is dead and awaiting cleanup.
/// </summary>
[TagDefinition("Dead", "Dead", "Marks the entity as defeated, awaiting cleanup.")]
public readonly record struct Dead;