namespace Realm.Ecs.Components.Combat;

/// <summary>
/// Defines an entity's attack capabilities.
/// </summary>
public record struct Attack(float Damage, float Range, float Cooldown, float CurrentCooldown = 0);