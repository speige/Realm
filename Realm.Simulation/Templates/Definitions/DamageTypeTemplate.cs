namespace Templates.Definitions
{
    using Templates.Ids;

    /// <summary>
    /// Defines a damage type, used for damage calculations and resistances.
    /// </summary>
    public readonly record struct DamageTypeTemplate : IBaseTemplate<DamageTypeTemplate>
    {
        public UniqueId<DamageTypeTemplate> UniqueId { get; init; }
        public UniqueId<Presentation2dTemplate> Presentation2d { get; init; }
    }
}