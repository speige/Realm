using System.Numerics;
using Arch.Core;

namespace Realm.Ecs.Components.Core;

/// <summary>
/// Represents the target of a pending or active cast action.
/// </summary>
internal record struct CastTarget(Vector3 Position, Entity EntityTarget);
