using Arch.Core;
using Godot;
using MemoryPack;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Services;
using System.Collections.Generic;

public class NetworkService
{
	private readonly WorldAccessor _ecsWorldAccessor;
	private World _ecsWorld => _ecsWorldAccessor.Current;

	private readonly List<NetworkCommand> _unacknowledgedCommands = new();
	private readonly List<WorldSnapshot> _queuedDeltas = new();
	private readonly Dictionary<int, Vector3> _clientCameraPositions = new();
	private readonly Dictionary<int, Dictionary<int, UnitSnapshot>> _lastBaselineSnapshotsPerClient = new();
	private readonly Dictionary<int, List<DelayedPacket>> _spectatorDelayedPackets = new();

	private readonly List<UnitSnapshot> _pendingUnitSpawns = new();
	private readonly List<Entity> _pendingUnitKills = new();

	private struct DelayedPacket
	{
		public string FunctionName;
		public object[] Arguments;
		public double SendTime;
	}

	public NetworkService(WorldAccessor ecsWorldAccessor)
	{
		_ecsWorldAccessor = ecsWorldAccessor;
	}

	public void Clear()
	{
		_unacknowledgedCommands.Clear();
		_queuedDeltas.Clear();
		_clientCameraPositions.Clear();
		_lastBaselineSnapshotsPerClient.Clear();
		_spectatorDelayedPackets.Clear();
		_pendingUnitSpawns.Clear();
		_pendingUnitKills.Clear();
	}

	public int GetServerEntityId(int localEntityId)
	{
		Entity worldEntity = FindWorldEntity();
		if (worldEntity != Entity.Null && _ecsWorld.Has<NetworkMappingState>(worldEntity))
		{
			var mapping = _ecsWorld.Get<NetworkMappingState>(worldEntity);
			if (mapping.ClientToServerEntityMap.TryGetValue(localEntityId, out int serverId))
			{
				return serverId;
			}
		}
		return localEntityId;
	}

	public Entity FindServerEntity(int entityId, List<Unit3D> allUnits)
	{
		foreach (var unit in allUnits)
		{
			if (unit.Entity.Id == entityId)
			{
				return unit.Entity;
			}
		}
		return Entity.Null;
	}

	public Prop3D FindClosestProp(System.Numerics.Vector3 position, string propIdType, List<Prop3D> allProps)
	{
		Prop3D closest = null;
		float closestDist = float.MaxValue;
		foreach (var prop in allProps)
		{
			if (GodotObject.IsInstanceValid(prop))
			{
				if (!string.IsNullOrEmpty(propIdType) && prop.PropId != propIdType)
				{
					continue;
				}
				Vector3 godotPos = new Vector3(position.X, position.Y, position.Z);
				float dist = prop.GlobalPosition.DistanceTo(godotPos);
				if (dist < closestDist)
				{
					closestDist = dist;
					closest = prop;
				}
			}
		}
		return closest;
	}

