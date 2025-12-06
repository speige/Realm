namespace Templates.Definitions
{
    using Templates.Ids;

    /// <summary>
    /// Defines how an entity is presented in the Heads-Up Display (HUD), like its icon and text.
    /// </summary>
    public readonly record struct Presentation2dTemplate : IBaseTemplate<Presentation2dTemplate>
    {
        public UniqueId<Presentation2dTemplate> UniqueId { get; init; }
        public string DisplayName { get; init; }
        public string TooltipBasic { get; init; }
        public string TooltipExtended { get; init; }
        public UniqueId<Icon2dTemplate> Icon2d { get; init; }
    }
}