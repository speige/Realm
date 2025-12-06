using System.Reflection;
using Templates.Definitions;

namespace Templates.Ids
{
    using System;
    using System.Collections.Concurrent;
    using System.Threading;
    using Templates;

    public static class TemplateRegistry
    {
        private static readonly ConcurrentDictionary<string, int> _nameToUniqueId = new(StringComparer.InvariantCultureIgnoreCase);
        private static readonly ConcurrentDictionary<int, string> _uniqueIdToName = new();
        private static int _nextId = 1;
        
        private static readonly ConcurrentDictionary<Type, object> _templateDictionaries = new();
        private static readonly ConcurrentDictionary<int, Type> _uniqueIdToType = new();

        public static T Register<T>(string name, Func<T, T>? updateFunc = null) where T : struct, IBaseTemplate<T>
        {
            var templateType = typeof(T);

            int id = _nameToUniqueId.GetOrAdd(name, n =>
            {
                int newId = Interlocked.Increment(ref _nextId) - 1;
                _uniqueIdToName.TryAdd(newId, n);
                _uniqueIdToType.TryAdd(newId, templateType);
                return newId;
            });

            System.Type? registeredTypeForId = GetType(id);
            if (registeredTypeForId != null && registeredTypeForId != templateType)
            {
	            System.Diagnostics.Debug.Assert(false, $"Template name '{name}' (ID: {id}) is already registered for type '{registeredTypeForId.Name}', cannot register as '{templateType.Name}'.");
                return default;
            }
            
            var dictForType = (ConcurrentDictionary<int, T>)_templateDictionaries.GetOrAdd(
                templateType,
                _ => new ConcurrentDictionary<int, T>()
            );

            T resultTemplate = dictForType.GetOrAdd(id, templateId =>
            {
                T createdTemplate = default;
                
                var uniqueIdProperty = templateType.GetProperty("UniqueId", BindingFlags.Public | BindingFlags.Instance);
                if (uniqueIdProperty == null)
                {
                    throw new InvalidOperationException($"Template type {templateType.Name} does not have a public instance 'UniqueId' property.");
                }
                
                var backingField = templateType.GetField($"<{uniqueIdProperty.Name}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
                if (backingField == null)
                {
                    throw new InvalidOperationException($"Could not find backing field for 'UniqueId' property on template type {templateType.Name}. This is required for reflection-based initialization.");
                }

                var uniqueIdValue = (UniqueId<T>)templateId;
                
                //NOTE: necessary hack to temporarily convert value type to reference type so updated value can persist
                object boxedInstance = createdTemplate;
                backingField.SetValue(boxedInstance, uniqueIdValue);
                createdTemplate = (T)boxedInstance;
                
                return createdTemplate;
            });
            
            if (updateFunc != null)
            {
                resultTemplate = updateFunc(resultTemplate);
                UpdateRegistration(resultTemplate);
            }

            return resultTemplate;
        }

        public static void UpdateRegistration<T>(T updatedTemplate) where T : struct, IBaseTemplate<T>
        {
            var templateType = typeof(T);
            var id = updatedTemplate.UniqueId.GlobalUniqueId;

            if (_templateDictionaries.TryGetValue(templateType, out var dictObj))
            {
                var dict = (ConcurrentDictionary<int, T>)dictObj;

                if (dict.ContainsKey(id))
                {
                    dict.AddOrUpdate(id,
                        addValueFactory: (key) =>
                        {
                            throw new InvalidOperationException("TemplateRegistry.UpdateRegistration Unreachable Code executed");
                        },
                        updateValueFactory: (key, oldValue) => updatedTemplate);
                }
                else
                {
                    System.Diagnostics.Debug.Assert(false, $"Cannot update template with ID {id} of type {templateType.Name}: Template not found in registry for update.");
                    return;
				}
            }
            else
            {
	            System.Diagnostics.Debug.Assert(false, $"Cannot update template of type {templateType.Name}: Type not found in registry.");
	            return;
            }
        }

        public static string GetName(int id)
        {
            return _uniqueIdToName.TryGetValue(id, out var name) ? name : string.Empty;
        }

        public static int GetId(string name)
        {
            return _nameToUniqueId.GetValueOrDefault(name, -1);
        }

        public static T Get<T>(UniqueId<T> id) where T : struct, IBaseTemplate<T>
        {
            if (_templateDictionaries.TryGetValue(typeof(T), out var dictObj))
            {
                var dict = (ConcurrentDictionary<int, T>)dictObj;
                if (dict.TryGetValue(id.GlobalUniqueId, out var template))
                {
                    return template;
                }
            }
            return default;
        }

        public static T Get<T>(string name) where T : struct, IBaseTemplate<T>
        {
            int id = GetId(name);
            if (id == -1)
            {
                return default;
            }

            return Get<T>(id);
        }

        public static Type? GetType(string name)
        {
	        int id = GetId(name);
	        if (id == -1)
	        {
		        return null;
	        }

            return GetType(id);
        }

        public static Type? GetType(int id)
        {
            return _uniqueIdToType.GetValueOrDefault(id);
        }

        public static object? Get(int id, Type type)
        {
            if (_templateDictionaries.TryGetValue(type, out dynamic? dictionary))
            {
	            if (dictionary != null)
	            {
		            if (dictionary.TryGetValue(id, out object? result))
		            {
			            return result;
		            }
	            }
            }

            return null;
        }
    }
}