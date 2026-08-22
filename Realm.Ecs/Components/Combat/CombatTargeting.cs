namespace Realm.Ecs.Components.Combat;

/// <summary>
///     Defines which target types an entity's attacks can affect.
/// </summary>
internal record struct CombatTargeting(bool CanTargetAir, bool CanTargetGround);