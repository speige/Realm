using Arch.Core;
using Realm.Ecs.Components.Terrain;
using DotRecast.Detour;
using Godot;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

public partial class RuntimeTerrain : StaticBody3D
{
	public const uint TerrainCollisionLayer = 2;
	public static RuntimeTerrain Instance { get; protected set; }
	public static bool IsMinimapRendering { get; set; } = false;

	public override void _ExitTree()
	{
		if (Instance == this) Instance = null;
	}

	public RuntimeTerrain()
	{
		Instance = this;
		CollisionLayer = 1U | TerrainCollisionLayer;
	}

	protected virtual bool IsRuntimeOnly => true;

	public static Image NormalizeAlbedoLuminance(Image sourceImage, float targetLinearLuminance = 0.35f, float maxScaleFactor = 2.2f)
	{
		return Realm.Godot.Utils.PlayerColorShaderManager.NormalizeAlbedoImage(sourceImage, targetLinearLuminance, 0.2f, maxScaleFactor);
	}

	protected static float[,] ComputeSeparableBoxBlur(float[,] input, int w, int h, int radius)
	{
		float[,] temp = new float[w, h];
		float[,] result = new float[w, h];
		int windowSize = 2 * radius + 1;
		float invWindow = 1.0f / windowSize;

		for (int y = 0; y < h; y++)
		{
			float sum = 0.0f;
			for (int k = -radius; k <= radius; k++)
			{
				int px = (k % w + w) % w;
				sum += input[px, y];
			}
			temp[0, y] = sum * invWindow;

			for (int x = 1; x < w; x++)
			{
				int removeX = ((x - 1 - radius) % w + w) % w;
				int addX = ((x + radius) % w + w) % w;
				sum += input[addX, y] - input[removeX, y];
				temp[x, y] = sum * invWindow;
			}
		}

		for (int x = 0; x < w; x++)
		{
			float sum = 0.0f;
			for (int k = -radius; k <= radius; k++)
			{
				int py = (k % h + h) % h;
				sum += temp[x, py];
			}
			result[x, 0] = sum * invWindow;

			for (int y = 1; y < h; y++)
			{
				int removeY = ((y - 1 - radius) % h + h) % h;
				int addY = ((y + radius) % h + h) % h;
				sum += temp[x, addY] - temp[x, removeY];
				result[x, y] = sum * invWindow;
			}
		}

		return result;
	}

	public const int TargetTextureResolutionBits = 9;
	public const int TargetTextureResolution = 1 << TargetTextureResolutionBits;

	public static Image AutoCropToPowerOfTwoSquare(Image sourceImg)
	{
		if (sourceImg == null) return null;
		int w = sourceImg.GetWidth();
		int h = sourceImg.GetHeight();
		int minDim = Math.Min(w, h);
		if (minDim <= 0) return sourceImg;

		int powerOfTwo = 1;
		while ((powerOfTwo << 1) <= minDim)
		{
			powerOfTwo <<= 1;
		}

		if (w == powerOfTwo && h == powerOfTwo)
		{
			return sourceImg;
		}

		int cropX = (w - powerOfTwo) / 2;
		int cropY = (h - powerOfTwo) / 2;

		return sourceImg.GetRegion(new Rect2I(cropX, cropY, powerOfTwo, powerOfTwo));
	}

	public static Image ProcessAndResizeImage(Image sourceImg, int targetSize)
	{
		if (sourceImg == null) return null;
		if (sourceImg.GetWidth() == targetSize && sourceImg.GetHeight() == targetSize)
		{
			return sourceImg;
		}

		Image resized = (Image)sourceImg.Duplicate();
		Image.Interpolation interpolationMode = sourceImg.GetWidth() > targetSize
			? Image.Interpolation.Lanczos
			: Image.Interpolation.Cubic;

		resized.Resize(targetSize, targetSize, interpolationMode);
		return resized;
	}

	public const float DefaultQuadSize = TerrainState.DefaultQuadSize;
	public const float DefaultCellSize = TerrainState.DefaultCellSize;

	internal TerrainState GetTerrainStateSafe()
	{
		if (GameHost.Instance != null && 
			GameHost.Instance.EcsWorld != null && 
			GameHost.Instance.EcsWorld.IsAlive(GameHost.Instance.WorldEntity) && 
			GameHost.Instance.EcsWorld.Has<TerrainState>(GameHost.Instance.WorldEntity))
		{
			return GameHost.Instance.EcsWorld.Get<TerrainState>(GameHost.Instance.WorldEntity);
		}
		return new TerrainState(128, 128, DefaultQuadSize, DefaultCellSize, (TerrainCell[,])null, null, null, null);
	}

	public float CellSize
	{
		get => GetTerrainStateSafe().CellSize;
		protected set
		{
			ref var state = ref GameHost.Instance.EcsWorld.Get<TerrainState>(GameHost.Instance.WorldEntity);
			GameHost.Instance.EcsWorld.Set(GameHost.Instance.WorldEntity, new TerrainState(
				state.Width, state.Depth, state.QuadSize, value,
				state.Cells, state.PathingCodes, state.NavMesh, state.NavMeshQuery
			));
		}
	}

	public DtNavMesh NavMesh => GetTerrainStateSafe().NavMesh;
	public DtNavMeshQuery NavMeshQuery => GetTerrainStateSafe().NavMeshQuery;

	public int Width
	{
		get => GetTerrainStateSafe().Width;
		protected set
		{
			ref var state = ref GameHost.Instance.EcsWorld.Get<TerrainState>(GameHost.Instance.WorldEntity);
			GameHost.Instance.EcsWorld.Set(GameHost.Instance.WorldEntity, new TerrainState(
				value, state.Depth, state.QuadSize, state.CellSize,
				state.Cells, state.PathingCodes, state.NavMesh, state.NavMeshQuery
			));
		}
	}

	public int Depth
	{
		get => GetTerrainStateSafe().Depth;
		protected set
		{
			ref var state = ref GameHost.Instance.EcsWorld.Get<TerrainState>(GameHost.Instance.WorldEntity);
			GameHost.Instance.EcsWorld.Set(GameHost.Instance.WorldEntity, new TerrainState(
				state.Width, value, state.QuadSize, state.CellSize,
				state.Cells, state.PathingCodes, state.NavMesh, state.NavMeshQuery
			));
		}
	}

	public float QuadSize
	{
		get => GetTerrainStateSafe().QuadSize;
		protected set
		{
			ref var state = ref GameHost.Instance.EcsWorld.Get<TerrainState>(GameHost.Instance.WorldEntity);
			GameHost.Instance.EcsWorld.Set(GameHost.Instance.WorldEntity, new TerrainState(
				state.Width, state.Depth, value, state.CellSize,
				state.Cells, state.PathingCodes, state.NavMesh, state.NavMeshQuery
			));
		}
	}

	public const float TIER_HEIGHT = TerrainCell.TIER_HEIGHT;
	public static float WATER_DELTA => (GameHost.Instance != null && GameHost.Instance.EditorBlockLevelHeight > 0.001f ? GameHost.Instance.EditorBlockLevelHeight : TIER_HEIGHT) * 0.30f;

