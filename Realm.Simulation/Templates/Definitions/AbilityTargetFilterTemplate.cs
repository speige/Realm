namespace Templates.Definitions
{
    using Templates.Ids;

    /// <summary>
    /// Defines a set of criteria for filtering and selecting valid targets.
    /// Used by abilities and effects to determine what they can affect.
    /// </summary>
    public readonly record struct AbilityTargetFilterTemplate : IBaseTemplate<AbilityTargetFilterTemplate>
    {
        public UniqueId<AbilityTargetFilterTemplate> UniqueId { get; init; }
        public TargetAlliance Alliances { get; init; }
        public UniqueId<TagTemplate>[] RequiredTags { get; init; }
        public UniqueId<TagTemplate>[] ExcludedTags { get; init; }
        public float SearchRadius { get; init; }
    }
}
