namespace Realm.Ecs.Components.Combat;

/// <summary>
///     Represents the entity that last attacked/damaged this entity.
/// </summary>
internal record struct LastAttacker(Arch.Core.Entity Value);
