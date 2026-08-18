using Arch.Core;
using Realm.Ecs.Services;
using Godot;
using Realm.Godot.ReplaySystem;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Common;
using System;
using System.Collections.Generic;

public class FogOfWarService
{
	private readonly WorldAccessor _ecsWorldAccessor;
	private World EcsWorld => _ecsWorldAccessor.Current;
	private MeshInstance3D _fogMeshInstance;
	private ShaderMaterial _fogMeshMaterial;
	private WorldEnvironment _worldEnv;
	private float _fogUpdateTimer;
	private Image _fogImage;
	private ImageTexture _fogTexture;
	private bool _isEditorFogInitialized;

	public FogOfWarService(WorldAccessor ecsWorldAccessor)
	{
		_ecsWorldAccessor = ecsWorldAccessor;
	}

	private Entity _cachedWorldEntity = Entity.Null;

	private Entity FindWorldEntity()
	{
		if (_cachedWorldEntity != Entity.Null && EcsWorld.IsAlive(_cachedWorldEntity) && EcsWorld.Has<FogAndWeatherState>(_cachedWorldEntity))
		{
			return _cachedWorldEntity;
		}

		Entity worldEntity = Entity.Null;
		var query = QueryCache.AllFogAndWeatherStateQuery;
		EcsWorld.Query(in query, (Entity entity) => worldEntity = entity);
		_cachedWorldEntity = worldEntity;
		return _cachedWorldEntity;
	}

	private byte[,] FogGrid
	{
		get
		{
			var worldEntity = FindWorldEntity();
			if (worldEntity != Entity.Null && EcsWorld.Has<FogAndWeatherState>(worldEntity))
				return EcsWorld.Get<FogAndWeatherState>(worldEntity).FogGrid;
			return new byte[32, 32];
		}
		set
		{
			var worldEntity = FindWorldEntity();
			if (worldEntity != Entity.Null && EcsWorld.Has<FogAndWeatherState>(worldEntity))
			{
				ref var state = ref EcsWorld.Get<FogAndWeatherState>(worldEntity);
				state.FogGrid = value;
			}
		}
	}

	private string FogOfWarType
	{
		get
		{
			var worldEntity = FindWorldEntity();
			if (worldEntity != Entity.Null && EcsWorld.Has<FogAndWeatherState>(worldEntity))
				return EcsWorld.Get<FogAndWeatherState>(worldEntity).FogOfWarType;
			return "grey";
		}
	}

	private float BaseFogDensity
	{
		get
		{
			var worldEntity = FindWorldEntity();
			if (worldEntity != Entity.Null && EcsWorld.Has<FogAndWeatherState>(worldEntity))
				return EcsWorld.Get<FogAndWeatherState>(worldEntity).BaseFogDensity;
			return 0f;
		}
	}

	public void Initialize(Node mainNode)
	{
		if (mainNode == null) return;

		_worldEnv = mainNode.GetTree()?.Root?.GetNodeOrNull<WorldEnvironment>("Main/WorldEnvironment");

		// Clean up any existing duplicate fog mesh immediately to avoid name conflicts
		var existing = mainNode.GetNodeOrNull<MeshInstance3D>("3DFogMesh");
		if (GodotObject.IsInstanceValid(existing))
		{
			mainNode.RemoveChild(existing);
			existing.QueueFree();
		}

		var fogMesh = new MeshInstance3D();
		fogMesh.Name = "3DFogMesh";
		fogMesh.Mesh = new ArrayMesh();

		var shaderMaterial = new ShaderMaterial();
		var shader = new Shader();
		shader.Code = @"
shader_type spatial;
render_mode unshaded, depth_draw_never, cull_disabled, blend_mix;

uniform sampler2D fog_texture : hint_default_white;
uniform vec2 fog_world_min = vec2(-125.0, -125.0);
uniform vec2 fog_world_size = vec2(250.0, 250.0);

varying vec3 v_world_pos;

void vertex() {
	v_world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	VERTEX += NORMAL * 0.05;
}

void fragment() {
	vec2 fog_uv = (v_world_pos.xz - fog_world_min) / fog_world_size;
	float fog_alpha = texture(fog_texture, clamp(fog_uv, 0.0, 1.0)).r;
	ALBEDO = vec3(0.0, 0.0, 0.0);
	ALPHA = fog_alpha;
	if (fog_alpha < 0.01) { discard; }
}
";
		shaderMaterial.Shader = shader;
		fogMesh.MaterialOverride = shaderMaterial;
		mainNode.AddChild(fogMesh);
		fogMesh.GlobalPosition = new Vector3(0, 60.0f, 0);
		_fogMeshInstance = fogMesh;
		_fogMeshMaterial = shaderMaterial;
	}

