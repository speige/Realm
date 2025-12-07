using System; // For ArgumentException
using Realm.Ecs.Services; // For DefinitionManager

namespace Realm.Ecs.Common;

/// <summary>
/// A type-safe wrapper for a Tag ID.
/// Its constructor is internal, enforcing creation via trusted methods.
/// </summary>
public readonly record struct TagId
{
    internal TagId(string value) => Value = value;
    public string Value { get; }
}

/// <summary>
/// A type-safe wrapper for a Resource ID.
/// Its constructor is internal, enforcing creation via trusted methods.
/// </summary>
public readonly record struct ResourceId
{
    internal ResourceId(string value) => Value = value;
    public string Value { get; }
}

/// <summary>
/// A type-safe wrapper for a Stat ID.
/// Its constructor is internal, enforcing creation via trusted methods.
/// </summary>
public readonly record struct StatId
{
    internal StatId(string value) => Value = value;
    public string Value { get; }
}

/// <summary>
/// Provides extension methods for type-safe ID construction from the DefinitionManager.
/// </summary>
public static class IdExtensions
{
    /// <summary>
    /// Creates a type-safe TagId wrapper from a string.
    /// Performs a runtime check to ensure the ID is registered with the DefinitionManager.
    /// </summary>
    public static TagId AsTagId(this string id, DefinitionManager manager)
    {
        if (!manager.IsValidTag(id))
        {
            throw new ArgumentException($"Tag ID '{id}' is not registered with the DefinitionManager.");
        }
        return new TagId(id);
    }

    /// <summary>
    /// Creates a type-safe ResourceId wrapper from a string.
    /// Performs a runtime check to ensure the ID is registered with the DefinitionManager.
    /// </summary>
    public static ResourceId AsResourceId(this string id, DefinitionManager manager)
    {
        if (!manager.IsValidResource(id))
        {
            throw new ArgumentException($"Resource ID '{id}' is not registered with the DefinitionManager.");
        }
        return new ResourceId(id);
    }

    /// <summary>
    /// Creates a type-safe StatId wrapper from a string.
    /// Performs a runtime check to ensure the ID is registered with the DefinitionManager.
    /// </summary>
    public static StatId AsStatId(this string id, DefinitionManager manager)
    {
        if (!manager.IsValidStat(id))
        {
            throw new ArgumentException($"Stat ID '{id}' is not registered with the DefinitionManager.");
        }
        return new StatId(id);
    }
}
