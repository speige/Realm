using System;
using System.Collections.Generic;
using Godot;
using Realm.Ecs.Common;
using Arch.Core;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Resources;
using Realm.Godot.ReplaySystem;


public class InGameHUDViewModel
{
	private Entity GetTargetPlayerEntity()
	{
		if (GameHost.Instance == null || GameHost.Instance.EcsWorld == null)
			return Entity.Null;

		var world = GameHost.Instance.EcsWorld;
		var worldEntity = GameHost.Instance.WorldEntity;

		bool isSpectator = LobbyManager.Instance != null && LobbyManager.Instance.LocalPlayer != null && LobbyManager.Instance.LocalPlayer.Team == "Spectator";
		bool isPlayingReplay = ReplayPlaybackManager.Instance.IsPlayingReplay;

		if (isSpectator || isPlayingReplay)
		{
			int targetPeerId = isPlayingReplay 
				? ReplayPlaybackManager.Instance.SpectatorPerspective 
				: (InGameHUD.Instance != null ? InGameHUD.Instance.LiveSpectatorPerspective : -1);

			if (targetPeerId != -1 && worldEntity != Entity.Null && world.IsAlive(worldEntity) && world.Has<NetworkMappingState>(worldEntity))
			{
				var mapping = world.Get<NetworkMappingState>(worldEntity);
				if (mapping.PeerIdToPlayerEntityMap != null && mapping.PeerIdToPlayerEntityMap.TryGetValue(targetPeerId, out var spectatedPlayer))
				{
					return spectatedPlayer;
				}
			}
		}

		return GameHost.Instance.PlayerEntity;
	}

