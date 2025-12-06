namespace Templates.Definitions
{
    using Templates.Ids;

    /// <summary>
    /// Defines a component of a recipe's cost, specifying a resource type and amount.
    /// </summary>
    public readonly record struct ResourceCostTemplate : IBaseTemplate<ResourceCostTemplate>
    {
        public UniqueId<ResourceCostTemplate> UniqueId { get; init; }
        public UniqueId<PlayerResourceTemplate> ResourceType { get; init; }
        public int Amount { get; init; }
    }
}
