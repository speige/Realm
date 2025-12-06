namespace Templates.Definitions
{
    using Templates.Ids;

    /// <summary>
    /// An effect that deals damage to a target.
    /// </summary>
    public readonly record struct DamageEffectTemplate : IBaseEffectTemplate<DamageEffectTemplate>, IBaseEffectTemplate
	{
        public UniqueId<DamageEffectTemplate> UniqueId { get; init; }

        public UniqueId<DamageTypeTemplate> DamageType { get; init; }
        public int Amount { get; init; }
    }
}