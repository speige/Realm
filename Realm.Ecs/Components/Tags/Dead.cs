using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Tags;

/// <summary>
///     Tag indicating the entity is dead and awaiting cleanup.
/// </summary>
[TagDefinition("Dead", "Dead", "Marks the entity as defeated, awaiting cleanup.")]
internal readonly record struct Dead;