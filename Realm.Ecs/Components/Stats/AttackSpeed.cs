using Realm.Ecs.Common;

namespace Realm.Ecs.Components.Stats;

/// <summary>
///     Represents the Attack Speed stat.
/// </summary>
[StatDefinition("AttackSpeed", "Attack Speed", "The speed at which attacks are performed.")]
internal readonly record struct AttackSpeed;