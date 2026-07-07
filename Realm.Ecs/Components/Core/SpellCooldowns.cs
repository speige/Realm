namespace Realm.Ecs.Components.Core;

/// <summary>
///     Tracks cooldown timers for commander abilities/spells.
/// </summary>
internal record struct SpellCooldowns(float FireballCooldown, float LightningCooldown, float HolyLightCooldown);
