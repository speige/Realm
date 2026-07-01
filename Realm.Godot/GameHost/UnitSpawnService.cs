using Arch.Core;
using Godot;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using System;

internal class UnitSpawnService
{
	private readonly World _ecsWorld;

	public UnitSpawnService(World ecsWorld)
	{
		_ecsWorld = ecsWorld;
	}

	public string GetFallbackModelPath(string unitId, bool isBuilding)
	{
		if (isBuilding)
		{
			return unitId switch
			{
				"castle" => "res://Assets/3d/Buildings/altar.glb",
				"tower" => "res://Assets/3d/Buildings/altar_pillar.glb",
				_ => "res://Assets/3d/Buildings/altar.glb"
			};
		}
		else
		{
			return unitId switch
			{
				"worker" => "res://Assets/3d/Characters/adventurer.glb",
				"soldier" => "res://Assets/3d/Characters/armored_warlord.glb",
				"archer" => "res://Assets/3d/Characters/armored_dragon.glb",
				"priest" => "res://Assets/3d/Characters/armored_battlelord.glb",
				_ => "res://Assets/3d/Characters/adventurer.glb"
			};
		}
	}

	public int GetUnitPathingFlags(GameHost.UnitMetadata meta)
	{
		if (meta.PathingCapabilities == null || meta.PathingCapabilities.Length == 0)
		{
			if (meta.MovementType == "air" || meta.MovementType == "flying")
			{
				return 4;
			}
			else if (meta.MovementType == "amphibious")
			{
				return 8 | 1;
			}
			return 8;
		}

		int flags = 0;
		foreach (var cap in meta.PathingCapabilities)
		{
			switch (cap.ToLower())
			{
				case "shallow_water":
					flags |= 1;
					break;
				case "deep_water":
					flags |= 2;
					break;
				case "flying":
				case "air":
					flags |= 4;
					break;
				case "ground":
					flags |= 8;
					break;
				case "unpathable":
					flags |= 16;
					break;
			}
		}
		return flags;
	}

	public string GetEnemyUnitName(string unitTypeId, string defaultName)
	{
		return unitTypeId switch
		{
			"worker" => "Orc Worker",
			"soldier" => "Orc Raider",
			"archer" => "Dark Archer",
			"priest" => "Orc Shaman",
			"castle" => "Orc Stronghold",
			"tower" => "Orc Totem Tower",
			_ => defaultName
		};
	}

	public Entity CreateEcsUnitEntity(string id, string name, float hp, float damage, float range, float armor, float speed, Vector3 pos, Realm.Ecs.Common.PlayerEntity owner, Entity playerEntity, bool hasShieldsUpgrade, bool hasWeaponsUpgrade)
	{
		float cooldown = 1.5f;
		bool isHero = false;
		if (GameHost.UnitRegistry.TryGetValue(id, out var regMeta))
		{
			cooldown = regMeta.AttackCooldown > 0 ? regMeta.AttackCooldown : 1.5f;
			isHero = regMeta.IsHero;
		}

		var entity = _ecsWorld.Create();
		_ecsWorld.Add(entity, new DefinitionId(id));
		_ecsWorld.Add(entity, new Name(name));
		_ecsWorld.Add(entity, new Position(new System.Numerics.Vector3(pos.X, pos.Y, pos.Z)));
		_ecsWorld.Add(entity, new Owner(owner));

		if (isHero)
		{
			_ecsWorld.Add(entity, new Realm.Ecs.Components.Tags.Hero());
			_ecsWorld.Add(entity, new Realm.Ecs.Components.Meta.Level(1));
			_ecsWorld.Add(entity, new Realm.Ecs.Components.Meta.Experience(0f));
		}

		bool isPlayer = owner.Value == playerEntity;
		if (isPlayer)
		{
			if (hasShieldsUpgrade)
			{
				armor += 2f;
			}
			if (hasWeaponsUpgrade && (damage > 0 || id == "priest") && id != "castle" && id != "tower")
			{
				damage += 3f;
			}
		}

		_ecsWorld.Add(entity, new Health(hp, hp));

		if (damage > 0 || id == "priest")
		{
			_ecsWorld.Add(entity, new Attack(damage, range, cooldown));
		}

		_ecsWorld.Add(entity, new Armor(armor));

		if (speed > 0)
		{
			_ecsWorld.Add(entity, new MovementStats(speed, 20f, 10f));
			_ecsWorld.Add(entity, new Realm.Ecs.Components.Tags.Movable());
			_ecsWorld.Add(entity, new Inventory(1));
		}
		else
		{
			_ecsWorld.Add(entity, new Building());
			if (id == "tower")
			{
				_ecsWorld.Add(entity, new TowerUpgradeLevel(1));
			}
		}

		return entity;
	}
}
