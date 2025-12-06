namespace Templates.Definitions
{
    using Templates.Ids;

    /// <summary>
    /// Defines an upgrade that can be researched by a player to improve units or unlock abilities.
    /// </summary>
    public readonly record struct UpgradeTemplate : IBaseTemplate<UpgradeTemplate>
    {
        public UniqueId<UpgradeTemplate> UniqueId { get; init; }
        public UniqueId<Presentation2dTemplate> Presentation2d { get; init; }
        public UniqueId<ResourceCostTemplate>[] ResearchCosts { get; init; }
        public float ResearchTimeSeconds { get; init; }
        public UniqueId<UpgradeTemplate>[] Prerequisites { get; init; }
    }
}
