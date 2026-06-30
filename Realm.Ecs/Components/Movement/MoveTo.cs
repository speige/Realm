using System.Numerics;

namespace Realm.Ecs.Components.Movement;

/// <summary>
///     An intent component that signals an entity should move to a target position.
/// </summary>
internal record struct MoveTo(Vector3 Target);