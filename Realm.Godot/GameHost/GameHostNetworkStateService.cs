using Arch.Core;
using Godot;
using Realm.Ecs.Components.Core;

public class GameHostNetworkStateService
{
    private readonly World _ecsWorld;

    public bool WasClientInMultiplayer { get; private set; } = false;
    public bool IsConnectionLost { get; private set; } = false;

    public GameHostNetworkStateService(World ecsWorld)
    {
        _ecsWorld = ecsWorld;
    }

    private Entity FindWorldEntity()
    {
        Entity worldEntity = Entity.Null;
        var query = new QueryDescription().WithAll<NetworkState>();
        _ecsWorld.Query(in query, (Entity entity) => worldEntity = entity);
        return worldEntity;
    }

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
        LastSnapshotReceivedTime = Time.GetTicksMsec();
    }

    public void UpdateConnectionStatus(bool multiplayerActive, bool isServer)
    {
        bool isLost;
        if (multiplayerActive && !isServer)
        {
            ulong now = Time.GetTicksMsec();
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
            isLost = true;
        }
        IsConnectionLost = isLost;
    }
}
