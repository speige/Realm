using Arch.Core;
using Godot;
using MemoryPack;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Movement;
using Realm.MapAPI;
using System.Collections.Generic;

public partial class GameHost
{
	private NetworkService _networkService;

	public int GetOwnerPeerId(Entity unitEntity) => _networkService.GetOwnerPeerId(unitEntity);

	private int GetServerEntityId(Entity localEntity) =>
		_networkService.GetServerEntityId(localEntity.Id);

	public bool TryGetLocalEntity(int serverEntityId, out Entity localEntity)
	{
		if (_networkService == null)
		{
			localEntity = Entity.Null;
			return false;
		}
		return _networkService.TryGetLocalEntity(serverEntityId, out localEntity);
	}

	public void SetBackupResources(float gold, float wood, float stone) =>
		_networkService.SetBackupResources(gold, wood, stone);

	public void KillUnitDeferredExternal(Unit3D unit)
	{
		CallDeferred("KillUnitDeferred", unit);
	}

	private void QueueClientCommand(string commandType, List<int> unitIds, Vector3 targetPos, int targetEntityId, string argString)
	{
		var targetNumerics = new System.Numerics.Vector3(targetPos.X, targetPos.Y, targetPos.Z);
		var (_, payload) = _networkService.BuildClientCommand(commandType, unitIds, targetNumerics, targetEntityId, argString);
		RpcId(1, nameof(SubmitCommand), payload);
	}

