namespace Realm.Ecs.Common;

/// <summary>
///     Attribute for defining a tag component's metadata.
/// </summary>
internal class TagDefinitionAttribute : DefinitionAttribute
{
	public TagDefinitionAttribute(string id, string? displayName = null, string? description = null)
		: base(id, displayName, description)
	{
	}
}