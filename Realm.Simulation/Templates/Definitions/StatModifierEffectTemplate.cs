namespace Templates.Definitions
{
    using Templates.Ids;

    /// <summary>
    /// Defines a modification to a single unit stat, such as hit points or speed.
    /// </summary>
    public readonly record struct StatModifierEffectTemplate : IBaseEffectTemplate<StatModifierEffectTemplate>, IBaseEffectTemplate
{
        public UniqueId<StatModifierEffectTemplate> UniqueId { get; init; }
		
        public UniqueId<StatTemplate> StatToModify { get; init; }
        public ModifierType Type { get; init; }
        public float Value { get; init; }
    }
}
