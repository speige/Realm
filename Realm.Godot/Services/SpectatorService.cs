using Arch.Core;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Services;
using System;

public class SpectatorService
{
	private readonly WorldAccessor _ecsWorldAccessor;
	private World _ecsWorld => _ecsWorldAccessor.Current;

	public SpectatorService(WorldAccessor ecsWorldAccessor)
	{
		_ecsWorldAccessor = ecsWorldAccessor;
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
