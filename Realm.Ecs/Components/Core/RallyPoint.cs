using System.Numerics;

namespace Realm.Ecs.Components.Core;

/// <summary>
///     Represents a rally point for trained units to move to upon spawning.
/// </summary>
internal record struct RallyPoint(Vector3 Value);
