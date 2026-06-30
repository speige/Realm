namespace Realm.Ecs.Components.Combat;

/// <summary>
///     Defines an entity's attack capabilities.
/// </summary>
internal record struct Attack(float Damage, float Range, float Cooldown, float CurrentCooldown = 0);