	public const int PATHING_SHALLOW_WATER = (int)TerrainPathingFlags.ShallowWater;
	public const int PATHING_DEEP_WATER = (int)TerrainPathingFlags.DeepWater;
	public const int PATHING_FLYING = (int)TerrainPathingFlags.Flying;
	public const int PATHING_GROUND = (int)TerrainPathingFlags.Ground;
	public const int PATHING_BUILDABLE = (int)TerrainPathingFlags.Buildable;

	public static int GetDefaultPathingCode(WaterType waterMode)
	{
		switch (waterMode)
		{
			case WaterType.Shallow:
				return PATHING_SHALLOW_WATER | PATHING_FLYING;
			case WaterType.Deep:
				return PATHING_DEEP_WATER | PATHING_FLYING;
			case WaterType.None:
			default:
				return PATHING_GROUND | PATHING_BUILDABLE | PATHING_FLYING;
		}
	}

	public static int GetDefaultPathingCode(TerrainCell cell)
	{
		return GetDefaultPathingCode(cell.WaterMode);
	}

	protected TerrainCell[,] _localCells;
	protected int[,] _localPathingCodes;

	public TerrainCell[,] Cells
	{
		get
		{
			if (GameHost.Instance != null && 
				GameHost.Instance.EcsWorld != null && 
				GameHost.Instance.EcsWorld.IsAlive(GameHost.Instance.WorldEntity) && 
				GameHost.Instance.EcsWorld.Has<TerrainState>(GameHost.Instance.WorldEntity))
			{
				ref var state = ref GameHost.Instance.EcsWorld.Get<TerrainState>(GameHost.Instance.WorldEntity);
				if (state.Cells == null)
				{
					state.Cells = new TerrainCell[state.Width, state.Depth];
				}
				return state.Cells;
			}
			if (_localCells == null)
			{
				_localCells = new TerrainCell[Width, Depth];
			}
			return _localCells;
		}
		set
		{
			_localCells = value;
			if (GameHost.Instance != null && 
				GameHost.Instance.EcsWorld != null && 
				GameHost.Instance.EcsWorld.IsAlive(GameHost.Instance.WorldEntity) && 
				GameHost.Instance.EcsWorld.Has<TerrainState>(GameHost.Instance.WorldEntity))
			{
				ref var state = ref GameHost.Instance.EcsWorld.Get<TerrainState>(GameHost.Instance.WorldEntity);
				int targetWidth = value != null ? value.GetLength(0) : state.Width;
				int targetDepth = value != null ? value.GetLength(1) : state.Depth;
				GameHost.Instance.EcsWorld.Set(GameHost.Instance.WorldEntity, new TerrainState(
					targetWidth, targetDepth, state.QuadSize, state.CellSize,
					value, state.PathingCodes, state.NavMesh, state.NavMeshQuery
				));
			}
		}
	}

	public sbyte[,] MacroTiers
	{
		get
		{
			var c = Cells;
			if (c == null) return null;
			int w = Width;
			int d = Depth;
			var result = new sbyte[w, d];
			for (int z = 0; z < d; z++)
				for (int x = 0; x < w; x++)
					result[x, z] = c[x, z].MacroTier;
			return result;
		}
	}

	public float[,] Heights
	{
		get => TerrainState.CalculateHeights(Width, Depth, Cells);
		set => SetHeights(value);
	}

	public void SetHeights(float[,] heights)
	{
		if (heights == null) return;
		Cells = TerrainState.CalculateCells(Width, Depth, heights);
	}

	public TerrainSplatWeights[,] SplatMap { get; set; }
	public TerrainSplatWeights[,] CliffSplatMap { get; set; }

	public float CliffJitterStrength { get; set; } = 1.0f;
	public float CliffJitterScale { get; set; } = 0.20f;
	public float CliffRimNoiseStrength { get; set; } = 0.30f;
	public float BlendSoftness { get; set; } = 0.04f;
	public float BlendNoiseStrength { get; set; } = 0.22f;
	public float BlendNoiseScale { get; set; } = 0.22f;

	public int[,] PathingCodes
	{
		get
		{
			if (GameHost.Instance != null && 
				GameHost.Instance.EcsWorld != null && 
				GameHost.Instance.EcsWorld.IsAlive(GameHost.Instance.WorldEntity) && 
				GameHost.Instance.EcsWorld.Has<TerrainState>(GameHost.Instance.WorldEntity))
			{
				ref var state = ref GameHost.Instance.EcsWorld.Get<TerrainState>(GameHost.Instance.WorldEntity);
				if (state.PathingCodes == null)
				{
					state.PathingCodes = new int[state.Width, state.Depth];
					var cells = Cells;
					for (int z = 0; z < state.Depth; z++)
					{
						for (int x = 0; x < state.Width; x++)
						{
							state.PathingCodes[x, z] = GetDefaultPathingCode(cells[x, z]);
						}
					}
				}
				return state.PathingCodes;
			}
			if (_localPathingCodes == null)
			{
				_localPathingCodes = new int[Width, Depth];
				var cells = Cells;
				for (int z = 0; z < Depth; z++)
				{
					for (int x = 0; x < Width; x++)
					{
						_localPathingCodes[x, z] = GetDefaultPathingCode(cells[x, z]);
					}
				}
			}
			return _localPathingCodes;
		}
		set
		{
			_localPathingCodes = value;
			if (GameHost.Instance != null && 
				GameHost.Instance.EcsWorld != null && 
				GameHost.Instance.EcsWorld.IsAlive(GameHost.Instance.WorldEntity) && 
				GameHost.Instance.EcsWorld.Has<TerrainState>(GameHost.Instance.WorldEntity))
			{
				ref var state = ref GameHost.Instance.EcsWorld.Get<TerrainState>(GameHost.Instance.WorldEntity);
				int targetWidth = value != null ? value.GetLength(0) : state.Width;
				int targetDepth = value != null ? value.GetLength(1) : state.Depth;
				GameHost.Instance.EcsWorld.Set(GameHost.Instance.WorldEntity, new TerrainState(
					targetWidth, targetDepth, state.QuadSize, state.CellSize,
					state.Cells, value, state.NavMesh, state.NavMeshQuery
				));
			}
		}
	}

	public const int CHUNK_SIZE = 16;
	protected int _chunkedWidth = 0;
	protected int _chunkedDepth = 0;

	public class TerrainChunk
	{
		public MeshInstance3D MeshInstance;
		public ArrayMesh ArrayMesh;
		public CollisionShape3D CollisionShape;
		public MeshInstance3D ShallowWaterMesh;
		public ArrayMesh ShallowWaterArrayMesh;
		public MeshInstance3D DeepWaterMesh;
		public ArrayMesh DeepWaterArrayMesh;
		public int StartX;
		public int StartZ;
		public int EndX;
		public int EndZ;
		public Vector3[] VerticesCache;
		public Color[] ColorsCache;
		public float[] TexIndicesCache;
		public float[] TexWeightsCache01;
		public float[] CliffIndicesCache;
		public float[] CliffWeightsCache;
		public Vector3[] NormalsCache;
		public Vector2[] UvsCache;
		public int[] IndicesCache;
		public float[] MapDataCache;
		public Dictionary<TerrainVertexKey, int> WeldVertexMap;
		public int[] WeldRemapTable;
	}
	
