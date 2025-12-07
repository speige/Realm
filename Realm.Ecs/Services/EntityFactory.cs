using Arch.Core;
using Realm.Ecs.Archetypes;
using Realm.Ecs.Common; // For IdExtensions and specialized definitions
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;

namespace Realm.Ecs.Services;

/// <summary>
/// A generic factory for creating entities from archetypes using reflection.
/// This has been optimized to cache reflection results for performance.
/// </summary>
public class EntityFactory
{
    private readonly World _world;
    private readonly ArchetypeManager _archetypeManager;
    private readonly DefinitionManager _definitionManager; // Consolidated manager
    private readonly Dictionary<Type, MethodInfo> _setComponentCache = new();

    private static readonly MethodInfo SetComponentMethodInfo = typeof(World).GetMethod(nameof(World.Set));

    public EntityFactory(World world, ArchetypeManager archetypeManager, DefinitionManager definitionManager)
    {
        _world = world;
        _archetypeManager = archetypeManager;
        _definitionManager = definitionManager; // Initialize new dependency
        CacheComponentSetters();
    }

    private void CacheComponentSetters()
    {
        var componentProperties = typeof(UnitArchetype).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name is not "Id" and not "Name" and not "Description" and not "Capabilities" and not "Abilities" && p.PropertyType.IsValueType);

        foreach (var prop in componentProperties)
        {
            _setComponentCache[prop.PropertyType] = SetComponentMethodInfo.MakeGenericMethod(prop.PropertyType);
        }
    }

    public Entity SpawnUnit(string archetypeId, Vector3 position)
    {
        var archetype = _archetypeManager.GetUnitArchetype(archetypeId);
        if (archetype == null) throw new ArgumentException($"Archetype with ID '{archetypeId}' not found.");

        var entity = _world.Create();

        var properties = archetype.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.Name is not "Id" and not "Name" and not "Description" and not "Capabilities" and not "Abilities");

        foreach (var prop in properties)
        {
            var componentValue = prop.GetValue(archetype);
            if (componentValue != null)
            {
                if (_setComponentCache.TryGetValue(prop.PropertyType, out var setter))
                {
                    setter.Invoke(_world, new object[] { entity, componentValue });
                }
            }
        }
        
        _world.Set(entity, new DefinitionId(archetypeId));
        _world.Set(entity, new Name(archetype.Name));
        _world.Set(entity, new Position(position));
        
        // Dynamically add capabilities as components (empty structs)
        var addMethodInfo = typeof(World).GetMethod(nameof(World.Add)); // Use Add for empty tags
        foreach (var capabilityId in archetype.Capabilities)
        {
            // Validate capabilityId against the DefinitionManager
            var tagInfo = _definitionManager.GetTag(capabilityId);
            if (tagInfo.HasValue)
            {
                var (definition, tagType) = tagInfo.Value;
                
                // Ensure the discovered type is a struct (tag component)
                if (tagType != null && tagType.IsValueType)
                {
                     var genericAdd = addMethodInfo.MakeGenericMethod(tagType);
                     genericAdd.Invoke(_world, new object[] { entity });
                }
                else
                {
                    System.Console.WriteLine($"Warning: Capability ID '{capabilityId}' has a definition but does not map to a valid tag struct.");
                }
            }
            else
            {
                System.Console.WriteLine($"Warning: Capability ID '{capabilityId}' is not defined in TagManager and cannot be added.");
            }
        }

        return entity;
    }
}