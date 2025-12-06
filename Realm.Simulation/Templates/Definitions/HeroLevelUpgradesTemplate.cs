namespace Templates.Definitions
{
    using Templates.Ids;

    /// <summary>
    /// Defines bonuses applied to a hero's stats upon leveling up.
    /// </summary>
    public readonly record struct HeroLevelUpgradesTemplate : IBaseTemplate<HeroLevelUpgradesTemplate>
    {
        public UniqueId<HeroLevelUpgradesTemplate> UniqueId { get; init; }
        public UniqueId<StatBonusTemplate>[] StatBonusesPerLevel { get; init; }
    }
}
