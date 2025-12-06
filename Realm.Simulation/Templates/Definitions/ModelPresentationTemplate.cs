namespace Templates.Definitions
{
    using Templates.Ids;

    /// <summary>
    /// Defines a generic model representation.
    /// </summary>
    public readonly record struct ModelPresentationTemplate : IBaseTemplate<ModelPresentationTemplate>
    {
        public UniqueId<ModelPresentationTemplate> UniqueId { get; init; }
        public string ModelPath { get; init; }
    }
}
