using Arch.Core;
using Realm.Ecs.Components.Terrain;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Recast;
using DotRecast.Recast.Geom;
using Godot;
using System;
using System.Collections.Generic;

public partial class EditableTerrain : StaticBody3D
{
	private TerrainState GetTerrainStateSafe()
	{
		if (GameHost.Instance != null && 
			GameHost.Instance.EcsWorld != null && 
			GameHost.Instance.EcsWorld.IsAlive(GameHost.Instance.WorldEntity) && 
			GameHost.Instance.EcsWorld.Has<TerrainState>(GameHost.Instance.WorldEntity))
		{
			return GameHost.Instance.EcsWorld.Get<TerrainState>(GameHost.Instance.WorldEntity);
		}
		return new TerrainState(126, 126, 2.0f, 0.2f, -2.0f, true, null, null, null, null);
	}

	public float CellSize
	{
		get => GetTerrainStateSafe().CellSize;
		private set
		{
			ref var state = ref GameHost.Instance.EcsWorld.Get<TerrainState>(GameHost.Instance.WorldEntity);
			GameHost.Instance.EcsWorld.Set(GameHost.Instance.WorldEntity, new TerrainState(
				state.Width, state.Depth, state.Spacing, value, state.WaterHeight, state.WaterEnabled,
				state.Heights, state.PathingCodes, state.NavMesh, state.NavMeshQuery
			));
		}
	}

	public DtNavMesh NavMesh
	{
		get => GetTerrainStateSafe().NavMesh;
		private set
		{
			ref var state = ref GameHost.Instance.EcsWorld.Get<TerrainState>(GameHost.Instance.WorldEntity);
			GameHost.Instance.EcsWorld.Set(GameHost.Instance.WorldEntity, new TerrainState(
				state.Width, state.Depth, state.Spacing, state.CellSize, state.WaterHeight, state.WaterEnabled,
				state.Heights, state.PathingCodes, value, state.NavMeshQuery
			));
		}
	}

	public DtNavMeshQuery NavMeshQuery
	{
		get => GetTerrainStateSafe().NavMeshQuery;
		private set
		{
			ref var state = ref GameHost.Instance.EcsWorld.Get<TerrainState>(GameHost.Instance.WorldEntity);
			GameHost.Instance.EcsWorld.Set(GameHost.Instance.WorldEntity, new TerrainState(
				state.Width, state.Depth, state.Spacing, state.CellSize, state.WaterHeight, state.WaterEnabled,
				state.Heights, state.PathingCodes, state.NavMesh, value
			));
		}
	}

	public int Width
	{
		get => GetTerrainStateSafe().Width;
		private set
		{
			ref var state = ref GameHost.Instance.EcsWorld.Get<TerrainState>(GameHost.Instance.WorldEntity);
			GameHost.Instance.EcsWorld.Set(GameHost.Instance.WorldEntity, new TerrainState(
				value, state.Depth, state.Spacing, state.CellSize, state.WaterHeight, state.WaterEnabled,
				state.Heights, state.PathingCodes, state.NavMesh, state.NavMeshQuery
			));
		}
	}

	public int Depth
	{
		get => GetTerrainStateSafe().Depth;
		private set
		{
			ref var state = ref GameHost.Instance.EcsWorld.Get<TerrainState>(GameHost.Instance.WorldEntity);
			GameHost.Instance.EcsWorld.Set(GameHost.Instance.WorldEntity, new TerrainState(
				state.Width, value, state.Spacing, state.CellSize, state.WaterHeight, state.WaterEnabled,
				state.Heights, state.PathingCodes, state.NavMesh, state.NavMeshQuery
			));
		}
	}

	public float Spacing
	{
		get => GetTerrainStateSafe().Spacing;
		private set
		{
			ref var state = ref GameHost.Instance.EcsWorld.Get<TerrainState>(GameHost.Instance.WorldEntity);
			GameHost.Instance.EcsWorld.Set(GameHost.Instance.WorldEntity, new TerrainState(
				state.Width, state.Depth, value, state.CellSize, state.WaterHeight, state.WaterEnabled,
				state.Heights, state.PathingCodes, state.NavMesh, state.NavMeshQuery
			));
		}
	}

