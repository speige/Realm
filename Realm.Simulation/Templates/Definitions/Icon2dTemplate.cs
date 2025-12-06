namespace Templates.Definitions
{
    using Templates.Ids;
    
    /// <summary>
    /// Defines a 2D model representation, typically a static image.
    /// </summary>
    public readonly record struct Icon2dTemplate : IBaseTemplate<Icon2dTemplate>
    {
        public UniqueId<Icon2dTemplate> UniqueId { get; init; }
        public string PngFilePath { get; init; }
    }
}
