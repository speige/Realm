using Templates.Ids;
using Templates.Definitions;

namespace Templates.Definitions
{
    /// <summary>
    /// Defines the blueprint for a unit, the primary actor in the game.
    /// </summary>
    public readonly record struct UnitTemplate : IBaseTemplate<UnitTemplate>
    {
        public UniqueId<UnitTemplate> UniqueId { get; init; }
        public UniqueId<Presentation2dTemplate> Presentation2d { get; init; }
        public UniqueId<Presentation3dTemplate> Model { get; init; }
        public int MaxHitPoints { get; init; }
        public UniqueId<UnitResourceTemplate> PrimaryResourceType { get; init; }
        public int BaseArmor { get; init; }
        public UniqueId<ArmorTypeTemplate> ArmorType { get; init; }
        public int FoodSupplyCost { get; init; }
        public int FoodSupplyProvided { get; init; }
        public float AcquisitionRange { get; init; }
        public float SightRadius { get; init; }
        public UniqueId<MovementTemplate> MovementType { get; init; }
        public UniqueId<TeamTemplate> Team { get; init; }
        public UniqueId<AbilityTemplate>[] AbilityIds { get; init; }
        public UniqueId<ResourceCostTemplate>[] ProductionCosts { get; init; }
        public UniqueId<CommandTemplate> DefaultCommand { get; init; }
        public UniqueId<StatBonusTemplate>[] BaseStatBonuses { get; init; }
    }
}
