namespace Templates.Definitions
{
    using Templates.Ids;

    /// <summary>
    /// Defines a recipe for producing a unit, ability, or upgrade. Includes cost and time.
    /// </summary>
    public readonly record struct RecipeTemplate : IBaseTemplate<RecipeTemplate>
    {
        public UniqueId<RecipeTemplate> UniqueId { get; init; }
        public UniqueId<Presentation2dTemplate> Presentation2d { get; init; }
        public UniqueId<ResourceCostTemplate>[] ResourceCosts { get; init; }
        public float ProductionTimeSeconds { get; init; }
        public UniqueId<AbilityTemplate>[] ProducedAbilitiess { get; init; }
        public UniqueId<UnitTemplate>[] ProducedUnits { get; init; }
    }
}
