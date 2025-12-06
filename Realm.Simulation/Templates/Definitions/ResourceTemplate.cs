namespace Templates.Definitions
{
    using Templates.Ids;

    /// <summary>
    /// Defines a type of resource tracked per player, such as Gold, Wood, or Stone.
    /// </summary>
    public readonly record struct PlayerResourceTemplate : IBaseTemplate<PlayerResourceTemplate>
    {
        public UniqueId<PlayerResourceTemplate> UniqueId { get; init; }
        public UniqueId<Presentation2dTemplate> Presentation2d { get; init; }
        public int MaxResourceAmount { get; init; }
        public float RegenerationRatePerSecond { get; init; }
    }
}
