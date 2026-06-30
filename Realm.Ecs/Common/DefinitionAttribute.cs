namespace Realm.Ecs.Common;

/// <summary>
///     Base attribute for defining metadata for various game elements (tags, resources, stats).
///     This allows game elements to be defined directly in code with associated display information.
/// </summary>
[AttributeUsage(AttributeTargets.Struct)]
public abstract class DefinitionAttribute : Attribute
{
	public DefinitionAttribute(string id, string? displayName = null, string? description = null)
	{
		Id = id;
		DisplayName = displayName ?? id;
		Description = description ?? string.Empty;
	}

	public string Id { get; }
	public string DisplayName { get; set; }
	public string Description { get; set; }
}