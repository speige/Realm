using Arch.Core;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Ecs.Components.Tags;
using System;
using System.Collections.Generic;

public class CheatService
{
	private readonly WorldAccessor _ecsWorldAccessor;
	private World EcsWorld => _ecsWorldAccessor.Current;

	public CheatService(WorldAccessor ecsWorldAccessor)
	{
		_ecsWorldAccessor = ecsWorldAccessor;
	}

	public enum CheatResult
	{
		None,
		Stonks,
		Gigachad,
		AbsoluteUnit,
		ThanosSnap,
		EzClap,
		NoCap,
		WarpSpeed,
		UnlimitedPower
	}

	internal (CheatResult Result, int AffectedCount) TryTriggerCheat(
		string text,
		bool isMultiplayer,
		Entity playerEntity,
		DefinitionManager definitionManager,
		IEnumerable<Entity> selectedEntities)
	{
		if (isMultiplayer)
		{
			return (CheatResult.None, 0);
		}

		string lower = text.ToLowerInvariant().Trim();

		if (lower == "securethebag")
		{
			if (playerEntity != Entity.Null && EcsWorld.IsAlive(playerEntity) && EcsWorld.Has<PlayerResources>(playerEntity))
			{
				ref var playerRes = ref EcsWorld.Get<PlayerResources>(playerEntity);
				var goldId = "gold".AsResourceId(definitionManager);
				var woodId = "wood".AsResourceId(definitionManager);
				var stoneId = "stone".AsResourceId(definitionManager);

				const float resourceCap = 9999f;
				if (playerRes.Value.TryGetValue(goldId, out var currentGold)) playerRes.Value[goldId] = (int)Math.Min(resourceCap, currentGold + 10000);
				if (playerRes.Value.TryGetValue(woodId, out var currentWood)) playerRes.Value[woodId] = (int)Math.Min(resourceCap, currentWood + 10000);
				if (playerRes.Value.TryGetValue(stoneId, out var currentStone)) playerRes.Value[stoneId] = (int)Math.Min(resourceCap, currentStone + 10000);
			}
			return (CheatResult.Stonks, 0);
		}

		if (lower == "gigachad")
		{
			if (GameHost.Instance != null)
			{
				GameHost.Instance.GigachadEnabled = true;
			}
			int affected = 0;
			var query = QueryCache.AllHealthAndOwnerQuery;
			EcsWorld.Query(in query, (Entity entity, ref Owner owner) =>
			{
				bool isFriendly = owner.PlayerEntity.Value == playerEntity;
				if (isFriendly && EcsWorld.IsAlive(entity))
				{
					if (EcsWorld.Has<Health>(entity))
					{
						EcsWorld.Set(entity, new Health(9000f, 9000f));
					}
					if (EcsWorld.Has<Attack>(entity))
					{
						var atk = EcsWorld.Get<Attack>(entity);
						EcsWorld.Set(entity, new Attack(9001f, atk.Range, atk.Cooldown, atk.CurrentCooldown));
					}
					affected++;
				}
			});
			return (CheatResult.Gigachad, affected);
		}

		if (lower == "needforspeed")
		{
			int affected = 0;
			foreach (var entity in selectedEntities)
			{
				if (EcsWorld.IsAlive(entity))
				{
					if (EcsWorld.Has<MovementStats>(entity))
					{
						var mv = EcsWorld.Get<MovementStats>(entity);
						EcsWorld.Set(entity, new MovementStats(25f, mv.Acceleration, mv.TurnRate));
					}
					affected++;
				}
			}
			return (CheatResult.AbsoluteUnit, affected);
		}

		if (lower == "thanossnap")
		{
			var targets = new List<Entity>();
			var query = QueryCache.AllHealthAndOwnerQuery;
			EcsWorld.Query(in query, (Entity entity, ref Owner owner) =>
			{
				bool isEnemy = false;
				if (EcsWorld.IsAlive(owner.PlayerEntity.Value) && EcsWorld.Has<Name>(owner.PlayerEntity.Value))
				{
					isEnemy = EcsWorld.Get<Name>(owner.PlayerEntity.Value).Value == "Enemy_AI";
				}
				if (isEnemy)
				{
					targets.Add(entity);
				}
			});

			int destroyed = 0;
			foreach (var entity in targets)
			{
				if (EcsWorld.IsAlive(entity))
				{
					var hp = EcsWorld.Get<Health>(entity);
					EcsWorld.Set(entity, new Health(0f, hp.Max));
					if (!EcsWorld.Has<Dead>(entity))
					{
						EcsWorld.Add<Dead>(entity);
					}
					if (GameHost.Instance != null && GameHost.TryGetUnit3D(entity, out var unit3D))
					{
						GameHost.Instance.TriggerKillUnit(unit3D);
					}
					destroyed++;
				}
			}
			return (CheatResult.ThanosSnap, destroyed);
		}

		if (lower == "ezclap")
		{
			if (GameHost.Instance != null)
			{
				((Realm.MapAPI.IGameAPI)GameHost.Instance).TriggerVictory();
			}
			return (CheatResult.EzClap, 0);
		}

		if (lower == "aura")
		{
			Entity worldEntity = Entity.Null;
			var query = QueryCache.AllFogAndWeatherStateQuery;
			EcsWorld.Query(in query, ent => worldEntity = ent);

			if (worldEntity != Entity.Null && EcsWorld.IsAlive(worldEntity) && EcsWorld.Has<FogAndWeatherState>(worldEntity))
			{
				ref var state = ref EcsWorld.Get<FogAndWeatherState>(worldEntity);
				state.FogOfWarType = "visible";
			}
			if (GameHost.Instance?.FogOfWarService != null)
			{
				GameHost.Instance.FogOfWarService.TriggerImmediateUpdate();
			}
			return (CheatResult.NoCap, 0);
		}

		if (lower == "speedrun")
		{
			if (GameHost.Instance != null)
			{
				GameHost.Instance.FastBuildEnabled = !GameHost.Instance.FastBuildEnabled;
			}
			return (CheatResult.WarpSpeed, 0);
		}

		if (lower == "skibidi")
		{
			if (GameHost.Instance != null)
			{
				GameHost.Instance.UnlimitedPowerEnabled = !GameHost.Instance.UnlimitedPowerEnabled;
				if (GameHost.Instance.UnlimitedPowerEnabled)
				{
					GameHost.Instance.FireballCooldown = 0f;
					GameHost.Instance.LightningCooldown = 0f;
					GameHost.Instance.HolyLightCooldown = 0f;
					if (playerEntity != Entity.Null && EcsWorld.IsAlive(playerEntity) && EcsWorld.Has<SpellCooldowns>(playerEntity))
					{
						EcsWorld.Set(playerEntity, new SpellCooldowns(0f, 0f, 0f));
					}
				}
			}
			return (CheatResult.UnlimitedPower, 0);
		}

		return (CheatResult.None, 0);
	}
}