	public const int PATHING_SHALLOW_WATER = 1;
	public const int PATHING_DEEP_WATER = 2;
	public const int PATHING_FLYING = 4;
	public const int PATHING_GROUND = 8;
	public const int PATHING_UNPATHABLE = 16;

	private float[,] _localHeights;
	private int[,] _localPathingCodes;
	private float _localWaterHeight = -2.0f;
	private bool _localWaterEnabled = true;

	public float[,] Heights
	{
		get
		{
			if (GameHost.Instance != null && 
				GameHost.Instance.EcsWorld != null && 
				GameHost.Instance.EcsWorld.IsAlive(GameHost.Instance.WorldEntity) && 
				GameHost.Instance.EcsWorld.Has<TerrainState>(GameHost.Instance.WorldEntity))
			{
				return GameHost.Instance.EcsWorld.Get<TerrainState>(GameHost.Instance.WorldEntity).Heights;
			}
			return _localHeights;
		}
		private set
		{
			if (GameHost.Instance != null && 
				GameHost.Instance.EcsWorld != null && 
				GameHost.Instance.EcsWorld.IsAlive(GameHost.Instance.WorldEntity) && 
				GameHost.Instance.EcsWorld.Has<TerrainState>(GameHost.Instance.WorldEntity))
			{
				ref var state = ref GameHost.Instance.EcsWorld.Get<TerrainState>(GameHost.Instance.WorldEntity);
				GameHost.Instance.EcsWorld.Set(GameHost.Instance.WorldEntity, new TerrainState(
					state.Width, state.Depth, state.Spacing, state.CellSize, state.WaterHeight, state.WaterEnabled,
					value, state.PathingCodes, state.NavMesh, state.NavMeshQuery
				));
			}
			_localHeights = value;
		}
	}

	public Color[,] Colors { get; private set; }

	public int[,] PathingCodes
	{
		get
		{
			if (GameHost.Instance != null && 
				GameHost.Instance.EcsWorld != null && 
				GameHost.Instance.EcsWorld.IsAlive(GameHost.Instance.WorldEntity) && 
				GameHost.Instance.EcsWorld.Has<TerrainState>(GameHost.Instance.WorldEntity))
			{
				return GameHost.Instance.EcsWorld.Get<TerrainState>(GameHost.Instance.WorldEntity).PathingCodes;
			}
			return _localPathingCodes;
		}
		private set
		{
			if (GameHost.Instance != null && 
				GameHost.Instance.EcsWorld != null && 
				GameHost.Instance.EcsWorld.IsAlive(GameHost.Instance.WorldEntity) && 
				GameHost.Instance.EcsWorld.Has<TerrainState>(GameHost.Instance.WorldEntity))
			{
				ref var state = ref GameHost.Instance.EcsWorld.Get<TerrainState>(GameHost.Instance.WorldEntity);
				GameHost.Instance.EcsWorld.Set(GameHost.Instance.WorldEntity, new TerrainState(
					state.Width, state.Depth, state.Spacing, state.CellSize, state.WaterHeight, state.WaterEnabled,
					state.Heights, value, state.NavMesh, state.NavMeshQuery
				));
			}
			_localPathingCodes = value;
		}
	}

	private MeshInstance3D _meshInstance;
	private CollisionShape3D _collisionShape;
	private ArrayMesh _arrayMesh;
	private ShaderMaterial _material;

	private MeshInstance3D _waterMesh;

	private Vector3[] _verticesCache;
	private Color[] _colorsCache;
	private Vector3[] _normalsCache;
	private Vector2[] _uvsCache;
	private int[] _indicesCache;
	private float[] _mapDataCache;

