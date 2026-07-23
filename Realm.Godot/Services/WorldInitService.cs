using Arch.Core;
using DotRecast.Detour;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using Realm.Ecs.Common;
using System;

public class WorldInitService
{
	private readonly WorldAccessor _ecsWorldAccessor;
	private World EcsWorld => _ecsWorldAccessor.Current;

	public WorldInitService(WorldAccessor ecsWorldAccessor)
	{
		_ecsWorldAccessor = ecsWorldAccessor;
	}

	private void AddOrSet<T>(Entity entity, T component)
	{
		if (EcsWorld.Has<T>(entity))
		{
			EcsWorld.Set(entity, component);
		}
		else
		{
			EcsWorld.Add(entity, component);
		}
	}

	public Entity SetupWorldEntityComponents(
		int width,
		int depth,
		float spacing,
		float cellSize,
		float waterHeight,
		bool waterEnabled,
		float[,] heights,
		int[,] pathingCodes,
		DtNavMesh navMesh,
		DtNavMeshQuery navMeshQuery)
	{
		Entity worldEntity = Entity.Null;
		var worldQuery = QueryCache.AllTerrainStateQuery;
		EcsWorld.Query(in worldQuery, entity => worldEntity = entity);

		if (worldEntity == Entity.Null)
		{
			worldEntity = EcsWorld.Create();
		}

		AddOrSet(worldEntity, new WorldState(0f, 0, 0f, true));
		AddOrSet(worldEntity, new ReplayState(0, 500f, 400f, 200f));
		AddOrSet(worldEntity, new NetworkState(1, 0f, 0, -1, -1, false, 0, 1));
		AddOrSet(worldEntity, new NetworkMappingState(new(), new(), new()));
		if (!EcsWorld.Has<EditorState>(worldEntity))
		{
			EcsWorld.Add(worldEntity, new EditorState(true, 4.0f, -95.0f, 95.0f, -95.0f, 125.0f, AssetResolver.ResolveSkybox("jade_shrine.png"), false));
		}
		AddOrSet(worldEntity, new InputState(0, null, null, null, false));
		AddOrSet(worldEntity, new VFXQueue(new System.Collections.Generic.List<VFXRequest>()));
		AddOrSet(worldEntity, new CameraState
		{
			MoveSpeed = 35.0f,
			ZoomSpeed = 10.0f,
			MinZoom = 10.0f,
			MaxZoom = 60.0f,
			ZoomStep = 4.0f,
			EdgePanMargin = 20.0f,
			EnableEdgePanning = true,
			IsLocked = false,
			LimitLeft = null,
			LimitRight = null,
			LimitTop = null,
			LimitBottom = null,
			TargetHeight = 35.0f,
			CurrentHeight = 35.0f,
			IsDraggingMouse = false,
			LastMousePosition = System.Numerics.Vector2.Zero,
			TargetYaw = 0.0f,
			CurrentYaw = 0.0f,
			TargetPitch = -55.0f,
			CurrentPitch = -55.0f,
			IsTopDown = false,
			YawSwing = 0.0f,
			PitchSwing = 0.0f
		});

		if (EcsWorld.Has<TerrainState>(worldEntity))
		{
			ref var existing = ref EcsWorld.Get<TerrainState>(worldEntity);
			if (heights != null) existing.Heights = heights;
			if (pathingCodes != null) existing.PathingCodes = pathingCodes;
			if (navMesh != null) existing.NavMesh = navMesh;
			if (navMeshQuery != null) existing.NavMeshQuery = navMeshQuery;
			EcsWorld.Set(worldEntity, existing);
		}
		else
		{
			EcsWorld.Add(worldEntity, new TerrainState(
				width, depth, spacing, cellSize, waterHeight, waterEnabled,
				heights ?? new float[width, depth], pathingCodes ?? new int[width, depth], navMesh, navMeshQuery
			));
		}

		AddOrSet(worldEntity, new FogAndWeatherState(new byte[32, 32], "grey", "clear", 0f));
		AddOrSet(worldEntity, new SpectatorPerspective(-1));
		AddOrSet(worldEntity, new CountdownState(false, 0f, ""));
		AddOrSet(worldEntity, new LeaderboardState(false, "", new System.Collections.Generic.Dictionary<string, string>()));
		AddOrSet(worldEntity, new ScriptZonesState(new System.Collections.Generic.List<ZoneBounds>()));

		var players = new ScriptPlayer[12];
		for (int i = 0; i < 12; i++)
		{
			players[i] = new ScriptPlayer
			{
				Gold = i == 0 ? 500f : 0f,
				Wood = i == 0 ? 400f : 0f,
				Active = i == 0,
				Name = $"Player {i + 1}",
				KillCount = 0
			};
		}
		AddOrSet(worldEntity, new ScriptPlayersState(players));
		AddOrSet(worldEntity, new CombatAlertState(0f));

		return worldEntity;
	}
}
