using Arch.Core;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Tags;
using System;

internal class TechTreeService
{
	private readonly World _ecsWorld;

	public TechTreeService(World ecsWorld)
	{
		_ecsWorld = ecsWorld;
	}

	public bool BuyWeaponsUpgrade(Entity playerEntity)
	{
		if (!_ecsWorld.IsAlive(playerEntity)) return false;
		ref var upgrades = ref _ecsWorld.Get<PlayerUpgrades>(playerEntity);
		if (upgrades.WeaponsUpgrade) return false;

		upgrades.WeaponsUpgrade = true;

		var query = new QueryDescription().WithAll<Attack, Owner>().WithNone<Dead, Building>();
		_ecsWorld.Query(in query, (Entity entity, ref Attack atk, ref Owner owner) =>
		{
			if (owner.PlayerEntity.Value == playerEntity)
			{
				atk.Damage += 3f;
			}
		});
		return true;
	}

	public bool BuyShieldsUpgrade(Entity playerEntity)
	{
		if (!_ecsWorld.IsAlive(playerEntity)) return false;
		ref var upgrades = ref _ecsWorld.Get<PlayerUpgrades>(playerEntity);
		if (upgrades.ShieldsUpgrade) return false;

		upgrades.ShieldsUpgrade = true;

		var query = new QueryDescription().WithAll<Armor, Owner>().WithNone<Dead>();
		_ecsWorld.Query(in query, (Entity entity, ref Armor arm, ref Owner owner) =>
		{
			if (owner.PlayerEntity.Value == playerEntity)
			{
				arm.Value += 2f;
			}
		});
		return true;
	}

	public bool BuyHarvestingUpgrade(Entity playerEntity)
	{
		if (!_ecsWorld.IsAlive(playerEntity)) return false;
		ref var upgrades = ref _ecsWorld.Get<PlayerUpgrades>(playerEntity);
		if (upgrades.HarvestingUpgrade) return false;

		upgrades.HarvestingUpgrade = true;
		return true;
	}
}
