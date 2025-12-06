namespace Templates.Definitions
{
    using Templates.Ids;

    /// <summary>
    /// Defines the properties of a unit group formation, used for pathing and movement.
    /// </summary>
    public readonly record struct GroupFormationTemplate : IBaseTemplate<GroupFormationTemplate>
    {
        public UniqueId<GroupFormationTemplate> UniqueId { get; init; }
        public int Rows { get; init; }
        public int Columns { get; init; }
        public float Spacing { get; init; }
    }
}