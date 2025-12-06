namespace Templates.Definitions
{
    using Templates.Ids;

    /// <summary>
    /// Defines an armor type, used for damage calculations.
    /// </summary>
    public readonly record struct ArmorTypeTemplate : IBaseTemplate<ArmorTypeTemplate>
    {
        public UniqueId<ArmorTypeTemplate> UniqueId { get; init; }
        public UniqueId<Presentation2dTemplate> Presentation2d { get; init; }
    }
}