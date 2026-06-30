namespace Realm.Ecs.Common;

/// <summary>
///     Represents a Stat Definition.
/// </summary>
internal class StatDefinition : Definition
{
	public StatDefinition(string id, string? displayName = null, string? description = null)
		: base(id, displayName, description)
	{
	}
}