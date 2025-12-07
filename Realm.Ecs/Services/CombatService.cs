using Arch.Core;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Tags;

namespace Realm.Ecs.Services;

/// <summary>
/// Demonstrates how Combat-related components are used.
/// A real game would have systems that perform these actions every frame.
/// </summary>
public class CombatService
{
    private readonly World _world;
    public CombatService(World world) { _world = world; }

    /// <summary>
    /// Makes one entity attack another.
    /// This shows the interplay of Attack, Armor, and Health components.
    /// </summary>
    public void PerformAttack(Entity attacker, Entity defender)
    {
        if (!_world.Has<Attack>(attacker) || !_world.Has<Health>(defender)) return;
        
        var attack = _world.Get<Attack>(attacker);
        var health = _world.Get<Health>(defender);
        var armor = _world.Has<Armor>(defender) ? _world.Get<Armor>(defender) : new Armor(0);

        var damage = attack.Damage - armor.Value;
        if (damage < 1) damage = 1;

        health.Current -= damage;

        if (health.Current <= 0)
        {
            health.Current = 0;
            _world.Add<Dead>(defender);
            System.Console.WriteLine($"{_world.Get<Name>(defender).Value} has been slain!");
        }

        _world.Set(defender, health);
    }
}
