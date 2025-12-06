using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Templates.Definitions;
using Templates.Ids;
using Templates;

namespace Realm.Simulation.Tools
{
    public static class TemplateLoader
    {
        public static void LoadTemplatesFromJson(string jsonFilePath)
        {
            var jsonString = File.ReadAllText(jsonFilePath);
            var rootJson = JObject.Parse(jsonString);

            // --- First Pass: Register all template names and get their UniqueIds ---
            // This ensures all IDs are known before any template tries to resolve references.
            var allTemplatesToProcess = new List<(Type templateType, string templateName, JObject templateJson)>();

            foreach (var property in rootJson.Properties())
            {
                string collectionName = property.Name;
                string templateTypeName = collectionName.EndsWith("s", StringComparison.OrdinalIgnoreCase) && collectionName.Length > 1
                                        ? collectionName.Substring(0, collectionName.Length - 1)
                                        : collectionName;

                Type? templateType = Assembly.GetExecutingAssembly().GetTypes()
                    .Where(t => t.IsValueType && !t.IsAbstract && !t.IsInterface && t.IsPublic)
                    .Where(t => t.Namespace == typeof(AbilityTemplate).Namespace)
                    .FirstOrDefault(t => t.Name.Equals(templateTypeName, StringComparison.OrdinalIgnoreCase));

                if (templateType == null)
                {
                    Console.WriteLine($"Warning: Could not find C# type for template collection '{collectionName}' (inferred type '{templateTypeName}'). Skipping.");
                    continue;
                }

                JArray? templatesArray = property.Value as JArray;
                if (templatesArray == null)
                {
                    Console.WriteLine($"Warning: Value for '{collectionName}' is not an array. Skipping.");
                    continue;
                }

                foreach (JObject templateJson in templatesArray.OfType<JObject>())
                {
                    if (!templateJson.TryGetValue("name", StringComparison.OrdinalIgnoreCase, out JToken? nameToken) || nameToken?.Type != JTokenType.String)
                    {
                        Console.WriteLine($"Warning: Template in '{collectionName}' is missing a 'name' property or it's not a string. Skipping template: {templateJson.ToString(Formatting.None)}");
                        continue;
                    }
                    string templateName = nameToken.ToString();
                    
                    MethodInfo? registerMethodDefinition = typeof(TemplateRegistry)
                        .GetMethods(BindingFlags.Public | BindingFlags.Static)
                        .FirstOrDefault(m =>
                        {
                            // Check method name and if it's a generic method definition
                            if (m.Name != "Register" || !m.IsGenericMethodDefinition) return false;

                            // Ensure it has one generic type parameter (the <T> in Register<T>)
                            Type[] genericArgs = m.GetGenericArguments();
                            if (genericArgs.Length != 1) return false;
                            Type methodGenericType = genericArgs[0]; // This is our 'T'

                            // Get method parameters
                            ParameterInfo[] parameters = m.GetParameters();
                            if (parameters.Length != 2) return false;

                            // Check first parameter: string
                            if (parameters[0].ParameterType != typeof(string)) return false;

                            // Check second parameter: Func<T, T> where T is the method's generic type, and it's optional
                            if (!parameters[1].ParameterType.IsGenericType ||
                                parameters[1].ParameterType.GetGenericTypeDefinition() != typeof(Func<,>) ||
                                parameters[1].ParameterType.GetGenericArguments()[0] != methodGenericType ||
                                parameters[1].ParameterType.GetGenericArguments()[1] != methodGenericType ||
                                !parameters[1].IsOptional)
                            {
                                return false;
                            }

                            return true;
                        });

                    if (registerMethodDefinition == null)
                    {
                        throw new InvalidOperationException("TemplateRegistry.Register generic method definition not found with the expected signature.");
                    }
                    
                    MethodInfo genericRegisterMethod = registerMethodDefinition.MakeGenericMethod(templateType);
                    genericRegisterMethod.Invoke(null, new object[] { templateName, null });

                    allTemplatesToProcess.Add((templateType, templateName, templateJson));
                }
            }

            // --- Second Pass: Hydrate all properties, now that all IDs are registered ---
            foreach (var (templateType, templateName, templateJson) in allTemplatesToProcess)
            {
                PropertyInfo? uniqueIdProp = templateType.GetProperty("UniqueId", BindingFlags.Public | BindingFlags.Instance);
                if (uniqueIdProp == null)
                {
                    throw new InvalidOperationException($"Template type {templateType.Name} does not have a public 'UniqueId' property.");
                }

                // Dynamically invoke TemplateRegistry.Get<T>(string name)
                MethodInfo? getByNameMethod = typeof(TemplateRegistry).GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Where(m => m.Name == "Get" && m.IsGenericMethodDefinition)
                    .Where(m => m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string))
                    .FirstOrDefault();

                if (getByNameMethod == null) throw new InvalidOperationException("TemplateRegistry.Get<T>(string name) method definition not found.");

                MethodInfo genericGetByNameMethod = getByNameMethod.MakeGenericMethod(templateType);
                
                object? registeredTemplate = genericGetByNameMethod.Invoke(null, new object[] { templateName });
                if (registeredTemplate == null)
                {
                    throw new InvalidOperationException($"TemplateRegistry.Get<{templateType.Name}>('{templateName}') returned null after first pass registration.");
                }

                object? registeredUniqueIdValue = uniqueIdProp.GetValue(registeredTemplate);

                object populatedInstance = templateJson.ToObject(templateType)!;

                uniqueIdProp.SetValue(populatedInstance, registeredUniqueIdValue);

                MethodInfo? updateMethod = typeof(TemplateRegistry).GetMethod("UpdateRegistration");
                if (updateMethod == null) throw new InvalidOperationException("TemplateRegistry.UpdateRegistration method not found.");
                
                MethodInfo genericUpdateMethod = updateMethod.MakeGenericMethod(templateType);
                genericUpdateMethod.Invoke(null, new object[] { populatedInstance });

                Console.WriteLine($"Registered/Updated {templateType.Name}: '{templateName}'");
            }
        }
    }
}