namespace Templates.Definitions
{
    using Templates.Ids;

    /// <summary>
    /// Defines a specific resource and its starting amount for a player.
    /// </summary>
    public readonly record struct StartingResourceTemplate : IBaseTemplate<StartingResourceTemplate>
    {
        public UniqueId<StartingResourceTemplate> UniqueId { get; init; }
        public UniqueId<PlayerResourceTemplate> ResourceType { get; init; }
        public int Amount { get; init; }
    }
}