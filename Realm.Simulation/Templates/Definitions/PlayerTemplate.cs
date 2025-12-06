namespace Templates.Definitions
{
    using Templates.Ids;

    /// <summary>
    /// Defines a player in the game, including starting resources, race, and team settings.
    /// </summary>
    public readonly record struct PlayerTemplate : IBaseTemplate<PlayerTemplate>
    {
        public UniqueId<PlayerTemplate> UniqueId { get; init; }
        public UniqueId<RaceTemplate> Race { get; init; }
        public UniqueId<TeamTemplate> Team { get; init; }
        public UniqueId<StartingResourceTemplate>[] StartingResources { get; init; }
    }
}