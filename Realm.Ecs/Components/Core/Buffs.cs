namespace Realm.Ecs.Components.Core;

/// <summary>
///     Holds active status effects and buffs for an entity.
/// </summary>
internal record struct Buffs(Dictionary<string, float> Value);
