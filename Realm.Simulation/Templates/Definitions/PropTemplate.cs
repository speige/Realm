namespace Templates.Definitions
{
    using Templates.Ids;

    /// <summary>
    /// Defines the blueprint for a prop, a static or destructible object in the game world.
    /// Props can optionally be resource nodes (e.g., a tree that provides wood).
    /// This composition allows for flexibility in defining world objects.
    /// </summary>
    public readonly record struct PropTemplate : IBaseTemplate<PropTemplate>
    {
        public UniqueId<PropTemplate> UniqueId { get; init; }
        public UniqueId<Presentation3dTemplate> Model { get; init; }
        public UniqueId<PathingTypeTemplate>[] BlockedPathingTypeIds { get; init; }
        public bool IsDestructible { get; init; }
        public int MaxHitPoints { get; init; }
        public UniqueId<ResourceNodeTemplate> ResourceNode { get; init; }
    }
}