	public float Gold
	{
		get
		{
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld != null)
			{
				var world = GameHost.Instance.EcsWorld;
				var player = GetTargetPlayerEntity();
				if (player != Entity.Null && world.IsAlive(player) && world.Has<PlayerResources>(player))
				{
					var res = world.Get<PlayerResources>(player);
					var goldId = "gold".AsResourceId(GameHost.Instance.DefinitionManager);
					if (res.Value.TryGetValue(goldId, out var val)) return val;
				}
			}
			return 500f;
		}
		set
		{
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld != null)
			{
				var world = GameHost.Instance.EcsWorld;
				var player = GetTargetPlayerEntity();
				if (player != Entity.Null && world.IsAlive(player) && world.Has<PlayerResources>(player))
				{
					ref var res = ref world.Get<PlayerResources>(player);
					var goldId = "gold".AsResourceId(GameHost.Instance.DefinitionManager);
					res.Value[goldId] = (int)value;
				}
			}
		}
	}

	public float Wood
	{
		get
		{
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld != null)
			{
				var world = GameHost.Instance.EcsWorld;
				var player = GetTargetPlayerEntity();
				if (player != Entity.Null && world.IsAlive(player) && world.Has<PlayerResources>(player))
				{
					var res = world.Get<PlayerResources>(player);
					var woodId = "wood".AsResourceId(GameHost.Instance.DefinitionManager);
					if (res.Value.TryGetValue(woodId, out var val)) return val;
				}
			}
			return 400f;
		}
		set
		{
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld != null)
			{
				var world = GameHost.Instance.EcsWorld;
				var player = GetTargetPlayerEntity();
				if (player != Entity.Null && world.IsAlive(player) && world.Has<PlayerResources>(player))
				{
					ref var res = ref world.Get<PlayerResources>(player);
					var woodId = "wood".AsResourceId(GameHost.Instance.DefinitionManager);
					res.Value[woodId] = (int)value;
				}
			}
		}
	}

	public float Stone
	{
		get
		{
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld != null)
			{
				var world = GameHost.Instance.EcsWorld;
				var player = GetTargetPlayerEntity();
				if (player != Entity.Null && world.IsAlive(player) && world.Has<PlayerResources>(player))
				{
					var res = world.Get<PlayerResources>(player);
					var stoneId = "stone".AsResourceId(GameHost.Instance.DefinitionManager);
					if (res.Value.TryGetValue(stoneId, out var val)) return val;
				}
			}
			return 200f;
		}
		set
		{
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld != null)
			{
				var world = GameHost.Instance.EcsWorld;
				var player = GetTargetPlayerEntity();
				if (player != Entity.Null && world.IsAlive(player) && world.Has<PlayerResources>(player))
				{
					ref var res = ref world.Get<PlayerResources>(player);
					var stoneId = "stone".AsResourceId(GameHost.Instance.DefinitionManager);
					res.Value[stoneId] = (int)value;
				}
			}
		}
	}

	public float ResourceGatherMultiplier
	{
		get
		{
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld != null)
			{
				var world = GameHost.Instance.EcsWorld;
				var player = GetTargetPlayerEntity();
				if (player != Entity.Null && world.IsAlive(player) && world.Has<PlayerUpgrades>(player))
				{
					return world.Get<PlayerUpgrades>(player).HarvestingUpgrade ? 1.5f : 1.0f;
				}
			}
			return 1.0f;
		}
		set
		{
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld != null)
			{
				var world = GameHost.Instance.EcsWorld;
				var player = GetTargetPlayerEntity();
				if (player != Entity.Null && world.IsAlive(player) && world.Has<PlayerUpgrades>(player))
				{
					ref var upgrades = ref world.Get<PlayerUpgrades>(player);
					upgrades.HarvestingUpgrade = value > 1.0f;
				}
			}
		}
	}

	public float GoldPerSec { get; set; } = 1.5f;
	public float WoodPerSec { get; set; } = 1.0f;
	public float StonePerSec { get; set; } = 0.8f;

	public int CurrentPopulation { get; set; }
	public int MaxPopulation { get; set; }
	public string ClockText { get; set; } = "0:00 (Day)";

	public bool IsConnectionLost { get; set; }

	public bool CountdownActive
	{
		get
		{
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld != null && GameHost.Instance.WorldEntity != Entity.Null)
			{
				var world = GameHost.Instance.EcsWorld;
				if (world.IsAlive(GameHost.Instance.WorldEntity) && world.Has<CountdownState>(GameHost.Instance.WorldEntity))
				{
					return world.Get<CountdownState>(GameHost.Instance.WorldEntity).Active;
				}
			}
			return false;
		}
		set
		{
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld != null && GameHost.Instance.WorldEntity != Entity.Null)
			{
				var world = GameHost.Instance.EcsWorld;
				if (world.IsAlive(GameHost.Instance.WorldEntity) && world.Has<CountdownState>(GameHost.Instance.WorldEntity))
				{
					ref var countdown = ref world.Get<CountdownState>(GameHost.Instance.WorldEntity);
					countdown.Active = value;
				}
			}
		}
	}

	public float CountdownDuration
	{
		get
		{
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld != null && GameHost.Instance.WorldEntity != Entity.Null)
			{
				var world = GameHost.Instance.EcsWorld;
				if (world.IsAlive(GameHost.Instance.WorldEntity) && world.Has<CountdownState>(GameHost.Instance.WorldEntity))
				{
					return world.Get<CountdownState>(GameHost.Instance.WorldEntity).Duration;
				}
			}
			return 0f;
		}
		set
		{
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld != null && GameHost.Instance.WorldEntity != Entity.Null)
			{
				var world = GameHost.Instance.EcsWorld;
				if (world.IsAlive(GameHost.Instance.WorldEntity) && world.Has<CountdownState>(GameHost.Instance.WorldEntity))
				{
					ref var countdown = ref world.Get<CountdownState>(GameHost.Instance.WorldEntity);
					countdown.Duration = value;
				}
			}
		}
	}

	public string CountdownText
	{
		get
		{
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld != null && GameHost.Instance.WorldEntity != Entity.Null)
			{
				var world = GameHost.Instance.EcsWorld;
				if (world.IsAlive(GameHost.Instance.WorldEntity) && world.Has<CountdownState>(GameHost.Instance.WorldEntity))
				{
					return world.Get<CountdownState>(GameHost.Instance.WorldEntity).Text;
				}
			}
			return "";
		}
		set
		{
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld != null && GameHost.Instance.WorldEntity != Entity.Null)
			{
				var world = GameHost.Instance.EcsWorld;
				if (world.IsAlive(GameHost.Instance.WorldEntity) && world.Has<CountdownState>(GameHost.Instance.WorldEntity))
				{
					ref var countdown = ref world.Get<CountdownState>(GameHost.Instance.WorldEntity);
					countdown.Text = value;
				}
			}
		}
	}

	public bool LeaderboardVisible
	{
		get
		{
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld != null && GameHost.Instance.WorldEntity != Entity.Null)
			{
				var world = GameHost.Instance.EcsWorld;
				if (world.IsAlive(GameHost.Instance.WorldEntity) && world.Has<LeaderboardState>(GameHost.Instance.WorldEntity))
				{
					return world.Get<LeaderboardState>(GameHost.Instance.WorldEntity).Visible;
				}
			}
			return false;
		}
		set
		{
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld != null && GameHost.Instance.WorldEntity != Entity.Null)
			{
				var world = GameHost.Instance.EcsWorld;
				if (world.IsAlive(GameHost.Instance.WorldEntity) && world.Has<LeaderboardState>(GameHost.Instance.WorldEntity))
				{
					ref var lb = ref world.Get<LeaderboardState>(GameHost.Instance.WorldEntity);
					lb.Visible = value;
				}
			}
		}
	}

	public string LeaderboardTitle
	{
		get
		{
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld != null && GameHost.Instance.WorldEntity != Entity.Null)
			{
				var world = GameHost.Instance.EcsWorld;
				if (world.IsAlive(GameHost.Instance.WorldEntity) && world.Has<LeaderboardState>(GameHost.Instance.WorldEntity))
				{
					return world.Get<LeaderboardState>(GameHost.Instance.WorldEntity).Title;
				}
			}
			return "";
		}
		set
		{
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld != null && GameHost.Instance.WorldEntity != Entity.Null)
			{
				var world = GameHost.Instance.EcsWorld;
				if (world.IsAlive(GameHost.Instance.WorldEntity) && world.Has<LeaderboardState>(GameHost.Instance.WorldEntity))
				{
					ref var lb = ref world.Get<LeaderboardState>(GameHost.Instance.WorldEntity);
					lb.Title = value;
				}
			}
		}
	}

	public Dictionary<string, string> LeaderboardValues
	{
		get
		{
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld != null && GameHost.Instance.WorldEntity != Entity.Null)
			{
				var world = GameHost.Instance.EcsWorld;
				if (world.IsAlive(GameHost.Instance.WorldEntity) && world.Has<LeaderboardState>(GameHost.Instance.WorldEntity))
				{
					return world.Get<LeaderboardState>(GameHost.Instance.WorldEntity).Values;
				}
			}
			return new Dictionary<string, string>();
		}
	}

	public List<SelectedUnitInfo> SelectedUnits { get; } = new();
	public Prop3D SelectedProp { get; set; }
	public int CycleSelectionIndex { get; set; }

	public bool IsChatActive { get; set; }

	public float FireballCooldown { get; set; }
	public float LightningCooldown { get; set; }
	public float HolyLightCooldown { get; set; }

	public string CurrentWeather { get; set; } = "clear";
	public string FogOfWarType { get; set; } = "grey";
	public bool ShowMinimapTerrain { get; set; } = true;
	public byte[,] FogGrid { get; set; } = new byte[32, 32];

	public int IdleCount { get; set; }

	public bool IsBuildSubMenuOpen { get; set; }

	public class SelectedUnitInfo
	{
		public Entity Entity { get; set; }
		public string UnitId { get; set; }
		public string Name { get; set; }
		public float Health { get; set; }
		public float MaxHealth { get; set; }
		public float Damage { get; set; }
		public float Range { get; set; }
		public float Armor { get; set; }
		public float Speed { get; set; }
		public float Dps { get; set; }
		public bool IsEnemy { get; set; }
		public bool IsBuilding { get; set; }
		public bool IsUnderConstruction { get; set; }
		public string StateText { get; set; }
		public string Description { get; set; }
		
		public bool HasFireball { get; set; }
		public bool HasLightning { get; set; }
		public bool HasHolyLight { get; set; }
		public int Potions { get; set; }

		public bool HasProduction { get; set; }
		public string ProductionTitle { get; set; }
		public float ProductionProgress { get; set; }
		public float ProductionMaxProgress { get; set; }
		public List<string> ProductionQueue { get; set; } = new();
	}

	public void Update(double delta)
	{
		GoldPerSec = 1.5f * ResourceGatherMultiplier;
		WoodPerSec = 1.0f * ResourceGatherMultiplier;
		StonePerSec = 0.8f * ResourceGatherMultiplier;

		if (GameHost.Instance != null)
		{
			if (GameHost.Instance.EcsWorld != null)
			{
				var world = GameHost.Instance.EcsWorld;
				var player = GetTargetPlayerEntity();
				if (player != Entity.Null && world.IsAlive(player) && world.Has<PlayerPopulation>(player))
				{
					var pop = world.Get<PlayerPopulation>(player);
					CurrentPopulation = pop.Current;
					MaxPopulation = pop.Max;
				}
				else
				{
					CurrentPopulation = GameHost.Instance.CurrentPopulation;
					MaxPopulation = GameHost.Instance.MaxPopulation;
				}
			}
			else
			{
				CurrentPopulation = GameHost.Instance.CurrentPopulation;
				MaxPopulation = GameHost.Instance.MaxPopulation;
			}

			float t = GameHost.Instance.GameElapsedTime;
			int mins = (int)(t / 60);
			int secs = (int)(t % 60);
			string phase = GameHost.Instance.TimeOfDayIndex switch
			{
				0 => TranslationServer.Translate("Day"),
				1 => TranslationServer.Translate("Sunset"),
				2 => TranslationServer.Translate("Night"),
				3 => TranslationServer.Translate("Dawn"),
				_ => TranslationServer.Translate("Day")
			};
			ClockText = $"{mins}:{secs:D2} ({phase})";

			IsConnectionLost = GameHost.Instance.IsConnectionLost;
			CycleSelectionIndex = GameHost.Instance.CycleSelectionIndex;
			FireballCooldown = GameHost.Instance.FireballCooldown;
			LightningCooldown = GameHost.Instance.LightningCooldown;
			HolyLightCooldown = GameHost.Instance.HolyLightCooldown;
		}

		int idleCount = 0;
		if (GameHost.Instance != null)
		{
			foreach (var unit in GameHost.Instance.AllUnits)
			{
				if (unit.IsEnemy || unit.IsBuilding) continue;
				var world = GameHost.Instance.EcsWorld;
				if (world.IsAlive(unit.Entity))
				{
					bool isMovable = world.Has<Realm.Ecs.Components.Tags.Movable>(unit.Entity);
					bool hasMoveTo = world.Has<MoveTo>(unit.Entity);
					bool hasAttackTarget = world.Has<AttackTarget>(unit.Entity);
					bool hasAttackMove = world.Has<Realm.Ecs.Components.Movement.AttackMove>(unit.Entity);
					bool hasPatrol = world.Has<Patrol>(unit.Entity);
					bool hasFollow = world.Has<Realm.Ecs.Components.Movement.Follow>(unit.Entity);
					bool hasHealTarget = world.Has<HealingTarget>(unit.Entity);
					if (isMovable && !hasMoveTo && !hasAttackTarget && !hasAttackMove && !hasPatrol && !hasFollow && !hasHealTarget)
					{
						idleCount++;
					}
				}
			}
		}
		IdleCount = idleCount;
	}

	public void UpdateSelectedUnits(List<Unit3D> selectedUnits)
	{
		SelectedUnits.Clear();
		if (selectedUnits == null) return;

		foreach (var u in selectedUnits)
		{
			var info = new SelectedUnitInfo
			{
				Entity = u.Entity,
				UnitId = u.UnitId,
				IsEnemy = u.IsEnemy,
				IsBuilding = u.IsBuilding,
				Name = u.UnitId
			};

			if (GameHost.Instance != null && GameHost.Instance.EcsWorld.IsAlive(u.Entity))
			{
				var world = GameHost.Instance.EcsWorld;
				if (world.Has<Name>(u.Entity)) info.Name = world.Get<Name>(u.Entity).Value;
				if (world.Has<Realm.Ecs.Components.Tags.UnderConstruction>(u.Entity)) info.IsUnderConstruction = true;
				
				if (world.Has<Health>(u.Entity))
				{
					var hp = world.Get<Health>(u.Entity);
					info.MaxHealth = hp.Max;
					info.Health = hp.Current;
				}

				if (world.Has<Attack>(u.Entity))
				{
					var atk = world.Get<Attack>(u.Entity);
					info.Damage = atk.Damage;
					info.Range = atk.Range;
					if (atk.Cooldown > 0)
					{
						info.Dps = atk.Damage / atk.Cooldown;
					}
				}

				if (world.Has<Armor>(u.Entity)) info.Armor = world.Get<Armor>(u.Entity).Value;
				if (world.Has<MovementStats>(u.Entity)) info.Speed = world.Get<MovementStats>(u.Entity).Speed;

				if (world.Has<Gatherer>(u.Entity))
				{
					var gather = world.Get<Gatherer>(u.Entity);
					string stateLabel = gather.ReturningToBase ? "● " + TranslationServer.Translate("DELIVERING") : "● " + TranslationServer.Translate("HARVESTING");
					info.StateText = $"{stateLabel} ({gather.CarriedAmount:F0} / {gather.MaxCapacity:F0} {TranslationServer.Translate(gather.ResourceType.ToUpper())})";
				}
				else if (world.Has<Realm.Ecs.Components.Resources.BuildTask>(u.Entity))
				{
					var bt = world.Get<Realm.Ecs.Components.Resources.BuildTask>(u.Entity);
					int pct = (int)(bt.Progress / Mathf.Max(bt.TotalBuildTime, 0.001f) * 100f);
					info.StateText = $"🔨 {TranslationServer.Translate("CONSTRUCTING")} ({pct}%)";
				}
				else if (world.Has<Realm.Ecs.Components.Movement.HoldPosition>(u.Entity))   info.StateText = "● " + TranslationServer.Translate("HOLDING");
				else if (world.Has<Realm.Ecs.Components.Movement.Patrol>(u.Entity))     info.StateText = "● " + TranslationServer.Translate("PATROLLING");
				else if (world.Has<Realm.Ecs.Components.Movement.AttackMove>(u.Entity)) info.StateText = "● " + TranslationServer.Translate("ATTACK-MOVE");
				else if (world.Has<Realm.Ecs.Components.Movement.Follow>(u.Entity))     info.StateText = "● " + TranslationServer.Translate("FOLLOWING");
				else if (world.Has<Realm.Ecs.Components.Movement.MoveTo>(u.Entity))     info.StateText = "● " + TranslationServer.Translate("MOVING");
				else if (world.Has<AttackTarget>(u.Entity))                              info.StateText = "● " + TranslationServer.Translate("ATTACKING");
				else                                                                         info.StateText = "○ " + TranslationServer.Translate("IDLE");

				if (u.UnitId == "tower" && world.Has<TowerUpgradeLevel>(u.Entity))
				{
					int lvl = world.Get<TowerUpgradeLevel>(u.Entity).Value;
					info.StateText += $"   ★ {TranslationServer.Translate("LVL")} {lvl}";
				}

				if (world.Has<Inventory>(u.Entity))
				{
					info.Potions = world.Get<Inventory>(u.Entity).Potions;
				}

				if (u.IsBuilding && !u.IsEnemy && world.Has<Realm.Ecs.Components.Resources.ConstructionState>(u.Entity))
				{
					var cs = world.Get<Realm.Ecs.Components.Resources.ConstructionState>(u.Entity);
					info.HasProduction = true;
					info.ProductionTitle = TranslationServer.Translate("UNDER CONSTRUCTION");
					info.ProductionProgress = cs.Progress;
					info.ProductionMaxProgress = cs.TotalBuildTime;
				}
				else if (u.IsBuilding && u.UnitId == "castle" && world.Has<Realm.Ecs.Components.Core.ProductionQueue>(u.Entity))
				{
					var prod = world.Get<Realm.Ecs.Components.Core.ProductionQueue>(u.Entity);
					info.HasProduction = true;
					if (prod.UnitIds.Count > 0)
					{
						info.ProductionTitle = string.Format(TranslationServer.Translate("TRAINING: {0}"), prod.UnitIds[0].ToUpper());
						info.ProductionProgress = prod.CurrentProgress;
						info.ProductionMaxProgress = prod.BuildTime;
						info.ProductionQueue.AddRange(prod.UnitIds);
					}
					else
					{
						info.ProductionTitle = TranslationServer.Translate("PRODUCTION IDLE");
					}
				}
			}

			if (GameHost.UnitRegistry.TryGetValue(u.UnitId, out var regMeta))
			{
				info.Description = regMeta.Description;
				if (regMeta.Abilities != null)
				{
					info.HasFireball = Array.Exists(regMeta.Abilities, a => a == "fireball");
					info.HasLightning = Array.Exists(regMeta.Abilities, a => a == "lightning");
					info.HasHolyLight = Array.Exists(regMeta.Abilities, a => a == "holylight");
				}
				else
				{
					if (u.UnitId == "priest") info.HasHolyLight = true;
					else if (u.UnitId == "tower") { info.HasFireball = true; info.HasLightning = true; }
				}
			}
			else
			{
				if (u.UnitId == "priest") info.HasHolyLight = true;
				else if (u.UnitId == "tower") { info.HasFireball = true; info.HasLightning = true; }
			}

			SelectedUnits.Add(info);
		}
	}
}
