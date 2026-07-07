using System.Collections.Generic;
using Realm.Ecs.Services;
using Arch.Core;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Godot.ReplaySystem;

public class ReplayService
{
	private readonly WorldAccessor _ecsWorldAccessor;
	private World EcsWorld => _ecsWorldAccessor.Current;
	private ReplayRecorder _replayRecorder;
	private readonly Dictionary<int, ReplayUnitSnapshot> _lastRecordedUnits = new();

	public ReplayService(WorldAccessor ecsWorldAccessor)
	{
		_ecsWorldAccessor = ecsWorldAccessor;
	}

	public bool IsRecording => _replayRecorder != null;

	public void StartRecording(string path, string mapName, List<LobbyManager.PlayerInfo> players)
	{
		_lastRecordedUnits.Clear();
		_replayRecorder = new ReplayRecorder(path, mapName, players);
		_replayRecorder.Start();
	}

	public void StopRecording()
	{
		if (_replayRecorder != null)
		{
			_replayRecorder.Stop();
			_replayRecorder = null;
		}
	}

	public void SetupPlayersForPlayback(List<(int PeerId, string Name)> players)
	{
		Entity worldEntity = Entity.Null;
		var worldQuery = QueryCache.AllNetworkMappingStateQuery;
		EcsWorld.Query(in worldQuery, (Entity entity) => worldEntity = entity);

		if (worldEntity == Entity.Null)
		{
			return;
		}

		ref var mapping = ref EcsWorld.Get<NetworkMappingState>(worldEntity);
		mapping.ServerToClientEntityMap.Clear();
		mapping.ClientToServerEntityMap.Clear();
		mapping.PeerIdToPlayerEntityMap.Clear();

		foreach (var p in players)
		{
			var playerEntity = EcsWorld.Create();
			EcsWorld.Add(playerEntity, new Player());
			EcsWorld.Add(playerEntity, new Name(p.Name));
			
			EcsWorld.Add(playerEntity, new PlayerPopulation(0, 0));
			EcsWorld.Add(playerEntity, new SpellCooldowns(0f, 0f, 0f));
			EcsWorld.Add(playerEntity, new PlayerUpgrades(false, false, false));

			var resourcesDict = new Dictionary<ResourceId, int>
			{
				{ new ResourceId("gold"), 0 },
				{ new ResourceId("wood"), 0 },
				{ new ResourceId("stone"), 0 }
			};
			EcsWorld.Add(playerEntity, new PlayerResources(resourcesDict));

			mapping.PeerIdToPlayerEntityMap[p.PeerId] = playerEntity;
			if (p.PeerId == 1)
			{
				mapping.PlayerEntity = playerEntity;
			}
			else if (p.PeerId == -1)
			{
				mapping.EnemyPlayerEntity = playerEntity;
			}
		}
	}

	public (Entity Entity, string ModelPath, bool IsEnemy) SpawnUnitFromReplaySnapshot(ReplayUnitSnapshot snap)
	{
		if (!GameHost.UnitRegistry.TryGetValue(snap.UnitId, out var meta))
		{
			return (Entity.Null, "", false);
		}

		Entity worldEntity = Entity.Null;
		var worldQuery = QueryCache.AllNetworkMappingStateQuery;
		EcsWorld.Query(in worldQuery, (Entity entity) => worldEntity = entity);

		if (worldEntity == Entity.Null)
		{
			return (Entity.Null, "", false);
		}

		ref var mapping = ref EcsWorld.Get<NetworkMappingState>(worldEntity);

		Entity ownerPlayerEntity = mapping.PlayerEntity;
		if (mapping.PeerIdToPlayerEntityMap.TryGetValue(snap.OwnerPlayerEntityId, out var pe))
		{
			ownerPlayerEntity = pe;
		}
		bool isEnemy = ownerPlayerEntity != mapping.PlayerEntity;

		var entity = EcsWorld.Create();
		EcsWorld.Add(entity, new DefinitionId(snap.UnitId));
		EcsWorld.Add(entity, new Name(meta.Name));
		EcsWorld.Add(entity, new Position(snap.Position.ToNumerics()));
		EcsWorld.Add(entity, new Owner(ownerPlayerEntity.AsPlayerEntity(EcsWorld)));
		EcsWorld.Add(entity, new Health(snap.CurrentHp, snap.MaxHp));
		if (meta.Damage > 0 || snap.UnitId == "priest")
		{
			EcsWorld.Add(entity, new Attack(meta.Damage, meta.Range, meta.AttackCooldown));
		}
		EcsWorld.Add(entity, new Armor(meta.Armor));
		if (meta.Speed > 0)
		{
			EcsWorld.Add(entity, new MovementStats(meta.Speed, 20f, 10f));
			EcsWorld.Add(entity, new Realm.Ecs.Components.Tags.Movable());
			EcsWorld.Add(entity, new Inventory(1));
		}
		else
		{
			EcsWorld.Add(entity, new Building());
		}
		var target = new InterpolationTarget
		{
			Position = snap.Position.ToNumerics(),
			Velocity = snap.Velocity.ToNumerics(),
			RotationY = snap.RotationY
		};
		EcsWorld.Add(entity, target);

		mapping.ServerToClientEntityMap[snap.EntityId] = entity;
		mapping.ClientToServerEntityMap[entity.Id] = snap.EntityId;

		return (entity, meta.ModelPath ?? "", isEnemy);
	}

