namespace Templates.Definitions
{
    using Templates.Ids;

    /// <summary>
    /// A container for a set of behaviors applied to a unit for a specific duration.
    /// </summary>
    public readonly record struct ApplyEffectsTemplate : IBaseTemplate<ApplyEffectsTemplate>
    {
        public UniqueId<ApplyEffectsTemplate> UniqueId { get; init; }
        public UniqueId<Presentation2dTemplate> Presentation2d { get; init; }
        public float DurationSeconds { get; init; }
        public int MaxStacks { get; init; }

        // Composable behaviors
        public UniqueId<IBaseEffectTemplate>[] BaseEffects { get; init; }
    }
}