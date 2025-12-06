namespace Templates.Definitions
{
    using Templates.Ids;

    /// <summary>
    /// Defines a harvestable resource node in the world, like a gold mine or a forest.
    /// </summary>
    public readonly record struct ResourceNodeTemplate : IBaseTemplate<ResourceNodeTemplate>
    {
        public UniqueId<ResourceNodeTemplate> UniqueId { get; init; }
        public UniqueId<PlayerResourceTemplate> ResourceType { get; init; }
        public int MaxResourceAmount { get; init; }
        public float RegenerationRatePerSecond { get; init; }
    }
}