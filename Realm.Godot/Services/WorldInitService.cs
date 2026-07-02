using Arch.Core;
using DotRecast.Detour;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Services;
using System;

public class WorldInitService
{
	private readonly WorldAccessor _ecsWorldAccessor;
	private World _ecsWorld => _ecsWorldAccessor.Current;

	public WorldInitService(WorldAccessor ecsWorldAccessor)
	{
		_ecsWorldAccessor = ecsWorldAccessor;
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
		var worldEntity = _ecsWorld.Create();
		_ecsWorld.Add(worldEntity, new WorldState(0f, 0, 0f, true));
		_ecsWorld.Add(worldEntity, new ReplayState(0, 500f, 400f, 200f));
		_ecsWorld.Add(worldEntity, new NetworkState(1, 0f, 0, -1, -1, false, 0, 1));
		_ecsWorld.Add(worldEntity, new NetworkMappingState(new(), new(), new()));
		_ecsWorld.Add(worldEntity, new EditorState(true, 4.0f, -95.0f, 95.0f, -95.0f, 125.0f, "res://Assets/skybox_panoramic.jpg", false));
		_ecsWorld.Add(worldEntity, new InputState(0, null, null, null, false));
		_ecsWorld.Add(worldEntity, new CameraState
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

		_ecsWorld.Add(worldEntity, new TerrainState(
			width, depth, spacing, cellSize, waterHeight, waterEnabled,
			heights, pathingCodes, navMesh, navMeshQuery
		));

		_ecsWorld.Add(worldEntity, new FogAndWeatherState(new byte[32, 32], "grey", "clear", 0f));
		_ecsWorld.Add(worldEntity, new SpectatorPerspective(-1));
		_ecsWorld.Add(worldEntity, new CountdownState(false, 0f, ""));
		_ecsWorld.Add(worldEntity, new LeaderboardState(false, "", new System.Collections.Generic.Dictionary<string, string>()));
		_ecsWorld.Add(worldEntity, new ScriptZonesState(new System.Collections.Generic.List<ZoneBounds>()));

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
		_ecsWorld.Add(worldEntity, new ScriptPlayersState(players));
		_ecsWorld.Add(worldEntity, new CombatAlertState(0f));

		return worldEntity;
	}
}
