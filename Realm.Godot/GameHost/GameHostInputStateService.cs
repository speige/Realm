using Arch.Core;
using Realm.Ecs.Components.Core;
using System;

public class GameHostInputStateService
{
    private readonly World _ecsWorld;

    public GameHostInputStateService(World ecsWorld)
    {
        _ecsWorld = ecsWorld;
    }

    private Entity FindWorldEntity()
    {
        Entity worldEntity = Entity.Null;
        var query = new QueryDescription().WithAll<InputState>();
        _ecsWorld.Query(in query, (Entity entity) => worldEntity = entity);
        return worldEntity;
    }

    public string? ActiveSpellTargeting
    {
        get
        {
            var worldEntity = FindWorldEntity();
            return worldEntity != Entity.Null && _ecsWorld.Has<InputState>(worldEntity)
                ? _ecsWorld.Get<InputState>(worldEntity).ActiveSpellTargeting
                : null;
        }
        set
        {
            var worldEntity = FindWorldEntity();
            if (worldEntity == Entity.Null || !_ecsWorld.Has<InputState>(worldEntity)) return;
            ref var state = ref _ecsWorld.Get<InputState>(worldEntity);
            state.ActiveSpellTargeting = value;
        }
    }

    public string? ActiveCommandTargeting
    {
        get
        {
            var worldEntity = FindWorldEntity();
            return worldEntity != Entity.Null && _ecsWorld.Has<InputState>(worldEntity)
                ? _ecsWorld.Get<InputState>(worldEntity).ActiveCommandTargeting
                : null;
        }
        set
        {
            var worldEntity = FindWorldEntity();
            if (worldEntity == Entity.Null || !_ecsWorld.Has<InputState>(worldEntity)) return;
            ref var state = ref _ecsWorld.Get<InputState>(worldEntity);
            state.ActiveCommandTargeting = value;
        }
    }

    public string? ActiveBuildingPlacementType
    {
        get
        {
            var worldEntity = FindWorldEntity();
            return worldEntity != Entity.Null && _ecsWorld.Has<InputState>(worldEntity)
                ? _ecsWorld.Get<InputState>(worldEntity).ActiveBuildingPlacementType
                : null;
        }
        set
        {
            var worldEntity = FindWorldEntity();
            if (worldEntity == Entity.Null || !_ecsWorld.Has<InputState>(worldEntity)) return;
            ref var state = ref _ecsWorld.Get<InputState>(worldEntity);
            state.ActiveBuildingPlacementType = value;
        }
    }

    public bool ActivePingMode
    {
        get
        {
            var worldEntity = FindWorldEntity();
            return worldEntity != Entity.Null && _ecsWorld.Has<InputState>(worldEntity)
                && _ecsWorld.Get<InputState>(worldEntity).ActivePingMode;
        }
        set
        {
            var worldEntity = FindWorldEntity();
            if (worldEntity == Entity.Null || !_ecsWorld.Has<InputState>(worldEntity)) return;
            ref var state = ref _ecsWorld.Get<InputState>(worldEntity);
            state.ActivePingMode = value;
        }
    }

    public int GetCycleSelectionIndex(int selectedCount)
    {
        if (selectedCount == 0) return 0;
        var worldEntity = FindWorldEntity();
        if (worldEntity == Entity.Null || !_ecsWorld.Has<InputState>(worldEntity)) return 0;
        int val = _ecsWorld.Get<InputState>(worldEntity).CycleSelectionIndex;
        return Math.Clamp(val, 0, selectedCount - 1);
    }

    public void SetCycleSelectionIndex(int value, int selectedCount)
    {
        var worldEntity = FindWorldEntity();
        if (worldEntity == Entity.Null || !_ecsWorld.Has<InputState>(worldEntity)) return;
        ref var state = ref _ecsWorld.Get<InputState>(worldEntity);
        state.CycleSelectionIndex = selectedCount > 0 ? Math.Clamp(value, 0, selectedCount - 1) : 0;
    }
}
