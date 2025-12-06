namespace Templates.Definitions
{
    using Templates.Ids;

    /// <summary>
    /// Defines a unit's resource, such as Mana or Energy.
    /// </summary>
    public readonly record struct UnitResourceTemplate : IBaseTemplate<UnitResourceTemplate>
    {
        public UniqueId<UnitResourceTemplate> UniqueId { get; init; }
        public UniqueId<Presentation2dTemplate> Presentation2d { get; init; }
        public int MaxAmount { get; init; }
        public float InitialAmountPercentage { get; init; }
        public float RegenerationRatePerSecond { get; init; }
    }
}