namespace Realm.Ecs.Components.Resources;

/// <summary>
/// A component for entities that are harvestable resource nodes.
/// </summary>
public record struct ResourceNode(Guid ResourceTypeId, int Amount);