namespace Templates.Definitions
{
    using Templates.Ids;
    using System.Drawing; // For TintColor

    /// <summary>
    /// Defines a visual effect (e.g., particle system, animated mesh) to be displayed in the game world.
    /// </summary>
    public readonly record struct VisualEffectTemplate : IBaseTemplate<VisualEffectTemplate>
    {
        public UniqueId<VisualEffectTemplate> UniqueId { get; init; }
        public string GlbFilePath { get; init; }
        public float Scale { get; init; }
        public float DurationSeconds { get; init; }
        public Color TintColor { get; init; }
    }
}