namespace Realm.Ecs.Common;

/// <summary>
/// A common base class for serializable definitions like resources or stats.
/// </summary>
public class Definition
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    // Parameterized constructor for convenience
    public Definition(string id, string? displayName = null, string? description = null) // Made nullable
    {
        Id = id;
        DisplayName = displayName ?? id;
        Description = description ?? string.Empty;
    }
}

/// <summary>
/// Represents a Tag Definition.
/// </summary>
public class TagDefinition : Definition
{
    public TagDefinition(string id, string? displayName = null, string? description = null) // Made nullable
        : base(id, displayName, description) { }
}

/// <summary>
/// Represents a Resource Definition.
/// </summary>
public class ResourceDefinition : Definition
{
    public string IconPath { get; set; }

    public ResourceDefinition(string id, string? displayName = null, string? description = null, string? iconPath = null) // Made nullable
        : base(id, displayName, description)
    {
        IconPath = iconPath ?? string.Empty;
    }
}

/// <summary>
/// Represents a Stat Definition.
/// </summary>
public class StatDefinition : Definition
{
    public StatDefinition(string id, string? displayName = null, string? description = null) // Made nullable
        : base(id, displayName, description) { }
}
