using Arch.Core;
using Godot;
using MemoryPack;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using Realm.MapAPI;
using System.Collections.Generic;

public partial class GameHost
{
	private int GetServerEntityId(Entity localEntity)
	{
		if (_clientToServerEntityMap.TryGetValue(localEntity.Id, out int serverId))
		{
			return serverId;
		}
		return localEntity.Id;
	}

	private Entity FindServerEntity(int entityId)
	{
		foreach (var unit in AllUnits)
		{
			if (unit.Entity.Id == entityId)
			{
				return unit.Entity;
			}
		}
		return Entity.Null;
	}

	private Prop3D FindClosestProp(Vector3 position, string propIdType)
	{
		Prop3D closest = null;
		float closestDist = float.MaxValue;
		foreach (var prop in AllProps)
		{
			if (GodotObject.IsInstanceValid(prop))
			{
				if (!string.IsNullOrEmpty(propIdType) && prop.PropId != propIdType)
				{
					continue;
				}
				float dist = prop.GlobalPosition.DistanceTo(position);
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
		if (!EcsWorld.Has<Owner>(unitEntity)) return -1;
		var owner = EcsWorld.Get<Owner>(unitEntity).PlayerEntity;
		foreach (var kvp in _peerIdToPlayerEntityMap)
		{
			if (kvp.Value == owner.Value)
			{
				return kvp.Key;
			}
		}
		return -1;
	}

	private bool IsClientAuthorized(int peerId, Entity unitEntity)
	{
		if (!EcsWorld.Has<Owner>(unitEntity)) return false;
		var ownerComp = EcsWorld.Get<Owner>(unitEntity);
		if (_peerIdToPlayerEntityMap.TryGetValue(peerId, out var playerEntity))
		{
			return ownerComp.PlayerEntity.Value == playerEntity;
		}
		return false;
	}

	private bool IsUnitVisibleToPlayer(Entity playerEntity, Entity unitEntity)
	{
		if (EcsWorld.Has<Owner>(unitEntity) && EcsWorld.Get<Owner>(unitEntity).PlayerEntity.Value == playerEntity)
		{
			return true;
		}
		Vector3 unitPos = Vector3.Zero;
		foreach (var unit in AllUnits)
		{
			if (unit.Entity == unitEntity)
			{
				unitPos = unit.GlobalPosition;
				break;
			}
		}
		foreach (var unit in AllUnits)
		{
			if (EcsWorld.Has<Owner>(unit.Entity) && EcsWorld.Get<Owner>(unit.Entity).PlayerEntity.Value == playerEntity)
			{
				if (unit.GlobalPosition.DistanceTo(unitPos) <= 15.0f)
				{
					return true;
				}
			}
		}
		return false;
	}

	private void QueueClientCommand(string commandType, List<int> unitIds, Vector3 targetPos, int targetEntityId, string argString)
	{
		var cmd = new NetworkCommand
		{
			CommandId = _nextCommandId++,
			CommandType = commandType,
			UnitEntityIds = unitIds,
			TargetPosition = new NetworkVector3(targetPos),
			TargetEntityId = targetEntityId,
			ArgString = argString
		};
		_unacknowledgedCommands.Add(cmd);
		GD.Print($"[CLIENT_CMD_SENT] CommandType={commandType} Units={string.Join(",", unitIds)} Target={targetPos}");
		var payload = MemoryPackSerializer.Serialize(cmd);
		RpcId(1, nameof(SubmitCommand), payload);
	}

	private void UpdateClientTick(float fDelta)
	{
		_commandSendTimer += fDelta;
		if (_commandSendTimer >= 0.05f)
		{
			_commandSendTimer = 0f;
			foreach (var cmd in _unacknowledgedCommands)
			{
				var payload = MemoryPackSerializer.Serialize(cmd);
				RpcId(1, nameof(SubmitCommand), payload);
			}
			var camera = GetViewport().GetCamera3D();
			if (camera != null)
			{
				var pos = new NetworkVector3(camera.GlobalPosition);
				RpcId(1, nameof(UpdateClientCamera), MemoryPackSerializer.Serialize(pos));
			}
		}
		ProcessClientPredictionAndInterpolation(fDelta);
	}

	private float GetDynamicInterpolationFactor()
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

	private void ProcessClientPredictionAndInterpolation(float fDelta)
	{
		_fDelta = fDelta;
		_dynamicInterpolationFactor = GetDynamicInterpolationFactor();
		var query = new QueryDescription().WithAll<InterpolationTarget, Unit3D>();
		EcsWorld.Query(in query, _interpolationQueryDelegate);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void SubmitCommand(byte[] payload)
	{
		if (!Multiplayer.IsServer()) return;
		var sw = System.Diagnostics.Stopwatch.StartNew();
		int senderId = Multiplayer.GetRemoteSenderId();
		var cmd = MemoryPackSerializer.Deserialize<NetworkCommand>(payload);
		GD.Print($"[SERVER_CMD_RECEIVED] Peer={senderId} CommandType={cmd.CommandType} Units={string.Join(",", cmd.UnitEntityIds)} Target={cmd.TargetPosition.ToGodot()}");
		ExecuteServerCommand(senderId, cmd);
		RpcId(senderId, nameof(AcknowledgeCommand), cmd.CommandId);
		sw.Stop();
		float responseCpuMs = (float)sw.Elapsed.TotalMilliseconds;
		float adjustedResponseMs = responseCpuMs + _trackerLastTickDelay;
		if (_trackerApiDurations != null)
		{
			_trackerApiDurations.Add(adjustedResponseMs);
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void AcknowledgeCommand(int commandId)
	{
		_unacknowledgedCommands.RemoveAll(c => c.CommandId == commandId);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
	public void UpdateClientCamera(byte[] payload)
	{
		int senderId = Multiplayer.GetRemoteSenderId();
		var pos = MemoryPackSerializer.Deserialize<NetworkVector3>(payload);
		_clientCameraPositions[senderId] = pos.ToGodot();
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
	public void ReceiveSnapshot(byte[] payload)
	{
		if (Multiplayer.IsServer()) return;
		_lastSnapshotReceivedTime = Time.GetTicksMsec();
		ProcessSnapshotDirect(payload);
	}

	private void ProcessSnapshotDirect(byte[] payload)
	{
		var snapshot = MemoryPackSerializer.Deserialize<WorldSnapshot>(payload);
		if (snapshot.Sequence <= _lastAppliedSnapshotSequence) return;
		_lastAppliedSnapshotSequence = snapshot.Sequence;

		if (snapshot.IsBaseline)
		{
			_lastReceivedBaselineSeq = snapshot.Sequence;
			_hasReceivedInitialBaseline = true;
			_queuedDeltas.RemoveAll(d => d.Sequence <= snapshot.Sequence);
			ApplyWorldSnapshot(snapshot);
			while (_queuedDeltas.Count > 0 && _queuedDeltas[0].BaseSequence == _lastReceivedBaselineSeq)
			{
				var nextDelta = _queuedDeltas[0];
				_queuedDeltas.RemoveAt(0);
				ApplyWorldSnapshot(nextDelta);
			}
		}
		else
		{
			if (!_hasReceivedInitialBaseline || snapshot.BaseSequence != _lastReceivedBaselineSeq)
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

	private void ExecuteServerCommand(int peerId, NetworkCommand cmd)
	{
		if (cmd.CommandType == "move")
		{
			int cols = Mathf.CeilToInt(Mathf.Sqrt(cmd.UnitEntityIds.Count));
			float spacing = 2.2f;
			int unitIndex = 0;
			foreach (int serverId in cmd.UnitEntityIds)
			{
				var entity = FindServerEntity(serverId);
				if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;
				ClearUnitOrders(entity);
				int row = unitIndex / cols;
				int col = unitIndex % cols;
				float offsetX = (col - cols * 0.5f + 0.5f) * spacing;
				float offsetZ = row * spacing;
				Vector3 scattered = new Vector3(cmd.TargetPosition.X + offsetX, cmd.TargetPosition.Y, cmd.TargetPosition.Z + offsetZ);
				var moveTo = new MoveTo(new System.Numerics.Vector3(scattered.X, scattered.Y, scattered.Z));
				if (EcsWorld.Has<MoveTo>(entity)) EcsWorld.Set(entity, moveTo);
				else EcsWorld.Add(entity, moveTo);
				unitIndex++;
			}
		}
		else if (cmd.CommandType == "attack")
		{
			var targetEntity = FindServerEntity(cmd.TargetEntityId);
			if (targetEntity != Entity.Null)
			{
				foreach (int serverId in cmd.UnitEntityIds)
				{
					var entity = FindServerEntity(serverId);
					if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;
					ClearUnitOrders(entity);
					var attackTarget = new AttackTarget(targetEntity);
					if (EcsWorld.Has<AttackTarget>(entity)) EcsWorld.Set(entity, attackTarget);
					else EcsWorld.Add(entity, attackTarget);
				}
			}
		}
		else if (cmd.CommandType == "follow")
		{
			var targetEntity = FindServerEntity(cmd.TargetEntityId);
			if (targetEntity != Entity.Null)
			{
				foreach (int serverId in cmd.UnitEntityIds)
				{
					var entity = FindServerEntity(serverId);
					if (entity == Entity.Null || !IsClientAuthorized(peerId, entity) || entity == targetEntity) continue;
					ClearUnitOrders(entity);
					if (EcsWorld.Has<DefinitionId>(entity) && EcsWorld.Get<DefinitionId>(entity).Value == "priest")
					{
						var healTarget = new HealingTarget(targetEntity);
						EcsWorld.Add(entity, healTarget);
					}
					else if (EcsWorld.Has<Realm.Ecs.Components.Tags.Movable>(entity))
					{
						var follow = new Realm.Ecs.Components.Movement.Follow(targetEntity);
						if (EcsWorld.Has<Realm.Ecs.Components.Movement.Follow>(entity)) EcsWorld.Set(entity, follow);
						else EcsWorld.Add(entity, follow);
					}
				}
			}
		}
		else if (cmd.CommandType == "gather")
		{
			Prop3D prop = FindClosestProp(cmd.TargetPosition.ToGodot(), "");
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
						var entity = FindServerEntity(serverId);
						if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;
						if (EcsWorld.Has<DefinitionId>(entity) && EcsWorld.Get<DefinitionId>(entity).Value != "worker") continue;
						ClearUnitOrders(entity);
						var gatherer = new Gatherer(resType, prop);
						if (EcsWorld.Has<Gatherer>(entity)) EcsWorld.Set(entity, gatherer);
						else EcsWorld.Add(entity, gatherer);
						var moveTo = new MoveTo(new System.Numerics.Vector3(prop.GlobalPosition.X, prop.GlobalPosition.Y, prop.GlobalPosition.Z));
						if (EcsWorld.Has<MoveTo>(entity)) EcsWorld.Set(entity, moveTo);
						else EcsWorld.Add(entity, moveTo);
					}
				}
			}
		}
		else if (cmd.CommandType == "stop")
		{
			foreach (int serverId in cmd.UnitEntityIds)
			{
				var entity = FindServerEntity(serverId);
				if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;
				ClearUnitOrders(entity);
				foreach (var unit in AllUnits)
				{
					if (unit.Entity == entity)
					{
						unit.Velocity = Vector3.Zero;
						break;
					}
				}
			}
		}
		else if (cmd.CommandType == "hold")
		{
			foreach (int serverId in cmd.UnitEntityIds)
			{
				var entity = FindServerEntity(serverId);
				if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;
				ClearUnitOrders(entity);
				foreach (var unit in AllUnits)
				{
					if (unit.Entity == entity)
					{
						unit.Velocity = Vector3.Zero;
						break;
					}
				}
				if (!EcsWorld.Has<Realm.Ecs.Components.Movement.HoldPosition>(entity))
				{
					EcsWorld.Add<Realm.Ecs.Components.Movement.HoldPosition>(entity);
				}
			}
		}
		else if (cmd.CommandType == "patrol")
		{
			int cols = Mathf.CeilToInt(Mathf.Sqrt(cmd.UnitEntityIds.Count));
			float spacing = 2.2f;
			int unitIndex = 0;
			foreach (int serverId in cmd.UnitEntityIds)
			{
				var entity = FindServerEntity(serverId);
				if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;
				ClearUnitOrders(entity);
				if (EcsWorld.Has<Realm.Ecs.Components.Tags.Movable>(entity))
				{
					int row = unitIndex / cols;
					int col = unitIndex % cols;
					float offsetX = (col - cols * 0.5f + 0.5f) * spacing;
					float offsetZ = row * spacing;
					Vector3 unitPos = Vector3.Zero;
					foreach (var u in AllUnits)
					{
						if (u.Entity == entity)
						{
							unitPos = u.GlobalPosition;
							break;
						}
					}
					var patrolA = new System.Numerics.Vector3(unitPos.X, unitPos.Y, unitPos.Z);
					var patrolB = new System.Numerics.Vector3(cmd.TargetPosition.X + offsetX, cmd.TargetPosition.Y, cmd.TargetPosition.Z + offsetZ);
					var patrol = new Patrol(patrolA, patrolB);
					if (EcsWorld.Has<Patrol>(entity)) EcsWorld.Set(entity, patrol);
					else EcsWorld.Add(entity, patrol);
					var moveTo = new MoveTo(patrolB);
					if (EcsWorld.Has<MoveTo>(entity)) EcsWorld.Set(entity, moveTo);
					else EcsWorld.Add(entity, moveTo);
					unitIndex++;
				}
			}
		}
		else if (cmd.CommandType == "move_queued")
		{
			int cols = Mathf.CeilToInt(Mathf.Sqrt(cmd.UnitEntityIds.Count));
			float spacing = 2.2f;
			int unitIndex = 0;
			foreach (int serverId in cmd.UnitEntityIds)
			{
				var entity = FindServerEntity(serverId);
				if (entity == Entity.Null || !IsClientAuthorized(peerId, entity)) continue;
				if (!EcsWorld.Has<Realm.Ecs.Components.Tags.Movable>(entity)) continue;
				bool alreadyMoving = EcsWorld.Has<MoveTo>(entity);
				if (!alreadyMoving) ClearUnitOrders(entity);
				int row = unitIndex / cols;
				int col = unitIndex % cols;
				float offsetX = (col - cols * 0.5f + 0.5f) * spacing;
				float offsetZ = row * spacing;
				Vector3 scattered = new Vector3(cmd.TargetPosition.X + offsetX, cmd.TargetPosition.Y, cmd.TargetPosition.Z + offsetZ);
				var targetVec = new System.Numerics.Vector3(scattered.X, scattered.Y, scattered.Z);
				if (alreadyMoving)
				{
					if (EcsWorld.Has<WaypointQueue>(entity))
					{
						var q = EcsWorld.Get<WaypointQueue>(entity);
						q.Add(targetVec);
						EcsWorld.Set(entity, q);
					}
					else
					{
						var q = new WaypointQueue(targetVec);
						EcsWorld.Add(entity, q);
					}
				}
				else
				{
					var moveTo = new MoveTo(targetVec);
					if (EcsWorld.Has<MoveTo>(entity)) EcsWorld.Set(entity, moveTo);
					else EcsWorld.Add(entity, moveTo);
				}
				unitIndex++;
			}
		}
		else if (cmd.CommandType == "build")
		{
			string buildType = cmd.ArgString;
			if (UnitRegistry.TryGetValue(buildType, out var meta))
			{
				var playerOwner = _peerIdToPlayerEntityMap[peerId].AsPlayerEntity(EcsWorld);
				Vector3 position = cmd.TargetPosition.ToGodot();
				string modelPath = !string.IsNullOrEmpty(meta.ModelPath) ? meta.ModelPath : GetFallbackModelPath(buildType, true);
				var bldEntity = CreateEcsUnit(buildType, meta.Name, meta.MaxHp, meta.Damage, meta.Range, meta.Armor, 0f, position, playerOwner);
				SpawnUnit3D(bldEntity, buildType, modelPath, position, true, false);
			}
		}
		else if (cmd.CommandType == "spell")
		{
			string spellId = cmd.ArgString;
			Vector3 position = cmd.TargetPosition.ToGodot();
			IUnit caster = null;
			if (cmd.UnitEntityIds.Count > 0)
			{
				var casterEntity = FindServerEntity(cmd.UnitEntityIds[0]);
				if (casterEntity != Entity.Null && EcsWorld.IsAlive(casterEntity))
				{
					caster = GetUnitWrapper(casterEntity);
				}
			}
			OnSpellCast?.Invoke(caster, spellId, new System.Numerics.Vector3(position.X, position.Y, position.Z));
			if (spellId == "fireball")
			{
				DealSpellDamageAOE(position, 4.0f, 50f);
			}
			else if (spellId == "lightning")
			{
				DealSpellDamageAOE(position, 2.0f, 80f);
			}
			else if (spellId == "holylight")
			{
				HealAOE(position, 4.0f, 60f);
			}
			Vector3 effectPos = cmd.TargetPosition.ToGodot();
			if (LobbyManager.Instance != null)
			{
				foreach (var p in LobbyManager.Instance.PlayerList)
				{
					if (p.PeerId == _localPeerId || p.PeerId < 0) continue;
					QueueOrSendPacket(p.PeerId, nameof(PlaySpellEffect), spellId, effectPos);
				}
			}
		}
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void PlaySpellEffect(string spellId, Vector3 position)
	{
		if (spellId == "fireball")
		{
			SpawnFireblastEffect(position);
			SpawnTargetIndicator(position, new Color(0.9f, 0.3f, 0.1f));
		}
		else if (spellId == "lightning")
		{
			SpawnLightningEffect(position);
			SpawnTargetIndicator(position, new Color(0.2f, 0.5f, 1f));
		}
		else if (spellId == "holylight")
		{
			SpawnHolyLightEffect(position);
			SpawnTargetIndicator(position, new Color(0.2f, 0.9f, 0.3f));
		}
	}

	private void UpdateServerSnapshotTick(float fDelta)
	{
		_snapshotSequence++;
		bool isBaseline = (_snapshotSequence % 30 == 0);
		foreach (var p in LobbyManager.Instance.PlayerList)
		{
			if (p.PeerId == _localPeerId || p.PeerId < 0) continue;
			int peerId = p.PeerId;
			if (!_peerIdToPlayerEntityMap.TryGetValue(peerId, out var playerEntity)) continue;
			Vector3 cameraPos = _clientCameraPositions.TryGetValue(peerId, out var cam) ? cam : Vector3.Zero;
			var snapshotUnits = new List<UnitSnapshot>();
			bool hasBaseline = _lastBaselineSnapshotsPerClient.TryGetValue(peerId, out var lastBaselineMap);
			if (!hasBaseline && !isBaseline) continue;
			var nextBaselineMap = isBaseline ? new Dictionary<int, UnitSnapshot>() : null;
			foreach (var unit in AllUnits)
			{
				if (!GodotObject.IsInstanceValid(unit)) continue;
				if (!IsUnitVisibleToPlayer(playerEntity, unit.Entity)) continue;
				float distToCamera = unit.GlobalPosition.DistanceTo(cameraPos);
				bool isDetailed = distToCamera <= 35.0f;
				var currentSnap = new UnitSnapshot
				{
					EntityId = unit.Entity.Id,
					UnitId = unit.UnitId,
					OwnerPlayerEntityId = GetOwnerPeerId(unit.Entity),
					Position = new NetworkVector3(unit.GlobalPosition),
					RotationY = unit.GlobalRotation.Y,
					CurrentHp = EcsWorld.Has<Health>(unit.Entity) ? EcsWorld.Get<Health>(unit.Entity).Current : 0f,
					MaxHp = EcsWorld.Has<Health>(unit.Entity) ? EcsWorld.Get<Health>(unit.Entity).Max : 0f,
					IsDead = EcsWorld.Has<Dead>(unit.Entity),
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
				Sequence = _snapshotSequence,
				IsBaseline = isBaseline,
				BaseSequence = isBaseline ? _snapshotSequence : (_snapshotSequence / 30) * 30,
				Units = snapshotUnits
			};
			var payload = MemoryPackSerializer.Serialize(worldSnapshot);
			QueueOrSendPacket(peerId, nameof(ReceiveSnapshot), payload);
		}
		foreach (var unit in AllUnits)
		{
			if (GodotObject.IsInstanceValid(unit))
			{
				GD.Print($"[SERVER_STATE] Unit={unit.Entity.Id} Pos={unit.GlobalPosition}");
			}
		}
	}

	private void ApplyWorldSnapshot(WorldSnapshot snapshot)
	{
		foreach (var snap in snapshot.Units)
		{
			if (_serverToClientEntityMap.TryGetValue(snap.EntityId, out var localEntity))
			{
				if (EcsWorld.IsAlive(localEntity))
				{
					if (snap.IsDead)
					{
						if (!EcsWorld.Has<Dead>(localEntity))
						{
							EcsWorld.Add<Dead>(localEntity);
							var unit3D = EcsWorld.Get<Unit3D>(localEntity);
							CallDeferred("KillUnitDeferred", unit3D);
						}
						continue;
					}
					if (EcsWorld.Has<Health>(localEntity))
					{
						var hp = EcsWorld.Get<Health>(localEntity);
						hp.Current = snap.CurrentHp;
						hp.Max = snap.MaxHp;
						EcsWorld.Set(localEntity, hp);
					}
					var target = new InterpolationTarget
					{
						Position = snap.Position.ToNumerics(),
						Velocity = snap.Velocity.ToNumerics(),
						RotationY = snap.RotationY
					};
					var unit = EcsWorld.Get<Unit3D>(localEntity);
					Vector3 localPosBefore = Vector3.Zero;
					if (GodotObject.IsInstanceValid(unit))
					{
						localPosBefore = unit.GlobalPosition;
					}
					if (EcsWorld.Has<InterpolationTarget>(localEntity))
					{
						EcsWorld.Set(localEntity, target);
					}
					else
					{
						EcsWorld.Add(localEntity, target);
					}
					if (GodotObject.IsInstanceValid(unit))
					{
						GD.Print($"[CLIENT_SNAPSHOT_APPLIED] Sequence={snapshot.Sequence} Unit={snap.EntityId} ServerPos={snap.Position.ToGodot()} LocalPosBefore={localPosBefore}");
					}
				}
			}
			else
			{
				if (!snap.IsDead)
				{
					SpawnUnitFromSnapshot(snap);
				}
			}
		}
	}

	private void SpawnUnitFromSnapshot(UnitSnapshot snap)
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

	public bool TryGetLocalEntity(int serverEntityId, out Entity localEntity)
	{
		return _serverToClientEntityMap.TryGetValue(serverEntityId, out localEntity);
	}

	public void SetBackupResources(float gold, float wood, float stone)
	{
		_goldBackup = gold;
		_woodBackup = wood;
		_stoneBackup = stone;
	}

	public void KillUnitDeferredExternal(Unit3D unit)
	{
		CallDeferred("KillUnitDeferred", unit);
	}

	private void QueueOrSendPacket(int peerId, string funcName, params object[] args)
	{
		bool isSpectator = false;
		if (LobbyManager.Instance != null)
		{
			var p = LobbyManager.Instance.PlayerList.Find(x => x.PeerId == peerId);
			if (p != null && p.Team == "Spectator")
			{
				isSpectator = true;
			}
		}

		if (isSpectator && LobbyManager.Instance != null && LobbyManager.Instance.SpectatorDelay)
		{
			double sendTime = (Time.GetTicksMsec() / 1000.0) + 300.0;
			if (!_spectatorDelayedPackets.TryGetValue(peerId, out var list))
			{
				list = new List<DelayedPacket>();
				_spectatorDelayedPackets[peerId] = list;
			}
			list.Add(new DelayedPacket
			{
				FunctionName = funcName,
				Arguments = args,
				SendTime = sendTime
			});
		}
		else
		{
			if (funcName == nameof(ReceiveSnapshot))
			{
				RpcId(peerId, nameof(ReceiveSnapshot), (byte[])args[0]);
			}
			else if (funcName == nameof(PlaySpellEffect))
			{
				RpcId(peerId, nameof(PlaySpellEffect), (string)args[0], (Vector3)args[1]);
			}
		}
	}

	private void ProcessDelayedSpectatorPackets()
	{
		if (_spectatorDelayedPackets.Count == 0) return;

		double currentTime = Time.GetTicksMsec() / 1000.0;
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

		foreach (var kvp in _spectatorDelayedPackets)
		{
			int peerId = kvp.Key;
			var list = kvp.Value;
			int sentCount = 0;
			for (int i = 0; i < list.Count; i++)
			{
				if (list[i].SendTime <= currentTime)
				{
					var packet = list[i];
					if (packet.FunctionName == nameof(ReceiveSnapshot))
					{
						RpcId(peerId, nameof(ReceiveSnapshot), (byte[])packet.Arguments[0]);
					}
					else if (packet.FunctionName == nameof(PlaySpellEffect))
					{
						RpcId(peerId, nameof(PlaySpellEffect), (string)packet.Arguments[0], (Vector3)packet.Arguments[1]);
					}
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
	}

}
