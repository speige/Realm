using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Stats;

// These are empty structs that act as "tag components" for stat types.
// Their definitions are discovered via attributes for modding and validation.

/// <summary>
/// Represents the Attack Damage stat.
/// </summary>
[StatDefinition("AttackDamage", "Attack Damage", "The base damage dealt by attacks.")]
public readonly record struct AttackDamage;

/// <summary>
/// Represents the Attack Speed stat.
/// </summary>
[StatDefinition("AttackSpeed", "Attack Speed", "The speed at which attacks are performed.")]
public readonly record struct AttackSpeed;

/// <summary>
/// Represents the Movement Speed stat.
/// </summary>
[StatDefinition("MovementSpeed", "Movement Speed", "The speed at which the entity moves.")]
public readonly record struct MovementSpeed;

/// <summary>
/// Represents the Health stat.
/// </summary>
[StatDefinition("Health", "Health", "The entity's maximum hit points.")]
public readonly record struct HealthStat; // Renamed to HealthStat to avoid conflict with Health component

/// <summary>
/// Represents the Armor stat.
/// </summary>
[StatDefinition("Armor", "Armor", "Reduces incoming physical damage.")]
public readonly record struct ArmorStat; // Renamed to ArmorStat to avoid conflict with Armor component
