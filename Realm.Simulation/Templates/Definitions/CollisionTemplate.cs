namespace Templates.Definitions
{
    using Templates.Ids;

    /// <summary>
    /// Defines collision properties for an entity.
    /// </summary>
    public readonly record struct CollisionTemplate : IBaseTemplate<CollisionTemplate>
    {
        public UniqueId<CollisionTemplate> UniqueId { get; init; }
        public float CollisionRadius { get; init; }
        public UniqueId<PathingTypeTemplate>[] BlockedPathingTypes { get; init; }
    }
}