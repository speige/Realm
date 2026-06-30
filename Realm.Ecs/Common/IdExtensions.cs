using Realm.Ecs.Services;

namespace Realm.Ecs.Common;

/// <summary>
///     Provides extension methods for type-safe ID construction from the DefinitionManager.
/// </summary>
internal static class IdExtensions
{
	/// <summary>
	///     Creates a type-safe TagId wrapper from a string.
	///     Performs a runtime check to ensure the ID is registered with the DefinitionManager.
	/// </summary>
	public static TagId AsTagId(this string id, DefinitionManager manager)
	{
		if (!manager.IsValidTag(id))
			throw new ArgumentException($"Tag ID '{id}' is not registered with the DefinitionManager.");
		return new TagId(id);
	}

	/// <summary>
	///     Creates a type-safe ResourceId wrapper from a string.
	///     Performs a runtime check to ensure the ID is registered with the DefinitionManager.
	/// </summary>
	public static ResourceId AsResourceId(this string id, DefinitionManager manager)
	{
		if (!manager.IsValidResource(id))
			throw new ArgumentException($"Resource ID '{id}' is not registered with the DefinitionManager.");
		return new ResourceId(id);
	}

	/// <summary>
	///     Creates a type-safe StatId wrapper from a string.
	///     Performs a runtime check to ensure the ID is registered with the DefinitionManager.
	/// </summary>
	public static StatId AsStatId(this string id, DefinitionManager manager)
	{
		if (!manager.IsValidStat(id))
			throw new ArgumentException($"Stat ID '{id}' is not registered with the DefinitionManager.");
		return new StatId(id);
	}
}