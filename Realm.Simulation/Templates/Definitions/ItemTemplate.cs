using Templates.Ids;

namespace Templates.Definitions
{
    /// <summary>
    /// Defines an item that can be held or used by units.
    /// </summary>
    public readonly record struct ItemTemplate : IBaseTemplate<ItemTemplate>
    {
        public UniqueId<ItemTemplate> UniqueId { get; init; }
        public UniqueId<Presentation2dTemplate> Presentation2d { get; init; }
        public int MaxStackCount { get; init; }
        public float Weight { get; init; }
        public bool IsConsumable { get; init; }
        public bool CanBeDropped { get; init; }
        public bool DropsOnDeath { get; init; }
        public UniqueId<AbilityTemplate>[] GrantedAbilities { get; init; }
    }
}
