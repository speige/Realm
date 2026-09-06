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

	public string GetFallbackModelPath(string modelPathOrId, bool isBuilding)
	{
		if (string.IsNullOrWhiteSpace(modelPathOrId)) return "";

		if (modelPathOrId.StartsWith("res://") && Godot.FileAccess.FileExists(modelPathOrId))
		{
			return modelPathOrId;
		}

		if (System.IO.Path.IsPathRooted(modelPathOrId) && System.IO.File.Exists(modelPathOrId))
		{
			return modelPathOrId;
		}

		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
		string directCandidate = System.IO.Path.Combine(wsPath, modelPathOrId);
		if (System.IO.File.Exists(directCandidate)) return directCandidate;

		string filename = System.IO.Path.GetFileName(modelPathOrId);
		if (!filename.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) && !filename.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase))
		{
			filename += ".glb";
		}

		string primarySub = isBuilding ? "buildings" : "units";
		string cand = System.IO.Path.Combine(wsPath, "Assets", "models", primarySub, filename);
		if (System.IO.File.Exists(cand)) return cand;

		string[] subDirs = new[] { "units", "buildings", "resources", "props", "projectiles", "attachments", "weapons" };
		foreach (var sub in subDirs)
		{
			cand = System.IO.Path.Combine(wsPath, "Assets", "models", sub, filename);
			if (System.IO.File.Exists(cand)) return cand;
		}

		string modelsCand = System.IO.Path.Combine(wsPath, "Assets", "models", filename);
		if (System.IO.File.Exists(modelsCand)) return modelsCand;

		string rootCand = System.IO.Path.Combine(wsPath, filename);
		if (System.IO.File.Exists(rootCand)) return rootCand;

		foreach (var sub in subDirs)
		{
			string resCand = $"res://Assets/models/{sub}/{filename}";
			if (Godot.FileAccess.FileExists(resCand)) return resCand;
		}

		return modelPathOrId;
	}

	public int GetUnitPathingFlags(GameHost.UnitMetadata meta)
	{
		if (meta.PathingType != 0)
		{
			return meta.PathingType;
		}

		if (meta.PathingCapabilities != null && meta.PathingCapabilities.Length > 0)
		{
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
					case "buildable":
						flags |= 32;
						break;
				}
			}
			if (flags != 0) return flags;
		}

		return 8;
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

	public Entity CreateEcsUnitEntity(string id, string name, float hp, float damage, float range, float armor, float speed, float scanRadius, bool isHero, float attackCooldown, int pathingFlags, Vector3 pos, Realm.Ecs.Common.PlayerEntity owner, Entity playerEntity, bool hasShieldsUpgrade, bool hasWeaponsUpgrade, string[]? targets = null)
	{
		var entity = EcsWorld.Create();
		EcsWorld.Add(entity, new DefinitionId(id));
		EcsWorld.Add(entity, new Name(name));
		EcsWorld.Add(entity, new Position(new System.Numerics.Vector3(pos.X, pos.Y, pos.Z)));
		EcsWorld.Add(entity, new Owner(owner));

		bool canTargetAir = true;
		bool canTargetGround = true;
		if (targets != null && targets.Length > 0)
		{
			bool hasAir = false;
			bool hasGround = false;
			foreach (var targetType in targets)
			{
				string normalized = targetType.Trim().ToLowerInvariant();
				if (normalized == "air") hasAir = true;
				else if (normalized == "ground") hasGround = true;
			}
			canTargetAir = hasAir;
			canTargetGround = hasGround;
		}
		EcsWorld.Add(entity, new CombatTargeting(canTargetAir, canTargetGround));

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