	protected List<TerrainChunk> _chunks = new List<TerrainChunk>();
	protected ShaderMaterial _material;
	public ShaderMaterial Material => _material;
	protected ShaderMaterial _shallowWaterMaterial;
	protected ShaderMaterial _deepWaterMaterial;

	protected void CreateWater()
	{
		if (_shallowWaterMaterial != null && _deepWaterMaterial != null) return;

		var waterShader = new Shader();
		waterShader.Code = @"
shader_type spatial;
render_mode blend_mix;

uniform vec4 shallow_color : source_color = vec4(0.05, 0.30, 0.38, 0.55);
uniform vec4 deep_color : source_color = vec4(0.01, 0.06, 0.14, 0.98);
uniform vec4 foam_color : source_color = vec4(0.85, 0.95, 1.0, 0.85);
uniform float max_depth = 2.0;
uniform float foam_depth = 0.6;
uniform float wave_speed = 1.2;

uniform sampler2D depth_texture : hint_depth_texture, filter_linear;

uniform sampler2D shroud_texture : hint_default_white;
uniform vec2 shroud_world_min = vec2(-125.0, -125.0);
uniform vec2 shroud_world_size = vec2(250.0, 250.0);

varying vec3 v_world_pos;
varying vec3 v_world_normal;

void vertex() {
	v_world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	v_world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);

	float is_flat = smoothstep(0.4, 0.8, abs(v_world_normal.y));
	float w1 = sin(v_world_pos.x * 0.4 + TIME * wave_speed * 1.8);
	float w2 = cos(v_world_pos.z * 0.4 + TIME * wave_speed * 1.4);
	VERTEX.y += (w1 + w2) * 0.06 * is_flat;
}

void fragment() {
	float flat_factor = smoothstep(0.4, 0.8, abs(v_world_normal.y));
	float waterfall_factor = 1.0 - flat_factor;

	vec2 p1 = v_world_pos.xz * 0.25 + vec2(TIME * wave_speed * 0.12, TIME * wave_speed * 0.08);
	vec2 p2 = v_world_pos.xz * 0.35 - vec2(TIME * wave_speed * 0.09, TIME * wave_speed * 0.14);

	float n1 = sin(p1.x + sin(p1.y * 1.2)) * 0.5 + 0.5;
	float n2 = cos(p2.x - cos(p2.y * 1.3)) * 0.5 + 0.5;
	float combined_wave = (n1 + n2) * 0.5;

	vec3 flat_perturbed_normal = normalize(vec3((n1 - 0.5) * 0.25, 1.0, (n2 - 0.5) * 0.25));

	float flow_x = (v_world_pos.x + v_world_pos.z) * 0.3;
	float flow_y = v_world_pos.y * 1.2 + TIME * wave_speed * 1.1;

	float streak1 = sin(flow_y * 2.2 + sin(flow_x * 1.8)) * 0.5 + 0.5;
	float streak2 = cos(flow_y * 4.2 - cos(flow_x * 3.1)) * 0.5 + 0.5;
	float waterfall_foam_raw = streak1 * 0.5 + streak2 * 0.5;
	float waterfall_foam = smoothstep(0.25, 0.95, waterfall_foam_raw) * 0.28;

	vec3 wf_normal = normalize(vec3((streak1 - 0.5) * 0.08, (streak2 - 0.5) * 0.08, (streak1 - 0.5) * 0.08));

	vec3 local_normal = mix(wf_normal, flat_perturbed_normal, flat_factor);
	NORMAL = normalize(TANGENT * local_normal.x + BINORMAL * local_normal.z + NORMAL * local_normal.y);

	float depth_raw = texture(depth_texture, SCREEN_UV).r;
	vec4 upos = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth_raw, 1.0);
	float pixel_z = -upos.z / upos.w;
	float water_z = -VERTEX.z;
	float water_depth = max(0.0, pixel_z - water_z);

	float depth_factor = clamp(water_depth / max_depth, 0.0, 1.0);
	vec4 water_col = mix(shallow_color, deep_color, smoothstep(0.0, 0.85, depth_factor));

	float shore_fade = mix(1.0, smoothstep(0.001, 0.02, water_depth), flat_factor);
	float shore_proximity = (1.0 - smoothstep(0.0, foam_depth, water_depth)) * shore_fade;

	float pulse = sin(water_depth * 18.0 - TIME * wave_speed * 3.5) * 0.5 + 0.5;
	float flat_foam_mask = smoothstep(0.2, 0.8, shore_proximity * (0.6 + 0.4 * combined_wave) + pulse * shore_proximity * 0.4);

	float total_foam = mix(waterfall_foam, flat_foam_mask, flat_factor);
	vec3 final_color = mix(water_col.rgb, foam_color.rgb, total_foam * foam_color.a);
	float final_alpha = mix(water_col.a, 1.0, total_foam * foam_color.a);

	vec2 shroud_uv = (v_world_pos.xz - shroud_world_min) / shroud_world_size;
	shroud_uv = clamp(shroud_uv, vec2(0.0), vec2(1.0));
	float shroud_val = texture(shroud_texture, shroud_uv).r;
	float visibility = 1.0 - shroud_val;
	final_color = mix(vec3(0.0), final_color, visibility);