	private void UpdateClientTick(float fDelta)
	{
		_commandSendTimer += fDelta;
		if (_commandSendTimer >= 0.05f)
		{
			_commandSendTimer = 0f;
			foreach (var payload in _networkService.GetUnacknowledgedCommandPayloads())
			{
				RpcId(1, nameof(SubmitCommand), payload);
			}
			var camera = GetViewport().GetCamera3D();
			if (camera != null)
			{
				var pos = new NetworkVector3(camera.GlobalPosition);
				RpcId(1, nameof(UpdateClientCamera), MemoryPackSerializer.Serialize(pos));
			}
		}
		EcsWorld?.Mutate<Realm.Ecs.Components.Core.NetworkState>(_worldEntity, (ref Realm.Ecs.Components.Core.NetworkState netState) =>
			netState.DynamicInterpolationFactor = _networkService.ComputeDynamicInterpolationFactor());
		var query = new QueryDescription().WithAll<InterpolationTarget>();
		EcsWorld.Query(in query, _simulationService.InterpolationQueryDelegate);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
	public void SubmitCommand(byte[] payload)
	{
		if (!Multiplayer.IsServer()) return;
		var sw = System.Diagnostics.Stopwatch.StartNew();
		int senderId = Multiplayer.GetRemoteSenderId();
		var cmd = MemoryPackSerializer.Deserialize<NetworkCommand>(payload);
		GD.Print($"[SERVER_CMD_RECEIVED] Peer={senderId} CommandType={cmd.CommandType} Units={string.Join(",", cmd.UnitEntityIds)} Target={cmd.TargetPosition.ToGodot()}");

		var result = _networkService.ExecuteServerCommand(senderId, cmd, AllUnits, AllProps);

		foreach (int entityId in result.StopVelocityEntityIds)
		{
			foreach (var unit in AllUnits)
			{
				if (unit.Entity.Id == entityId)
				{
					unit.Velocity = Vector3.Zero;
					break;
				}
			}
		}

		foreach (int entityId in result.HoldVelocityEntityIds)
		{
			foreach (var unit in AllUnits)
			{
				if (unit.Entity.Id == entityId)
				{
					unit.Velocity = Vector3.Zero;
					break;
				}
			}
		}

		if (result.NeedsBuildUnit && UnitRegistry.TryGetValue(result.BuildUnitType, out var meta))
		{
			var playerOwner = _peerIdToPlayerEntityMap[result.BuildPeerOwner].AsPlayerEntity(EcsWorld);
			string modelPath = !string.IsNullOrEmpty(meta.ModelPath) ? meta.ModelPath : GetFallbackModelPath(result.BuildUnitType, true);
			var bldEntity = CreateEcsUnit(result.BuildUnitType, meta.Name, meta.MaxHp, meta.Damage, meta.Range, meta.Armor, 0f, result.BuildPosition, playerOwner);
			SpawnUnit3D(bldEntity, result.BuildUnitType, modelPath, result.BuildPosition, true, false);
		}

		if (result.NeedsSpellEffect)
		{
			string spellId = result.SpellId;
			Vector3 position = result.SpellPosition;
			IUnit caster = null;
			if (cmd.UnitEntityIds.Count > 0)
			{
				var casterEntity = _networkService.FindServerEntity(cmd.UnitEntityIds[0], AllUnits);
				if (casterEntity != Entity.Null && EcsWorld.IsAlive(casterEntity))
				{
					caster = GetUnitWrapper(casterEntity);
				}
			}
			var casterEnt = caster != null ? ((IEcsEntityWrapper)caster).Entity : Entity.Null;
			OnSpellCast?.Invoke(caster, spellId, new System.Numerics.Vector3(position.X, position.Y, position.Z));
			if (spellId == "fireball")
			{
				_simulationService.DealSpellDamageAOE(new System.Numerics.Vector3(position.X, position.Y, position.Z), 4.0f, 50f, casterEnt);
			}
			else if (spellId == "lightning")
			{
				_simulationService.DealSpellDamageAOE(new System.Numerics.Vector3(position.X, position.Y, position.Z), 2.0f, 80f, casterEnt);
			}
			else if (spellId == "holylight")
			{
				_simulationService.HealAOE(new System.Numerics.Vector3(position.X, position.Y, position.Z), 4.0f, 60f);
			}
			InGameHUD.Instance?.RefreshUI(SelectedUnits);
			if (LobbyManager.Instance != null)
			{
				foreach (var p in LobbyManager.Instance.PlayerList)
				{
					if (p.PeerId == _localPeerId || p.PeerId < 0) continue;
					QueueOrSendPacket(p.PeerId, nameof(PlaySpellEffect), spellId, position);
				}
			}
		}

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
		_networkService.AcknowledgeCommand(commandId);
	}

	[Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
	public void UpdateClientCamera(byte[] payload)
	{
		int senderId = Multiplayer.GetRemoteSenderId();
		var pos = MemoryPackSerializer.Deserialize<NetworkVector3>(payload);
		_networkService.RecordClientCameraPosition(senderId, pos.ToGodot());
	}

	[Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Unreliable)]
	public void ReceiveSnapshot(byte[] payload)
	{
		if (Multiplayer.IsServer()) return;
		_networkService.RecordSnapshotReceived(payload);

		foreach (var snap in _networkService.FlushPendingUnitSpawns())
		{
			SpawnUnitFromSnapshot(snap);
		}

		foreach (var localEntity in _networkService.FlushPendingUnitKills())
		{
			if (GameHost.TryGetUnit3D(localEntity, out var unit3D))
			{
				CallDeferred("KillUnitDeferred", unit3D);
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
		var snapshots = _networkService.BuildServerSnapshots(_localPeerId, AllUnits);
		foreach (var (peerId, payload) in snapshots)
		{
			QueueOrSendPacket(peerId, nameof(ReceiveSnapshot), payload);
		}
	}

	private void SpawnUnitFromSnapshot(UnitSnapshot snap)
	{
		var entity = _networkService.SpawnUnitFromSnapshot(snap, GetFallbackModelPath, out string modelPath, out bool isEnemy);
		if (entity == Entity.Null) return;
		SpawnUnit3D(entity, snap.UnitId, modelPath, snap.Position.ToGodot(), snap.IsBuilding, isEnemy);
	}

	private void QueueOrSendPacket(int peerId, string funcName, params object[] args)
	{
		if (_networkService.IsSpectatorWithDelay(peerId))
		{
			_networkService.QueueSpectatorDelayedPacket(peerId, funcName, args);
		}
		else
		{
			SendPacketImmediate(peerId, funcName, args);
		}
	}

	private void ProcessDelayedSpectatorPackets()
	{
		var readyPackets = _networkService.FlushReadySpectatorPackets();
		foreach (var packet in readyPackets)
		{
			SendPacketImmediate(packet.PeerId, packet.FunctionName, packet.Arguments);
		}
	}

	private void SendPacketImmediate(int peerId, string funcName, object[] args)
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
