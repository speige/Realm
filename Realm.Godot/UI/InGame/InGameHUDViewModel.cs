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
	public string ShroudType { get; set; } = "VisionShroud";
	public bool ShowMinimapTerrain { get; set; } = true;
	
	private static readonly byte[,] _emptyShroudGrid = new byte[32, 32];
	public byte[,] ShroudGrid { get; set; } = _emptyShroudGrid;
	public byte[,] FogGrid { get => ShroudGrid; set => ShroudGrid = value; }

	public int IdleCount { get; set; }

	public bool IsBuildSubMenuOpen { get; set; }

	private int _lastClockMins = -1;
	private int _lastClockSecs = -1;
	private int _lastClockPhase = -1;

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
		public List<string> Abilities { get; set; } = new();
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
			int phaseIdx = GameHost.Instance.TimeOfDayIndex;
			
			if (mins != _lastClockMins || secs != _lastClockSecs || phaseIdx != _lastClockPhase)
			{
				string phase = phaseIdx switch
				{
					0 => TranslationServer.Translate("Day"),
					1 => TranslationServer.Translate("Dusk"),
					2 => TranslationServer.Translate("Night"),
					3 => TranslationServer.Translate("Dawn"),
					_ => TranslationServer.Translate("Day")
				};
				ClockText = $"{mins}:{secs:D2} ({phase})";
				_lastClockMins = mins;
				_lastClockSecs = secs;
				_lastClockPhase = phaseIdx;
			}

			IsConnectionLost = GameHost.Instance.IsConnectionLost;
			CycleSelectionIndex = GameHost.Instance.CycleSelectionIndex;
			FireballCooldown = GameHost.Instance.FireballCooldown;
			LightningCooldown = GameHost.Instance.LightningCooldown;
			HolyLightCooldown = GameHost.Instance.HolyLightCooldown;
		}

		int idleCount = 0;
		if (GameHost.Instance != null && GameHost.Instance.EcsWorld != null)
		{
			var world = GameHost.Instance.EcsWorld;
			var targetPlayer = GetTargetPlayerEntity();
			
			if (targetPlayer != Entity.Null && world.IsAlive(targetPlayer))
			{
				world.Query(in Realm.Ecs.Common.QueryCache.AllIdleMovableQuery, (Entity entity, ref Owner owner) => {
					if (owner.PlayerEntity.Value == targetPlayer)
					{
						idleCount++;
					}
				});
			}
		}
		IdleCount = idleCount;
	}

	public void UpdateSelectedUnits(List<Unit3D> selectedUnits)
	{
		if (selectedUnits == null)
		{
			SelectedUnits.Clear();
			return;
		}

		while (SelectedUnits.Count > selectedUnits.Count)
		{
			SelectedUnits.RemoveAt(SelectedUnits.Count - 1);
		}
		while (SelectedUnits.Count < selectedUnits.Count)
		{
			SelectedUnits.Add(new SelectedUnitInfo());
		}

		for (int i = 0; i < selectedUnits.Count; i++)
		{
			var u = selectedUnits[i];
			var info = SelectedUnits[i];

			bool entityChanged = info.Entity != u.Entity;

			info.Entity = u.Entity;
			info.UnitId = u.UnitId;
			info.IsEnemy = u.IsEnemy;
			info.IsBuilding = u.IsBuilding;
			info.Name = u.UnitId;
			info.IsUnderConstruction = false;
			info.HasProduction = false;
			info.ProductionTitle = null;
			info.ProductionProgress = 0f;
			info.ProductionMaxProgress = 0f;

			if (entityChanged)
			{
				info.ProductionQueue.Clear();
				info.Abilities.Clear();
				info.Description = null;
			}

			info.Potions = 0;
			info.StateText = null;
			info.Health = 0f;
			info.MaxHealth = 0f;
			info.Damage = 0f;
			info.Range = 0f;
			info.Armor = 0f;
			info.Speed = 0f;
			info.Dps = 0f;

			if (GameHost.Instance != null && GameHost.Instance.EcsWorld.IsAlive(u.Entity))
			{
				var world = GameHost.Instance.EcsWorld;
				if (world.Has<Name>(u.Entity))
				{
					info.Name = world.Get<Name>(u.Entity).Value;
				}
				if (u.IsBuilding && world.Has<Realm.Ecs.Components.Tags.UnderConstruction>(u.Entity))
				{
					info.IsUnderConstruction = true;
				}
				
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

				if (world.Has<Armor>(u.Entity))
				{
					info.Armor = world.Get<Armor>(u.Entity).Value;
				}
				if (world.Has<MovementStats>(u.Entity))
				{
					info.Speed = world.Get<MovementStats>(u.Entity).Speed;
				}

				string cachedHolding = TranslationServer.Translate("HOLDING");
				string cachedPatrolling = TranslationServer.Translate("PATROLLING");
				string cachedAttackMove = TranslationServer.Translate("ATTACK-MOVE");
				string cachedFollowing = TranslationServer.Translate("FOLLOWING");
				string cachedMoving = TranslationServer.Translate("MOVING");
				string cachedAttacking = TranslationServer.Translate("ATTACKING");
				string cachedIdle = TranslationServer.Translate("IDLE");
				string cachedDelivering = TranslationServer.Translate("DELIVERING");
				string cachedHarvesting = TranslationServer.Translate("HARVESTING");
				string cachedConstructing = TranslationServer.Translate("CONSTRUCTING");
				string cachedUnderConstruction = TranslationServer.Translate("UNDER CONSTRUCTION");
				string cachedLvl = TranslationServer.Translate("LVL");
				string cachedProductionIdle = TranslationServer.Translate("PRODUCTION IDLE");

				if (world.Has<Gatherer>(u.Entity))
				{
					var gather = world.Get<Gatherer>(u.Entity);
					string stateLabel = gather.ReturningToBase ? "● " + cachedDelivering : "● " + cachedHarvesting;
					info.StateText = $"{stateLabel} ({gather.CarriedAmount:F0} / {gather.MaxCapacity:F0} {TranslationServer.Translate(gather.ResourceType.ToUpper())})";
				}
				else if (world.Has<Realm.Ecs.Components.Resources.BuildTask>(u.Entity))
				{
					var bt = world.Get<Realm.Ecs.Components.Resources.BuildTask>(u.Entity);
					int pct = (int)(bt.Progress / Mathf.Max(bt.TotalBuildTime, 0.001f) * 100f);
					info.StateText = $"🔨 {cachedConstructing} ({pct}%)";
				}
				else if (world.Has<Realm.Ecs.Components.Movement.HoldPosition>(u.Entity))
				{
					info.StateText = "● " + cachedHolding;
				}
				else if (world.Has<Realm.Ecs.Components.Movement.Patrol>(u.Entity))
				{
					info.StateText = "● " + cachedPatrolling;
				}
				else if (world.Has<Realm.Ecs.Components.Movement.AttackMove>(u.Entity))
				{
					info.StateText = "● " + cachedAttackMove;
				}
				else if (world.Has<Realm.Ecs.Components.Movement.Follow>(u.Entity))
				{
					info.StateText = "● " + cachedFollowing;
				}
				else if (world.Has<Realm.Ecs.Components.Movement.MoveTo>(u.Entity))
				{
					info.StateText = "● " + cachedMoving;
				}
				else if (world.Has<AttackTarget>(u.Entity))
				{
					info.StateText = "● " + cachedAttacking;
				}
				else
				{
					info.StateText = "○ " + cachedIdle;
				}

				if (u.UnitId == "tower" && world.Has<TowerUpgradeLevel>(u.Entity))
				{
					int lvl = world.Get<TowerUpgradeLevel>(u.Entity).Value;
					info.StateText += $"   ★ {cachedLvl} {lvl}";
				}

				if (world.Has<Inventory>(u.Entity))
				{
					info.Potions = world.Get<Inventory>(u.Entity).Potions;
				}

				if (u.IsBuilding && !u.IsEnemy && world.Has<Realm.Ecs.Components.Resources.ConstructionState>(u.Entity))
				{
					var cs = world.Get<Realm.Ecs.Components.Resources.ConstructionState>(u.Entity);
					info.HasProduction = true;
					info.ProductionTitle = cachedUnderConstruction;
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

						bool queueChanged = info.ProductionQueue.Count != prod.UnitIds.Count;
						if (!queueChanged)
						{
							for (int q = 0; q < prod.UnitIds.Count; q++)
							{
								if (info.ProductionQueue[q] != prod.UnitIds[q])
								{
									queueChanged = true;
									break;
								}
							}
						}
						if (queueChanged)
						{
							info.ProductionQueue.Clear();
							info.ProductionQueue.AddRange(prod.UnitIds);
						}
					}
					else
					{
						info.ProductionTitle = cachedProductionIdle;
						if (info.ProductionQueue.Count > 0)
						{
							info.ProductionQueue.Clear();
						}
					}
				}
			}

			if (!info.HasProduction && info.ProductionQueue.Count > 0)
			{
				info.ProductionQueue.Clear();
			}

			if (entityChanged || info.Abilities.Count == 0)
			{
				info.Abilities.Clear();
				var ecsWorld = GameHost.Instance?.EcsWorld;
				if (ecsWorld != null && ecsWorld.IsAlive(u.Entity) && ecsWorld.Has<Realm.Ecs.Components.Core.AbilityState>(u.Entity))
				{
					var state = ecsWorld.Get<Realm.Ecs.Components.Core.AbilityState>(u.Entity);
					if (state.Abilities != null && state.Abilities.Count > 0)
					{
						info.Abilities.AddRange(state.Abilities);
					}
				}

				if (info.Abilities.Count == 0)
				{
					if (GameHost.UnitRegistry.TryGetValue(u.UnitId, out var regMeta))
					{
						info.Description = regMeta.Description;
						if (regMeta.Abilities != null && regMeta.Abilities.Length > 0)
						{
							info.Abilities.AddRange(regMeta.Abilities);
						}
					}
				}
			}
		}
	}
}
