using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Core;

/// <summary>
///     Represents the ownership of an entity by a player.
/// </summary>
internal record struct Owner(PlayerEntity PlayerEntity);