namespace Realm.Ecs.Components.Core;

/// <summary>
///     Holds custom key-value metadata for the entity.
/// </summary>
internal record struct CustomMetadata(Dictionary<string, object> Value);
