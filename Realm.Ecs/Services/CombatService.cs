using Arch.Core;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Tags;

namespace Realm.Ecs.Services;

/// <summary>
///     Demonstrates how Combat-related components are used.
///     A real game would have systems that perform these actions every frame.
/// </summary>
internal class CombatService
{
	private readonly WorldAccessor _ecsWorldAccessor;

	public CombatService(WorldAccessor ecsWorldAccessor)
	{
		_ecsWorldAccessor = ecsWorldAccessor;
	}

	/// <summary>
	///     Makes one entity attack another.
	///     This shows the interplay of Attack, Armor, and Health components.
	/// </summary>
	public void PerformAttack(Entity attacker, Entity defender)
	{
		if (!_ecsWorldAccessor.Current.Has<Attack>(attacker) || !_ecsWorldAccessor.Current.Has<Health>(defender)) return;

		var attack = _ecsWorldAccessor.Current.Get<Attack>(attacker);
		var health = _ecsWorldAccessor.Current.Get<Health>(defender);
		var armor = _ecsWorldAccessor.Current.Has<Armor>(defender) ? _ecsWorldAccessor.Current.Get<Armor>(defender) : new Armor(0);

		var damage = attack.Damage - armor.Value;
		if (damage < 1) damage = 1;

		health.Current -= damage;

		if (health.Current <= 0)
		{
			health.Current = 0;
			_ecsWorldAccessor.Current.Add<Dead>(defender);
			Console.WriteLine($"{_ecsWorldAccessor.Current.Get<Name>(defender).Value} has been slain!");
		}

		_ecsWorldAccessor.Current.Set(defender, health);
	}
}