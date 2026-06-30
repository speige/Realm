namespace Realm.Ecs.Common;

/// <summary>
///     A common base class for serializable definitions like resources or stats.
/// </summary>
internal class Definition
{
	public Definition(string id, string? displayName = null, string? description = null)
	{
		Id = id;
		DisplayName = displayName ?? id;
		Description = description ?? string.Empty;
	}

	public string Id { get; set; } = string.Empty;
	public string DisplayName { get; set; } = string.Empty;
	public string Description { get; set; } = string.Empty;
}