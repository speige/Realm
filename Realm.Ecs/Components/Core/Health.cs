namespace Realm.Ecs.Components.Core;

/// <summary>
///     Represents the health of an entity.
/// </summary>
internal record struct Health(float Current, float Max);