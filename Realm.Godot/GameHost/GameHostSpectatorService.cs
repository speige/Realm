using Arch.Core;
using Realm.Ecs.Components.Core;
using System;

public class GameHostSpectatorService
{
	private readonly World _ecsWorld;

	public GameHostSpectatorService(World ecsWorld)
	{
		_ecsWorld = ecsWorld;
	}

	private Entity FindWorldEntity()
	{
		Entity worldEntity = Entity.Null;
		var query = new QueryDescription().WithAll<NetworkMappingState>();
		_ecsWorld.Query(in query, (Entity entity) => worldEntity = entity);
		return worldEntity;
	}

	public int GetSpectatorPerspective()
	{
		var worldEntity = FindWorldEntity();
		if (worldEntity != Entity.Null && _ecsWorld.IsAlive(worldEntity) && _ecsWorld.Has<SpectatorPerspective>(worldEntity))
		{
			return _ecsWorld.Get<SpectatorPerspective>(worldEntity).Value;
		}
		return -1;
	}

	public void SetSpectatorPerspective(int value)
	{
		var worldEntity = FindWorldEntity();
		if (worldEntity != Entity.Null && _ecsWorld.IsAlive(worldEntity))
		{
			if (_ecsWorld.Has<SpectatorPerspective>(worldEntity))
			{
				_ecsWorld.Set(worldEntity, new SpectatorPerspective(value));
			}
			else
			{
				_ecsWorld.Add(worldEntity, new SpectatorPerspective(value));
			}
		}
	}
}
