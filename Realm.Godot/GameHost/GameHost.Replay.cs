using Arch.Core;
using Godot;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using Realm.Godot.ReplaySystem;
using System.Collections.Generic;

public partial class GameHost
{
	public void ResetStateForReplayPlayback()
	{
		foreach (var unit in AllUnits)
		{
			if (GodotObject.IsInstanceValid(unit))
			{
				unit.QueueFree();
			}
		}
		AllUnits.Clear();

		EcsWorld?.Dispose();
		EcsWorld = World.Create();
		_serverToClientEntityMap.Clear();
		_clientToServerEntityMap.Clear();
		_peerIdToPlayerEntityMap.Clear();

		if (ReplayPlaybackManager.Instance.Header.Players != null)
		{
			foreach (var p in ReplayPlaybackManager.Instance.Header.Players)
			{
				var playerEntity = EcsWorld.Create();
				EcsWorld.Add(playerEntity, new Player());
				EcsWorld.Add(playerEntity, new Name(p.Name));
				_peerIdToPlayerEntityMap[p.PeerId] = playerEntity;
				if (p.PeerId == 1)
				{
					_playerEntity = playerEntity;
				}
				else if (p.PeerId == -1)
				{
					_enemyPlayerEntity = playerEntity;
				}
			}
		}
	}

	public void SpawnUnitFromReplaySnapshot(ReplayUnitSnapshot snap)
	{
		if (!UnitRegistry.TryGetValue(snap.UnitId, out var meta)) return;
		Entity ownerPlayerEntity = _playerEntity;
		if (_peerIdToPlayerEntityMap.TryGetValue(snap.OwnerPlayerEntityId, out var pe))
		{
			ownerPlayerEntity = pe;
		}
		bool isEnemy = ownerPlayerEntity != _playerEntity;
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

		string modelPath = !string.IsNullOrEmpty(meta.ModelPath) ? meta.ModelPath : GetFallbackModelPath(snap.UnitId, snap.IsBuilding);
		var unit3D = SpawnUnit3D(entity, snap.UnitId, modelPath, snap.Position.ToGodot(), snap.IsBuilding, isEnemy);
		_serverToClientEntityMap[snap.EntityId] = entity;
		_clientToServerEntityMap[entity.Id] = snap.EntityId;
	}

	private void RecordGameplayTick()
	{
		if (_replayRecorder == null) return;

		bool isKeyframe = (_replayTickCounter % 600 == 0);
		List<ReplayUnitSnapshot> unitsToRecord = ReplayObjectPool.RentList();
		List<int> activeIds = ReplayObjectPool.RentIntList();

		if (isKeyframe)
		{
			_lastRecordedUnits.Clear();
		}

		foreach (var unit in AllUnits)
		{
			if (!GodotObject.IsInstanceValid(unit)) continue;

			int entityId = unit.Entity.Id;
			string unitId = unit.UnitId;
			int ownerPlayerEntityId = GetOwnerPeerId(unit.Entity);
			Vector3 pos = unit.GlobalPosition;
			float rotY = unit.GlobalRotation.Y;
			float currentHp = EcsWorld.Has<Realm.Ecs.Components.Core.Health>(unit.Entity) ? EcsWorld.Get<Realm.Ecs.Components.Core.Health>(unit.Entity).Current : 0f;
			float maxHp = EcsWorld.Has<Realm.Ecs.Components.Core.Health>(unit.Entity) ? EcsWorld.Get<Realm.Ecs.Components.Core.Health>(unit.Entity).Max : 0f;
			bool isDead = EcsWorld.Has<Realm.Ecs.Components.Tags.Dead>(unit.Entity);
			bool isBuilding = unit.IsBuilding;
			Vector3 vel = unit.Velocity;

			activeIds.Add(entityId);

			if (isKeyframe)
			{
				var snap = new ReplayUnitSnapshot
				{
					EntityId = entityId,
					UnitId = unitId,
					OwnerPlayerEntityId = ownerPlayerEntityId,
					Position = new NetworkVector3(pos),
					RotationY = rotY,
					CurrentHp = currentHp,
					MaxHp = maxHp,
					IsDead = isDead,
					IsBuilding = isBuilding,
					Velocity = new NetworkVector3(vel)
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
							Position = new NetworkVector3(pos),
							RotationY = rotY,
							CurrentHp = currentHp,
							MaxHp = maxHp,
							IsDead = isDead,
							IsBuilding = isBuilding,
							Velocity = new NetworkVector3(vel)
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
						Position = new NetworkVector3(pos),
						RotationY = rotY,
						CurrentHp = currentHp,
						MaxHp = maxHp,
						IsDead = isDead,
						IsBuilding = isBuilding,
						Velocity = new NetworkVector3(vel)
					};
					unitsToRecord.Add(snap);
					_lastRecordedUnits[entityId] = snap;
				}
			}
		}

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

		float gold = InGameHUD.Instance != null ? InGameHUD.Instance.Gold : _goldBackup;
		float wood = InGameHUD.Instance != null ? InGameHUD.Instance.Wood : _woodBackup;
		float stone = InGameHUD.Instance != null ? InGameHUD.Instance.Stone : _stoneBackup;

		_replayRecorder.RecordTick(_replayTickCounter, unitsToRecord, gold, wood, stone, isKeyframe);

		ReplayObjectPool.ReturnList(unitsToRecord);
		ReplayObjectPool.ReturnIntList(activeIds);

		_replayTickCounter++;
	}

}
