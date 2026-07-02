using Arch.Core;
using Realm.Ecs.Services;
using Godot;
using Realm.Godot.ReplaySystem;
using Realm.Ecs.Components.Terrain;
using System;
using System.Collections.Generic;

public class FogOfWarService
{
	private readonly WorldAccessor _ecsWorldAccessor;
	private World _ecsWorld => _ecsWorldAccessor.Current;
	private MeshInstance3D _fogMeshInstance;
	private WorldEnvironment _worldEnv;
	private float _fogUpdateTimer = 0f;

	public FogOfWarService(WorldAccessor ecsWorldAccessor)
	{
		_ecsWorldAccessor = ecsWorldAccessor;
	}

	private Entity FindWorldEntity()
	{
		Entity worldEntity = Entity.Null;
		var query = new QueryDescription().WithAll<FogAndWeatherState>();
		_ecsWorld.Query(in query, (Entity entity) => worldEntity = entity);
		return worldEntity;
	}

	private byte[,] FogGrid
	{
		get
		{
			var worldEntity = FindWorldEntity();
			if (worldEntity != Entity.Null && _ecsWorld.Has<FogAndWeatherState>(worldEntity))
				return _ecsWorld.Get<FogAndWeatherState>(worldEntity).FogGrid;
			return new byte[32, 32];
		}
		set
		{
			var worldEntity = FindWorldEntity();
			if (worldEntity != Entity.Null && _ecsWorld.Has<FogAndWeatherState>(worldEntity))
			{
				ref var state = ref _ecsWorld.Get<FogAndWeatherState>(worldEntity);
				state.FogGrid = value;
			}
		}
	}

	private string FogOfWarType
	{
		get
		{
			var worldEntity = FindWorldEntity();
			if (worldEntity != Entity.Null && _ecsWorld.Has<FogAndWeatherState>(worldEntity))
				return _ecsWorld.Get<FogAndWeatherState>(worldEntity).FogOfWarType;
			return "grey";
		}
	}

	private float BaseFogDensity
	{
		get
		{
			var worldEntity = FindWorldEntity();
			if (worldEntity != Entity.Null && _ecsWorld.Has<FogAndWeatherState>(worldEntity))
				return _ecsWorld.Get<FogAndWeatherState>(worldEntity).BaseFogDensity;
			return 0f;
		}
	}

	public void Initialize(Node mainNode)
	{
		if (mainNode == null) return;

		_worldEnv = mainNode.GetTree()?.Root?.GetNodeOrNull<WorldEnvironment>("Main/WorldEnvironment");

		var fogMesh = new MeshInstance3D();
		fogMesh.Name = "3DFogMesh";

		var planeMesh = new PlaneMesh();
		planeMesh.Size = new Vector2(250f, 250f);
		fogMesh.Mesh = planeMesh;

		var shaderMaterial = new ShaderMaterial();
		var shader = new Shader();
		shader.Code = @"
			shader_type spatial;
			render_mode unshaded, depth_draw_never, cull_disabled;
			
			uniform sampler2D fog_texture : filter_linear;
			uniform vec4 shadow_color : source_color = vec4(0.0, 0.0, 0.0, 0.95);
			uniform vec4 black_color : source_color = vec4(0.0, 0.0, 0.0, 1.0);
			
			void fragment() {
				vec2 uv = UV;
				vec4 tex = texture(fog_texture, uv);
				float val = tex.r;
				
				if (val < 0.1) {
					ALBEDO = black_color.rgb;
					ALPHA = black_color.a;
				} else if (val < 0.6) {
					ALBEDO = shadow_color.rgb;
					ALPHA = shadow_color.a;
				} else {
					discard;
				}
			}
		";
		shaderMaterial.Shader = shader;

		fogMesh.MaterialOverride = shaderMaterial;
		mainNode.AddChild(fogMesh);
		fogMesh.GlobalPosition = new Vector3(0f, 0.35f, 15f);
		_fogMeshInstance = fogMesh;
	}

