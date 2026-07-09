using Arch.Core;
using Realm.Ecs.Components.Terrain;
using DotRecast.Detour;
using Godot;
using System;

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

	public DtNavMesh NavMesh => GetTerrainStateSafe().NavMesh;

	public DtNavMeshQuery NavMeshQuery => GetTerrainStateSafe().NavMeshQuery;

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
	public const int PATHING_BUILDABLE = 32;

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

	public TerrainSplatWeights[,] SplatMap { get; private set; }

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
	private float[] _texIndicesCache;
	private float[] _texWeightsCache01;
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

	public void UpdateWaterSize()
	{
		if (_waterMesh == null) return;
		var plane = new PlaneMesh();
		plane.Size = new Vector2(Width * Spacing, Depth * Spacing);
		_waterMesh.Mesh = plane;
		UpdateWaterTransform();
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

		if (SplatMap == null || SplatMap.GetLength(0) != Width || SplatMap.GetLength(1) != Depth)
		{
			SplatMap = new TerrainSplatWeights[Width, Depth];
			for (int z = 0; z < Depth; z++)
				for (int x = 0; x < Width; x++)
					SplatMap[x, z] = TerrainSplatWeights.CreateSolid(3);
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

varying vec4 v_tex_indices;
varying vec4 v_tex_weights;

void vertex() {
	v_tex_indices = CUSTOM0;
	v_tex_weights = CUSTOM1;
}

void fragment() {
	vec4 c0 = texture(terrain_textures, vec3(UV, v_tex_indices.x));
	vec4 c1 = texture(terrain_textures, vec3(UV, v_tex_indices.y));
	vec4 c2 = texture(terrain_textures, vec3(UV, v_tex_indices.z));
	vec4 c3 = texture(terrain_textures, vec3(UV, v_tex_indices.w));
	ALBEDO = (c0.rgb * v_tex_weights.x +
	          c1.rgb * v_tex_weights.y +
	          c2.rgb * v_tex_weights.z +
	          c3.rgb * v_tex_weights.w);
	ROUGHNESS = 0.9;
}
";

		var paths = new[]
		{
			"res://Assets/2d/TileSheets/ancient_ruin.png",
			"res://Assets/2d/TileSheets/deep_moss.png",
			"res://Assets/2d/TileSheets/grey_slate.png",
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

		if (SplatMap == null || SplatMap.GetLength(0) != w || SplatMap.GetLength(1) != d)
		{
			SplatMap = new TerrainSplatWeights[w, d];
			for (int z = 0; z < d; z++)
				for (int x = 0; x < w; x++)
					SplatMap[x, z] = TerrainSplatWeights.CreateSolid(3);
		}

		int cellWidth = w - 1;
		int cellDepth = d - 1;
		int triangleCount = cellWidth * cellDepth * 2;
		int vertexCount = triangleCount * 3;

		if (_verticesCache == null || _verticesCache.Length != vertexCount)
		{
			_verticesCache = new Vector3[vertexCount];
			_texIndicesCache = new float[vertexCount * 4];
			_texWeightsCache01 = new float[vertexCount * 4];
			_normalsCache = new Vector3[vertexCount];
			_uvsCache = new Vector2[vertexCount];
			_indicesCache = new int[vertexCount];
		}

		var gridNormals = new Vector3[w * d];
		for (int z = 0; z < d; z++)
		{
			for (int x = 0; x < w; x++)
			{
				int idx = z * w + x;
				float hl = Heights[Math.Max(0, x - 1), z];
				float hr = Heights[Math.Min(w - 1, x + 1), z];
				float hd = Heights[x, Math.Max(0, z - 1)];
				float hu = Heights[x, Math.Min(d - 1, z + 1)];
				
				Vector3 tangentX = new Vector3(2.0f * Spacing, hr - hl, 0.0f).Normalized();
				Vector3 tangentZ = new Vector3(0.0f, hu - hd, 2.0f * Spacing).Normalized();
				gridNormals[idx] = tangentZ.Cross(tangentX).Normalized();
			}
		}

		int vertexIndex = 0;
		for (int z = 0; z < cellDepth; z++)
		{
			for (int x = 0; x < cellWidth; x++)
			{
				// Triangle 1: (x, z), (x + 1, z), (x, z + 1)
				ProcessTriangle(x, z, x + 1, z, x, z + 1, ref vertexIndex, gridNormals);

				// Triangle 2: (x + 1, z), (x + 1, z + 1), (x, z + 1)
				ProcessTriangle(x + 1, z, x + 1, z + 1, x, z + 1, ref vertexIndex, gridNormals);
			}
		}

		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = _verticesCache;
		arrays[(int)Mesh.ArrayType.Normal] = _normalsCache;
		arrays[(int)Mesh.ArrayType.TexUV] = _uvsCache;
		arrays[(int)Mesh.ArrayType.Custom0] = _texIndicesCache;
		arrays[(int)Mesh.ArrayType.Custom1] = _texWeightsCache01;
		arrays[(int)Mesh.ArrayType.Index] = _indicesCache;

		_arrayMesh.ClearSurfaces();
		int custom0Format = (int)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift;
		int custom1Format = (int)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom1Shift;
		_arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays,
			new Godot.Collections.Array<Godot.Collections.Array>(),
			null,
			(Mesh.ArrayFormat)((int)(Mesh.ArrayFormat.FormatCustom0 | Mesh.ArrayFormat.FormatCustom1) | custom0Format | custom1Format));
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

	private void ProcessTriangle(
		int x0, int z0,
		int x1, int z1,
		int x2, int z2,
		ref int vertexIndex,
		Vector3[] gridNormals)
	{
		var s0 = SplatMap[x0, z0];
		var s1 = SplatMap[x1, z1];
		var s2 = SplatMap[x2, z2];

		var uniqueTexs = new System.Collections.Generic.List<int>();
		var texWeightsSum = new System.Collections.Generic.List<float>();

		void AddTexture(int index, float weight)
		{
			if (weight <= 0.0f) return;
			int idx = uniqueTexs.IndexOf(index);
			if (idx >= 0)
			{
				texWeightsSum[idx] += weight;
			}
			else
			{
				uniqueTexs.Add(index);
				texWeightsSum.Add(weight);
			}
		}

		AddTexture(s0.Index0, s0.Weight0);
		AddTexture(s0.Index1, s0.Weight1);
		AddTexture(s0.Index2, s0.Weight2);
		AddTexture(s0.Index3, s0.Weight3);

		AddTexture(s1.Index0, s1.Weight0);
		AddTexture(s1.Index1, s1.Weight1);
		AddTexture(s1.Index2, s1.Weight2);
		AddTexture(s1.Index3, s1.Weight3);

		AddTexture(s2.Index0, s2.Weight0);
		AddTexture(s2.Index1, s2.Weight1);
		AddTexture(s2.Index2, s2.Weight2);
		AddTexture(s2.Index3, s2.Weight3);

		if (uniqueTexs.Count == 0)
		{
			uniqueTexs.Add(3);
			texWeightsSum.Add(1.0f);
		}

		while (uniqueTexs.Count > 4)
		{
			int lowestIdx = 0;
			float lowestW = texWeightsSum[0];
			for (int i = 1; i < uniqueTexs.Count; i++)
			{
				if (texWeightsSum[i] < lowestW)
				{
					lowestW = texWeightsSum[i];
					lowestIdx = i;
				}
			}
			uniqueTexs.RemoveAt(lowestIdx);
			texWeightsSum.RemoveAt(lowestIdx);
		}

		while (uniqueTexs.Count < 4)
		{
			uniqueTexs.Add(uniqueTexs[0]);
		}

		int p0 = uniqueTexs[0];
		int p1 = uniqueTexs[1];
		int p2 = uniqueTexs[2];
		int p3 = uniqueTexs[3];

		PopulateTriangleVertex(x0, z0, p0, p1, p2, p3, s0, ref vertexIndex, gridNormals);
		PopulateTriangleVertex(x1, z1, p0, p1, p2, p3, s1, ref vertexIndex, gridNormals);
		PopulateTriangleVertex(x2, z2, p0, p1, p2, p3, s2, ref vertexIndex, gridNormals);
	}

	private void PopulateTriangleVertex(
		int x, int z,
		int p0, int p1, int p2, int p3,
		TerrainSplatWeights srcSplat,
		ref int vertexIndex,
		Vector3[] gridNormals)
	{
		float lx = (x - (Width - 1) / 2.0f) * Spacing;
		float lz = (z - (Depth - 1) / 2.0f) * Spacing;
		_verticesCache[vertexIndex] = new Vector3(lx, Heights[x, z], lz);

		_normalsCache[vertexIndex] = gridNormals[z * Width + x];

		_uvsCache[vertexIndex] = new Vector2((float)x / (Width - 1) * 25f, (float)z / (Depth - 1) * 25f);

		_indicesCache[vertexIndex] = vertexIndex;

		float w0 = 0f, w1 = 0f, w2 = 0f, w3 = 0f;

		float GetWeight(int texIndex)
		{
			float sum = 0.0f;
			if (srcSplat.Index0 == texIndex) sum += srcSplat.Weight0;
			if (srcSplat.Index1 == texIndex) sum += srcSplat.Weight1;
			if (srcSplat.Index2 == texIndex) sum += srcSplat.Weight2;
			if (srcSplat.Index3 == texIndex) sum += srcSplat.Weight3;
			return sum;
		}

		w0 = GetWeight(p0);
		w1 = GetWeight(p1);
		w2 = GetWeight(p2);
		w3 = GetWeight(p3);

		float sumW = w0 + w1 + w2 + w3;
		if (sumW > 0.0001f)
		{
			w0 /= sumW;
			w1 /= sumW;
			w2 /= sumW;
			w3 /= sumW;
		}
		else
		{
			w0 = 1.0f;
			w1 = 0.0f;
			w2 = 0.0f;
			w3 = 0.0f;
		}

		int sIdx = vertexIndex * 4;
		_texIndicesCache[sIdx + 0] = p0;
		_texIndicesCache[sIdx + 1] = p1;
		_texIndicesCache[sIdx + 2] = p2;
		_texIndicesCache[sIdx + 3] = p3;

		_texWeightsCache01[sIdx + 0] = w0;
		_texWeightsCache01[sIdx + 1] = w1;
		_texWeightsCache01[sIdx + 2] = w2;
		_texWeightsCache01[sIdx + 3] = w3;

		vertexIndex++;
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

	public void ResizeTerrain(int newWidth, int newDepth)
	{
		if (GameHost.Instance == null || GameHost.Instance.EcsWorld == null || !GameHost.Instance.EcsWorld.IsAlive(GameHost.Instance.WorldEntity)) return;
		if (!GameHost.Instance.EcsWorld.Has<TerrainState>(GameHost.Instance.WorldEntity)) return;

		ref var state = ref GameHost.Instance.EcsWorld.Get<TerrainState>(GameHost.Instance.WorldEntity);
		
		int oldWidth = state.Width;
		int oldDepth = state.Depth;
		float[,] oldHeights = state.Heights;
		int[,] oldPathing = state.PathingCodes;
		TerrainSplatWeights[,] oldSplatMap = SplatMap;

		float[,] newHeights = new float[newWidth, newDepth];
		int[,] newPathing = new int[newWidth, newDepth];
		TerrainSplatWeights[,] newSplatMap = new TerrainSplatWeights[newWidth, newDepth];

		int offsetX = (newWidth - oldWidth) / 2;
		int offsetZ = (newDepth - oldDepth) / 2;

		for (int z = 0; z < newDepth; z++)
		{
			for (int x = 0; x < newWidth; x++)
			{
				int oldX = x - offsetX;
				int oldZ = z - offsetZ;

				if (oldX >= 0 && oldX < oldWidth && oldZ >= 0 && oldZ < oldDepth)
				{
					if (oldHeights != null) newHeights[x, z] = oldHeights[oldX, oldZ];
					if (oldPathing != null) newPathing[x, z] = oldPathing[oldX, oldZ];
					if (oldSplatMap != null) newSplatMap[x, z] = oldSplatMap[oldX, oldZ];
				}
				else
				{
					newHeights[x, z] = 0.0f;
					newPathing[x, z] = PATHING_GROUND | PATHING_FLYING;
					newSplatMap[x, z] = TerrainSplatWeights.CreateSolid(3);
				}
			}
		}

		GameHost.Instance.EcsWorld.Set(GameHost.Instance.WorldEntity, new TerrainState(
			newWidth,
			newDepth,
			state.Spacing,
			state.CellSize,
			state.WaterHeight,
			state.WaterEnabled,
			newHeights,
			newPathing,
			state.NavMesh,
			state.NavMeshQuery
		));

		_localHeights = newHeights;
		_localPathingCodes = newPathing;
		SplatMap = newSplatMap;

		UpdateWaterSize();
		UpdateMeshAndPhysics();
	}

	public void ScaleTerrainData(int newWidth, int newDepth)
	{
		if (GameHost.Instance == null || GameHost.Instance.EcsWorld == null || !GameHost.Instance.EcsWorld.IsAlive(GameHost.Instance.WorldEntity)) return;
		if (!GameHost.Instance.EcsWorld.Has<TerrainState>(GameHost.Instance.WorldEntity)) return;

		ref var state = ref GameHost.Instance.EcsWorld.Get<TerrainState>(GameHost.Instance.WorldEntity);

		int oldWidth = state.Width;
		int oldDepth = state.Depth;
		float[,] oldHeights = state.Heights;
		int[,] oldPathing = state.PathingCodes;
		TerrainSplatWeights[,] oldSplatMap = SplatMap;

		float[,] newHeights = new float[newWidth, newDepth];
		int[,] newPathing = new int[newWidth, newDepth];
		TerrainSplatWeights[,] newSplatMap = new TerrainSplatWeights[newWidth, newDepth];

		for (int z = 0; z < newDepth; z++)
		{
			for (int x = 0; x < newWidth; x++)
			{
				float srcX = newWidth > 1 ? x * (oldWidth - 1) / (float)(newWidth - 1) : 0f;
				float srcZ = newDepth > 1 ? z * (oldDepth - 1) / (float)(newDepth - 1) : 0f;

				int x0 = Math.Clamp((int)Math.Floor(srcX), 0, oldWidth - 1);
				int z0 = Math.Clamp((int)Math.Floor(srcZ), 0, oldDepth - 1);

				if (oldHeights != null)
				{
					int x1 = Math.Clamp(x0 + 1, 0, oldWidth - 1);
					int z1 = Math.Clamp(z0 + 1, 0, oldDepth - 1);
					float tx = srcX - x0;
					float tz = srcZ - z0;
					newHeights[x, z] =
						(1 - tx) * (1 - tz) * oldHeights[x0, z0] +
						tx * (1 - tz) * oldHeights[x1, z0] +
						(1 - tx) * tz * oldHeights[x0, z1] +
						tx * tz * oldHeights[x1, z1];
				}

				newSplatMap[x, z] = oldSplatMap != null ? oldSplatMap[x0, z0] : TerrainSplatWeights.CreateSolid(3);

				if (oldPathing != null)
				{
					newPathing[x, z] = oldPathing[x0, z0];
				}
				else
				{
					newPathing[x, z] = PATHING_GROUND | PATHING_FLYING;
				}
			}
		}

		GameHost.Instance.EcsWorld.Set(GameHost.Instance.WorldEntity, new TerrainState(
			newWidth, newDepth, state.Spacing, state.CellSize, state.WaterHeight, state.WaterEnabled,
			newHeights, newPathing, state.NavMesh, state.NavMeshQuery
		));

		_localHeights = newHeights;
		_localPathingCodes = newPathing;
		SplatMap = newSplatMap;

		UpdateWaterSize();
		UpdateMeshAndPhysics();
	}

	public void RestoreTerrainFromSnapshot(int newWidth, int newDepth, float spacing, float waterHeight, bool waterEnabled, float[,] heights, int[,] pathingCodes, TerrainSplatWeights[,] splatMap)
	{
		if (GameHost.Instance == null || GameHost.Instance.EcsWorld == null || !GameHost.Instance.EcsWorld.IsAlive(GameHost.Instance.WorldEntity)) return;
		if (!GameHost.Instance.EcsWorld.Has<TerrainState>(GameHost.Instance.WorldEntity)) return;

		ref var state = ref GameHost.Instance.EcsWorld.Get<TerrainState>(GameHost.Instance.WorldEntity);

		float[,] clonedHeights = (float[,])heights.Clone();
		int[,] clonedPathing = (int[,])pathingCodes.Clone();
		TerrainSplatWeights[,] clonedSplatMap = (TerrainSplatWeights[,])splatMap.Clone();

		GameHost.Instance.EcsWorld.Set(GameHost.Instance.WorldEntity, new TerrainState(
			newWidth, newDepth, spacing, state.CellSize, waterHeight, waterEnabled,
			clonedHeights, clonedPathing, state.NavMesh, state.NavMeshQuery
		));

		_localHeights = clonedHeights;
		_localPathingCodes = clonedPathing;
		SplatMap = clonedSplatMap;

		_localWaterEnabled = waterEnabled;
		_localWaterHeight = waterHeight;
		if (_waterMesh != null)
		{
			_waterMesh.Visible = waterEnabled;
		}
		UpdateWaterTransform();
		UpdateWaterSize();
		UpdateMeshAndPhysics();
	}
}
