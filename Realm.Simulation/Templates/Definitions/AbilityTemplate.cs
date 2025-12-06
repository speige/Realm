namespace Templates.Definitions
{
    using Templates.Ids;

    /// <summary>
    /// Defines the blueprint for an ability that can be used by a unit.
    /// It specifies targeting, cost, and the game effect to trigger upon use.
    /// </summary>
    public readonly record struct AbilityTemplate : IBaseTemplate<AbilityTemplate>
    {
        public UniqueId<AbilityTemplate> UniqueId { get; init; }
        public UniqueId<Presentation2dTemplate> Presentation2d { get; init; }
        
        public float CastRange { get; init; }
        public UniqueId<AbilityTargetFilterTemplate> TargetFilter { get; init; }
        
        public int CooldownSeconds { get; init; }
        public UniqueId<ResourceCostTemplate>[] ResourceCosts { get; init; }

        public int MaxCharges { get; init; }
        public float ChargeRechargeTimeSeconds { get; init; }
        
        public UniqueId<ApplyEffectsTemplate> ApplyEffects { get; init; }

        public UniqueId<VisualEffectTemplate> VisualEffect { get; init; }
        public UniqueId<SoundEffectTemplate> SoundEffect { get; init; }
        public UniqueId<CommandTemplate> CommandID { get; init; }

	}
}