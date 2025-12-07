using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Resources;

// These are empty structs that act as "tag components" for resource types.
// Their definitions are discovered via attributes for modding and validation.

/// <summary>
/// Represents the Gold resource.
/// </summary>
[ResourceDefinition("Gold", "Gold", "A primary currency.", "res://icons/gold.png")]
public readonly record struct Gold;

/// <summary>
/// Represents the Wood resource.
/// </summary>
[ResourceDefinition("Wood", "Wood", "A building material.", "res://icons/wood.png")]
public readonly record struct Wood;

/// <summary>
/// Represents the Food resource.
/// </summary>
[ResourceDefinition("Food", "Food", "Sustains units.", "res://icons/food.png")]
public readonly record struct Food;
