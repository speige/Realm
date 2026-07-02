using Arch.Core;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using System;
using System.Collections.Generic;

public class CheatService
{
	private readonly WorldAccessor _ecsWorldAccessor;
	private World _ecsWorld => _ecsWorldAccessor.Current;

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
		NoCap
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

		if (lower == "stonks" || lower == "securethebag")
		{
			if (playerEntity != Entity.Null && _ecsWorld.IsAlive(playerEntity) && _ecsWorld.Has<PlayerResources>(playerEntity))
			{
				ref var playerRes = ref _ecsWorld.Get<PlayerResources>(playerEntity);
				var goldId = "gold".AsResourceId(definitionManager);
				var woodId = "wood".AsResourceId(definitionManager);
				var stoneId = "stone".AsResourceId(definitionManager);

				const float ResourceCap = 9999f;
				if (playerRes.Value.ContainsKey(goldId)) playerRes.Value[goldId] = (int)Math.Min(ResourceCap, playerRes.Value[goldId] + 10000);
				if (playerRes.Value.ContainsKey(woodId)) playerRes.Value[woodId] = (int)Math.Min(ResourceCap, playerRes.Value[woodId] + 10000);
				if (playerRes.Value.ContainsKey(stoneId)) playerRes.Value[stoneId] = (int)Math.Min(ResourceCap, playerRes.Value[stoneId] + 10000);
			}
			return (CheatResult.Stonks, 0);
		}

		if (lower == "gigachad" || lower == "maincharacter")
		{
			int affected = 0;
			foreach (var entity in selectedEntities)
			{
				if (_ecsWorld.IsAlive(entity))
				{
					if (_ecsWorld.Has<Health>(entity))
					{
						_ecsWorld.Set(entity, new Health(9000f, 9000f));
					}
					if (_ecsWorld.Has<Attack>(entity))
					{
						var atk = _ecsWorld.Get<Attack>(entity);
						_ecsWorld.Set(entity, new Attack(9001f, atk.Range, atk.Cooldown, atk.CurrentCooldown));
					}
					affected++;
				}
			}
			return (CheatResult.Gigachad, affected);
		}

		if (lower == "skibidi" || lower == "rizz" || lower == "absoluteunit")
		{
			int affected = 0;
			foreach (var entity in selectedEntities)
			{
				if (_ecsWorld.IsAlive(entity))
				{
					if (_ecsWorld.Has<MovementStats>(entity))
					{
						var mv = _ecsWorld.Get<MovementStats>(entity);
						_ecsWorld.Set(entity, new MovementStats(25f, mv.Acceleration, mv.TurnRate));
					}
					affected++;
				}
			}
			return (CheatResult.AbsoluteUnit, affected);
		}

		if (lower == "thanossnap" || lower == "emotionaldamage")
		{
			int destroyed = 0;
			var query = Realm.Ecs.Common.QueryCache.AllHealthAndOwnerQuery;
			_ecsWorld.Query(in query, (Entity entity, ref Owner owner) =>
			{
				bool isEnemy = false;
				if (_ecsWorld.IsAlive(owner.PlayerEntity.Value) && _ecsWorld.Has<Name>(owner.PlayerEntity.Value))
				{
					isEnemy = _ecsWorld.Get<Name>(owner.PlayerEntity.Value).Value == "Enemy_AI";
				}
				if (isEnemy)
				{
					var hp = _ecsWorld.Get<Health>(entity);
					_ecsWorld.Set(entity, new Health(0f, hp.Max));
					destroyed++;
				}
			});
			return (CheatResult.ThanosSnap, destroyed);
		}

		if (lower == "ezclap" || lower == "speedrun")
		{
			return (CheatResult.EzClap, 0);
		}

		if (lower == "nocap" || lower == "verydemure")
		{
			Entity worldEntity = Entity.Null;
			var query = Realm.Ecs.Common.QueryCache.AllFogAndWeatherStateQuery;
			_ecsWorld.Query(in query, (Entity ent) => worldEntity = ent);

			if (worldEntity != Entity.Null && _ecsWorld.IsAlive(worldEntity) && _ecsWorld.Has<FogAndWeatherState>(worldEntity))
			{
				ref var state = ref _ecsWorld.Get<FogAndWeatherState>(worldEntity);
				state.FogOfWarType = "visible";
			}
			return (CheatResult.NoCap, 0);
		}

		return (CheatResult.None, 0);
	}
}
