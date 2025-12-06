using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json; 
using NJsonSchema; 
using NJsonSchema.Generation; 
using Templates.Definitions; 
using Templates.Ids; 
using Templates; 

namespace Realm.Simulation.Tools
{
    public static class TemplateSchemaGenerator
    {
        public static void GenerateSchema(string outputPath)
        {
            var rootSchema = new JsonSchema();
            rootSchema.Type = JsonObjectType.Object;
            rootSchema.Description = "Schema for all Realm templates.";

            var templateTypes = Assembly.GetExecutingAssembly().GetTypes()
                .Where(t => t.IsValueType && !t.IsAbstract && !t.IsInterface && t.IsPublic)
                .Where(t => t.Namespace == typeof(AbilityTemplate).Namespace)
                .Where(t => t.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == typeof(IBaseTemplate<>)))
                .ToList();

            rootSchema.Definitions.Add("TemplateNameReference", new JsonSchema { Type = JsonObjectType.String, Description = "Reference to a template by its unique string name." });

            foreach (var templateType in templateTypes)
            {
                var typeName = templateType.Name;
                var typeSchema = JsonSchema.FromType(templateType);
                
                // Customize schema for properties that are UniqueId<T>
                foreach (var prop in templateType.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (typeSchema.Properties.TryGetValue(prop.Name, out var schemaProperty))
                    {
                        if (prop.PropertyType.IsGenericType && prop.PropertyType.GetGenericTypeDefinition() == typeof(UniqueId<>))
                        {
                            schemaProperty.Type = JsonObjectType.String;
                            schemaProperty.Format = null; 
                            schemaProperty.Description = $"Reference to {prop.PropertyType.GetGenericArguments()[0].Name} by its name.";
                            schemaProperty.Reference = null; 
                        }
                        // Handle arrays of UniqueId<T>
                        else if (prop.PropertyType.IsArray)
                        {
                            var elementType = prop.PropertyType.GetElementType();
                            if (elementType != null)
                            {
                                if (elementType.IsGenericType && elementType.GetGenericTypeDefinition() == typeof(UniqueId<>))
                                {
                                    schemaProperty.Type = JsonObjectType.Array;
                                    schemaProperty.Item = new JsonSchema { Type = JsonObjectType.String, Description = $"Reference to {elementType.GetGenericArguments()[0].Name} by its name." };
                                    schemaProperty.Description = $"Array of references to {elementType.GetGenericArguments()[0].Name} by name.";
                                    schemaProperty.Reference = null;
                                }
                            }
                        }
                    }
                }

                rootSchema.Definitions.Add(typeName, typeSchema);

                rootSchema.Properties.Add(typeName + "s", new JsonSchemaProperty
                {
                    Type = JsonObjectType.Array,
                    Item = new JsonSchema { Reference = typeSchema }, 
                    Description = $"Collection of {typeName} templates."
                });
            }
            
            File.WriteAllText(outputPath, rootSchema.ToJson(Formatting.Indented));
        }
    }
}
