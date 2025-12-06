namespace Templates.Definitions
{
    using Templates.Ids;
    using System.Drawing;

    /// <summary>
    /// Defines a 3D model representation and its associated visual properties.
    /// </summary>
    public readonly record struct Presentation3dTemplate : IBaseTemplate<Presentation3dTemplate>
    {
        public UniqueId<Presentation3dTemplate> UniqueId { get; init; }
        public string GlbFilePath { get; init; }
        public float DefaultScale { get; init; }
        public float AnimationRunSpeedMultiplier { get; init; }
        public float AnimationWalkSpeedMultiplier { get; init; }
        public bool HasShadow { get; init; }
        public Color TintColor { get; init; }
    }
}
