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

public class InGameHUDViewModel
{
	public float Gold { get; set; } = 500f;
	public float Wood { get; set; } = 400f;
	public float Stone { get; set; } = 200f;
	public float ResourceGatherMultiplier { get; set; } = 1.0f;
	public float GoldPerSec { get; set; } = 1.5f;
	public float WoodPerSec { get; set; } = 1.0f;
	public float StonePerSec { get; set; } = 0.8f;

	public int CurrentPopulation { get; set; }
	public int MaxPopulation { get; set; }
	public string ClockText { get; set; } = "0:00 (Day)";

	public bool IsConnectionLost { get; set; }

	public bool CountdownActive { get; set; }
	public float CountdownDuration { get; set; }
	public string CountdownText { get; set; } = "";

	public bool LeaderboardVisible { get; set; }
	public string LeaderboardTitle { get; set; } = "";
	public Dictionary<string, string> LeaderboardValues { get; } = new();

	public List<SelectedUnitInfo> SelectedUnits { get; } = new();
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
		if (CountdownActive)
		{
			CountdownDuration -= (float)delta;
			if (CountdownDuration <= 0f)
			{
				CountdownDuration = 0f;
				CountdownActive = false;
			}
		}

		if (GameHost.Instance != null && GameHost.Instance.EcsWorld != null && GameHost.Instance.PlayerEntity != Entity.Null)
		{
			var world = GameHost.Instance.EcsWorld;
			var player = GameHost.Instance.PlayerEntity;
			if (world.IsAlive(player) && world.Has<PlayerResources>(player))
			{
				var resources = world.Get<PlayerResources>(player).Value;
				var defManager = GameHost.Instance.DefinitionManager;
				var goldId = "gold".AsResourceId(defManager);
				var woodId = "wood".AsResourceId(defManager);
				var stoneId = "stone".AsResourceId(defManager);

				if (resources.TryGetValue(goldId, out var goldVal)) Gold = goldVal;
				if (resources.TryGetValue(woodId, out var woodVal)) Wood = woodVal;
				if (resources.TryGetValue(stoneId, out var stoneVal)) Stone = stoneVal;
			}
		}

		GoldPerSec = 1.5f * ResourceGatherMultiplier;
		WoodPerSec = 1.0f * ResourceGatherMultiplier;
		StonePerSec = 0.8f * ResourceGatherMultiplier;

		if (GameHost.Instance != null)
		{
			CurrentPopulation = GameHost.Instance.CurrentPopulation;
			MaxPopulation = GameHost.Instance.MaxPopulation;

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

				if (world.Has<GameHost.Gatherer>(u.Entity))
				{
					var gather = world.Get<GameHost.Gatherer>(u.Entity);
					string stateLabel = gather.ReturningToBase ? "● " + TranslationServer.Translate("DELIVERING") : "● " + TranslationServer.Translate("HARVESTING");
					info.StateText = $"{stateLabel} ({gather.CarriedAmount:F0} / {gather.MaxCapacity:F0} {TranslationServer.Translate(gather.ResourceType.ToUpper())})";
				}
				else if (world.Has<Realm.Ecs.Components.Movement.HoldPosition>(u.Entity))   info.StateText = "● " + TranslationServer.Translate("HOLDING");
				else if (world.Has<Realm.Ecs.Components.Movement.Patrol>(u.Entity))     info.StateText = "● " + TranslationServer.Translate("PATROLLING");
				else if (world.Has<Realm.Ecs.Components.Movement.AttackMove>(u.Entity)) info.StateText = "● " + TranslationServer.Translate("ATTACK-MOVE");
				else if (world.Has<Realm.Ecs.Components.Movement.Follow>(u.Entity))     info.StateText = "● " + TranslationServer.Translate("FOLLOWING");
				else if (world.Has<Realm.Ecs.Components.Movement.MoveTo>(u.Entity))     info.StateText = "● " + TranslationServer.Translate("MOVING");
				else if (world.Has<AttackTarget>(u.Entity))                              info.StateText = "● " + TranslationServer.Translate("ATTACKING");
				else                                                                         info.StateText = "○ " + TranslationServer.Translate("IDLE");

				if (u.UnitId == "tower" && world.Has<GameHost.TowerUpgradeLevel>(u.Entity))
				{
					int lvl = world.Get<GameHost.TowerUpgradeLevel>(u.Entity).Value;
					info.StateText += $"   ★ {TranslationServer.Translate("LVL")} {lvl}";
				}

				if (world.Has<Inventory>(u.Entity))
				{
					info.Potions = world.Get<Inventory>(u.Entity).Potions;
				}

				if (u.IsBuilding && u.UnitId == "castle" && world.Has<Realm.Ecs.Components.Core.ProductionQueue>(u.Entity))
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
