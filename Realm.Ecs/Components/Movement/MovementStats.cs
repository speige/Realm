namespace Realm.Ecs.Components.Movement;

/// <summary>
/// Defines the movement properties of an entity.
/// </summary>
public record struct MovementStats(float Speed, float Acceleration, float TurnRate);