	ALBEDO = final_color;
	ALPHA = final_alpha;
	ROUGHNESS = mix(0.1, 0.35, total_foam);
	SPECULAR = 0.5;
}
";

		_shallowWaterMaterial = new ShaderMaterial();
		_shallowWaterMaterial.Shader = waterShader;
		_shallowWaterMaterial.SetShaderParameter("shallow_color", new Color(0.08f, 0.45f, 0.55f, 0.70f));
		_shallowWaterMaterial.SetShaderParameter("deep_color", new Color(0.03f, 0.18f, 0.28f, 0.90f));
		_shallowWaterMaterial.SetShaderParameter("foam_color", new Color(0.9f, 0.98f, 1.0f, 0.85f));
		_shallowWaterMaterial.SetShaderParameter("max_depth", 1.5f);
		_shallowWaterMaterial.SetShaderParameter("foam_depth", 0.5f);
		_shallowWaterMaterial.SetShaderParameter("wave_speed", 1.0f);

		_deepWaterMaterial = new ShaderMaterial();
		_deepWaterMaterial.Shader = waterShader;
		_deepWaterMaterial.SetShaderParameter("shallow_color", new Color(0.02f, 0.12f, 0.22f, 0.85f));
		_deepWaterMaterial.SetShaderParameter("deep_color", new Color(0.005f, 0.03f, 0.08f, 0.99f));
		_deepWaterMaterial.SetShaderParameter("foam_color", new Color(0.85f, 0.95f, 1.0f, 0.90f));
		_deepWaterMaterial.SetShaderParameter("max_depth", 3.0f);
		_deepWaterMaterial.SetShaderParameter("foam_depth", 0.7f);
		_deepWaterMaterial.SetShaderParameter("wave_speed", 0.8f);

		UpdateWaterTransform();
	}

	public void RegenerateWaterMesh()
	{
		CreateWater();

		var cells = Cells;
		if (cells == null) return;

		int w = Width;
		int d = Depth;
		float qs = QuadSize;

		var shallowVerts = new List<Vector3>();
		var shallowNorms = new List<Vector3>();
		var shallowUvs = new List<Vector2>();
		var shallowIndices = new List<int>();

		var deepVerts = new List<Vector3>();
		var deepNorms = new List<Vector3>();
		var deepUvs = new List<Vector2>();
		var deepIndices = new List<int>();

		foreach (var chunk in _chunks)
		{
			shallowVerts.Clear();
			shallowNorms.Clear();
			shallowUvs.Clear();
			shallowIndices.Clear();

			deepVerts.Clear();
			deepNorms.Clear();
			deepUvs.Clear();
			deepIndices.Clear();

			for (int cz = chunk.StartZ; cz < chunk.EndZ; cz++)
			{
				for (int cx = chunk.StartX; cx < chunk.EndX; cx++)
				{
					var cell = cells[cx, cz];
					if (cell.WaterMode == WaterType.None) continue;

					bool isShallow = cell.WaterMode == WaterType.Shallow;
					var vList = isShallow ? shallowVerts : deepVerts;
					var nList = isShallow ? shallowNorms : deepNorms;
					var uvList = isShallow ? shallowUvs : deepUvs;
					var iList = isShallow ? shallowIndices : deepIndices;

					float minNodeY = Math.Min(Math.Min(cell.Y_NW, cell.Y_NE), Math.Min(cell.Y_SW, cell.Y_SE));
					float waterY = minNodeY + WATER_DELTA;

					float x0 = (cx - w / 2.0f) * qs;
					float x1 = (cx + 1 - w / 2.0f) * qs;
					float z0 = (cz - d / 2.0f) * qs;
					float z1 = (cz + 1 - d / 2.0f) * qs;

					int baseIdx = vList.Count;

					vList.Add(new Vector3(x0, waterY, z0));
					vList.Add(new Vector3(x1, waterY, z0));
					vList.Add(new Vector3(x1, waterY, z1));
					vList.Add(new Vector3(x0, waterY, z1));

					nList.Add(Vector3.Up);
					nList.Add(Vector3.Up);
					nList.Add(Vector3.Up);
					nList.Add(Vector3.Up);

					uvList.Add(new Vector2(0, 0));
					uvList.Add(new Vector2(1, 0));
					uvList.Add(new Vector2(1, 1));
					uvList.Add(new Vector2(0, 1));

					iList.Add(baseIdx);
					iList.Add(baseIdx + 1);
					iList.Add(baseIdx + 2);

					iList.Add(baseIdx);
					iList.Add(baseIdx + 2);
					iList.Add(baseIdx + 3);
				}
			}

			UpdateSingleWaterMesh(chunk.ShallowWaterMesh, chunk.ShallowWaterArrayMesh, shallowVerts, shallowNorms, shallowUvs, shallowIndices);
			UpdateSingleWaterMesh(chunk.DeepWaterMesh, chunk.DeepWaterArrayMesh, deepVerts, deepNorms, deepUvs, deepIndices);
		}
	}

	private void UpdateSingleWaterMesh(MeshInstance3D meshInstance, ArrayMesh arrayMesh, List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> indices)
	{
		if (meshInstance == null || arrayMesh == null) return;

		while (arrayMesh.GetSurfaceCount() > 0)
		{
			arrayMesh.ClearSurfaces();
		}

		if (verts.Count == 0 || indices.Count == 0) return;

		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
		arrays[(int)Mesh.ArrayType.Normal] = norms.ToArray();
		arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
		arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();

		arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
	}

	protected void UpdateWaterTransform()
	{
		UpdateWaterSize();
	}

	public void UpdateWaterSize()
	{
		if (_shallowWaterMaterial == null && _deepWaterMaterial == null) return;

		int w = Width;
		int d = Depth;
		float qs = QuadSize;
		float totalW = w * qs;
		float totalD = d * qs;

		Vector2 shroudMin = new Vector2(-totalW / 2.0f, -totalD / 2.0f);
		Vector2 shroudSize = new Vector2(totalW, totalD);

		if (_shallowWaterMaterial != null)
		{
			_shallowWaterMaterial.SetShaderParameter("shroud_world_min", shroudMin);
			_shallowWaterMaterial.SetShaderParameter("shroud_world_size", shroudSize);
		}
		if (_deepWaterMaterial != null)
		{
			_deepWaterMaterial.SetShaderParameter("shroud_world_min", shroudMin);
			_deepWaterMaterial.SetShaderParameter("shroud_world_size", shroudSize);
		}
	}

	public override void _Ready()
	{
		if (_material == null)
		{
			var shader = new Shader();
			shader.Code = @"
shader_type spatial;
render_mode blend_mix, depth_draw_opaque, cull_back;

uniform sampler2DArray terrain_textures : source_color, filter_linear_mipmap_anisotropic;
uniform sampler2DArray terrain_normals_pbr : hint_normal, filter_linear_mipmap_anisotropic;
uniform sampler2DArray cliff_textures : source_color, filter_linear_mipmap_anisotropic;
uniform sampler2DArray cliff_normals_pbr : hint_normal, filter_linear_mipmap_anisotropic;
uniform vec4 swatch_params[32];
uniform vec4 swatch_height_params[32];

uniform sampler2D shroud_texture : hint_default_white;
uniform vec2 shroud_world_min = vec2(-125.0, -125.0);
uniform vec2 shroud_world_size = vec2(250.0, 250.0);

uniform sampler2D pathing_texture : filter_nearest;
uniform bool pathing_visible = false;
uniform bool grid_visible = false;
uniform vec2 terrain_size = vec2(256.0, 256.0);

varying vec3 v_world_pos;
varying vec3 v_world_normal;
varying vec4 v_tex_indices;
varying vec4 v_tex_weights;
varying vec4 v_cliff_indices;
varying vec4 v_cliff_weights;
varying float v_is_cliff;

void vertex() {
	v_world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	v_world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
	v_tex_indices = CUSTOM0;
	v_tex_weights = CUSTOM1;
	v_cliff_indices = CUSTOM2;
	v_cliff_weights = CUSTOM3;
	v_is_cliff = COLOR.r;
}

void fragment() {
	vec2 uv = v_world_pos.xz * 0.1;
	vec3 base_col = vec3(0.3, 0.3, 0.3);
	
	if (v_is_cliff > 0.5) {
		int idx0 = int(v_cliff_indices.x + 0.5);
		base_col = texture(cliff_textures, vec3(uv, float(idx0))).rgb;
	} else {
		int idx0 = int(v_tex_indices.x + 0.5);
		base_col = texture(terrain_textures, vec3(uv, float(idx0))).rgb;
	}

	vec2 shroud_uv = (v_world_pos.xz - shroud_world_min) / shroud_world_size;
	shroud_uv = clamp(shroud_uv, vec2(0.0), vec2(1.0));
	float shroud_val = texture(shroud_texture, shroud_uv).r;
	float visibility = 1.0 - shroud_val;
	base_col = mix(vec3(0.0), base_col, visibility);

	ALBEDO = base_col;
	ROUGHNESS = 0.85;
	SPECULAR = 0.2;
}
";
			_material = new ShaderMaterial();
			_material.Shader = shader;
		}

		ReloadTerrainTextures();
		CreateChunks();
		UpdateMeshAndPhysics();
	}

	public void ApplyQualitySettings(GraphicsQuality quality)
	{
		ApplyQualitySettings((int)quality);
	}

	public void ApplyQualitySettings(int qualityIdx)
	{
		if (_material == null) return;
		_material.SetShaderParameter("quality_level", qualityIdx);
	}

	public static string GetKtxCmdPath()
	{
		string basePath = AppDomain.CurrentDomain.BaseDirectory;
		string ktxExe = System.IO.Path.Combine(basePath, "ktx.exe");
		if (System.IO.File.Exists(ktxExe)) return ktxExe;

		ktxExe = System.IO.Path.Combine(basePath, "tools", "ktx.exe");
		if (System.IO.File.Exists(ktxExe)) return ktxExe;

		return "ktx";
	}

	protected static Texture2DArray? _cachedAlbedoTextureArray;
	protected static Texture2DArray? _cachedNormalRoughnessTextureArray;
	protected static string? _cachedMapDir;
	protected List<string> _loadedTextureList = new List<string>();
	protected Godot.Vector4[] _swatchParamsCache = new Godot.Vector4[32];
	protected Godot.Vector4[] _swatchHeightParamsCache = new Godot.Vector4[32];

	public void ReloadTerrainTextures(bool forceReload = false)
	{
		if (_material == null) return;

		var albedoHeightImages = new Godot.Collections.Array<Image>();
		var normalRoughnessImages = new Godot.Collections.Array<Image>();

		var fb0 = Godot.Image.CreateEmpty(TargetTextureResolution, TargetTextureResolution, false, Godot.Image.Format.Rgba8);
		fb0.Fill(new Color(0.25f, 0.45f, 0.2f, 1.0f));
		fb0.GenerateMipmaps();

		var fb1 = Godot.Image.CreateEmpty(TargetTextureResolution, TargetTextureResolution, false, Godot.Image.Format.Rgba8);
		fb1.Fill(new Color(0.5f, 0.5f, 1.0f, 0.85f));
		fb1.GenerateMipmaps();

		albedoHeightImages.Add(fb0);
		normalRoughnessImages.Add(fb1);

		var albedoTextureArray = new Texture2DArray();
		albedoTextureArray.CreateFromImages(albedoHeightImages);
		var normalTextureArray = new Texture2DArray();
		normalTextureArray.CreateFromImages(normalRoughnessImages);
		_cachedAlbedoTextureArray = albedoTextureArray;
		_cachedNormalRoughnessTextureArray = normalTextureArray;

		_material.SetShaderParameter("terrain_textures", albedoTextureArray);
		_material.SetShaderParameter("terrain_normals_pbr", normalTextureArray);
		_material.SetShaderParameter("cliff_textures", albedoTextureArray);
		_material.SetShaderParameter("cliff_normals_pbr", normalTextureArray);
	}

	protected void CreateChunks()
	{
		foreach (var chunk in _chunks)
		{
			if (GodotObject.IsInstanceValid(chunk.MeshInstance)) chunk.MeshInstance.QueueFree();
			if (GodotObject.IsInstanceValid(chunk.CollisionShape)) chunk.CollisionShape.QueueFree();
			if (GodotObject.IsInstanceValid(chunk.ShallowWaterMesh)) chunk.ShallowWaterMesh.QueueFree();
			if (GodotObject.IsInstanceValid(chunk.DeepWaterMesh)) chunk.DeepWaterMesh.QueueFree();
		}
		_chunks.Clear();

		int w = Width;
		int d = Depth;
		_chunkedWidth = w;
		_chunkedDepth = d;

		for (int z = 0; z < d; z += CHUNK_SIZE)
		{
			for (int x = 0; x < w; x += CHUNK_SIZE)
			{
				int ex = Math.Min(x + CHUNK_SIZE, w);
				int ez = Math.Min(z + CHUNK_SIZE, d);
				
				var chunk = new TerrainChunk
				{
					StartX = x,
					StartZ = z,
					EndX = ex,
					EndZ = ez,
					ArrayMesh = new ArrayMesh()
				};

				chunk.MeshInstance = new MeshInstance3D();
				chunk.MeshInstance.Name = $"TerrainChunk_{x}_{z}";
				chunk.MeshInstance.Mesh = chunk.ArrayMesh;
				chunk.MeshInstance.MaterialOverride = _material;
				AddChild(chunk.MeshInstance);

				chunk.ShallowWaterArrayMesh = new ArrayMesh();
				chunk.ShallowWaterMesh = new MeshInstance3D();
				chunk.ShallowWaterMesh.Name = $"ShallowWaterChunk_{x}_{z}";
				chunk.ShallowWaterMesh.Mesh = chunk.ShallowWaterArrayMesh;
				if (_shallowWaterMaterial != null) chunk.ShallowWaterMesh.MaterialOverride = _shallowWaterMaterial;
				AddChild(chunk.ShallowWaterMesh);

				chunk.DeepWaterArrayMesh = new ArrayMesh();
				chunk.DeepWaterMesh = new MeshInstance3D();
				chunk.DeepWaterMesh.Name = $"DeepWaterChunk_{x}_{z}";
				chunk.DeepWaterMesh.Mesh = chunk.DeepWaterArrayMesh;
				if (_deepWaterMaterial != null) chunk.DeepWaterMesh.MaterialOverride = _deepWaterMaterial;
				AddChild(chunk.DeepWaterMesh);

				chunk.CollisionShape = new CollisionShape3D();
				chunk.CollisionShape.Name = $"TerrainCollision_{x}_{z}";
				
				float lx = (x + (ex - x) / 2.0f - w / 2.0f) * QuadSize;
				float lz = (z + (ez - z) / 2.0f - d / 2.0f) * QuadSize;
				chunk.CollisionShape.Position = new Vector3(lx, 0.0f, lz);
				
				AddChild(chunk.CollisionShape);
				
				_chunks.Add(chunk);
			}
		}
	}

	protected static readonly StringName ShroudTextureParam = "shroud_texture";
	protected ImageTexture _currentShroudTexture = null;
	protected static ImageTexture _clearShroudTexture = null;

	public static ImageTexture GetClearShroudTexture()
	{
		if (_clearShroudTexture == null)
		{
			var img = Image.CreateEmpty(32, 32, false, Image.Format.Rf);
			img.Fill(new Color(0f, 0f, 0f, 1f));
			_clearShroudTexture = ImageTexture.CreateFromImage(img);
		}
		return _clearShroudTexture;
	}

	public void BeginMinimapCapture()
	{
		var clearTex = GetClearShroudTexture();
		if (_material != null)
		{
			_material.SetShaderParameter(ShroudTextureParam, clearTex);
		}
		if (_shallowWaterMaterial != null)
		{
			_shallowWaterMaterial.SetShaderParameter(ShroudTextureParam, clearTex);
		}
		if (_deepWaterMaterial != null)
		{
			_deepWaterMaterial.SetShaderParameter(ShroudTextureParam, clearTex);
		}
		SetAllChunksVisible(true);
	}

	public void EndMinimapCapture()
	{
		if (_currentShroudTexture != null)
		{
			if (_material != null)
			{
				_material.SetShaderParameter(ShroudTextureParam, _currentShroudTexture);
			}
			if (_shallowWaterMaterial != null)
			{
				_shallowWaterMaterial.SetShaderParameter(ShroudTextureParam, _currentShroudTexture);
			}
			if (_deepWaterMaterial != null)
			{
				_deepWaterMaterial.SetShaderParameter(ShroudTextureParam, _currentShroudTexture);
			}
		}
	}

	public void SetShroudTexture(ImageTexture shroudTexture)
	{
		if (shroudTexture == null) return;
		_currentShroudTexture = shroudTexture;
		if (IsMinimapRendering) return;

		if (_material != null)
		{
			_material.SetShaderParameter(ShroudTextureParam, shroudTexture);
		}
		if (_shallowWaterMaterial != null)
		{
			_shallowWaterMaterial.SetShaderParameter(ShroudTextureParam, shroudTexture);
		}
		if (_deepWaterMaterial != null)
		{
			_deepWaterMaterial.SetShaderParameter(ShroudTextureParam, shroudTexture);
		}
	}

	public void SetFogTexture(ImageTexture fogTexture) => SetShroudTexture(fogTexture);

	public void UpdateMeshAndPhysics(bool rebuildPhysics = true, bool rebuildNavMesh = true, Rect2I? affectedRegion = null, bool rebuildWater = true)
	{
		UpdateMeshAndPhysics(rebuildPhysics, rebuildNavMesh, affectedRegion.HasValue ? new[] { affectedRegion.Value } : (IEnumerable<Rect2I>?)null, rebuildWater);
	}

	public void UpdateMeshAndPhysics(bool rebuildPhysics, bool rebuildNavMesh, IEnumerable<Rect2I>? affectedRegions, bool rebuildWater = true)
	{
		int w = Width;
		int d = Depth;

		if (SplatMap == null || SplatMap.GetLength(0) < w + 1 || SplatMap.GetLength(1) < d + 1)
		{
			var newSplatMap = new TerrainSplatWeights[w + 1, d + 1];
			for (int z = 0; z <= d; z++)
			{
				for (int x = 0; x <= w; x++)
				{
					if (SplatMap != null && x < SplatMap.GetLength(0) && z < SplatMap.GetLength(1))
						newSplatMap[x, z] = SplatMap[x, z];
					else
						newSplatMap[x, z] = TerrainSplatWeights.CreateSolid(0);
				}
			}
			SplatMap = newSplatMap;
		}

		if (CliffSplatMap == null || CliffSplatMap.GetLength(0) < w + 1 || CliffSplatMap.GetLength(1) < d + 1)
		{
			var newCliffSplatMap = new TerrainSplatWeights[w + 1, d + 1];
			for (int z = 0; z <= d; z++)
			{
				for (int x = 0; x <= w; x++)
				{
					newCliffSplatMap[x, z] = CliffSplatMap[x, z];
				}
			}
			CliffSplatMap = newCliffSplatMap;
		}

		if (_chunks.Count == 0 || _chunkedWidth != w || _chunkedDepth != d)
		{
			CreateChunks();
		}

		foreach (var chunk in _chunks)
		{
			if (affectedRegions != null)
			{
				bool intersectsAny = false;
				foreach (var region in affectedRegions)
				{
					if (!(chunk.EndX < region.Position.X || chunk.StartX > region.Position.X + region.Size.X ||
						  chunk.EndZ < region.Position.Y || chunk.StartZ > region.Position.Y + region.Size.Y))
					{
						intersectsAny = true;
						break;
					}
				}
				if (!intersectsAny)
				{
					continue;
				}
			}

			UpdateChunkMesh(chunk, rebuildPhysics);
		}

		if (rebuildWater)
		{
			RegenerateWaterMesh();
		}
	}

	public void SanitizeCornerHeights()
	{
		var cells = Cells;
		if (cells == null) return;

		int w = Width;
		int d = Depth;

		for (int gz = 0; gz <= d; gz++)
		{
			for (int gx = 0; gx <= w; gx++)
			{
				float h = GetGridNodeHeight(gx, gz, cells, w, d);

				if (gx > 0 && gz > 0 && gx - 1 < w && gz - 1 < d) cells[gx - 1, gz - 1].Y_SE = h;
				if (gx < w && gz > 0 && gz - 1 < d) cells[gx, gz - 1].Y_SW = h;
				if (gx > 0 && gz < d && gx - 1 < w) cells[gx - 1, gz].Y_NE = h;
				if (gx < w && gz < d) cells[gx, gz].Y_NW = h;
			}
		}
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public float GetGridNodeHeight(int gx, int gz)
	{
		return GetGridNodeHeight(gx, gz, Cells, Width, Depth);
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static float GetGridNodeHeight(int gx, int gz, TerrainCell[,] cells, int w, int d)
	{
		if (cells == null || w <= 0 || d <= 0) return 0f;
		if ((uint)gx < (uint)w && (uint)gz < (uint)d) return cells[gx, gz].Y_NW;
		int cellX = Math.Clamp(gx, 0, w - 1);
		int cellZ = Math.Clamp(gz, 0, d - 1);
		var cell = cells[cellX, cellZ];
		if (gx >= w && gz >= d) return cell.Y_SE;
		if (gx >= w) return cell.Y_NE;
		if (gz >= d) return cell.Y_SW;
		return cell.Y_NW;
	}

	[MethodImpl(MethodImplOptions.AggressiveInlining)]
	public static float GetGridNodeHeight(int gx, int gz, float[,] heights, int w, int d)
	{
		if (heights == null || w <= 0 || d <= 0) return 0f;
		int hW = heights.GetLength(0);
		int hD = heights.GetLength(1);
		int cellX = Math.Clamp(gx, 0, hW - 1);
		int cellZ = Math.Clamp(gz, 0, hD - 1);
		return heights[cellX, cellZ];
	}

	public virtual void SetPathingVisible(bool visible) { }
	public virtual void SetGridVisible(bool visible) { }
	public virtual void SetWireframeMode(bool enabled) { }
	public virtual void ToggleWireframeMode() { }
	public virtual void UpdatePathingTexture() { }
	public virtual void UpdateTextureParamDirect(
		string swatchName,
		string tileMode,
		float uvScale,
		float stochasticTileSize,
		float crossFade = 0.0f,
		float? brightness = null,
		string? tintStr = null,
		float heightScale = 1.0f,
		float heightOffset = 0.0f,
		float crevicePower = 1.0f,
		float edgeNoiseInfluence = 1.0f) { }
	public virtual void ResizeTerrain(int newWidth, int newDepth) { }
	public virtual void ScaleTerrainData(int newWidth, int newDepth) { }
	public virtual void RemapSplatIndices(IReadOnlyDictionary<int, int> remap) { }
	public virtual void RestoreTerrainFromSnapshot(int newWidth, int newDepth, float quadSize, TerrainCell[,] cells, int[,] pathingCodes, TerrainSplatWeights[,] splatMap) { }
	public virtual void RestoreTerrainFromSnapshot(int newWidth, int newDepth, float quadSize, float[,] heights, int[,] pathingCodes, TerrainSplatWeights[,] splatMap) { }
	public virtual void ProcessAndSaveRawTexture(string rawPngPath, string outputKtx2Path) { }

	public void UpdatePhysics(Rect2I? affectedRegion = null)
	{
		foreach (var chunk in _chunks)
		{
			if (affectedRegion.HasValue)
			{
				var region = affectedRegion.Value;
				if (chunk.EndX < region.Position.X || chunk.StartX > region.Position.X + region.Size.X ||
					chunk.EndZ < region.Position.Y || chunk.StartZ > region.Position.Y + region.Size.Y)
				{
					continue;
				}
			}
			UpdateChunkPhysics(chunk);
		}
	}

	protected void UpdateChunkPhysics(TerrainChunk chunk)
	{
		if (chunk.CollisionShape == null) return;

		var cells = Cells;
		if (cells == null) return;

		int w = Width;
		int d = Depth;
		float qs = QuadSize;

		int chunkW = chunk.EndX - chunk.StartX;
		int chunkD = chunk.EndZ - chunk.StartZ;

		int vCount = chunkW * chunkD * 6;
		var collisionFaces = new Vector3[vCount];
		int faceIdx = 0;

		float cxOrigin = (chunk.StartX + chunkW / 2.0f - w / 2.0f) * qs;
		float czOrigin = (chunk.StartZ + chunkD / 2.0f - d / 2.0f) * qs;

		for (int z = chunk.StartZ; z < chunk.EndZ; z++)
		{
			for (int x = chunk.StartX; x < chunk.EndX; x++)
			{
				var cell = cells[x, z];

				float x0 = (x - w / 2.0f) * qs - cxOrigin;
				float x1 = (x + 1 - w / 2.0f) * qs - cxOrigin;
				float z0 = (z - d / 2.0f) * qs - czOrigin;
				float z1 = (z + 1 - d / 2.0f) * qs - czOrigin;

				Vector3 pNW = new Vector3(x0, cell.Y_NW, z0);
				Vector3 pNE = new Vector3(x1, cell.Y_NE, z0);
				Vector3 pSW = new Vector3(x0, cell.Y_SW, z1);
				Vector3 pSE = new Vector3(x1, cell.Y_SE, z1);

				collisionFaces[faceIdx++] = pNW;
				collisionFaces[faceIdx++] = pNE;
				collisionFaces[faceIdx++] = pSE;

				collisionFaces[faceIdx++] = pNW;
				collisionFaces[faceIdx++] = pSE;
				collisionFaces[faceIdx++] = pSW;
			}
		}

		var shape = new ConcavePolygonShape3D();
		shape.SetFaces(collisionFaces);
		chunk.CollisionShape.Shape = shape;
	}

	protected void UpdateChunkMesh(TerrainChunk chunk, bool rebuildPhysics)
	{
		var cells = Cells;
		if (cells == null) return;

		int w = Width;
		int d = Depth;
		float qs = QuadSize;

		int chunkW = chunk.EndX - chunk.StartX;
		int chunkD = chunk.EndZ - chunk.StartZ;
		int quadCount = chunkW * chunkD;

		int maxVerts = quadCount * 4;
		int maxIndices = quadCount * 6;

		if (chunk.VerticesCache == null || chunk.VerticesCache.Length < maxVerts)
		{
			chunk.VerticesCache = new Vector3[maxVerts];
			chunk.NormalsCache = new Vector3[maxVerts];
			chunk.UvsCache = new Vector2[maxVerts];
			chunk.ColorsCache = new Color[maxVerts];
			chunk.TexIndicesCache = new float[maxVerts * 4];
			chunk.TexWeightsCache01 = new float[maxVerts * 4];
			chunk.CliffIndicesCache = new float[maxVerts * 4];
			chunk.CliffWeightsCache = new float[maxVerts * 4];
			chunk.IndicesCache = new int[maxIndices];
		}

		int vIdx = 0;
		int iIdx = 0;

		for (int z = chunk.StartZ; z < chunk.EndZ; z++)
		{
			for (int x = chunk.StartX; x < chunk.EndX; x++)
			{
				var cell = cells[x, z];

				float x0 = (x - w / 2.0f) * qs;
				float x1 = (x + 1 - w / 2.0f) * qs;
				float z0 = (z - d / 2.0f) * qs;
				float z1 = (z + 1 - d / 2.0f) * qs;

				int baseV = vIdx;

				chunk.VerticesCache[vIdx] = new Vector3(x0, cell.Y_NW, z0);
				chunk.NormalsCache[vIdx] = Vector3.Up;
				chunk.UvsCache[vIdx] = new Vector2((float)x / w, (float)z / d);
				chunk.ColorsCache[vIdx] = Colors.White;
				vIdx++;

				chunk.VerticesCache[vIdx] = new Vector3(x1, cell.Y_NE, z0);
				chunk.NormalsCache[vIdx] = Vector3.Up;
				chunk.UvsCache[vIdx] = new Vector2((float)(x + 1) / w, (float)z / d);
				chunk.ColorsCache[vIdx] = Colors.White;
				vIdx++;

				chunk.VerticesCache[vIdx] = new Vector3(x1, cell.Y_SE, z1);
				chunk.NormalsCache[vIdx] = Vector3.Up;
				chunk.UvsCache[vIdx] = new Vector2((float)(x + 1) / w, (float)(z + 1) / d);
				chunk.ColorsCache[vIdx] = Colors.White;
				vIdx++;

				chunk.VerticesCache[vIdx] = new Vector3(x0, cell.Y_SW, z1);
				chunk.NormalsCache[vIdx] = Vector3.Up;
				chunk.UvsCache[vIdx] = new Vector2((float)x / w, (float)(z + 1) / d);
				chunk.ColorsCache[vIdx] = Colors.White;
				vIdx++;

				chunk.IndicesCache[iIdx++] = baseV;
				chunk.IndicesCache[iIdx++] = baseV + 1;
				chunk.IndicesCache[iIdx++] = baseV + 2;

				chunk.IndicesCache[iIdx++] = baseV;
				chunk.IndicesCache[iIdx++] = baseV + 2;
				chunk.IndicesCache[iIdx++] = baseV + 3;
			}
		}

		while (chunk.ArrayMesh.GetSurfaceCount() > 0)
		{
			chunk.ArrayMesh.ClearSurfaces();
		}

		if (vIdx > 0 && iIdx > 0)
		{
			var arrays = new Godot.Collections.Array();
			arrays.Resize((int)Mesh.ArrayType.Max);

			var activeVerts = new Vector3[vIdx];
			Array.Copy(chunk.VerticesCache, activeVerts, vIdx);
			var activeNorms = new Vector3[vIdx];
			Array.Copy(chunk.NormalsCache, activeNorms, vIdx);
			var activeUvs = new Vector2[vIdx];
			Array.Copy(chunk.UvsCache, activeUvs, vIdx);
			var activeColors = new Color[vIdx];
			Array.Copy(chunk.ColorsCache, activeColors, vIdx);
			var activeIndices = new int[iIdx];
			Array.Copy(chunk.IndicesCache, activeIndices, iIdx);

			arrays[(int)Mesh.ArrayType.Vertex] = activeVerts;
			arrays[(int)Mesh.ArrayType.Normal] = activeNorms;
			arrays[(int)Mesh.ArrayType.TexUV] = activeUvs;
			arrays[(int)Mesh.ArrayType.Color] = activeColors;
			arrays[(int)Mesh.ArrayType.Index] = activeIndices;

			chunk.ArrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		}

		if (rebuildPhysics)
		{
			UpdateChunkPhysics(chunk);
		}

		if (IsRuntimeOnly)
		{
			chunk.VerticesCache = null;
			chunk.NormalsCache = null;
			chunk.ColorsCache = null;
			chunk.TexIndicesCache = null;
			chunk.TexWeightsCache01 = null;
			chunk.CliffIndicesCache = null;
			chunk.CliffWeightsCache = null;
			chunk.UvsCache = null;
			chunk.IndicesCache = null;
			chunk.MapDataCache = null;
			chunk.WeldVertexMap = null;
			chunk.WeldRemapTable = null;
		}
	}

	public override void _Process(double delta)
	{
		if (!IsMinimapRendering)
		{
			UpdateFrustumCulling();
		}
	}

	protected void UpdateFrustumCulling()
	{
		var viewport = GetViewport();
		if (viewport == null) return;
		var camera = viewport.GetCamera3D();
		if (camera == null) return;

		var frustum = camera.GetFrustum();
		float qs = QuadSize;
		int w = Width;
		int d = Depth;

		foreach (var chunk in _chunks)
		{
			if (chunk.MeshInstance == null) continue;

			float x0 = (chunk.StartX - w / 2.0f) * qs;
			float x1 = (chunk.EndX - w / 2.0f) * qs;
			float z0 = (chunk.StartZ - d / 2.0f) * qs;
			float z1 = (chunk.EndZ - d / 2.0f) * qs;

			var aabb = new Aabb(new Vector3(x0, -50f, z0), new Vector3(x1 - x0, 150f, z1 - z0));
			bool isVisible = true;
			for (int p = 0; p < frustum.Count; p++)
			{
				var plane = frustum[p];
				if (plane.IsPointOver(aabb.Position) &&
					plane.IsPointOver(aabb.Position + new Vector3(aabb.Size.X, 0, 0)) &&
					plane.IsPointOver(aabb.Position + new Vector3(0, 0, aabb.Size.Z)) &&
					plane.IsPointOver(aabb.Position + new Vector3(aabb.Size.X, 0, aabb.Size.Z)) &&
					plane.IsPointOver(aabb.Position + aabb.Size))
				{
					isVisible = false;
					break;
				}
			}

			chunk.MeshInstance.Visible = isVisible;
			if (chunk.ShallowWaterMesh != null) chunk.ShallowWaterMesh.Visible = isVisible;
			if (chunk.DeepWaterMesh != null) chunk.DeepWaterMesh.Visible = isVisible;
		}
	}

	public void SetAllChunksVisible(bool visible)
	{
		foreach (var chunk in _chunks)
		{
			if (chunk.MeshInstance != null) chunk.MeshInstance.Visible = visible;
			if (chunk.ShallowWaterMesh != null) chunk.ShallowWaterMesh.Visible = visible;
			if (chunk.DeepWaterMesh != null) chunk.DeepWaterMesh.Visible = visible;
		}
	}

	public void BakeNavMesh()
	{
		if (GameHost.Instance == null || GameHost.Instance.EcsWorld == null) return;
		var world = GameHost.Instance.EcsWorld;
		var worldEntity = GameHost.Instance.WorldEntity;
		if (world.IsAlive(worldEntity) && world.Has<TerrainState>(worldEntity))
		{
			ref var state = ref world.Get<TerrainState>(worldEntity);
			var terrainNavMeshService = ServiceLocator.Get<Realm.Ecs.Services.TerrainNavMeshService>();
			terrainNavMeshService.BakeNavMesh(ref state);
			world.Set(worldEntity, state);
		}
	}

	public void GetHeightAndNormal(float worldX, float worldZ, out float height, out Vector3 normal)
	{
		var cells = Cells;
		int w = Width;
		int d = Depth;
		float qs = QuadSize;

		if (cells == null || w <= 0 || d <= 0)
		{
			height = 0f;
			normal = Vector3.Up;
			return;
		}

		float gridX = (worldX / qs) + (w / 2.0f);
		float gridZ = (worldZ / qs) + (d / 2.0f);

		int cx = Math.Clamp((int)MathF.Floor(gridX), 0, w - 1);
		int cz = Math.Clamp((int)MathF.Floor(gridZ), 0, d - 1);

		float u = Math.Clamp(gridX - cx, 0.0f, 1.0f);
		float v = Math.Clamp(gridZ - cz, 0.0f, 1.0f);

		var cell = cells[cx, cz];

		float hNW = cell.Y_NW;
		float hNE = cell.Y_NE;
		float hSW = cell.Y_SW;
		float hSE = cell.Y_SE;

		if (u + v <= 1.0f)
		{
			height = hNW + u * (hNE - hNW) + v * (hSW - hNW);
			Vector3 edge1 = new Vector3(qs, hNE - hNW, 0f);
			Vector3 edge2 = new Vector3(0f, hSW - hNW, qs);
			normal = edge2.Cross(edge1).Normalized();
		}
		else
		{
			height = hSE + (1.0f - u) * (hSW - hSE) + (1.0f - v) * (hNE - hSE);
			Vector3 edge1 = new Vector3(-qs, hSW - hSE, 0f);
			Vector3 edge2 = new Vector3(0f, hNE - hSE, -qs);
			normal = edge2.Cross(edge1).Normalized();
		}
	}

	public readonly struct TerrainVertexKey : IEquatable<TerrainVertexKey>
	{
		private readonly int _positionX;
		private readonly int _positionY;
		private readonly int _positionZ;
		private readonly int _normalX;
		private readonly int _normalY;
		private readonly int _normalZ;
		private readonly int _textureU;
		private readonly int _textureV;

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static int FastRound(float v, float scale) => (int)(v * scale + (v >= 0f ? 0.5f : -0.5f));

		public TerrainVertexKey(Vector3 position, Vector3 normal, Vector2 uv)
		{
			_positionX = FastRound(position.X, 2000.0f);
			_positionY = FastRound(position.Y, 2000.0f);
			_positionZ = FastRound(position.Z, 2000.0f);
			_normalX = FastRound(normal.X, 1000.0f);
			_normalY = FastRound(normal.Y, 1000.0f);
			_normalZ = FastRound(normal.Z, 1000.0f);
			_textureU = FastRound(uv.X, 1000.0f);
			_textureV = FastRound(uv.Y, 1000.0f);
		}

		public bool Equals(TerrainVertexKey other)
		{
			return _positionX == other._positionX &&
				   _positionY == other._positionY &&
				   _positionZ == other._positionZ &&
				   _normalX == other._normalX &&
				   _normalY == other._normalY &&
				   _normalZ == other._normalZ &&
				   _textureU == other._textureU &&
				   _textureV == other._textureV;
		}

		public override bool Equals(object? obj) => obj is TerrainVertexKey other && Equals(other);

		public override int GetHashCode()
		{
			unchecked
			{
				int hash = 17;
				hash = hash * 31 + _positionX;
				hash = hash * 31 + _positionY;
				hash = hash * 31 + _positionZ;
				hash = hash * 31 + _normalX;
				hash = hash * 31 + _normalY;
				hash = hash * 31 + _normalZ;
				hash = hash * 31 + _textureU;
				hash = hash * 31 + _textureV;
				return hash;
			}
		}
	}
}
