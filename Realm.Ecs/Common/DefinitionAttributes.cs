namespace Realm.Ecs.Common;

/// <summary>
///     Base attribute for defining metadata for various game elements (tags, resources, stats).
///     This allows game elements to be defined directly in code with associated display information.
/// </summary>
[AttributeUsage(AttributeTargets.Struct)]
public abstract class DefinitionAttribute : Attribute
{
	protected DefinitionAttribute(string id, string? displayName = null, string? description = null) // Made nullable
	{
		Id = id;
		DisplayName = displayName ?? id;
		Description = description ?? string.Empty;
	}

	public string Id { get; }
	public string DisplayName { get; set; }
	public string Description { get; set; }
}

/// <summary>
///     Attribute for defining a tag component's metadata.
/// </summary>
public class TagDefinitionAttribute : DefinitionAttribute
{
	public TagDefinitionAttribute(string id, string? displayName = null, string? description = null) // Made nullable
		: base(id, displayName, description)
	{
	}
}

/// <summary>
///     Attribute for defining a resource type's metadata.
/// </summary>
public class ResourceDefinitionAttribute : DefinitionAttribute
{
	public ResourceDefinitionAttribute(string id, string? displayName = null, string? description = null,
		string? iconPath = null) // Made nullable
		: base(id, displayName, description)
	{
		IconPath = iconPath ?? string.Empty;
	}

	public string IconPath { get; set; }
}

/// <summary>
///     Attribute for defining a stat type's metadata.
/// </summary>
public class StatDefinitionAttribute : DefinitionAttribute
{
	public StatDefinitionAttribute(string id, string? displayName = null, string? description = null) // Made nullable
		: base(id, displayName, description)
	{
	}
}