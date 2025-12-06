namespace Templates.Definitions
{
    using Templates.Ids;

    /// <summary>
    /// Defines a team and its relationships (allies/enemies). Set in the game lobby.
    /// </summary>
    public readonly record struct TeamTemplate : IBaseTemplate<TeamTemplate>
    {
        public UniqueId<TeamTemplate> UniqueId { get; init; }
        public UniqueId<Presentation2dTemplate> Presentation2d { get; init; }
        public UniqueId<TeamTemplate>[] Allies { get; init; }
        public UniqueId<TeamTemplate>[] Enemies { get; init; }
    }
}
