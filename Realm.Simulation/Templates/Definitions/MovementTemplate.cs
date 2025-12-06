namespace Templates.Definitions
{
    using Templates.Ids;

    /// <summary>
    /// Defines movement properties for a unit, such as speed and turn rate.
    /// </summary>
    public readonly record struct MovementTemplate : IBaseTemplate<MovementTemplate>
    {
        public UniqueId<MovementTemplate> UniqueId { get; init; }
        public float BaseSpeed { get; init; }
        public float TurnRateDegreesPerSecond { get; init; }
        public UniqueId<CollisionTemplate> Collision { get; init; }
        public UniqueId<PathingTypeTemplate> PathingType { get; init; }
        public UniqueId<GroupFormationTemplate> GroupFormation { get; init; }
    }
}