	public void RecordGameplayTick()
	{
		if (_replayRecorder == null) return;

		Entity worldEntity = Entity.Null;
		var worldQuery = QueryCache.AllReplayStateAndNetworkMappingStateQuery;
		EcsWorld.Query(in worldQuery, (Entity entity) => worldEntity = entity);

		if (worldEntity == Entity.Null) return;

		ref var replayState = ref EcsWorld.Get<ReplayState>(worldEntity);
		int currentTick = replayState.ReplayTickCounter;

		bool isKeyframe = (currentTick % 600 == 0);
		List<ReplayUnitSnapshot> unitsToRecord = ReplayObjectPool.RentList();
		List<int> activeIds = ReplayObjectPool.RentIntList();

		if (isKeyframe)
		{
			_lastRecordedUnits.Clear();
		}

		var mapping = EcsWorld.Get<NetworkMappingState>(worldEntity);

		var unitQuery = QueryCache.AllDefinitionIdAndPositionAndOwnerQuery;
		EcsWorld.Query(in unitQuery, (Entity entity, ref DefinitionId defId, ref Position posComp, ref Owner ownerComp) =>
		{
			int entityId = entity.Id;
			string unitId = defId.Value;
			
			int ownerPlayerEntityId = -1;
			var owner = ownerComp.PlayerEntity;
			foreach (var kvp in mapping.PeerIdToPlayerEntityMap)
			{
				if (kvp.Value == owner.Value)
				{
					ownerPlayerEntityId = kvp.Key;
					break;
				}
			}

			System.Numerics.Vector3 pos = posComp.Value;

			float rotY = 0f;
			if (EcsWorld.Has<RotationY>(entity))
			{
				rotY = EcsWorld.Get<RotationY>(entity).Value;
			}

			float currentHp = EcsWorld.Has<Health>(entity) ? EcsWorld.Get<Health>(entity).Current : 0f;
			float maxHp = EcsWorld.Has<Health>(entity) ? EcsWorld.Get<Health>(entity).Max : 0f;
			bool isDead = EcsWorld.Has<Dead>(entity);
			bool isBuilding = EcsWorld.Has<Building>(entity);

			System.Numerics.Vector3 vel = System.Numerics.Vector3.Zero;
			if (EcsWorld.Has<Velocity>(entity))
			{
				vel = EcsWorld.Get<Velocity>(entity).Value;
			}

			activeIds.Add(entityId);

			if (isKeyframe)
			{
				var snap = new ReplayUnitSnapshot
				{
					EntityId = entityId,
					UnitId = unitId,
					OwnerPlayerEntityId = ownerPlayerEntityId,
					Position = new NetworkVector3(pos.X, pos.Y, pos.Z),
					RotationY = rotY,
					CurrentHp = currentHp,
					MaxHp = maxHp,
					IsDead = isDead,
					IsBuilding = isBuilding,
					Velocity = new NetworkVector3(vel.X, vel.Y, vel.Z)
				};
				unitsToRecord.Add(snap);
				_lastRecordedUnits[entityId] = snap;
			}
			else
			{
				if (_lastRecordedUnits.TryGetValue(entityId, out var last))
				{
					bool changed = last.UnitId != unitId ||
								   last.OwnerPlayerEntityId != ownerPlayerEntityId ||
								   last.Position.X != pos.X ||
								   last.Position.Y != pos.Y ||
								   last.Position.Z != pos.Z ||
								   last.RotationY != rotY ||
								   last.CurrentHp != currentHp ||
								   last.MaxHp != maxHp ||
								   last.IsDead != isDead ||
								   last.IsBuilding != isBuilding ||
								   last.Velocity.X != vel.X ||
								   last.Velocity.Y != vel.Y ||
								   last.Velocity.Z != vel.Z;

					if (changed)
					{
						var snap = new ReplayUnitSnapshot
						{
							EntityId = entityId,
							UnitId = unitId,
							OwnerPlayerEntityId = ownerPlayerEntityId,
							Position = new NetworkVector3(pos.X, pos.Y, pos.Z),
							RotationY = rotY,
							CurrentHp = currentHp,
							MaxHp = maxHp,
							IsDead = isDead,
							IsBuilding = isBuilding,
							Velocity = new NetworkVector3(vel.X, vel.Y, vel.Z)
						};
						unitsToRecord.Add(snap);
						_lastRecordedUnits[entityId] = snap;
					}
				}
				else
				{
					var snap = new ReplayUnitSnapshot
					{
						EntityId = entityId,
						UnitId = unitId,
						OwnerPlayerEntityId = ownerPlayerEntityId,
						Position = new NetworkVector3(pos.X, pos.Y, pos.Z),
						RotationY = rotY,
						CurrentHp = currentHp,
						MaxHp = maxHp,
						IsDead = isDead,
						IsBuilding = isBuilding,
						Velocity = new NetworkVector3(vel.X, vel.Y, vel.Z)
					};
					unitsToRecord.Add(snap);
					_lastRecordedUnits[entityId] = snap;
				}
			}
		});

		if (!isKeyframe)
		{
			List<int> destroyedIds = ReplayObjectPool.RentIntList();
			foreach (var pair in _lastRecordedUnits)
			{
				if (!activeIds.Contains(pair.Key))
				{
					destroyedIds.Add(pair.Key);
				}
			}

			foreach (int id in destroyedIds)
			{
				var deadSnap = _lastRecordedUnits[id];
				var deadEventSnap = new ReplayUnitSnapshot
				{
					EntityId = deadSnap.EntityId,
					UnitId = deadSnap.UnitId,
					OwnerPlayerEntityId = deadSnap.OwnerPlayerEntityId,
					Position = deadSnap.Position,
					RotationY = deadSnap.RotationY,
					CurrentHp = 0f,
					MaxHp = deadSnap.MaxHp,
					IsDead = true,
					IsBuilding = deadSnap.IsBuilding,
					Velocity = default
				};
				unitsToRecord.Add(deadEventSnap);
				_lastRecordedUnits.Remove(id);
			}
			ReplayObjectPool.ReturnIntList(destroyedIds);
		}

		float gold = replayState.GoldBackup;
		float wood = replayState.WoodBackup;
		float stone = replayState.StoneBackup;

		Entity playerEnt = mapping.PlayerEntity;
		if (EcsWorld.IsAlive(playerEnt) && EcsWorld.Has<PlayerResources>(playerEnt))
		{
			var dict = EcsWorld.Get<PlayerResources>(playerEnt).Value;
			if (dict.TryGetValue(new ResourceId("gold"), out var gVal)) gold = gVal;
			if (dict.TryGetValue(new ResourceId("wood"), out var wVal)) wood = wVal;
			if (dict.TryGetValue(new ResourceId("stone"), out var sVal)) stone = sVal;
		}

		_replayRecorder.RecordTick(currentTick, unitsToRecord, gold, wood, stone, isKeyframe);

		ReplayObjectPool.ReturnList(unitsToRecord);
		ReplayObjectPool.ReturnIntList(activeIds);

		replayState.ReplayTickCounter++;
	}
}
