namespace Templates.Definitions
{
    using Templates.Ids;

    /// <summary>
    /// Defines a player's race (e.g., Human, Orc), including available units and upgrades.
    /// </summary>
    public readonly record struct RaceTemplate : IBaseTemplate<RaceTemplate>
    {
        public UniqueId<RaceTemplate> UniqueId { get; init; }
        public UniqueId<Presentation2dTemplate> Presentation2d { get; init; }
        public UniqueId<UnitTemplate>[] AvailableUnits { get; init; }
        public UniqueId<UpgradeTemplate>[] AvailableUpgrades { get; init; }
    }
}
