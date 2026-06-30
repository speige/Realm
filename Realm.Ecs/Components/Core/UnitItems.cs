namespace Realm.Ecs.Components.Core;

/// <summary>
///     Holds active items in the unit's inventory.
/// </summary>
internal record struct UnitItems(List<string> Value);