	public int GetOwnerPeerId(Entity unitEntity)
	{
		if (!_ecsWorld.Has<Owner>(unitEntity)) return -1;
		var owner = _ecsWorld.Get<Owner>(unitEntity).PlayerEntity;
		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !_ecsWorld.Has<NetworkMappingState>(worldEntity)) return -1;
		var mapping = _ecsWorld.Get<NetworkMappingState>(worldEntity);
		foreach (var kvp in mapping.PeerIdToPlayerEntityMap)
		{
			if (kvp.Value == owner.Value)
			{
				return kvp.Key;
			}
		}
		return -1;
	}

	public bool IsClientAuthorized(int peerId, Entity unitEntity)
	{
		if (!_ecsWorld.Has<Owner>(unitEntity)) return false;
		var ownerComp = _ecsWorld.Get<Owner>(unitEntity);
		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !_ecsWorld.Has<NetworkMappingState>(worldEntity)) return false;
		var mapping = _ecsWorld.Get<NetworkMappingState>(worldEntity);
		if (mapping.PeerIdToPlayerEntityMap.TryGetValue(peerId, out var playerEntity))
		{
			return ownerComp.PlayerEntity.Value == playerEntity;
		}
		return false;
	}

	public bool IsUnitVisibleToPlayer(Entity playerEntity, Entity unitEntity, List<Unit3D> allUnits)
	{
		if (_ecsWorld.Has<Owner>(unitEntity) && _ecsWorld.Get<Owner>(unitEntity).PlayerEntity.Value == playerEntity)
		{
			return true;
		}
		Vector3 unitPos = Vector3.Zero;
		foreach (var unit in allUnits)
		{
			if (unit.Entity == unitEntity)
			{
				unitPos = unit.GlobalPosition;
				break;
			}
		}
		foreach (var unit in allUnits)
		{
			if (_ecsWorld.Has<Owner>(unit.Entity) && _ecsWorld.Get<Owner>(unit.Entity).PlayerEntity.Value == playerEntity)
			{
				if (unit.GlobalPosition.DistanceTo(unitPos) <= 15.0f)
				{
					return true;
				}
			}
		}
		return false;
	}

	public float ComputeDynamicInterpolationFactor()
	{
		float bufferTime = 0.1f;
		if (LobbyManager.Instance != null && LobbyManager.Instance.LocalPlayer != null)
		{
			string latStr = LobbyManager.Instance.LocalPlayer.Latency;
			if (!string.IsNullOrEmpty(latStr) && latStr != "--" && latStr.Contains(" ms"))
			{
				string numStr = latStr.Replace(" ms", "").Trim();
				if (float.TryParse(numStr, out float rttMs))
				{
					bufferTime = Mathf.Max(0.1f, (rttMs / 1000f) * 1.5f);
				}
			}
		}
		return 1f / bufferTime;
	}

	public (int CommandId, byte[] Payload) BuildClientCommand(string commandType, List<int> unitIds, System.Numerics.Vector3 targetPos, int targetEntityId, string argString)
	{
		int commandId = 1;
		Entity worldEntity = FindWorldEntity();
		if (worldEntity != Entity.Null && _ecsWorld.Has<NetworkState>(worldEntity))
		{
			commandId = _ecsWorld.Get<NetworkState>(worldEntity).NextCommandId;
			ref var ns = ref _ecsWorld.Get<NetworkState>(worldEntity);
			ns.NextCommandId++;
		}

		var cmd = new NetworkCommand
		{
			CommandId = commandId,
			CommandType = commandType,
			UnitEntityIds = unitIds,
			TargetPosition = new NetworkVector3(targetPos.X, targetPos.Y, targetPos.Z),
			TargetEntityId = targetEntityId,
			ArgString = argString
		};
		_unacknowledgedCommands.Add(cmd);
		GD.Print($"[CLIENT_CMD_SENT] CommandType={commandType} Units={string.Join(",", unitIds)} Target={targetPos}");
		return (cmd.CommandId, MemoryPackSerializer.Serialize(cmd));
	}

	public IReadOnlyList<byte[]> GetUnacknowledgedCommandPayloads()
	{
		var payloads = new List<byte[]>(_unacknowledgedCommands.Count);
		foreach (var cmd in _unacknowledgedCommands)
		{
			payloads.Add(MemoryPackSerializer.Serialize(cmd));
		}
		return payloads;
	}

	public void AcknowledgeCommand(int commandId)
	{
		_unacknowledgedCommands.RemoveAll(c => c.CommandId == commandId);
	}

	public void RecordClientCameraPosition(int peerId, Vector3 position)
	{
		_clientCameraPositions[peerId] = position;
	}

	public void RecordSnapshotReceived(byte[] payload)
	{
		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !_ecsWorld.Has<NetworkState>(worldEntity)) return;
		ref var ns = ref _ecsWorld.Get<NetworkState>(worldEntity);
		ns.LastSnapshotReceivedTime = Godot.Time.GetTicksMsec();
		ProcessSnapshotDirect(payload);
	}

	private void ProcessSnapshotDirect(byte[] payload)
	{
		var snapshot = MemoryPackSerializer.Deserialize<WorldSnapshot>(payload);
		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !_ecsWorld.Has<NetworkState>(worldEntity)) return;

		ref var networkState = ref _ecsWorld.Get<NetworkState>(worldEntity);
		if (snapshot.Sequence <= networkState.LastAppliedSnapshotSequence) return;
		networkState.LastAppliedSnapshotSequence = snapshot.Sequence;

		if (snapshot.IsBaseline)
		{
			networkState.LastReceivedBaselineSeq = snapshot.Sequence;
			networkState.HasReceivedInitialBaseline = true;
			_queuedDeltas.RemoveAll(d => d.Sequence <= snapshot.Sequence);
			ApplyWorldSnapshot(snapshot);
			while (_queuedDeltas.Count > 0 && _queuedDeltas[0].BaseSequence == snapshot.Sequence)
			{
				var nextDelta = _queuedDeltas[0];
				_queuedDeltas.RemoveAt(0);
				ApplyWorldSnapshot(nextDelta);
			}
		}
		else
		{
			if (!networkState.HasReceivedInitialBaseline || snapshot.BaseSequence != networkState.LastReceivedBaselineSeq)
			{
				_queuedDeltas.Add(snapshot);
				_queuedDeltas.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));
			}
			else
			{
				ApplyWorldSnapshot(snapshot);
			}
		}
	}

	private void ApplyWorldSnapshot(WorldSnapshot snapshot)
	{
		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !_ecsWorld.Has<NetworkMappingState>(worldEntity)) return;
		var mapping = _ecsWorld.Get<NetworkMappingState>(worldEntity);

		foreach (var snap in snapshot.Units)
		{
			if (mapping.ServerToClientEntityMap.TryGetValue(snap.EntityId, out var localEntity))
			{
				if (_ecsWorld.IsAlive(localEntity))
				{
					if (snap.IsDead)
					{
						if (!_ecsWorld.Has<Dead>(localEntity))
						{
							_ecsWorld.Add<Dead>(localEntity);
							_pendingUnitKills.Add(localEntity);
						}
						continue;
					}
					if (_ecsWorld.Has<Health>(localEntity))
					{
						var hp = _ecsWorld.Get<Health>(localEntity);
						hp.Current = snap.CurrentHp;
						hp.Max = snap.MaxHp;
						_ecsWorld.Set(localEntity, hp);
					}
					var target = new InterpolationTarget
					{
						Position = snap.Position.ToNumerics(),
						Velocity = snap.Velocity.ToNumerics(),
						RotationY = snap.RotationY
					};
					if (_ecsWorld.Has<InterpolationTarget>(localEntity))
					{
						_ecsWorld.Set(localEntity, target);
					}
					else
					{
						_ecsWorld.Add(localEntity, target);
					}
					GD.Print($"[CLIENT_SNAPSHOT_APPLIED] Sequence={snapshot.Sequence} Unit={snap.EntityId} ServerPos={snap.Position.ToGodot()}");
				}
			}
			else
			{
				if (!snap.IsDead)
				{
					_pendingUnitSpawns.Add(snap);
				}
			}
		}
	}

	public IReadOnlyList<UnitSnapshot> FlushPendingUnitSpawns()
	{
		var result = new List<UnitSnapshot>(_pendingUnitSpawns);
		_pendingUnitSpawns.Clear();
		return result;
	}

	public IReadOnlyList<Entity> FlushPendingUnitKills()
	{
		var result = new List<Entity>(_pendingUnitKills);
		_pendingUnitKills.Clear();
		return result;
	}

	public Entity SpawnUnitFromSnapshot(UnitSnapshot snap, System.Func<string, bool, string> getFallbackModelPath, out string modelPath, out bool isEnemy)
	{
		modelPath = string.Empty;
		isEnemy = false;

		if (!GameHost.UnitRegistry.TryGetValue(snap.UnitId, out var meta))
		{
			return Entity.Null;
		}

		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !_ecsWorld.Has<NetworkMappingState>(worldEntity)) return Entity.Null;

		ref var mapping = ref _ecsWorld.Get<NetworkMappingState>(worldEntity);

		Entity ownerPlayerEntity = mapping.PlayerEntity;
		if (mapping.PeerIdToPlayerEntityMap.TryGetValue(snap.OwnerPlayerEntityId, out var pe))
		{
			ownerPlayerEntity = pe;
		}
		int localPeerId = 1;
		var mainLoop = Engine.GetMainLoop();
		if (mainLoop is SceneTree tree)
		{
			localPeerId = tree.GetMultiplayer().GetUniqueId();
		}
		isEnemy = ArePeersEnemies(localPeerId, snap.OwnerPlayerEntityId);

		var entity = _ecsWorld.Create();
		_ecsWorld.Add(entity, new DefinitionId(snap.UnitId));
		_ecsWorld.Add(entity, new Name(meta.Name));
		_ecsWorld.Add(entity, new Position(snap.Position.ToNumerics()));
		_ecsWorld.Add(entity, new Owner(ownerPlayerEntity.AsPlayerEntity(_ecsWorld)));
		_ecsWorld.Add(entity, new Health(snap.CurrentHp, snap.MaxHp));
		if (meta.Damage > 0 || snap.UnitId == "priest")
		{
			_ecsWorld.Add(entity, new Attack(meta.Damage, meta.Range, meta.AttackCooldown));
		}
		_ecsWorld.Add(entity, new Armor(meta.Armor));
		if (meta.Speed > 0)
		{
			_ecsWorld.Add(entity, new MovementStats(meta.Speed, 20f, 10f));
			_ecsWorld.Add(entity, new Movable());
			_ecsWorld.Add(entity, new Inventory(1));
		}
		else
		{
			_ecsWorld.Add(entity, new Building());
		}
		var interpolationTarget = new InterpolationTarget
		{
			Position = snap.Position.ToNumerics(),
			Velocity = snap.Velocity.ToNumerics(),
			RotationY = snap.RotationY
		};
		_ecsWorld.Add(entity, interpolationTarget);
		_ecsWorld.Add(entity, new UnitFaction(isEnemy));

		mapping.ServerToClientEntityMap[snap.EntityId] = entity;
		mapping.ClientToServerEntityMap[entity.Id] = snap.EntityId;

		modelPath = !string.IsNullOrEmpty(meta.ModelPath) ? meta.ModelPath : getFallbackModelPath(snap.UnitId, snap.IsBuilding);
		return entity;
	}

	public bool TryGetLocalEntity(int serverEntityId, out Entity localEntity)
	{
		localEntity = Entity.Null;
		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !_ecsWorld.Has<NetworkMappingState>(worldEntity)) return false;
		var mapping = _ecsWorld.Get<NetworkMappingState>(worldEntity);
		return mapping.ServerToClientEntityMap.TryGetValue(serverEntityId, out localEntity);
	}

	public void SetBackupResources(float gold, float wood, float stone)
	{
		var worldQuery = Realm.Ecs.Common.QueryCache.AllReplayStateQuery;
		_ecsWorld.Query(in worldQuery, (Entity entity) =>
		{
			ref var state = ref _ecsWorld.Get<ReplayState>(entity);
			state.GoldBackup = gold;
			state.WoodBackup = wood;
			state.StoneBackup = stone;
		});
	}

	public readonly struct ServerCommandResult
	{
		public readonly bool NeedsBuildUnit;
		public readonly string BuildUnitType;
		public readonly Vector3 BuildPosition;
		public readonly int BuildPeerOwner;

		public readonly bool NeedsSpellEffect;
		public readonly string SpellId;
		public readonly Vector3 SpellPosition;

		public readonly List<int> StopVelocityEntityIds;
		public readonly List<int> HoldVelocityEntityIds;

		public ServerCommandResult(
			bool needsBuildUnit, string buildUnitType, Vector3 buildPosition, int buildPeerOwner,
			bool needsSpellEffect, string spellId, Vector3 spellPosition,
			List<int> stopVelocityEntityIds, List<int> holdVelocityEntityIds)
		{
			NeedsBuildUnit = needsBuildUnit;
			BuildUnitType = buildUnitType;
			BuildPosition = buildPosition;
			BuildPeerOwner = buildPeerOwner;
			NeedsSpellEffect = needsSpellEffect;
			SpellId = spellId;
			SpellPosition = spellPosition;
			StopVelocityEntityIds = stopVelocityEntityIds;
			HoldVelocityEntityIds = holdVelocityEntityIds;
		}
	}

	public ServerCommandResult ExecuteServerCommand(int peerId, NetworkCommand cmd, List<Unit3D> allUnits, List<Prop3D> allProps)
	{
		var stopVelocityEntityIds = new List<int>();
		var holdVelocityEntityIds = new List<int>();

		if (cmd.CommandType == "move")
		{
			int cols = Mathf.CeilToInt(Mathf.Sqrt(cmd.UnitEntityIds.Count));
			float spacing = 2.2f;
			int unitIndex = 0;

			System.Numerics.Vector3 groupCenter = System.Numerics.Vector3.Zero;
			int movableCount = 0;
			foreach (int serverId in cmd.UnitEntityIds)
			{
				var entity = FindServerEntity(serverId, allUnits);
				if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;
				if (_ecsWorld.Has<Position>(entity))
				{
					groupCenter += _ecsWorld.Get<Position>(entity).Value;
					movableCount++;
				}
			}
			if (movableCount > 0)
			{
				groupCenter /= movableCount;
			}

			System.Numerics.Vector3 moveDir = new System.Numerics.Vector3(cmd.TargetPosition.X, cmd.TargetPosition.Y, cmd.TargetPosition.Z) - groupCenter;
			moveDir.Y = 0f;
			if (moveDir.LengthSquared() > 0.01f)
			{
				moveDir = System.Numerics.Vector3.Normalize(moveDir);
			}
			else
			{
				moveDir = new System.Numerics.Vector3(0f, 0f, -1f);
			}
			System.Numerics.Vector3 right = new System.Numerics.Vector3(-moveDir.Z, 0f, moveDir.X);

			foreach (int serverId in cmd.UnitEntityIds)
			{
				var entity = FindServerEntity(serverId, allUnits);
				if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;
				ClearUnitOrders(entity);
				int row = unitIndex / cols;
				int col = unitIndex % cols;
				float offsetX = (col - cols * 0.5f + 0.5f) * spacing;
				float offsetZ = -row * spacing;
				var targetPos = new System.Numerics.Vector3(cmd.TargetPosition.X, cmd.TargetPosition.Y, cmd.TargetPosition.Z);
				var scattered = targetPos + right * offsetX + moveDir * offsetZ;
				var moveTo = new MoveTo(scattered);
				if (_ecsWorld.Has<MoveTo>(entity)) _ecsWorld.Set(entity, moveTo);
				else _ecsWorld.Add(entity, moveTo);
				unitIndex++;
			}
		}
		else if (cmd.CommandType == "attack")
		{
			var targetEntity = FindServerEntity(cmd.TargetEntityId, allUnits);
			if (targetEntity != Entity.Null)
			{
				foreach (int serverId in cmd.UnitEntityIds)
				{
					var entity = FindServerEntity(serverId, allUnits);
					if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;
					ClearUnitOrders(entity);
					var attackTarget = new AttackTarget(targetEntity);
					if (_ecsWorld.Has<AttackTarget>(entity)) _ecsWorld.Set(entity, attackTarget);
					else _ecsWorld.Add(entity, attackTarget);
				}
			}
		}
		else if (cmd.CommandType == "follow")
		{
			var targetEntity = FindServerEntity(cmd.TargetEntityId, allUnits);
			if (targetEntity != Entity.Null)
			{
				foreach (int serverId in cmd.UnitEntityIds)
				{
					var entity = FindServerEntity(serverId, allUnits);
					if (entity == Entity.Null || !IsClientAuthorized(peerId, entity) || entity == targetEntity) continue;
					ClearUnitOrders(entity);
					if (_ecsWorld.Has<DefinitionId>(entity) && _ecsWorld.Get<DefinitionId>(entity).Value == "priest")
					{
						var healTarget = new HealingTarget(targetEntity);
						_ecsWorld.Add(entity, healTarget);
					}
					else if (_ecsWorld.Has<Movable>(entity))
					{
						var follow = new Realm.Ecs.Components.Movement.Follow(targetEntity);
						if (_ecsWorld.Has<Realm.Ecs.Components.Movement.Follow>(entity)) _ecsWorld.Set(entity, follow);
						else _ecsWorld.Add(entity, follow);
					}
				}
			}
		}
		else if (cmd.CommandType == "gather")
		{
			var targetPos = new System.Numerics.Vector3(cmd.TargetPosition.X, cmd.TargetPosition.Y, cmd.TargetPosition.Z);
			Prop3D prop = FindClosestProp(targetPos, "", allProps);
			if (prop != null)
			{
				string resType = prop.PropId switch
				{
					"goldmine" => "gold",
					"tree" => "wood",
					"rock" => "stone",
					_ => null
				};
				if (resType != null)
				{
					foreach (int serverId in cmd.UnitEntityIds)
					{
						var entity = FindServerEntity(serverId, allUnits);
						if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;
						if (_ecsWorld.Has<DefinitionId>(entity) && _ecsWorld.Get<DefinitionId>(entity).Value != "worker") continue;
						ClearUnitOrders(entity);
						var gatherer = new Gatherer(resType, prop.Entity);
						if (_ecsWorld.Has<Gatherer>(entity)) _ecsWorld.Set(entity, gatherer);
						else _ecsWorld.Add(entity, gatherer);
						var moveTo = new MoveTo(new System.Numerics.Vector3(prop.GlobalPosition.X, prop.GlobalPosition.Y, prop.GlobalPosition.Z));
						if (_ecsWorld.Has<MoveTo>(entity)) _ecsWorld.Set(entity, moveTo);
						else _ecsWorld.Add(entity, moveTo);
					}
				}
			}
		}
		else if (cmd.CommandType == "stop")
		{
			foreach (int serverId in cmd.UnitEntityIds)
			{
				var entity = FindServerEntity(serverId, allUnits);
				if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;
				ClearUnitOrders(entity);
				stopVelocityEntityIds.Add(serverId);
			}
		}
		else if (cmd.CommandType == "hold")
		{
			foreach (int serverId in cmd.UnitEntityIds)
			{
				var entity = FindServerEntity(serverId, allUnits);
				if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;
				ClearUnitOrders(entity);
				holdVelocityEntityIds.Add(serverId);
				if (!_ecsWorld.Has<Realm.Ecs.Components.Movement.HoldPosition>(entity))
				{
					_ecsWorld.Add<Realm.Ecs.Components.Movement.HoldPosition>(entity);
				}
			}
		}
		else if (cmd.CommandType == "patrol")
		{
			int cols = Mathf.CeilToInt(Mathf.Sqrt(cmd.UnitEntityIds.Count));
			float spacing = 2.2f;
			int unitIndex = 0;

			System.Numerics.Vector3 groupCenter = System.Numerics.Vector3.Zero;
			int movableCount = 0;
			foreach (int serverId in cmd.UnitEntityIds)
			{
				var entity = FindServerEntity(serverId, allUnits);
				if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;
				if (_ecsWorld.Has<Position>(entity))
				{
					groupCenter += _ecsWorld.Get<Position>(entity).Value;
					movableCount++;
				}
			}
			if (movableCount > 0)
			{
				groupCenter /= movableCount;
			}

			System.Numerics.Vector3 moveDir = new System.Numerics.Vector3(cmd.TargetPosition.X, cmd.TargetPosition.Y, cmd.TargetPosition.Z) - groupCenter;
			moveDir.Y = 0f;
			if (moveDir.LengthSquared() > 0.01f)
			{
				moveDir = System.Numerics.Vector3.Normalize(moveDir);
			}
			else
			{
				moveDir = new System.Numerics.Vector3(0f, 0f, -1f);
			}
			System.Numerics.Vector3 right = new System.Numerics.Vector3(-moveDir.Z, 0f, moveDir.X);

			foreach (int serverId in cmd.UnitEntityIds)
			{
				var entity = FindServerEntity(serverId, allUnits);
				if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;
				ClearUnitOrders(entity);
				if (_ecsWorld.Has<Movable>(entity))
				{
					int row = unitIndex / cols;
					int col = unitIndex % cols;
					float offsetX = (col - cols * 0.5f + 0.5f) * spacing;
					float offsetZ = -row * spacing;
					Vector3 unitPos = Vector3.Zero;
					foreach (var u in allUnits)
					{
						if (u.Entity == entity)
						{
							unitPos = u.GlobalPosition;
							break;
						}
					}
					var patrolA = new System.Numerics.Vector3(unitPos.X, unitPos.Y, unitPos.Z);
					var targetPos = new System.Numerics.Vector3(cmd.TargetPosition.X, cmd.TargetPosition.Y, cmd.TargetPosition.Z);
					var patrolB = targetPos + right * offsetX + moveDir * offsetZ;
					var patrol = new Patrol(patrolA, patrolB);
					if (_ecsWorld.Has<Patrol>(entity)) _ecsWorld.Set(entity, patrol);
					else _ecsWorld.Add(entity, patrol);
					var moveTo = new MoveTo(patrolB);
					if (_ecsWorld.Has<MoveTo>(entity)) _ecsWorld.Set(entity, moveTo);
					else _ecsWorld.Add(entity, moveTo);
					unitIndex++;
				}
			}
		}
		else if (cmd.CommandType == "move_queued")
		{
			int cols = Mathf.CeilToInt(Mathf.Sqrt(cmd.UnitEntityIds.Count));
			float spacing = 2.2f;
			int unitIndex = 0;

			System.Numerics.Vector3 groupCenter = System.Numerics.Vector3.Zero;
			int movableCount = 0;
			foreach (int serverId in cmd.UnitEntityIds)
			{
				var entity = FindServerEntity(serverId, allUnits);
				if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;
				if (_ecsWorld.Has<Position>(entity))
				{
					groupCenter += _ecsWorld.Get<Position>(entity).Value;
					movableCount++;
				}
			}
			if (movableCount > 0)
			{
				groupCenter /= movableCount;
			}

			System.Numerics.Vector3 moveDir = new System.Numerics.Vector3(cmd.TargetPosition.X, cmd.TargetPosition.Y, cmd.TargetPosition.Z) - groupCenter;
			moveDir.Y = 0f;
			if (moveDir.LengthSquared() > 0.01f)
			{
				moveDir = System.Numerics.Vector3.Normalize(moveDir);
			}
			else
			{
				moveDir = new System.Numerics.Vector3(0f, 0f, -1f);
			}
			System.Numerics.Vector3 right = new System.Numerics.Vector3(-moveDir.Z, 0f, moveDir.X);

			foreach (int serverId in cmd.UnitEntityIds)
			{
				var entity = FindServerEntity(serverId, allUnits);
				if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;
				if (!_ecsWorld.Has<Movable>(entity)) continue;
				bool alreadyMoving = _ecsWorld.Has<MoveTo>(entity);
				if (!alreadyMoving) ClearUnitOrders(entity);
				int row = unitIndex / cols;
				int col = unitIndex % cols;
				float offsetX = (col - cols * 0.5f + 0.5f) * spacing;
				float offsetZ = -row * spacing;
				var targetPos = new System.Numerics.Vector3(cmd.TargetPosition.X, cmd.TargetPosition.Y, cmd.TargetPosition.Z);
				var scattered = targetPos + right * offsetX + moveDir * offsetZ;
				if (alreadyMoving)
				{
					if (_ecsWorld.Has<WaypointQueue>(entity))
					{
						var q = _ecsWorld.Get<WaypointQueue>(entity);
						q.Add(scattered);
						_ecsWorld.Set(entity, q);
					}
					else
					{
						var q = new WaypointQueue(scattered);
						_ecsWorld.Add(entity, q);
					}
				}
				else
				{
					var moveTo = new MoveTo(scattered);
					if (_ecsWorld.Has<MoveTo>(entity)) _ecsWorld.Set(entity, moveTo);
					else _ecsWorld.Add(entity, moveTo);
				}
				unitIndex++;
			}
		}
		else if (cmd.CommandType == "train")
		{
			var goldResourceId = new ResourceId("Gold");
			var woodResourceId = new ResourceId("Wood");
			var stoneResourceId = new ResourceId("Stone");
			foreach (int serverId in cmd.UnitEntityIds)
			{
				var entity = FindServerEntity(serverId, allUnits);
				if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;
				
				string unitId = cmd.ArgString;
				if (GameHost.UnitRegistry.TryGetValue(unitId, out var meta))
				{
					var ownerComp = _ecsWorld.Get<Owner>(entity);
					var ownerEntity = ownerComp.PlayerEntity.Value;
					if (_ecsWorld.TryGet<PlayerResources>(ownerEntity, out var res))
					{
						int costGold = (int)meta.CostGold;
						int costWood = (int)meta.CostWood;
						int costStone = (int)meta.CostStone;
						if (res.Value[goldResourceId] >= costGold && 
							res.Value[woodResourceId] >= costWood && 
							res.Value[stoneResourceId] >= costStone)
						{
							res.Value[goldResourceId] -= costGold;
							res.Value[woodResourceId] -= costWood;
							res.Value[stoneResourceId] -= costStone;
							_ecsWorld.Set(ownerEntity, res);

							if (!_ecsWorld.Has<ProductionQueue>(entity))
							{
								_ecsWorld.Add(entity, new ProductionQueue());
							}
							ref var prod = ref _ecsWorld.Get<ProductionQueue>(entity);
							prod.UnitIds.Add(unitId);
							if (prod.UnitIds.Count == 1)
							{
								prod.BuildTime = meta.ProductionTime;
								prod.CurrentProgress = 0f;
							}
						}
					}
				}
			}
		}
		else if (cmd.CommandType == "cancel_train")
		{
			var goldResourceId = new ResourceId("Gold");
			var woodResourceId = new ResourceId("Wood");
			var stoneResourceId = new ResourceId("Stone");
			foreach (int serverId in cmd.UnitEntityIds)
			{
				var entity = FindServerEntity(serverId, allUnits);
				if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;
				if (!_ecsWorld.Has<ProductionQueue>(entity)) continue;

				ref var prod = ref _ecsWorld.Get<ProductionQueue>(entity);
				int idx = cmd.TargetEntityId;
				if (idx >= 0 && idx < prod.UnitIds.Count)
				{
					string unitId = prod.UnitIds[idx];
					prod.UnitIds.RemoveAt(idx);
					if (idx == 0)
					{
						prod.CurrentProgress = 0f;
						if (prod.UnitIds.Count > 0)
						{
							if (GameHost.UnitRegistry.TryGetValue(prod.UnitIds[0], out var meta))
							{
								prod.BuildTime = meta.ProductionTime;
							}
						}
					}
					
					if (GameHost.UnitRegistry.TryGetValue(unitId, out var regMeta))
					{
						var ownerComp = _ecsWorld.Get<Owner>(entity);
						var ownerEntity = ownerComp.PlayerEntity.Value;
						if (_ecsWorld.TryGet<PlayerResources>(ownerEntity, out var res))
						{
							res.Value[goldResourceId] += (int)regMeta.CostGold;
							res.Value[woodResourceId] += (int)regMeta.CostWood;
							res.Value[stoneResourceId] += (int)regMeta.CostStone;
							_ecsWorld.Set(ownerEntity, res);
						}
					}
				}
			}
		}

		bool needsBuildUnit = cmd.CommandType == "build";
		string buildUnitType = needsBuildUnit ? cmd.ArgString : null;
		Vector3 buildPosition = needsBuildUnit ? cmd.TargetPosition.ToGodot() : Vector3.Zero;
		int buildPeerOwner = needsBuildUnit ? peerId : -1;

		bool needsSpellEffect = cmd.CommandType == "spell";
		string spellId = needsSpellEffect ? cmd.ArgString : null;
		Vector3 spellPosition = needsSpellEffect ? cmd.TargetPosition.ToGodot() : Vector3.Zero;

		return new ServerCommandResult(
			needsBuildUnit, buildUnitType, buildPosition, buildPeerOwner,
			needsSpellEffect, spellId, spellPosition,
			stopVelocityEntityIds, holdVelocityEntityIds);
	}

	public List<(int PeerId, byte[] Payload)> BuildServerSnapshots(int localPeerId, List<Unit3D> allUnits)
	{
		var results = new List<(int PeerId, byte[] Payload)>();
		Entity worldEntity = FindWorldEntity();
		if (worldEntity == Entity.Null || !_ecsWorld.Has<NetworkState>(worldEntity)) return results;
		if (!_ecsWorld.Has<NetworkMappingState>(worldEntity)) return results;

		ref var networkState = ref _ecsWorld.Get<NetworkState>(worldEntity);
		networkState.SnapshotSequence++;
		int snapshotSequence = networkState.SnapshotSequence;
		bool isBaseline = (snapshotSequence % 30 == 0);

		if (LobbyManager.Instance == null) return results;

		var mapping = _ecsWorld.Get<NetworkMappingState>(worldEntity);

		foreach (var p in LobbyManager.Instance.PlayerList)
		{
			if (p.PeerId == localPeerId || p.PeerId < 0) continue;
			int peerId = p.PeerId;

			if (!mapping.PeerIdToPlayerEntityMap.TryGetValue(peerId, out var playerEntity)) continue;

			Vector3 cameraPos = _clientCameraPositions.TryGetValue(peerId, out var cam) ? cam : Vector3.Zero;
			var snapshotUnits = new List<UnitSnapshot>();
			bool hasBaseline = _lastBaselineSnapshotsPerClient.TryGetValue(peerId, out var lastBaselineMap);
			if (!hasBaseline && !isBaseline) continue;
			var nextBaselineMap = isBaseline ? new Dictionary<int, UnitSnapshot>() : null;

			foreach (var unit in allUnits)
			{
				if (!GodotObject.IsInstanceValid(unit)) continue;
				if (!IsUnitVisibleToPlayer(playerEntity, unit.Entity, allUnits)) continue;
				float distToCamera = unit.GlobalPosition.DistanceTo(cameraPos);
				bool isDetailed = distToCamera <= 35.0f;
				var currentSnap = new UnitSnapshot
				{
					EntityId = unit.Entity.Id,
					UnitId = unit.UnitId,
					OwnerPlayerEntityId = GetOwnerPeerId(unit.Entity),
					Position = new NetworkVector3(unit.GlobalPosition),
					RotationY = unit.GlobalRotation.Y,
					CurrentHp = _ecsWorld.Has<Health>(unit.Entity) ? _ecsWorld.Get<Health>(unit.Entity).Current : 0f,
					MaxHp = _ecsWorld.Has<Health>(unit.Entity) ? _ecsWorld.Get<Health>(unit.Entity).Max : 0f,
					IsDead = _ecsWorld.Has<Dead>(unit.Entity),
					IsBuilding = unit.IsBuilding,
					IsDetailed = isDetailed,
					Velocity = new NetworkVector3(unit.Velocity)
				};
				if (isBaseline)
				{
					snapshotUnits.Add(currentSnap);
					nextBaselineMap[unit.Entity.Id] = currentSnap;
				}
				else
				{
					bool changed = true;
					if (lastBaselineMap.TryGetValue(unit.Entity.Id, out var baseSnap))
					{
						if (isDetailed)
						{
							bool posChanged = baseSnap.Position.ToGodot().DistanceTo(unit.GlobalPosition) > 0.05f;
							bool rotChanged = Mathf.Abs(baseSnap.RotationY - unit.GlobalRotation.Y) > 0.05f;
							bool hpChanged = Mathf.Abs(baseSnap.CurrentHp - currentSnap.CurrentHp) > 0.1f;
							bool deadChanged = baseSnap.IsDead != currentSnap.IsDead;
							changed = posChanged || rotChanged || hpChanged || deadChanged;
						}
						else
						{
							bool posChanged = baseSnap.Position.ToGodot().DistanceTo(unit.GlobalPosition) > 1.0f;
							bool deadChanged = baseSnap.IsDead != currentSnap.IsDead;
							changed = posChanged || deadChanged;
							currentSnap.RotationY = 0f;
							currentSnap.CurrentHp = 0f;
							currentSnap.MaxHp = 0f;
							currentSnap.Velocity = new NetworkVector3(0f, 0f, 0f);
						}
					}
					if (changed)
					{
						snapshotUnits.Add(currentSnap);
					}
				}
			}

			if (isBaseline)
			{
				_lastBaselineSnapshotsPerClient[peerId] = nextBaselineMap;
			}

			var worldSnapshot = new WorldSnapshot
			{
				Sequence = snapshotSequence,
				IsBaseline = isBaseline,
				BaseSequence = isBaseline ? snapshotSequence : (snapshotSequence / 30) * 30,
				Units = snapshotUnits
			};
			var payload = MemoryPackSerializer.Serialize(worldSnapshot);
			results.Add((peerId, payload));
		}

		foreach (var unit in allUnits)
		{
			if (GodotObject.IsInstanceValid(unit))
			{
				GD.Print($"[SERVER_STATE] Unit={unit.Entity.Id} Pos={unit.GlobalPosition}");
			}
		}

		return results;
	}

	public void QueueSpectatorDelayedPacket(int peerId, string functionName, object[] arguments)
	{
		double sendTime = (Godot.Time.GetTicksMsec() / 1000.0) + 300.0;
		if (!_spectatorDelayedPackets.TryGetValue(peerId, out var list))
		{
			list = new List<DelayedPacket>();
			_spectatorDelayedPackets[peerId] = list;
		}
		list.Add(new DelayedPacket
		{
			FunctionName = functionName,
			Arguments = arguments,
			SendTime = sendTime
		});
	}

	public readonly struct ReadyPacket
	{
		public readonly int PeerId;
		public readonly string FunctionName;
		public readonly object[] Arguments;

		public ReadyPacket(int peerId, string functionName, object[] arguments)
		{
			PeerId = peerId;
			FunctionName = functionName;
			Arguments = arguments;
		}
	}

	public List<ReadyPacket> FlushReadySpectatorPackets()
	{
		if (_spectatorDelayedPackets.Count == 0) return new List<ReadyPacket>();

		double currentTime = Godot.Time.GetTicksMsec() / 1000.0;
		var disconnectedPeers = new List<int>();
		foreach (var key in _spectatorDelayedPackets.Keys)
		{
			if (LobbyManager.Instance == null || !LobbyManager.Instance.PlayerList.Exists(p => p.PeerId == key))
			{
				disconnectedPeers.Add(key);
			}
		}
		foreach (var peerId in disconnectedPeers)
		{
			_spectatorDelayedPackets.Remove(peerId);
		}

		var readyPackets = new List<ReadyPacket>();
		foreach (var kvp in _spectatorDelayedPackets)
		{
			int peerId = kvp.Key;
			var list = kvp.Value;
			int sentCount = 0;
			for (int i = 0; i < list.Count; i++)
			{
				if (list[i].SendTime <= currentTime)
				{
					readyPackets.Add(new ReadyPacket(peerId, list[i].FunctionName, list[i].Arguments));
					sentCount++;
				}
				else
				{
					break;
				}
			}
			if (sentCount > 0)
			{
				list.RemoveRange(0, sentCount);
			}
		}
		return readyPackets;
	}

	public bool IsSpectatorWithDelay(int peerId)
	{
		if (LobbyManager.Instance == null) return false;
		var p = LobbyManager.Instance.PlayerList.Find(x => x.PeerId == peerId);
		return p != null && p.Team == "Spectator" && LobbyManager.Instance.SpectatorDelay;
	}

	private void ClearUnitOrders(Entity entity)
	{
		if (_ecsWorld.Has<MoveTo>(entity)) _ecsWorld.Remove<MoveTo>(entity);
		if (_ecsWorld.Has<PathFollow>(entity)) _ecsWorld.Remove<PathFollow>(entity);
		if (_ecsWorld.Has<AttackTarget>(entity)) _ecsWorld.Remove<AttackTarget>(entity);
		if (_ecsWorld.Has<Realm.Ecs.Components.Movement.AttackMove>(entity)) _ecsWorld.Remove<Realm.Ecs.Components.Movement.AttackMove>(entity);
		if (_ecsWorld.Has<Realm.Ecs.Components.Movement.HoldPosition>(entity)) _ecsWorld.Remove<Realm.Ecs.Components.Movement.HoldPosition>(entity);
		if (_ecsWorld.Has<Realm.Ecs.Components.Movement.Follow>(entity)) _ecsWorld.Remove<Realm.Ecs.Components.Movement.Follow>(entity);
		if (_ecsWorld.Has<Patrol>(entity)) _ecsWorld.Remove<Patrol>(entity);
		if (_ecsWorld.Has<HealingTarget>(entity)) _ecsWorld.Remove<HealingTarget>(entity);
		if (_ecsWorld.Has<WaypointQueue>(entity)) _ecsWorld.Remove<WaypointQueue>(entity);
		if (_ecsWorld.Has<Gatherer>(entity)) _ecsWorld.Remove<Gatherer>(entity);
	}

	private Entity FindWorldEntity()
	{
		Entity worldEntity = Entity.Null;
		var query = Realm.Ecs.Common.QueryCache.AllNetworkStateQuery;
		_ecsWorld.Query(in query, (Entity entity) => worldEntity = entity);
		return worldEntity;
	}

	public bool WasClientInMultiplayer { get; private set; } = false;
	public bool IsConnectionLost { get; private set; } = false;

	public int LocalPeerId
	{
		get
		{
			var worldEntity = FindWorldEntity();
			return worldEntity != Entity.Null && _ecsWorld.Has<NetworkState>(worldEntity)
				? _ecsWorld.Get<NetworkState>(worldEntity).LocalPeerId
				: 1;
		}
		set
		{
			var worldEntity = FindWorldEntity();
			if (worldEntity != Entity.Null && _ecsWorld.Has<NetworkState>(worldEntity))
			{
				ref var state = ref _ecsWorld.Get<NetworkState>(worldEntity);
				state.LocalPeerId = value;
			}
		}
	}

	public float CommandSendTimer
	{
		get
		{
			var worldEntity = FindWorldEntity();
			return worldEntity != Entity.Null && _ecsWorld.Has<NetworkState>(worldEntity)
				? _ecsWorld.Get<NetworkState>(worldEntity).CommandSendTimer
				: 0f;
		}
		set
		{
			var worldEntity = FindWorldEntity();
			if (worldEntity != Entity.Null && _ecsWorld.Has<NetworkState>(worldEntity))
			{
				ref var state = ref _ecsWorld.Get<NetworkState>(worldEntity);
				state.CommandSendTimer = value;
			}
		}
	}

	public ulong LastSnapshotReceivedTime
	{
		get
		{
			var worldEntity = FindWorldEntity();
			return worldEntity != Entity.Null && _ecsWorld.Has<NetworkState>(worldEntity)
				? _ecsWorld.Get<NetworkState>(worldEntity).LastSnapshotReceivedTime
				: 0;
		}
		set
		{
			var worldEntity = FindWorldEntity();
			if (worldEntity != Entity.Null && _ecsWorld.Has<NetworkState>(worldEntity))
			{
				ref var state = ref _ecsWorld.Get<NetworkState>(worldEntity);
				state.LastSnapshotReceivedTime = value;
			}
		}
	}

	public void MarkClientEnteredMultiplayer()
	{
		WasClientInMultiplayer = true;
		LastSnapshotReceivedTime = Godot.Time.GetTicksMsec();
	}

	public void UpdateConnectionStatus(bool multiplayerActive, bool isServer)
	{
		bool isLost;
		if (multiplayerActive && !isServer)
		{
			ulong now = Godot.Time.GetTicksMsec();
			ulong lastReceived = LastSnapshotReceivedTime;
			if (lastReceived > 0)
			{
				double timeSinceLastSnapshot = (now - lastReceived) / 1000.0;
				isLost = timeSinceLastSnapshot > 30.0;
			}
			else
			{
				isLost = false;
			}
		}
		else
		{
			isLost = false;
		}
		IsConnectionLost = isLost;
	}

	public static bool ArePeersEnemies(int peerId1, int peerId2)
	{
		if (LobbyManager.Instance == null || LobbyManager.Instance.PlayerList == null || LobbyManager.Instance.PlayerList.Count == 0)
		{
			return peerId1 != peerId2;
		}

		var p1 = LobbyManager.Instance.PlayerList.Find(x => x.PeerId == peerId1);
		var p2 = LobbyManager.Instance.PlayerList.Find(x => x.PeerId == peerId2);

		if (p1 == null || p2 == null)
		{
			string t1 = p1?.Team ?? "Team 1";
			string t2 = p2?.Team ?? "Team 2";
			return t1 != t2;
		}

		return p1.Team != p2.Team;
	}
}
