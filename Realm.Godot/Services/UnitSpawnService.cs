using Arch.Core;
using Godot;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Services;
using System;

internal class UnitSpawnService
{
	private readonly WorldAccessor _ecsWorldAccessor;
	private World EcsWorld => _ecsWorldAccessor.Current;

	public UnitSpawnService(WorldAccessor ecsWorldAccessor)
	{
		_ecsWorldAccessor = ecsWorldAccessor;
	}

	public string GetFallbackModelPath(string unitId, bool isBuilding)
	{
		string wsPath = Godot.ProjectSettings.GlobalizePath("user://temp_map_workspace");
		string filename = System.IO.Path.GetFileName(unitId);
		if (!filename.EndsWith(".glb") && !filename.EndsWith(".gltf")) filename += ".glb";
		string primarySub = isBuilding ? "building" : "character";
		string cand = System.IO.Path.Combine(wsPath, "Assets", "models", primarySub, filename);
		if (System.IO.File.Exists(cand)) return cand;

		string[] subDirs = new[] { "character", "building", "environment", "props" };
		foreach (var sub in subDirs)
		{
			cand = System.IO.Path.Combine(wsPath, "Assets", "models", sub, filename);
			if (System.IO.File.Exists(cand)) return cand;
		}

		return unitId;
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

	public Entity CreateEcsUnitEntity(string id, string name, float hp, float damage, float range, float armor, float speed, float scanRadius, bool isHero, float attackCooldown, int pathingFlags, Vector3 pos, Realm.Ecs.Common.PlayerEntity owner, Entity playerEntity, bool hasShieldsUpgrade, bool hasWeaponsUpgrade)
	{
		var entity = EcsWorld.Create();
		EcsWorld.Add(entity, new DefinitionId(id));
		EcsWorld.Add(entity, new Name(name));
		EcsWorld.Add(entity, new Position(new System.Numerics.Vector3(pos.X, pos.Y, pos.Z)));
		EcsWorld.Add(entity, new Owner(owner));

		if (isHero)
		{
			EcsWorld.Add(entity, new Realm.Ecs.Components.Tags.Hero());
			EcsWorld.Add(entity, new Realm.Ecs.Components.Meta.Level(1));
			EcsWorld.Add(entity, new Realm.Ecs.Components.Meta.Experience(0f));
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

		EcsWorld.Add(entity, new Health(hp, hp));

		if (damage > 0 || id == "priest")
		{
			EcsWorld.Add(entity, new Attack(damage, range, attackCooldown));
		}

		EcsWorld.Add(entity, new Armor(armor));
		EcsWorld.Add(entity, new CollisionScale(1.0f));
		EcsWorld.Add(entity, new ScanRadius(scanRadius));

		var baseStatsDict = new System.Collections.Generic.Dictionary<Realm.Ecs.Common.StatId, float>
		{
			{ new Realm.Ecs.Common.StatId("Armor"), armor },
			{ new Realm.Ecs.Common.StatId("Attack"), damage },
			{ new Realm.Ecs.Common.StatId("MovementSpeed"), speed }
		};
		EcsWorld.Add(entity, new Realm.Ecs.Components.Stats.Stats(baseStatsDict));

		if (speed > 0)
		{
			EcsWorld.Add(entity, new MovementStats(speed, 20f, 10f));
			EcsWorld.Add(entity, new PathingFlags(pathingFlags));
			EcsWorld.Add(entity, new Realm.Ecs.Components.Tags.Movable());
			EcsWorld.Add(entity, new Inventory(1));
		}
		else
		{
			EcsWorld.Add(entity, new Building());
			if (id.IndexOf("tower", StringComparison.OrdinalIgnoreCase) >= 0)
			{
				EcsWorld.Add(entity, new TowerUpgradeLevel(1));
			}
		}

		return entity;
	}
}
