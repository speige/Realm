namespace Templates.Definitions
{
    using Templates.Ids;

    /// <summary>
    /// Defines a generic stat that can be applied to units.
    /// Stats are identified by a unique name and can be modified by effects, items, or upgrades.
    /// </summary>
    public readonly record struct StatTemplate : IBaseTemplate<StatTemplate>
    {
        public UniqueId<StatTemplate> UniqueId { get; init; }

    }
}