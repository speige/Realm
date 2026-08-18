using Arch.Core;
using Realm.Ecs.Services;
using Godot;
using Realm.Godot.ReplaySystem;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Common;
using System;
using System.Collections.Generic;

public class ShroudService
{
	private readonly WorldAccessor _ecsWorldAccessor;
	private World EcsWorld => _ecsWorldAccessor.Current;
	private MeshInstance3D _shroudMeshInstance;
	private ShaderMaterial _shroudMeshMaterial;
	private float _shroudUpdateTimer;
	private Image _shroudImage;
	private ImageTexture _shroudTexture;
	private bool _isEditorShroudInitialized;

	public ShroudService(WorldAccessor ecsWorldAccessor)
	{
		_ecsWorldAccessor = ecsWorldAccessor;
	}

	private Entity _cachedWorldEntity = Entity.Null;

	private Entity FindWorldEntity()
	{
		if (_cachedWorldEntity != Entity.Null && EcsWorld.IsAlive(_cachedWorldEntity) && EcsWorld.Has<ShroudState>(_cachedWorldEntity))
		{
			return _cachedWorldEntity;
		}

		Entity worldEntity = Entity.Null;
		var query = QueryCache.AllShroudStateQuery;
		EcsWorld.Query(in query, (Entity entity) => worldEntity = entity);
		_cachedWorldEntity = worldEntity;
		return _cachedWorldEntity;
	}

	private byte[,] ShroudGrid
	{
		get
		{
			var worldEntity = FindWorldEntity();
			if (worldEntity != Entity.Null && EcsWorld.Has<ShroudState>(worldEntity))
				return EcsWorld.Get<ShroudState>(worldEntity).ShroudGrid;
			return new byte[32, 32];
		}
		set
		{
			var worldEntity = FindWorldEntity();
			if (worldEntity != Entity.Null && EcsWorld.Has<ShroudState>(worldEntity))
			{
				ref var state = ref EcsWorld.Get<ShroudState>(worldEntity);
				state.ShroudGrid = value;
			}
		}
	}

	private string ShroudType
	{
		get
		{
			var worldEntity = FindWorldEntity();
			if (worldEntity != Entity.Null && EcsWorld.Has<ShroudState>(worldEntity))
				return EcsWorld.Get<ShroudState>(worldEntity).ShroudType;
			return "VisionShroud";
		}
	}

	public void Initialize(Node mainNode)
	{
		if (mainNode == null) return;

		var existingShroud = mainNode.GetNodeOrNull<MeshInstance3D>("3DShroudMesh");
		if (GodotObject.IsInstanceValid(existingShroud))
		{
			mainNode.RemoveChild(existingShroud);
			existingShroud.QueueFree();
		}

		var existingFog = mainNode.GetNodeOrNull<MeshInstance3D>("3DFogMesh");
		if (GodotObject.IsInstanceValid(existingFog))
		{
			mainNode.RemoveChild(existingFog);
			existingFog.QueueFree();
		}

		var shroudMesh = new MeshInstance3D();
		shroudMesh.Name = "3DShroudMesh";
		shroudMesh.Mesh = new ArrayMesh();

		var shaderMaterial = new ShaderMaterial();
		var shader = new Shader();
		shader.Code = @"
shader_type spatial;
render_mode unshaded, depth_draw_never, cull_disabled, blend_mix;

uniform sampler2D shroud_texture : hint_default_white;
uniform vec2 shroud_world_min = vec2(-125.0, -125.0);
uniform vec2 shroud_world_size = vec2(250.0, 250.0);

varying vec3 v_world_pos;

void vertex() {
	v_world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	VERTEX += NORMAL * 0.05;
}

void fragment() {
	vec2 shroud_uv = (v_world_pos.xz - shroud_world_min) / shroud_world_size;
	float shroud_alpha = texture(shroud_texture, clamp(shroud_uv, 0.0, 1.0)).r;
	ALBEDO = vec3(0.0, 0.0, 0.0);
	ALPHA = shroud_alpha;
	if (shroud_alpha < 0.01) { discard; }
}
";
		shaderMaterial.Shader = shader;
		shroudMesh.MaterialOverride = shaderMaterial;
		mainNode.AddChild(shroudMesh);
		shroudMesh.GlobalPosition = new Vector3(0, 60.0f, 0);
		_shroudMeshInstance = shroudMesh;
		_shroudMeshMaterial = shaderMaterial;
	}

