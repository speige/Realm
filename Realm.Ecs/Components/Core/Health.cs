namespace Realm.Ecs.Components.Core;

/// <summary>
/// Represents the health of an entity.
/// </summary>
public record struct Health(float Current, float Max);