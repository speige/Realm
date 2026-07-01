namespace Realm.Ecs.Components.Resources;

/// <summary>
///     A component for entities that are harvestable resource nodes.
/// </summary>
internal record struct ResourceNode(Guid ResourceTypeId, float Amount);