using Godot;
using Arch.Core;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Tags;
using Realm.MapAPI;
using System;
using System.Collections.Generic;

public partial class GameHost
{
	private readonly Dictionary<int, UnitWrapper> _unitWrapperCache = new();

	public UnitWrapper GetUnitWrapper(Entity entity)
	{
		if (!EcsWorld.IsAlive(entity))
		{
			throw new ArgumentException("Entity is not alive", nameof(entity));
		}
		if (_unitWrapperCache.TryGetValue(entity.Id, out var wrapper))
		{
			return wrapper;
		}
		wrapper = new UnitWrapper(entity, EcsWorld);
		_unitWrapperCache[entity.Id] = wrapper;
		return wrapper;
	}

	private void DealSpellDamageAOE(Vector3 position, float radius, float damage, bool enemyOnly = true)
	{
		var unitsCopy = new List<Unit3D>(AllUnits);
		foreach (var unit in unitsCopy)
		{
			if (enemyOnly && !unit.IsEnemy) continue;
			if (unit.GlobalPosition.DistanceTo(position) <= radius)
			{
				if (EcsWorld.IsAlive(unit.Entity) && EcsWorld.Has<Health>(unit.Entity))
				{
					if (EcsWorld.Has<Realm.Ecs.Components.Tags.Invulnerable>(unit.Entity)) continue;

					IUnit caster = null;
					if (SelectedUnits.Count > 0 && EcsWorld.IsAlive(SelectedUnits[0].Entity))
					{
						caster = GetUnitWrapper(SelectedUnits[0].Entity);
					}
					if (caster != null)
					{
						var casterEntity = ((IEcsEntityWrapper)caster).Entity;
						if (EcsWorld.Has<LastAttacker>(unit.Entity))
						{
							EcsWorld.Set(unit.Entity, new LastAttacker(casterEntity));
						}
						else
						{
							EcsWorld.Add(unit.Entity, new LastAttacker(casterEntity));
						}
					}
					OnUnitDamaged?.Invoke(GetUnitWrapper(unit.Entity), caster ?? GetUnitWrapper(unit.Entity), damage);

					var hp = EcsWorld.Get<Health>(unit.Entity);
					float newHp = Math.Max(0, hp.Current - damage);
					EcsWorld.Set(unit.Entity, new Health(newHp, hp.Max));

					if (newHp <= 0)
					{
						KillUnit(unit);
					}
					else
					{
						FlashDamageUnit(unit);
					}
				}
			}
		}

		InGameHUD.Instance?.RefreshUI(SelectedUnits);
	}

	private void HealAOE(Vector3 position, float radius, float healAmount)
	{
		foreach (var unit in AllUnits)
		{
			if (!unit.IsEnemy && unit.GlobalPosition.DistanceTo(position) <= radius)
			{
				if (EcsWorld.Has<Health>(unit.Entity))
				{
					var hp = EcsWorld.Get<Health>(unit.Entity);
					float newHp = Math.Min(hp.Max, hp.Current + healAmount);
					EcsWorld.Set(unit.Entity, new Health(newHp, hp.Max));

					if (EcsWorld.Has<Unit3D>(unit.Entity))
					{
						FlashHealUnit(unit);
					}
				}
			}
		}
		InGameHUD.Instance?.RefreshUI(SelectedUnits);
	}

	private void KillUnit(Unit3D unit)
	{
		IUnit killer = null;
		if (EcsWorld.IsAlive(unit.Entity))
		{
			if (EcsWorld.Has<LastAttacker>(unit.Entity))
			{
				var killerEntity = EcsWorld.Get<LastAttacker>(unit.Entity).Value;
				if (EcsWorld.IsAlive(killerEntity))
				{
					killer = GetUnitWrapper(killerEntity);
				}
			}
			OnUnitDied?.Invoke(GetUnitWrapper(unit.Entity), killer);

			int id = unit.Entity.Id;
			_unitWrapperCache.Remove(id);
		}

		if (SelectedUnits.Contains(unit))
		{
			SelectedUnits.Remove(unit);
		}
		AllUnits.Remove(unit);
		if (unit.UnitId == "castle")
		{
			_castlesList.Remove(unit);
		}

		if (unit.IsEnemy && UnitRegistry.TryGetValue(unit.UnitId, out var bountyMeta) && bountyMeta.GoldBounty > 0f)
		{
			if (InGameHUD.Instance != null)
			{
				InGameHUD.Instance.Gold = Math.Min(ResourceCap, InGameHUD.Instance.Gold + bountyMeta.GoldBounty);
			}
		}

		if (!unit.IsEnemy && UnitRegistry.TryGetValue(unit.UnitId, out var killMeta))
		{
			if (unit.UnitId == "castle")
			{
				MaxPopulation = Math.Max(0, MaxPopulation - 20);
			}
			if (!EcsWorld.Has<BypassPopulationTag>(unit.Entity))
			{
				CurrentPopulation = Math.Max(0, CurrentPopulation - killMeta.PopCost);
			}
		}

		if (_multiplayerActive)
		{
			if (_clientToServerEntityMap.TryGetValue(unit.Entity.Id, out int serverId))
			{
				_serverToClientEntityMap.Remove(serverId);
			}
			_clientToServerEntityMap.Remove(unit.Entity.Id);
		}

		if (EcsWorld.IsAlive(unit.Entity))
		{
			EcsWorld.Destroy(unit.Entity);
		}

		var tween = CreateTween();
		tween.SetParallel(true);
		tween.TweenProperty(unit, "position:y", -3.0f, 1.0f);
		tween.TweenProperty(unit, "scale", Vector3.Zero, 1.0f);
		tween.Chain().TweenCallback(Callable.From(unit.QueueFree));

		if (unit.UnitId == "castle")
		{
			if (unit.IsEnemy)
			{
				GD.Print("[GameHost] Enemy Castle destroyed! Player wins!");
				Callable.From(() => UIManager.Instance?.TransitionTo(GameScreen.GameOver, true)).CallDeferred();
			}
			else
			{
				GD.Print("[GameHost] Player Castle destroyed! Player loses!");
				Callable.From(() => UIManager.Instance?.TransitionTo(GameScreen.GameOver, false)).CallDeferred();
			}
		}

		GD.Print($"Unit {unit.Name} died.");
	}
}