	public void Tick(float delta, List<Unit3D> allUnits, List<Prop3D> allProps, List<Decal> allDecals, int spectatorPerspective, bool isPlayingReplay, bool isSpectator)
	{
		if (GameHost.Instance != null && GameHost.Instance.IsMapEditorMode)
		{
			if (GodotObject.IsInstanceValid(_shroudMeshInstance))
			{
				_shroudMeshInstance.Visible = false;
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
			if (!_isEditorShroudInitialized && GameHost.Instance?.GroundTerrain != null)
			{
				_isEditorShroudInitialized = true;
				if (_shroudImage == null)
				{
					_shroudImage = Image.CreateEmpty(32, 32, false, Image.Format.Rf);
				}
				_shroudImage.Fill(new Color(0f, 0f, 0f, 1f));
				if (_shroudTexture == null)
				{
					_shroudTexture = ImageTexture.CreateFromImage(_shroudImage);
				}
				else
				{
					_shroudTexture.Update(_shroudImage);
				}
				GameHost.Instance.GroundTerrain.SetShroudTexture(_shroudTexture);
			}
			return;
		}

		_isEditorShroudInitialized = false;

		string shroudType = ShroudType;
		if (GodotObject.IsInstanceValid(_shroudMeshInstance))
		{
			bool shouldMeshBeVisible = !string.Equals(shroudType, "visible", StringComparison.OrdinalIgnoreCase) && !EditableTerrain.IsMinimapRendering;
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
			_shroudMeshInstance.Visible = shouldMeshBeVisible;
		}

		_shroudUpdateTimer += delta;
		if (_shroudUpdateTimer >= 0.1f)
		{
			_shroudUpdateTimer = 0f;
			UpdateShroud(allUnits, allProps, allDecals, spectatorPerspective, isPlayingReplay, isSpectator);
		}
	}

	public void TriggerImmediateUpdate()
	{
		if (GameHost.Instance != null)
		{
			int specPerspective = GameHost.Instance.SpectatorService?.GetSpectatorPerspective() ?? -1;
			UpdateShroud(GameHost.Instance.AllUnits, GameHost.Instance.AllProps, GameHost.Instance.AllDecals, specPerspective, ReplayPlaybackManager.Instance.IsPlayingReplay, LobbyManager.Instance != null && LobbyManager.Instance.LocalPlayer != null && LobbyManager.Instance.LocalPlayer.Team == "Spectator");
			InGameHUD.Instance?.QueueMinimapRedraw();
		}
	}

	public void CleanUp()
	{
		if (GodotObject.IsInstanceValid(_shroudMeshInstance))
		{
			_shroudMeshInstance.QueueFree();
			_shroudMeshInstance = null;
		}
	}

	private void UpdateShroud(List<Unit3D> allUnits, List<Prop3D> allProps, List<Decal> allDecals, int spectatorPerspective, bool isPlayingReplay, bool isSpectator)
	{
		if (GameHost.Instance == null) return;

		if (isPlayingReplay || isSpectator)
		{
			int targetOwnerId = isPlayingReplay
				? ReplayPlaybackManager.Instance.SpectatorPerspective
				: spectatorPerspective;

			if (targetOwnerId == -1)
			{
				var shroudGrid = ShroudGrid;
				for (int x = 0; x < 32; x++)
					for (int z = 0; z < 32; z++)
						shroudGrid[x, z] = ShroudState.Visible;
				ShroudGrid = shroudGrid;

				foreach (var unit in allUnits)
					if (unit != null && GodotObject.IsInstanceValid(unit))
						unit.Visible = true;

				foreach (var prop in allProps)
					if (prop != null && GodotObject.IsInstanceValid(prop))
						prop.Visible = true;

				foreach (var decal in allDecals)
					if (decal != null && GodotObject.IsInstanceValid(decal))
						decal.Visible = true;

				Update3DShroudMesh();
				return;
			}
			else
			{
				var shroudGrid = ShroudGrid;
				for (int x = 0; x < 32; x++)
				{
					for (int z = 0; z < 32; z++)
					{
						if (shroudGrid[x, z] == ShroudState.Visible)
							shroudGrid[x, z] = ShroudState.VisionShroud;
					}
				}

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
									shroudGrid[nx, nz] = ShroudState.Visible;
						}
					}
				}

				ShroudGrid = shroudGrid;

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
						unit.Visible = (ShroudGrid[gx, gz] == ShroudState.Visible);
					}
				}

				foreach (var prop in allProps)
				{
					if (prop == null || !GodotObject.IsInstanceValid(prop)) continue;
					Vector3 pos = prop.GlobalPosition;
					int gx = (int)Mathf.Clamp((pos.X / 250f + 0.5f) * 32, 0, 31);
					int gz = (int)Mathf.Clamp((pos.Z / 250f + 0.5f) * 32, 0, 31);
					prop.Visible = (ShroudGrid[gx, gz] != ShroudState.ExplorationShroud);
				}

				foreach (var decal in allDecals)
				{
					if (decal == null || !GodotObject.IsInstanceValid(decal)) continue;
					Vector3 pos = decal.GlobalPosition;
					int gx = (int)Mathf.Clamp((pos.X / 250f + 0.5f) * 32, 0, 31);
					int gz = (int)Mathf.Clamp((pos.Z / 250f + 0.5f) * 32, 0, 31);
					decal.Visible = (ShroudGrid[gx, gz] != ShroudState.ExplorationShroud);
				}

				Update3DShroudMesh();
				return;
			}
		}

		string currentShroudType = ShroudType;

		if (string.Equals(currentShroudType, "visible", StringComparison.OrdinalIgnoreCase))
		{
			var shroudGrid = ShroudGrid;
			for (int x = 0; x < 32; x++)
				for (int z = 0; z < 32; z++)
					shroudGrid[x, z] = ShroudState.Visible;
			ShroudGrid = shroudGrid;

			foreach (var unit in allUnits)
				if (unit != null && GodotObject.IsInstanceValid(unit))
					unit.Visible = true;

			foreach (var prop in allProps)
				if (prop != null && GodotObject.IsInstanceValid(prop))
					prop.Visible = true;

			foreach (var decal in allDecals)
				if (decal != null && GodotObject.IsInstanceValid(decal))
					decal.Visible = true;

			Update3DShroudMesh();
			return;
		}

		{
			var shroudGrid = ShroudGrid;
			for (int x = 0; x < 32; x++)
			{
				for (int z = 0; z < 32; z++)
				{
					if (shroudGrid[x, z] == ShroudState.Visible)
						shroudGrid[x, z] = ShroudState.VisionShroud;
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
								shroudGrid[nx, nz] = ShroudState.Visible;
					}
				}
			}

			ShroudGrid = shroudGrid;

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
				unit.Visible = (ShroudGrid[gx, gz] == ShroudState.Visible);
			}

			foreach (var prop in allProps)
			{
				if (prop == null || !GodotObject.IsInstanceValid(prop)) continue;
				Vector3 pos = prop.GlobalPosition;
				int gx = (int)Mathf.Clamp((pos.X / 250f + 0.5f) * 32, 0, 31);
				int gz = (int)Mathf.Clamp((pos.Z / 250f + 0.5f) * 32, 0, 31);
				prop.Visible = (ShroudGrid[gx, gz] != ShroudState.ExplorationShroud);
			}

			foreach (var decal in allDecals)
			{
				if (decal == null || !GodotObject.IsInstanceValid(decal)) continue;
				Vector3 pos = decal.GlobalPosition;
				int gx = (int)Mathf.Clamp((pos.X / 250f + 0.5f) * 32, 0, 31);
				int gz = (int)Mathf.Clamp((pos.Z / 250f + 0.5f) * 32, 0, 31);
				decal.Visible = (ShroudGrid[gx, gz] != ShroudState.ExplorationShroud);
			}

			Update3DShroudMesh();
		}
	}

	private void Update3DShroudMesh()
	{
		if (GodotObject.IsInstanceValid(_shroudMeshInstance) && GameHost.Instance?.GroundTerrain != null)
		{
			Vector2 targetSize = new Vector2(GameHost.Instance.GroundTerrain.Width * GameHost.Instance.GroundTerrain.QuadSize, GameHost.Instance.GroundTerrain.Depth * GameHost.Instance.GroundTerrain.QuadSize);
			if (_shroudMeshInstance.Mesh is PlaneMesh planeMesh)
			{
				if (planeMesh.Size != targetSize)
				{
					planeMesh.Size = targetSize;
				}
			}
			else
			{
				var plane = new PlaneMesh { Size = targetSize };
				_shroudMeshInstance.Mesh = plane;
			}
		}

		if (_shroudImage == null)
		{
			_shroudImage = Image.CreateEmpty(32, 32, false, Image.Format.Rf);
		}

		var shroudGrid = ShroudGrid;
		for (int gz = 0; gz < 32; gz++)
		{
			for (int gx = 0; gx < 32; gx++)
			{
				byte val = shroudGrid[gx, gz];
				float alpha = val switch
				{
					ShroudState.ExplorationShroud => 1.0f,
					ShroudState.VisionShroud => 0.48f,
					ShroudState.Visible => 0.0f,
					_ => 1.0f
				};
				_shroudImage.SetPixel(gx, gz, new Color(alpha, alpha, alpha, 1f));
			}
		}

		if (_shroudTexture == null)
		{
			_shroudTexture = ImageTexture.CreateFromImage(_shroudImage);
		}
		else
		{
			_shroudTexture.Update(_shroudImage);
		}

		if (GameHost.Instance?.GroundTerrain != null)
		{
			GameHost.Instance.GroundTerrain.SetShroudTexture(_shroudTexture);
		}

		_shroudMeshMaterial?.SetShaderParameter("shroud_texture", _shroudTexture);
	}
}
