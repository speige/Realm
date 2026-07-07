using Arch.Core;

namespace Realm.Ecs.Common;

internal static class WorldExtensions
{
    /// <summary>
    ///     Returns the component value for <paramref name="entity"/> when the entity is alive
    ///     and the component exists; otherwise returns <paramref name="fallback"/>.
    /// </summary>
    internal static TComponent GetOrDefault<TComponent>(
        this World world,
        Entity entity,
        TComponent fallback = default)
        where TComponent : struct
    {
        if (world.IsAlive(entity) && world.Has<TComponent>(entity))
            return world.Get<TComponent>(entity);
        return fallback;
    }

    /// <summary>
    ///     Selects a single field from the component for <paramref name="entity"/> when the entity
    ///     is alive and the component exists; otherwise returns <paramref name="fallback"/>.
    /// </summary>
    internal static TField GetFieldOrDefault<TComponent, TField>(
        this World world,
        Entity entity,
        Func<TComponent, TField> selector,
        TField fallback = default)
        where TComponent : struct
    {
        if (world.IsAlive(entity) && world.Has<TComponent>(entity))
            return selector(world.Get<TComponent>(entity));
        return fallback;
    }

    /// <summary>
    ///     Returns <see langword="true"/> and writes the component into <paramref name="value"/>
    ///     when the entity is alive and carries the component; otherwise returns
    ///     <see langword="false"/> and leaves <paramref name="value"/> at <see langword="default"/>.
    /// </summary>
    internal static bool TryGet<TComponent>(
        this World world,
        Entity entity,
        out TComponent value)
        where TComponent : struct
    {
        if (world.IsAlive(entity) && world.Has<TComponent>(entity))
        {
            value = world.Get<TComponent>(entity);
            return true;
        }
        value = default;
        return false;
    }

    /// <summary>
    ///     Mutates a component on <paramref name="entity"/> via <paramref name="mutator"/> when
    ///     the entity is alive and the component exists. The mutation receives a
    ///     <see langword="ref"/> to the live component so no copy is required.
    /// </summary>
    internal static void Mutate<TComponent>(
        this World world,
        Entity entity,
        ComponentMutator<TComponent> mutator)
        where TComponent : struct
    {
        if (world.IsAlive(entity) && world.Has<TComponent>(entity))
            mutator(ref world.Get<TComponent>(entity));
    }

    /// <summary>
    ///     Replaces the component on <paramref name="entity"/> with <paramref name="newValue"/>
    ///     when the entity is alive and the component exists. Has no effect otherwise.
    /// </summary>
    internal static void SetIfAlive<TComponent>(
        this World world,
        Entity entity,
        TComponent newValue)
        where TComponent : struct
    {
        if (world.IsAlive(entity) && world.Has<TComponent>(entity))
            world.Set(entity, newValue);
    }
}

/// <summary>
///     A delegate used by <see cref="WorldExtensions.Mutate{TComponent}"/> to perform
///     in-place mutations on a component without boxing or copying.
/// </summary>
internal delegate void ComponentMutator<TComponent>(ref TComponent component)
    where TComponent : struct;
