namespace Templates.Definitions
{
    using Templates.Ids;

    /// <summary>
    /// Defines a sound effect to be played in the game.
    /// </summary>
    public readonly record struct SoundEffectTemplate : IBaseTemplate<SoundEffectTemplate>
    {
        public UniqueId<SoundEffectTemplate> UniqueId { get; init; }
        public string FilePath { get; init; }
        public float VolumeDb { get; init; }
        public float PitchScale { get; init; }
        public bool Loop { get; init; }
    }
}