	public float WaterHeight
	{
		get
		{
			if (GameHost.Instance != null && 
				GameHost.Instance.EcsWorld != null && 
				GameHost.Instance.EcsWorld.IsAlive(GameHost.Instance.WorldEntity) && 
				GameHost.Instance.EcsWorld.Has<TerrainState>(GameHost.Instance.WorldEntity))
			{
				return GameHost.Instance.EcsWorld.Get<TerrainState>(GameHost.Instance.WorldEntity).WaterHeight;
			}
			return _localWaterHeight;
		}
		set
		{
			if (GameHost.Instance != null && 
				GameHost.Instance.EcsWorld != null && 
				GameHost.Instance.EcsWorld.IsAlive(GameHost.Instance.WorldEntity) && 
				GameHost.Instance.EcsWorld.Has<TerrainState>(GameHost.Instance.WorldEntity))
			{
				ref var state = ref GameHost.Instance.EcsWorld.Get<TerrainState>(GameHost.Instance.WorldEntity);
				GameHost.Instance.EcsWorld.Set(GameHost.Instance.WorldEntity, new TerrainState(
					state.Width, state.Depth, state.Spacing, state.CellSize, value, state.WaterEnabled,
					state.Heights, state.PathingCodes, state.NavMesh, state.NavMeshQuery
				));
			}
			_localWaterHeight = value;
			UpdateWaterTransform();
		}
	}

	public bool WaterEnabled
	{
		get
		{
			if (GameHost.Instance != null && 
				GameHost.Instance.EcsWorld != null && 
				GameHost.Instance.EcsWorld.IsAlive(GameHost.Instance.WorldEntity) && 
				GameHost.Instance.EcsWorld.Has<TerrainState>(GameHost.Instance.WorldEntity))
			{
				return GameHost.Instance.EcsWorld.Get<TerrainState>(GameHost.Instance.WorldEntity).WaterEnabled;
			}
			return _localWaterEnabled;
		}
		set
		{
			if (GameHost.Instance != null && 
				GameHost.Instance.EcsWorld != null && 
				GameHost.Instance.EcsWorld.IsAlive(GameHost.Instance.WorldEntity) && 
				GameHost.Instance.EcsWorld.Has<TerrainState>(GameHost.Instance.WorldEntity))
			{
				ref var state = ref GameHost.Instance.EcsWorld.Get<TerrainState>(GameHost.Instance.WorldEntity);
				GameHost.Instance.EcsWorld.Set(GameHost.Instance.WorldEntity, new TerrainState(
					state.Width, state.Depth, state.Spacing, state.CellSize, state.WaterHeight, value,
					state.Heights, state.PathingCodes, state.NavMesh, state.NavMeshQuery
				));
			}
			_localWaterEnabled = value;
			if (_waterMesh != null)
			{
				_waterMesh.Visible = value;
			}
		}
	}

	private void CreateWater()
	{
		if (_waterMesh != null) return;

		_waterMesh = new MeshInstance3D();
		_waterMesh.Name = "WaterMesh";
		
		var plane = new PlaneMesh();
		plane.Size = new Vector2(Width * Spacing, Depth * Spacing);
		_waterMesh.Mesh = plane;

		var mat = new StandardMaterial3D();
		mat.AlbedoColor = new Color(0.0f, 0.35f, 0.7f, 0.5f);
		mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		mat.Roughness = 0.1f;
		mat.Metallic = 0.2f;
		mat.BacklightEnabled = true;
		mat.Backlight = new Color(0.0f, 0.5f, 1.0f);
		_waterMesh.MaterialOverride = mat;
		
		AddChild(_waterMesh);
		_waterMesh.Visible = WaterEnabled;
		UpdateWaterTransform();
	}

	private void UpdateWaterTransform()
	{
		if (_waterMesh != null)
		{
			_waterMesh.Position = new Vector3(0.0f, WaterHeight, 0.0f);
		}
	}

