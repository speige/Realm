namespace Realm.Ecs.Components.Abilities;

/// <summary>
///     A component that defines the targeting criteria for an ability.
/// </summary>
internal record struct AbilityTargetFilter(TargetAlliance Alliances, float SearchRadius);