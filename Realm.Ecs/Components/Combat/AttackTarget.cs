using Arch.Core;

namespace Realm.Ecs.Components.Combat;

/// <summary>
///     An intent component that signals an entity should attack a specific target.
/// </summary>
internal record struct AttackTarget(Entity Target);