	public void Tick(float delta, List<Unit3D> allUnits, List<Prop3D> allProps, List<Decal> allDecals, Camera3D camera3D, int spectatorPerspective, bool isPlayingReplay, bool isSpectator)
	{
		if (GameHost.Instance != null && GameHost.Instance.IsMapEditorMode)
		{
			if (GodotObject.IsInstanceValid(_fogMeshInstance))
			{
				_fogMeshInstance.Visible = false;
			}
			if (_worldEnv != null && _worldEnv.Environment != null)
			{
				_worldEnv.Environment.FogEnabled = false;
			}
			foreach (var unit in allUnits)
			{
				if (unit != null && GodotObject.IsInstanceValid(unit))
				{
					unit.Visible = true;
				}
			}
			foreach (var prop in allProps)
			{
				if (prop != null && GodotObject.IsInstanceValid(prop))
				{
					prop.Visible = true;
				}
			}
			foreach (var decal in allDecals)
			{
				if (decal != null && GodotObject.IsInstanceValid(decal))
				{
					decal.Visible = true;
				}
			}
			if (!_isEditorFogInitialized && GameHost.Instance?.GroundTerrain != null)
			{
				_isEditorFogInitialized = true;
				if (_fogImage == null)
				{
					_fogImage = Image.CreateEmpty(32, 32, false, Image.Format.Rf);
				}
				_fogImage.Fill(new Color(0f, 0f, 0f, 1f));
				if (_fogTexture == null)
				{
					_fogTexture = ImageTexture.CreateFromImage(_fogImage);
				}
				else
				{
					_fogTexture.Update(_fogImage);
				}
				GameHost.Instance.GroundTerrain.SetFogTexture(_fogTexture);
			}
			return;
		}

		_isEditorFogInitialized = false;

		string fogOfWarType = FogOfWarType;
		if (GodotObject.IsInstanceValid(_fogMeshInstance))
		{
			bool shouldMeshBeVisible = (fogOfWarType != "visible");
			if (shouldMeshBeVisible && (isPlayingReplay || isSpectator))
			{
				int targetOwnerId = isPlayingReplay
					? ReplayPlaybackManager.Instance.SpectatorPerspective
					: spectatorPerspective;
				if (targetOwnerId == -1)
				{
					shouldMeshBeVisible = false;
				}
			}
			_fogMeshInstance.Visible = shouldMeshBeVisible;
		}

		_fogUpdateTimer += delta;
		if (_fogUpdateTimer >= 0.1f)
		{
			_fogUpdateTimer = 0f;
			UpdateFogOfWar(allUnits, allProps, allDecals, spectatorPerspective, isPlayingReplay, isSpectator);
		}

		float baseFogDensity = BaseFogDensity;
		if (GameSettings.QualityIdx > GraphicsQuality.Low && baseFogDensity > 0f && camera3D != null && GodotObject.IsInstanceValid(camera3D))
		{
			if (_worldEnv != null && _worldEnv.Environment != null)
			{
				_worldEnv.Environment.FogEnabled = true;
				float height = camera3D.GlobalPosition.Y;
				float scale = 18.0f / Mathf.Max(8.0f, height);
				_worldEnv.Environment.FogDensity = baseFogDensity * scale;
			}
		}
		else
		{
			if (_worldEnv != null && _worldEnv.Environment != null)
			{
				_worldEnv.Environment.FogEnabled = false;
			}
		}
	}

	public void TriggerImmediateUpdate()
	{
		if (GameHost.Instance != null)
		{
			int specPerspective = GameHost.Instance.SpectatorService?.GetSpectatorPerspective() ?? -1;
			UpdateFogOfWar(GameHost.Instance.AllUnits, GameHost.Instance.AllProps, GameHost.Instance.AllDecals, specPerspective, ReplayPlaybackManager.Instance.IsPlayingReplay, LobbyManager.Instance != null && LobbyManager.Instance.LocalPlayer != null && LobbyManager.Instance.LocalPlayer.Team == "Spectator");
			InGameHUD.Instance?.QueueMinimapRedraw();
		}
	}

	public void CleanUp()
	{
		if (GodotObject.IsInstanceValid(_fogMeshInstance))
		{
			_fogMeshInstance.QueueFree();
			_fogMeshInstance = null;
		}
	}

