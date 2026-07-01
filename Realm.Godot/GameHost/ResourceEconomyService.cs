using Arch.Core;
using Godot;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Services;
using System;
using System.Collections.Generic;

internal class ResourceEconomyService
{
	private readonly World _ecsWorld;

	private float _fDelta;

	private readonly List<(Entity Worker, Gatherer NewState, Vector3? NewDestination)> _tickGatherersToUpdate = new();
	private readonly QueryDescription _gatherQuery = new QueryDescription().WithAll<Position, Gatherer>().WithNone<Dead>();
	private readonly QueryDescription _passiveIncomeQuery = new QueryDescription().WithAll<PlayerResources>().WithNone<Dead>();

	private ForEachWithEntity<Position, Gatherer> _gatherQueryDelegate = null!;
	private ForEachWithEntity<PlayerResources> _passiveIncomeQueryDelegate = null!;

	private List<Prop3D> _allPropsRef;
	private List<Unit3D> _castlesListRef;
	private DefinitionManager _definitionManagerRef;
	private ResourceId _goldResourceId;
	private ResourceId _woodResourceId;
	private ResourceId _stoneResourceId;

	public Action<string, float> OnResourceDepositedForPlayer;
	public Action<Entity> OnClearUnitOrdersRequested;
	public Action<Entity> OnStopGatheringMovementRequested;

	public ResourceEconomyService(World ecsWorld)
	{
		_ecsWorld = ecsWorld;
		_gatherQueryDelegate = GatherQueryAction;
		_passiveIncomeQueryDelegate = UpdatePassiveIncomeQueryAction;
	}

	public void SetRuntimeReferences(
		List<Prop3D> allProps,
		List<Unit3D> castlesList,
		DefinitionManager definitionManager,
		ResourceId goldResourceId,
		ResourceId woodResourceId,
		ResourceId stoneResourceId)
	{
		_allPropsRef = allProps;
		_castlesListRef = castlesList;
		_definitionManagerRef = definitionManager;
		_goldResourceId = goldResourceId;
		_woodResourceId = woodResourceId;
		_stoneResourceId = stoneResourceId;
	}

	public void StepEconomy(float delta)
	{
		_fDelta = delta;

		_ecsWorld.Query(in _passiveIncomeQuery, _passiveIncomeQueryDelegate);
		ProcessGatheringTicks();
	}

	private Entity FindWorldEntity()
	{
		Entity worldEntity = Entity.Null;
		var query = new QueryDescription().WithAll<WorldState>();
		_ecsWorld.Query(in query, (Entity entity) => worldEntity = entity);
		return worldEntity;
	}

