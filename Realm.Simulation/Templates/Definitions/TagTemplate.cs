namespace Templates.Definitions
{
    using Templates.Ids;

    /// <summary>
    /// Defines a tag that can be applied to units or other entities for filtering and identification.
    /// E.g., "Mechanical", "Biological", "Hero".
    /// </summary>
    public readonly record struct TagTemplate : IBaseTemplate<TagTemplate>
    {
        public UniqueId<TagTemplate> UniqueId { get; init; }
        public UniqueId<Presentation2dTemplate> Presentation2d { get; init; }
    }
}
