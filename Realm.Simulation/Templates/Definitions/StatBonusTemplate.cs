namespace Templates.Definitions
{
    using Templates.Ids;

    /// <summary>
    /// Defines a bonus to a specific stat, either flat or percentage-based.
    /// </summary>
    public readonly record struct StatBonusTemplate : IBaseTemplate<StatBonusTemplate>
    {
        public UniqueId<StatBonusTemplate> UniqueId { get; init; } // Not strictly necessary for a "sub-template" but good for consistency
        public UniqueId<StatTemplate> Stat { get; init; }
        public ModifierType Type { get; init; }
        public float Value { get; init; }
    }
}