	private bool GetHarvestingUpgrade()
	{
		var worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null) return false;
		var playerEntity = _ecsWorld.Get<NetworkMappingState>(worldEntity).PlayerEntity;
		return _ecsWorld.GetFieldOrDefault<PlayerUpgrades, bool>(playerEntity, u => u.HarvestingUpgrade);
	}

	private float GetGameElapsedTime()
	{
		var worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null) return 0f;
		return _ecsWorld.GetFieldOrDefault<WorldState, float>(worldEntity, s => s.GameElapsedTime);
	}

	private void UpdatePassiveIncomeQueryAction(Entity ent, ref PlayerResources res)
	{
		float goldPerSec = 1.5f;
		float woodPerSec = 1.0f;
		float stonePerSec = 0.8f;

		var worldEntity = FindWorldEntity();
		if (worldEntity != Entity.Null && _ecsWorld.Has<NetworkMappingState>(worldEntity))
		{
			var playerEntityForUpgrade = _ecsWorld.Get<NetworkMappingState>(worldEntity).PlayerEntity;
			if (ent == playerEntityForUpgrade && GetHarvestingUpgrade())
			{
				goldPerSec *= 1.5f;
				woodPerSec *= 1.5f;
				stonePerSec *= 1.5f;
			}
		}

		if (res.Value.ContainsKey(_goldResourceId)) res.Value[_goldResourceId] = (int)Math.Min(GameHost.ResourceCap, res.Value[_goldResourceId] + _fDelta * goldPerSec);
		if (res.Value.ContainsKey(_woodResourceId)) res.Value[_woodResourceId] = (int)Math.Min(GameHost.ResourceCap, res.Value[_woodResourceId] + _fDelta * woodPerSec);
		if (res.Value.ContainsKey(_stoneResourceId)) res.Value[_stoneResourceId] = (int)Math.Min(GameHost.ResourceCap, res.Value[_stoneResourceId] + _fDelta * stonePerSec);
	}

	private void ProcessGatheringTicks()
	{
		_tickGatherersToUpdate.Clear();
		_ecsWorld.Query(in _gatherQuery, _gatherQueryDelegate);

		foreach (var (worker, newState, dest) in _tickGatherersToUpdate)
		{
			if (_ecsWorld.IsAlive(worker))
			{
				_ecsWorld.Set(worker, newState);
				if (dest.HasValue)
				{
					var moveTo = new MoveTo(new System.Numerics.Vector3(dest.Value.X, dest.Value.Y, dest.Value.Z));
					if (_ecsWorld.Has<MoveTo>(worker)) _ecsWorld.Set(worker, moveTo);
					else _ecsWorld.Add(worker, moveTo);
				}
			}
		}
	}

	private Prop3D FindNearbyResourceNode(Vector3 pos, string type, float radius)
	{
		Prop3D closest = null;
		float closestDist = radius;
		if (_allPropsRef != null)
		{
			foreach (var prop in _allPropsRef)
			{
				if (GodotObject.IsInstanceValid(prop))
				{
					string pType = prop.PropId switch
					{
						"goldmine" => "gold",
						"tree" => "wood",
						"rock" => "stone",
						_ => null
					};

					if (pType == type)
					{
						float d = pos.DistanceTo(prop.GlobalPosition);
						if (d < closestDist)
						{
							closestDist = d;
							closest = prop;
						}
					}
				}
			}
		}
		return closest;
	}

	private void GatherQueryAction(Entity entity, ref Position pos, ref Gatherer gather)
	{
		var currentPos = new Vector3(pos.Value.X, pos.Value.Y, pos.Value.Z);

		if (gather.ReturningToBase)
		{
			Unit3D nearestCastle = null;
			float nearestDist = float.MaxValue;
			if (_castlesListRef != null)
			{
				foreach (var u in _castlesListRef)
				{
					var wOwner = _ecsWorld.Get<Owner>(entity).PlayerEntity;
					var uOwner = _ecsWorld.Get<Owner>(u.Entity).PlayerEntity;
					if (uOwner == wOwner && GodotObject.IsInstanceValid(u))
					{
						float dist = currentPos.DistanceTo(u.GlobalPosition);
						if (dist < nearestDist)
						{
							nearestDist = dist;
							nearestCastle = u;
						}
					}
				}
			}

			if (nearestCastle == null)
			{
				var newState = gather;
				newState.ReturningToBase = false;
				newState.CarriedAmount = 0;
				_tickGatherersToUpdate.Add((entity, newState, null));
				return;
			}

			float castleRadius = 6.0f;
			if (currentPos.DistanceTo(nearestCastle.GlobalPosition) <= castleRadius)
			{
				float carry = gather.CarriedAmount;
				var ownerEntity = _ecsWorld.Get<Owner>(entity).PlayerEntity.Value;
				if (_ecsWorld.Has<PlayerResources>(ownerEntity))
				{
					ref var playerRes = ref _ecsWorld.Get<PlayerResources>(ownerEntity);
					var resId = gather.ResourceType.AsResourceId(_definitionManagerRef);
					if (playerRes.Value.ContainsKey(resId))
					{
						playerRes.Value[resId] = (int)Math.Min(GameHost.ResourceCap, playerRes.Value[resId] + carry);
					}
				}

				var worldEntity = FindWorldEntity();
				if (worldEntity != Entity.Null)
				{
					var playerEntityForAlert = _ecsWorld.Get<NetworkMappingState>(worldEntity).PlayerEntity;
					if (ownerEntity == playerEntityForAlert)
					{
						OnResourceDepositedForPlayer?.Invoke(gather.ResourceType, carry);
					}
				}

				Prop3D targetNode = null;
				if (_ecsWorld.IsAlive(gather.TargetEntity) && _ecsWorld.Has<Prop3D>(gather.TargetEntity))
				{
					targetNode = _ecsWorld.Get<Prop3D>(gather.TargetEntity);
				}

				if (GodotObject.IsInstanceValid(targetNode))
				{
					var newState = gather;
					newState.ReturningToBase = false;
					newState.CarriedAmount = 0f;
					var dest = targetNode.GlobalPosition;
					_tickGatherersToUpdate.Add((entity, newState, dest));
				}
				else
				{
					var newState = gather;
					newState.ReturningToBase = false;
					newState.CarriedAmount = 0f;
					newState.TargetEntity = Entity.Null;
					_tickGatherersToUpdate.Add((entity, newState, null));
				}
			}
			else
			{
				if (!_ecsWorld.Has<MoveTo>(entity))
				{
					var dest = nearestCastle.GlobalPosition;
					_tickGatherersToUpdate.Add((entity, gather, dest));
				}
			}
		}
		else
		{
			Prop3D targetNode = null;
			if (_ecsWorld.IsAlive(gather.TargetEntity) && _ecsWorld.Has<Prop3D>(gather.TargetEntity))
			{
				targetNode = _ecsWorld.Get<Prop3D>(gather.TargetEntity);
			}

			if (!GodotObject.IsInstanceValid(targetNode))
			{
				Prop3D alternate = FindNearbyResourceNode(currentPos, gather.ResourceType, 25.0f);
				if (alternate != null)
				{
					var newState = gather;
					newState.TargetEntity = alternate.Entity;
					var dest = alternate.GlobalPosition;
					_tickGatherersToUpdate.Add((entity, newState, dest));
				}
				else
				{
					OnClearUnitOrdersRequested?.Invoke(entity);
				}
				return;
			}

			float dist = currentPos.DistanceTo(targetNode.GlobalPosition);
			float gatherRange = 3.5f;
			if (dist <= gatherRange)
			{
				if (_ecsWorld.Has<MoveTo>(entity))
				{
					OnStopGatheringMovementRequested?.Invoke(entity);
				}

				var newState = gather;
				float mineRate = 4.0f * _fDelta;

				var worldEntity = FindWorldEntity();
				if (worldEntity != Entity.Null)
				{
					var enemyPlayerEntity = _ecsWorld.Get<NetworkMappingState>(worldEntity).EnemyPlayerEntity;
					bool isEnemy = _ecsWorld.Get<Owner>(entity).PlayerEntity == enemyPlayerEntity.AsPlayerEntity(_ecsWorld);
					if (!isEnemy && GetHarvestingUpgrade()) mineRate *= 1.5f;
				}

				float nodeRemaining = targetNode.ResourceAmount;
				if (mineRate > nodeRemaining)
				{
					mineRate = nodeRemaining;
				}

				targetNode.ResourceAmount -= mineRate;
				newState.CarriedAmount = Math.Min(gather.MaxCapacity, gather.CarriedAmount + mineRate);

				if (_ecsWorld.Has<Unit3D>(entity))
				{
					var worker3D = _ecsWorld.Get<Unit3D>(entity);
					float gameElapsed = GetGameElapsedTime();
					float pulse = 1.0f + Mathf.Sin(gameElapsed * 10f) * 0.1f;
					worker3D.Scale = new Vector3(pulse * 0.9f, (2.0f - pulse) * 0.9f, pulse * 0.9f);
				}

				if (targetNode.ResourceAmount <= 0f)
				{
					var depletedNode = targetNode;
					if (_allPropsRef != null)
					{
						_allPropsRef.Remove(depletedNode);
					}
					if (_ecsWorld.IsAlive(depletedNode.Entity))
					{
						_ecsWorld.Destroy(depletedNode.Entity);
					}
					depletedNode.QueueFree();
				}

				if (newState.CarriedAmount >= gather.MaxCapacity)
				{
					newState.ReturningToBase = true;
					Unit3D nearestCastle = null;
					float nearestDist = float.MaxValue;
					if (_castlesListRef != null)
					{
						foreach (var u in _castlesListRef)
						{
							var wOwner = _ecsWorld.Get<Owner>(entity).PlayerEntity;
							var uOwner = _ecsWorld.Get<Owner>(u.Entity).PlayerEntity;
							if (uOwner == wOwner && GodotObject.IsInstanceValid(u))
							{
								float d = currentPos.DistanceTo(u.GlobalPosition);
								if (d < nearestDist)
								{
									nearestDist = d;
									nearestCastle = u;
								}
							}
						}
					}

					if (nearestCastle != null)
					{
						var dest = nearestCastle.GlobalPosition;
						_tickGatherersToUpdate.Add((entity, newState, dest));
					}
					else
					{
						_tickGatherersToUpdate.Add((entity, newState, null));
					}
				}
				else
				{
					_tickGatherersToUpdate.Add((entity, newState, null));
				}
			}
			else
			{
				if (!_ecsWorld.Has<MoveTo>(entity))
				{
					var dest = targetNode.GlobalPosition;
					_tickGatherersToUpdate.Add((entity, gather, dest));
				}
			}
		}
	}
}