	public override void _Ready()
	{
		var state = GetTerrainStateSafe();
		if (state.Heights == null || state.Heights.GetLength(0) != Width || state.Heights.GetLength(1) != Depth)
		{
			var newHeights = new float[Width, Depth];
			for (int z = 0; z < Depth; z++)
				for (int x = 0; x < Width; x++)
					newHeights[x, z] = 0.0f;
			Heights = newHeights;
		}

		if (Colors == null || Colors.GetLength(0) != Width || Colors.GetLength(1) != Depth)
		{
			Colors = new Color[Width, Depth];
			for (int z = 0; z < Depth; z++)
				for (int x = 0; x < Width; x++)
					Colors[x, z] = new Color(0.2f, 0.6f, 0.2f); // Default Grass Green
		}

		if (state.PathingCodes == null || state.PathingCodes.GetLength(0) != Width || state.PathingCodes.GetLength(1) != Depth)
		{
			var newPathing = new int[Width, Depth];
			for (int z = 0; z < Depth; z++)
				for (int x = 0; x < Width; x++)
					newPathing[x, z] = PATHING_GROUND | PATHING_FLYING;
			PathingCodes = newPathing;
		}

		_meshInstance = new MeshInstance3D();
		_meshInstance.Name = "TerrainMesh";
		AddChild(_meshInstance);

		_collisionShape = new CollisionShape3D();
		_collisionShape.Name = "TerrainCollision";
		AddChild(_collisionShape);

		_arrayMesh = new ArrayMesh();
		
		var shader = new Shader();
		shader.Code = @"
shader_type spatial;

uniform sampler2DArray terrain_textures : source_color;
varying vec3 v_color;
varying float v_rot;

const vec3 SWATCH_COLORS[12] = vec3[](
	vec3(0.95, 0.95, 1.0),   // 1 (River Silt / Sand)
	vec3(0.5, 0.5, 0.52),    // 2 (Cinder Rock)
	vec3(0.5, 0.45, 0.38),   // 3 (Arid Dust)
	vec3(0.2, 0.6, 0.2),     // 4 (Deep Moss)
	vec3(0.38, 0.38, 0.4),   // 5 (Crag Stone)
	vec3(0.4, 0.28, 0.18),   // 6 (Ash Soil)
	vec3(0.3, 0.7, 0.2),     // 7 (Fern Grove)
	vec3(0.12, 0.48, 0.18),  // 8 (Mossy Stone)
	vec3(0.7, 0.55, 0.35),   // 9 (Holy Moss)
	vec3(0.85, 0.75, 0.5),   // 10 (Void Shard)
	vec3(0.45, 0.55, 0.65),  // 11 (Fallback Grass)
	vec3(0.6, 0.3, 0.15)     // 12 (Fallback Dirt)
);

void vertex() {
	v_color = COLOR.rgb;
	v_rot = COLOR.a;
}

void fragment() {
	float angle = v_rot * 6.2831853;
	float cos_a = cos(angle);
	float sin_a = sin(angle);
	mat2 rot_mat = mat2(vec2(cos_a, -sin_a), vec2(sin_a, cos_a));
	vec2 rotated_uv = rot_mat * (UV - vec2(12.5, 12.5)) + vec2(12.5, 12.5);

	int first_idx = 0;
	int second_idx = 0;
	float first_dist = 99999.0;
	float second_dist = 99999.0;
	for (int i = 0; i < 12; i++) {
		float dist = distance(v_color, SWATCH_COLORS[i]);
		if (dist < first_dist) {
			second_dist = first_dist;
			second_idx = first_idx;
			first_dist = dist;
			first_idx = i;
		} else if (dist < second_dist) {
			second_dist = dist;
			second_idx = i;
		}
	}
	vec4 tex_color1 = texture(terrain_textures, vec3(rotated_uv, float(first_idx)));
	vec4 tex_color2 = texture(terrain_textures, vec3(rotated_uv, float(second_idx)));
	float t = 0.0;
	float total_dist = first_dist + second_dist;
	if (total_dist > 0.0001) {
		t = first_dist / total_dist;
	}
	ALBEDO = mix(tex_color1.rgb, tex_color2.rgb, t);
	ROUGHNESS = 0.9;
}
";

		var paths = new string[]
		{
			"res://Assets/2d/TileSheets/ancient_ruin.png",
			"res://Assets/2d/TileSheets/deep_moss.png",
			"res://Assets/2d/TileSheets/gray_slate.png",
			"res://Assets/2d/TileSheets/iron_dust.png",
			"res://Assets/2d/TileSheets/lava_vein.png",
			"res://Assets/2d/TileSheets/mossy_stone.png",
			"res://Assets/2d/TileSheets/pale_sand.png",
			"res://Assets/2d/TileSheets/river_silt.png",
			"res://Assets/2d/TileSheets/royal_marble.png",
			"res://Assets/2d/TileSheets/tarn_mud.png",
			"res://Assets/2d/TileSheets/dark_wood.png",
			"res://Assets/2d/TileSheets/mist_grove.png"
		};

		var images = new Godot.Collections.Array<Image>();
		int texWidth = 0;
		int texHeight = 0;
		Image.Format format = Image.Format.Rgb8;

		foreach (var path in paths)
		{
			var tex = GD.Load<Texture2D>(path);
			if (tex == null)
			{
				tex = GD.Load<Texture2D>("res://Assets/terrain_default.png");
			}

			if (tex != null)
			{
				var img = tex.GetImage();
				if (texWidth == 0)
				{
					texWidth = img.GetWidth();
					texHeight = img.GetHeight();
					format = img.GetFormat();
				}
				else
				{
					if (img.GetWidth() != texWidth || img.GetHeight() != texHeight)
					{
						img.Resize(texWidth, texHeight);
					}
					if (img.GetFormat() != format)
					{
						img.Convert(format);
					}
				}
				images.Add(img);
			}
		}

		var textureArray = new Texture2DArray();
		textureArray.CreateFromImages(images);

		_material = new ShaderMaterial();
		_material.Shader = shader;
		_material.SetShaderParameter("terrain_textures", textureArray);

		_meshInstance.MaterialOverride = _material;

		CreateWater();
		UpdateMeshAndPhysics();
	}