	public void Tick(float delta, List<Unit3D> allUnits, Camera3D camera3D, int spectatorPerspective, bool isPlayingReplay, bool isSpectator)
	{
		_fogUpdateTimer += delta;
		if (_fogUpdateTimer >= 0.1f)
		{
			_fogUpdateTimer = 0f;
			UpdateFogOfWar(allUnits, spectatorPerspective, isPlayingReplay, isSpectator);
		}

		float baseFogDensity = BaseFogDensity;
		if (baseFogDensity > 0f && camera3D != null && GodotObject.IsInstanceValid(camera3D))
		{
			if (_worldEnv != null && _worldEnv.Environment != null)
			{
				float height = camera3D.GlobalPosition.Y;
				float scale = 18.0f / Mathf.Max(8.0f, height);
				_worldEnv.Environment.FogDensity = baseFogDensity * scale;
			}
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

	private void UpdateFogOfWar(List<Unit3D> allUnits, int spectatorPerspective, bool isPlayingReplay, bool isSpectator)
	{
		if (GameHost.Instance == null || GameHost.Instance.GroundTerrain == null) return;

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
					float scanRadius = (_ecsWorld.IsAlive(unit.Entity) && _ecsWorld.Has<Realm.Ecs.Components.Combat.ScanRadius>(unit.Entity))
						? _ecsWorld.Get<Realm.Ecs.Components.Combat.ScanRadius>(unit.Entity).Value
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

				float scanRadius = (_ecsWorld.IsAlive(unit.Entity) && _ecsWorld.Has<Realm.Ecs.Components.Combat.ScanRadius>(unit.Entity))
					? _ecsWorld.Get<Realm.Ecs.Components.Combat.ScanRadius>(unit.Entity).Value
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

			Update3DFogMesh();
		}
	}

	private float GetFogValueAtVertex(int x, int z)
	{
		var fogGrid = FogGrid;
		float sum = 0f;
		int count = 0;
		for (int dx = -1; dx <= 0; dx++)
		{
			for (int dz = -1; dz <= 0; dz++)
			{
				int cx = x + dx;
				int cz = z + dz;
				if (cx >= 0 && cx < 32 && cz >= 0 && cz < 32)
				{
					byte val = fogGrid[cx, cz];
					float alpha = val switch
					{
						0 => 1.0f,
						1 => 0.48f,
						2 => 0.0f,
						_ => 1.0f
					};
					sum += alpha;
					count++;
				}
			}
		}
		return count > 0 ? (sum / count) : 1.0f;
	}

	private void Update3DFogMesh()
	{
		if (_fogMeshInstance == null || _fogMeshInstance.Mesh == null) return;
		var arrMesh = _fogMeshInstance.Mesh as ArrayMesh;
		if (arrMesh == null) return;

		float cellWidth = 250f / 32f;
		float cellHeight = 250f / 32f;

		var vertices = new Vector3[33 * 33];
		var colors = new Color[33 * 33];
		var indices = new int[32 * 32 * 6];

		for (int z = 0; z <= 32; z++)
		{
			for (int x = 0; x <= 32; x++)
			{
				int idx = x + z * 33;
				float wx = (x - 16f) * cellWidth;
				float wz = (z - 16f) * cellHeight;
				float h = GameHost.Instance != null ? GameHost.Instance.GetTerrainHeightAt(new Vector3(wx, 0f, wz)) : 0f;
				vertices[idx] = new Vector3(wx, h + 0.15f, wz);

				float alpha = GetFogValueAtVertex(x, z);
				colors[idx] = new Color(0f, 0f, 0f, alpha);
			}
		}

		int iIdx = 0;
		for (int z = 0; z < 32; z++)
		{
			for (int x = 0; x < 32; x++)
			{
				int topLeft = x + z * 33;
				int topRight = (x + 1) + z * 33;
				int bottomLeft = x + (z + 1) * 33;
				int bottomRight = (x + 1) + (z + 1) * 33;

				indices[iIdx++] = topLeft;
				indices[iIdx++] = topRight;
				indices[iIdx++] = bottomLeft;

				indices[iIdx++] = bottomLeft;
				indices[iIdx++] = topRight;
				indices[iIdx++] = bottomRight;
			}
		}

		arrMesh.ClearSurfaces();
		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices;
		arrays[(int)Mesh.ArrayType.Color] = colors;
		arrays[(int)Mesh.ArrayType.Index] = indices;
		arrMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
	}
}
