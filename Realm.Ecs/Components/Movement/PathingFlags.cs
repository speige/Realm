namespace Realm.Ecs.Components.Movement;

/// <summary>
///     Bitmask of terrain types this unit can traverse during pathfinding.
///     Bit 1 = shallow water, bit 2 = deep water, bit 4 = flying/air, bit 8 = ground, bit 16 = buildable.
/// </summary>
internal record struct PathingFlags(int Value);
