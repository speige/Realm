namespace Templates.Definitions
{
    using Templates.Ids;

    /// <summary>
    /// Defines a command ID for abilities, allowing UI elements to find and display the correct ability.
    /// </summary>
    public readonly record struct CommandTemplate : IBaseTemplate<CommandTemplate>
    {
        public UniqueId<CommandTemplate> UniqueId { get; init; }
    }
}