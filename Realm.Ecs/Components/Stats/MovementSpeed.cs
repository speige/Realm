using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Stats;

/// <summary>
///     Represents the Movement Speed stat.
/// </summary>
[StatDefinition("MovementSpeed", "Movement Speed", "The speed at which the entity moves.")]
internal readonly record struct MovementSpeed;