	public void UpdateMeshAndPhysics(bool rebuildPhysics = true, bool rebuildNavMesh = true)
	{
		int w = Width;
		int d = Depth;

		if (Colors == null || Colors.GetLength(0) != w || Colors.GetLength(1) != d)
		{
			Colors = new Color[w, d];
			for (int z = 0; z < d; z++)
				for (int x = 0; x < w; x++)
					Colors[x, z] = new Color(0.2f, 0.6f, 0.2f);
		}

		int vertexCount = w * d;
		if (_verticesCache == null || _verticesCache.Length != vertexCount)
		{
			_verticesCache = new Vector3[vertexCount];
			_colorsCache = new Color[vertexCount];
			_normalsCache = new Vector3[vertexCount];
			_uvsCache = new Vector2[vertexCount];
		}


		for (int z = 0; z < Depth; z++)
		{
			for (int x = 0; x < Width; x++)
			{
				int idx = z * Width + x;
				float lx = (x - (Width - 1) / 2.0f) * Spacing;
				float lz = (z - (Depth - 1) / 2.0f) * Spacing;
				_verticesCache[idx] = new Vector3(lx, Heights[x, z], lz);
				_colorsCache[idx] = Colors[x, z];
				_uvsCache[idx] = new Vector2((float)x / (Width - 1) * 25f, (float)z / (Depth - 1) * 25f);
			}
		}


		for (int z = 0; z < Depth; z++)
		{
			for (int x = 0; x < Width; x++)
			{
				int idx = z * Width + x;
				float hl = Heights[Math.Max(0, x - 1), z];
				float hr = Heights[Math.Min(Width - 1, x + 1), z];
				float hd = Heights[x, Math.Max(0, z - 1)];
				float hu = Heights[x, Math.Min(Depth - 1, z + 1)];
				
				Vector3 tangentX = new Vector3(2.0f * Spacing, hr - hl, 0.0f).Normalized();
				Vector3 tangentZ = new Vector3(0.0f, hu - hd, 2.0f * Spacing).Normalized();
				_normalsCache[idx] = tangentZ.Cross(tangentX).Normalized();
			}
		}


		int cellWidth = Width - 1;
		int cellDepth = Depth - 1;
		int indexCount = cellWidth * cellDepth * 6;
		if (_indicesCache == null || _indicesCache.Length != indexCount)
		{
			_indicesCache = new int[indexCount];
		}
		int iIdx = 0;
		for (int z = 0; z < cellDepth; z++)
		{
			for (int x = 0; x < cellWidth; x++)
			{
				int v00 = z * Width + x;
				int v10 = z * Width + (x + 1);
				int v01 = (z + 1) * Width + x;
				int v11 = (z + 1) * Width + (x + 1);
				
				_indicesCache[iIdx++] = v00;
				_indicesCache[iIdx++] = v10;
				_indicesCache[iIdx++] = v01;
				
				_indicesCache[iIdx++] = v10;
				_indicesCache[iIdx++] = v11;
				_indicesCache[iIdx++] = v01;
			}
		}

		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = _verticesCache;
		arrays[(int)Mesh.ArrayType.Normal] = _normalsCache;
		arrays[(int)Mesh.ArrayType.Color] = _colorsCache;
		arrays[(int)Mesh.ArrayType.TexUV] = _uvsCache;
		arrays[(int)Mesh.ArrayType.Index] = _indicesCache;

		_arrayMesh.ClearSurfaces();
		_arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		_meshInstance.Mesh = _arrayMesh;

		if (!rebuildPhysics) return;


		var heightMapShape = new HeightMapShape3D();
		heightMapShape.MapWidth = Width;
		heightMapShape.MapDepth = Depth;
		
		int mapDataCount = Width * Depth;
		if (_mapDataCache == null || _mapDataCache.Length != mapDataCount)
		{
			_mapDataCache = new float[mapDataCount];
		}
		for (int z = 0; z < Depth; z++)
		{
			for (int x = 0; x < Width; x++)
			{
				_mapDataCache[z * Width + x] = Heights[x, z];
			}
		}
		heightMapShape.MapData = _mapDataCache;
		_collisionShape.Shape = heightMapShape;
		_collisionShape.Scale = new Vector3(Spacing, 1.0f, Spacing);
		if (rebuildNavMesh)
		{
			if (GameHost.Instance == null || !GameHost.Instance.IsMapEditorMode)
			{
				BakeNavMesh();
			}
		}
	}