	private void UpdateFogOfWar(List<Unit3D> allUnits, List<Prop3D> allProps, List<Decal> allDecals, int spectatorPerspective, bool isPlayingReplay, bool isSpectator)
	{
		if (GameHost.Instance == null) return;

		if (isPlayingReplay || isSpectator)
		{
			int targetOwnerId = isPlayingReplay
				? ReplayPlaybackManager.Instance.SpectatorPerspective
				: spectatorPerspective;

			if (targetOwnerId == -1)
			{
				var fogGrid = FogGrid;
				for (int x = 0; x < 32; x++)
					for (int z = 0; z < 32; z++)
						fogGrid[x, z] = 2;
				FogGrid = fogGrid;

				foreach (var unit in allUnits)
					if (unit != null && GodotObject.IsInstanceValid(unit))
						unit.Visible = true;

				foreach (var prop in allProps)
					if (prop != null && GodotObject.IsInstanceValid(prop))
						prop.Visible = true;

				foreach (var decal in allDecals)
					if (decal != null && GodotObject.IsInstanceValid(decal))
						decal.Visible = true;

				Update3DFogMesh();
				return;
			}
			else
			{
				var fogGrid = FogGrid;
				for (int x = 0; x < 32; x++)
					for (int z = 0; z < 32; z++)
						fogGrid[x, z] = 0;

				foreach (var unit in allUnits)
				{
					if (unit == null || !GodotObject.IsInstanceValid(unit)) continue;
					int ownerId = GameHost.Instance.GetOwnerPeerId(unit.Entity);
					if (ownerId != targetOwnerId) continue;
					Vector3 pos = unit.GlobalPosition;
					int gx = (int)Mathf.Clamp((pos.X / 250f + 0.5f) * 32, 0, 31);
					int gz = (int)Mathf.Clamp((pos.Z / 250f + 0.5f) * 32, 0, 31);
					float scanRadius = (EcsWorld.IsAlive(unit.Entity) && EcsWorld.Has<Realm.Ecs.Components.Combat.ScanRadius>(unit.Entity))
						? EcsWorld.Get<Realm.Ecs.Components.Combat.ScanRadius>(unit.Entity).Value
						: 15.0f;
					int rGrid = (int)Math.Max(1, Math.Ceiling(scanRadius / (250f / 32f)));
					for (int dx = -rGrid; dx <= rGrid; dx++)
					{
						for (int dz = -rGrid; dz <= rGrid; dz++)
						{
							int nx = gx + dx;
							int nz = gz + dz;
							if (nx >= 0 && nx < 32 && nz >= 0 && nz < 32)
								if (dx * dx + dz * dz <= rGrid * rGrid)
									fogGrid[nx, nz] = 2;
						}
					}
				}

				FogGrid = fogGrid;

				foreach (var unit in allUnits)
				{
					if (unit == null || !GodotObject.IsInstanceValid(unit)) continue;
					int ownerId = GameHost.Instance.GetOwnerPeerId(unit.Entity);
					if (ownerId == targetOwnerId)
					{
						unit.Visible = true;
					}
					else
					{
						Vector3 pos = unit.GlobalPosition;
						int gx = (int)Mathf.Clamp((pos.X / 250f + 0.5f) * 32, 0, 31);
						int gz = (int)Mathf.Clamp((pos.Z / 250f + 0.5f) * 32, 0, 31);
						unit.Visible = (FogGrid[gx, gz] == 2);
					}
				}

				foreach (var prop in allProps)
				{
					if (prop == null || !GodotObject.IsInstanceValid(prop)) continue;
					Vector3 pos = prop.GlobalPosition;
					int gx = (int)Mathf.Clamp((pos.X / 250f + 0.5f) * 32, 0, 31);
					int gz = (int)Mathf.Clamp((pos.Z / 250f + 0.5f) * 32, 0, 31);
					prop.Visible = (FogGrid[gx, gz] != 0);
				}

				foreach (var decal in allDecals)
				{
					if (decal == null || !GodotObject.IsInstanceValid(decal)) continue;
					Vector3 pos = decal.GlobalPosition;
					int gx = (int)Mathf.Clamp((pos.X / 250f + 0.5f) * 32, 0, 31);
					int gz = (int)Mathf.Clamp((pos.Z / 250f + 0.5f) * 32, 0, 31);
					decal.Visible = (FogGrid[gx, gz] != 0);
				}

				Update3DFogMesh();
				return;
			}
		}

		string fogOfWarType = FogOfWarType;

		if (fogOfWarType == "visible")
		{
			var fogGrid = FogGrid;
			for (int x = 0; x < 32; x++)
				for (int z = 0; z < 32; z++)
					fogGrid[x, z] = 2;
			FogGrid = fogGrid;

			foreach (var unit in allUnits)
				if (unit != null && GodotObject.IsInstanceValid(unit))
					unit.Visible = true;

			foreach (var prop in allProps)
				if (prop != null && GodotObject.IsInstanceValid(prop))
					prop.Visible = true;

			foreach (var decal in allDecals)
				if (decal != null && GodotObject.IsInstanceValid(decal))
					decal.Visible = true;

			Update3DFogMesh();
			return;
		}

		{
			var fogGrid = FogGrid;
			for (int x = 0; x < 32; x++)
			{
				for (int z = 0; z < 32; z++)
				{
					if (fogOfWarType == "black")
						fogGrid[x, z] = 0;
					else if (fogGrid[x, z] == 2)
						fogGrid[x, z] = 1;
				}
			}

			foreach (var unit in allUnits)
			{
				if (unit == null || !GodotObject.IsInstanceValid(unit)) continue;
				if (unit.IsEnemy) continue;

				Vector3 pos = unit.GlobalPosition;
				int gx = (int)Mathf.Clamp((pos.X / 250f + 0.5f) * 32, 0, 31);
				int gz = (int)Mathf.Clamp((pos.Z / 250f + 0.5f) * 32, 0, 31);

				float scanRadius = (EcsWorld.IsAlive(unit.Entity) && EcsWorld.Has<Realm.Ecs.Components.Combat.ScanRadius>(unit.Entity))
					? EcsWorld.Get<Realm.Ecs.Components.Combat.ScanRadius>(unit.Entity).Value
					: 15.0f;

				int rGrid = (int)Math.Max(1, Math.Ceiling(scanRadius / (250f / 32f)));
				for (int dx = -rGrid; dx <= rGrid; dx++)
				{
					for (int dz = -rGrid; dz <= rGrid; dz++)
					{
						int nx = gx + dx;
						int nz = gz + dz;
						if (nx >= 0 && nx < 32 && nz >= 0 && nz < 32)
							if (dx * dx + dz * dz <= rGrid * rGrid)
								fogGrid[nx, nz] = 2;
					}
				}
			}

			FogGrid = fogGrid;

			foreach (var unit in allUnits)
			{
				if (unit == null || !GodotObject.IsInstanceValid(unit)) continue;
				if (!unit.IsEnemy)
				{
					unit.Visible = true;
					continue;
				}

				Vector3 pos = unit.GlobalPosition;
				int gx = (int)Mathf.Clamp((pos.X / 250f + 0.5f) * 32, 0, 31);
				int gz = (int)Mathf.Clamp((pos.Z / 250f + 0.5f) * 32, 0, 31);
				unit.Visible = (FogGrid[gx, gz] == 2);
			}

			foreach (var prop in allProps)
			{
				if (prop == null || !GodotObject.IsInstanceValid(prop)) continue;
				Vector3 pos = prop.GlobalPosition;
				int gx = (int)Mathf.Clamp((pos.X / 250f + 0.5f) * 32, 0, 31);
				int gz = (int)Mathf.Clamp((pos.Z / 250f + 0.5f) * 32, 0, 31);
				prop.Visible = (FogGrid[gx, gz] != 0);
			}

			foreach (var decal in allDecals)
			{
				if (decal == null || !GodotObject.IsInstanceValid(decal)) continue;
				Vector3 pos = decal.GlobalPosition;
				int gx = (int)Mathf.Clamp((pos.X / 250f + 0.5f) * 32, 0, 31);
				int gz = (int)Mathf.Clamp((pos.Z / 250f + 0.5f) * 32, 0, 31);
				decal.Visible = (FogGrid[gx, gz] != 0);
			}

			Update3DFogMesh();
		}
	}

