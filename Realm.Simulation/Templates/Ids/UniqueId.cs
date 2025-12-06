using System.Diagnostics;

namespace Templates.Ids
{
    public readonly record struct UniqueId<T>
    {
	    public readonly int GlobalUniqueId { get; init; }

	    public static explicit operator int(UniqueId<T> id) => id.GlobalUniqueId;

	    public static implicit operator UniqueId<T>(string name)
	    {
		    return TemplateRegistry.GetId(name);
	    }

	    public static implicit operator UniqueId<T>(int globalUniqueId)
        {
            Type? registeredType = TemplateRegistry.GetType(globalUniqueId);
            if (registeredType == null || !typeof(T).IsAssignableFrom(registeredType))
            {
                System.Diagnostics.Debug.Assert(false, $"Implicit conversion of ID {globalUniqueId} to UniqueId<{typeof(T).Name}> failed type check. Registered type is {registeredType?.Name ?? "null"}.");
                return default;
            }

            return new UniqueId<T> { GlobalUniqueId = globalUniqueId };
        }
    }
}