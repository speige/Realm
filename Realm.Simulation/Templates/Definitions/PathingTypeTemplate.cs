namespace Templates.Definitions
{
    using Templates.Ids;

    /// <summary>
    /// Defines a type of pathing, such as Ground, Air, or Water, and what it can traverse.
    /// </summary>
    public readonly record struct PathingTypeTemplate : IBaseTemplate<PathingTypeTemplate>
    {
        public UniqueId<PathingTypeTemplate> UniqueId { get; init; }
        public UniqueId<Presentation2dTemplate> Presentation2d { get; init; }
        public UniqueId<PathingTypeTemplate>[] CanTraverseTypes { get; init; }
    }
}
