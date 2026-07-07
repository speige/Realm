namespace Realm.Ecs.Components.Core;

/// <summary>
///     Tracks the current and maximum population limit for a player.
/// </summary>
internal record struct PlayerPopulation(int Current, int Max);
