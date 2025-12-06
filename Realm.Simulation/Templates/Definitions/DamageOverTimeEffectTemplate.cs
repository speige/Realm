namespace Templates.Definitions
{
    using Templates.Ids;

    /// <summary>
    /// Defines a behavior that applies damage periodically to a unit.
    /// </summary>
    public readonly record struct DamageOverTimeEffectTemplate : IBaseEffectTemplate<DamageOverTimeEffectTemplate>, IBaseEffectTemplate
	{
		public UniqueId<DamageOverTimeEffectTemplate> UniqueId { get; init; }

		public int DamageAmount { get; init; }
        public float IntervalSeconds { get; init; }
        public UniqueId<DamageTypeTemplate> DamageType { get; init; }
	}
}