	public void BakeNavMesh()
	{
		var world = GameHost.Instance.EcsWorld;
		var worldEntity = GameHost.Instance.WorldEntity;
		if (world != null && world.IsAlive(worldEntity) && world.Has<TerrainState>(worldEntity))
		{
			ref var state = ref world.Get<TerrainState>(worldEntity);
			var terrainNavMeshService = ServiceLocator.Get<Realm.Ecs.Services.TerrainNavMeshService>();
			terrainNavMeshService.BakeNavMesh(ref state);
			world.Set(worldEntity, state);
		}
	}

	public void GetHeightAndNormal(float worldX, float worldZ, out float height, out Vector3 normal)
	{
		float halfW = (Width - 1) / 2.0f * Spacing;
		float halfD = (Depth - 1) / 2.0f * Spacing;
		float gridX = (worldX + halfW) / Spacing;
		float gridZ = (worldZ + halfD) / Spacing;
		int x0 = (int)Math.Floor(gridX);
		int z0 = (int)Math.Floor(gridZ);
		x0 = Math.Max(0, Math.Min(Width - 2, x0));
		z0 = Math.Max(0, Math.Min(Depth - 2, z0));
		float tx = gridX - x0;
		float tz = gridZ - z0;
		float h00 = Heights[x0, z0];
		float h10 = Heights[x0 + 1, z0];
		float h01 = Heights[x0, z0 + 1];
		float h11 = Heights[x0 + 1, z0 + 1];
		height = (1 - tx) * (1 - tz) * h00 + tx * (1 - tz) * h10 + (1 - tx) * tz * h01 + tx * tz * h11;
		Vector3 n00 = GetVertexNormal(x0, z0);
		Vector3 n10 = GetVertexNormal(x0 + 1, z0);
		Vector3 n01 = GetVertexNormal(x0, z0 + 1);
		Vector3 n11 = GetVertexNormal(x0 + 1, z0 + 1);
		normal = ((1 - tx) * (1 - tz) * n00 + tx * (1 - tz) * n10 + (1 - tx) * tz * n01 + tx * tz * n11).Normalized();
	}

	private Vector3 GetVertexNormal(int x, int z)
	{
		float hl = Heights[Math.Max(0, x - 1), z];
		float hr = Heights[Math.Min(Width - 1, x + 1), z];
		float hd = Heights[x, Math.Max(0, z - 1)];
		float hu = Heights[x, Math.Min(Depth - 1, z + 1)];
		Vector3 tangentX = new Vector3(2.0f * Spacing, hr - hl, 0.0f).Normalized();
		Vector3 tangentZ = new Vector3(0.0f, hu - hd, 2.0f * Spacing).Normalized();
		return tangentZ.Cross(tangentX).Normalized();
	}
}