	private void Update3DFogMesh()
	{
		if (GodotObject.IsInstanceValid(_fogMeshInstance) && GameHost.Instance?.GroundTerrain != null)
		{
			Vector2 targetSize = new Vector2(GameHost.Instance.GroundTerrain.Width * GameHost.Instance.GroundTerrain.QuadSize, GameHost.Instance.GroundTerrain.Depth * GameHost.Instance.GroundTerrain.QuadSize);
			if (_fogMeshInstance.Mesh is PlaneMesh planeMesh)
			{
				if (planeMesh.Size != targetSize)
				{
					planeMesh.Size = targetSize;
				}
			}
			else
			{
				var plane = new PlaneMesh { Size = targetSize };
				_fogMeshInstance.Mesh = plane;
			}
		}

		if (_fogImage == null)
		{
			_fogImage = Image.CreateEmpty(32, 32, false, Image.Format.Rf);
		}

		var fogGrid = FogGrid;
		for (int gz = 0; gz < 32; gz++)
		{
			for (int gx = 0; gx < 32; gx++)
			{
				byte val = fogGrid[gx, gz];
				float alpha = val switch
				{
					0 => 1.0f,
					1 => 0.48f,
					2 => 0.0f,
					_ => 1.0f
				};
				_fogImage.SetPixel(gx, gz, new Color(alpha, alpha, alpha, 1f));
			}
		}

		if (_fogTexture == null)
		{
			_fogTexture = ImageTexture.CreateFromImage(_fogImage);
		}
		else
		{
			_fogTexture.Update(_fogImage);
		}

		if (GameHost.Instance?.GroundTerrain != null)
		{
			GameHost.Instance.GroundTerrain.SetFogTexture(_fogTexture);
		}

		_fogMeshMaterial?.SetShaderParameter("fog_texture", _fogTexture);
	}
}
