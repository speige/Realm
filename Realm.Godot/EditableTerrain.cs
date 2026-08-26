using Arch.Core;
using Realm.Ecs.Components.Terrain;
using DotRecast.Detour;
using Godot;
using System;
using System.Collections.Generic;

public partial class EditableTerrain : StaticBody3D
{
	public const uint TerrainCollisionLayer = 2;
	public static EditableTerrain Instance { get; private set; }
	public static bool IsMinimapRendering { get; set; } = false;

	public override void _ExitTree()
	{
		if (Instance == this) Instance = null;
	}

	public EditableTerrain()
	{
		CollisionLayer = 1U | TerrainCollisionLayer;
	}

	public static Image NormalizeAlbedoLuminance(Image sourceImage, float targetLinearLuminance = 0.35f, float maxScaleFactor = 2.2f)
	{
		return Realm.Godot.Utils.PlayerColorShaderManager.NormalizeAlbedoImage(sourceImage, targetLinearLuminance, 0.2f, maxScaleFactor);
	}

	private static float[,] ComputeSeparableBoxBlur(float[,] input, int w, int h, int radius)
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

	public void ProcessAndSaveRawTexture(string rawPngPath, string outputKtx2Path)
	{
		var img = Godot.Image.LoadFromFile(rawPngPath);
		if (img == null) return;
		img = AutoCropToPowerOfTwoSquare(img);
		
		int w = img.GetWidth();
		int h = img.GetHeight();
		if (img.GetFormat() != Godot.Image.Format.Rgba8)
		{
			img.Convert(Godot.Image.Format.Rgba8);
		}

		img = NormalizeAlbedoLuminance(img);
		
		var layer0 = Godot.Image.CreateEmpty(w, h, false, Godot.Image.Format.Rgba8);
		var layer1 = Godot.Image.CreateEmpty(w, h, false, Godot.Image.Format.Rgba8);

		float[,] luminance = new float[w, h];
		for (int y = 0; y < h; y++)
		{
			for (int x = 0; x < w; x++)
			{
				var color = img.GetPixel(x, y);
				luminance[x, y] = 0.299f * color.R + 0.587f * color.G + 0.114f * color.B;
			}
		}

		float[,] fineMean = ComputeSeparableBoxBlur(luminance, w, h, 3);
		float[,] coarseMean = ComputeSeparableBoxBlur(luminance, w, h, 14);

		float[,] rawHeight = new float[w, h];
		int totalPixels = w * h;
		float[] flatHeights = new float[totalPixels];
		int flatIdx = 0;

		for (int y = 0; y < h; y++)
		{
			int py = y > 0 ? y - 1 : h - 1;
			int ny = y < h - 1 ? y + 1 : 0;

			for (int x = 0; x < w; x++)
			{
				int px = x > 0 ? x - 1 : w - 1;
				int nx = x < w - 1 ? x + 1 : 0;

				float lum = luminance[x, y];
				float highFreq = lum - fineMean[x, y];
				float midFreq = fineMean[x, y] - coarseMean[x, y];

				float dx = (luminance[nx, y] - luminance[px, y]) * 0.5f;
				float dy = (luminance[x, ny] - luminance[x, py]) * 0.5f;
				float gradMag = MathF.Sqrt(dx * dx + dy * dy);
				float laplacian = luminance[nx, y] + luminance[px, y] + luminance[x, ny] + luminance[x, py] - 4.0f * lum;

				float structuralValue = 0.5f + (highFreq * 2.2f) + (midFreq * 1.4f) + (laplacian * 0.5f) - (gradMag * 0.25f);
				rawHeight[x, y] = structuralValue;
				flatHeights[flatIdx++] = structuralValue;
			}
		}

		Array.Sort(flatHeights);
		int p1Index = Math.Clamp((int)(totalPixels * 0.01f), 0, totalPixels - 1);
		int p99Index = Math.Clamp((int)(totalPixels * 0.99f), 0, totalPixels - 1);
		float lowPercentile = flatHeights[p1Index];
		float highPercentile = flatHeights[p99Index];

		float normRange = highPercentile - lowPercentile;
		float invNormRange = normRange > 1e-5f ? 1.0f / normRange : 0.0f;
		float[,] normalizedHeight = new float[w, h];

		for (int y = 0; y < h; y++)
		{
			for (int x = 0; x < w; x++)
			{
				var color = img.GetPixel(x, y);
				float rawVal = rawHeight[x, y];
				float hVal = invNormRange > 0.0f
					? Math.Clamp((rawVal - lowPercentile) * invNormRange, 0.0f, 1.0f)
					: 0.5f;

				normalizedHeight[x, y] = hVal;
				layer0.SetPixel(x, y, new Godot.Color(color.R, color.G, color.B, hVal));
			}
		}
		
		for (int y = 0; y < h; y++)
		{
			for (int x = 0; x < w; x++)
			{
				int px = x > 0 ? x - 1 : w - 1;
				int nx = x < w - 1 ? x + 1 : 0;
				int py = y > 0 ? y - 1 : h - 1;
				int ny = y < h - 1 ? y + 1 : 0;
				
				float h00 = normalizedHeight[px, py];
				float h10 = normalizedHeight[x, py];
				float h20 = normalizedHeight[nx, py];
				
				float h01 = normalizedHeight[px, y];
				float h11 = normalizedHeight[x, y];
				float h21 = normalizedHeight[nx, y];
				
				float h02 = normalizedHeight[px, ny];
				float h12 = normalizedHeight[x, ny];
				float h22 = normalizedHeight[nx, ny];
				
				float dx = ((h20 + 2.0f * h21 + h22) - (h00 + 2.0f * h01 + h02)) / 8.0f;
				float dy = ((h02 + 2.0f * h12 + h22) - (h00 + 2.0f * h10 + h20)) / 8.0f;
				float dz = 1.0f / 4.0f;
				
				var normal = new Godot.Vector3(-dx, -dy, dz).Normalized();
				
				float laplacian = (h10 + h01 + h21 + h12) - 4.0f * h11;
				float height = h11;
				float ao = 1.0f - Godot.Mathf.Clamp(laplacian * 4.0f, 0.0f, 0.5f);
				ao *= (0.65f + 0.35f * height);
				ao = Godot.Mathf.Clamp(ao, 0.0f, 1.0f);

				float r = (normal.X * 0.5f + 0.5f);
				float g = (normal.Y * 0.5f + 0.5f);
				float b = ao;

				float contrastHeight = (height - 0.5f) * 1.4f + 0.5f;
				contrastHeight = Godot.Mathf.Clamp(contrastHeight, 0.0f, 1.0f);
				float highDetail = Math.Abs(luminance[x, y] - fineMean[x, y]);
				float roughness = Godot.Mathf.Clamp(Godot.Mathf.Lerp(0.85f, 0.45f, contrastHeight) + highDetail * 0.8f, 0.15f, 0.95f);

				layer1.SetPixel(x, y, new Godot.Color(r, g, b, roughness));
			}
		}
		
		string tempL0 = $"user://temp_l0_{System.Guid.NewGuid()}.png";
		string tempL1 = $"user://temp_l1_{System.Guid.NewGuid()}.png";
		
		layer0.SavePng(tempL0);
		layer1.SavePng(tempL1);
		
		string globalTempL0 = Godot.ProjectSettings.GlobalizePath(tempL0);
		string globalTempL1 = Godot.ProjectSettings.GlobalizePath(tempL1);
		string globalOutput = Godot.ProjectSettings.GlobalizePath(outputKtx2Path);
		
		string dir = System.IO.Path.GetDirectoryName(globalOutput);
		if (!System.IO.Directory.Exists(dir))
		{
			System.IO.Directory.CreateDirectory(dir);
		}
		
		string ktxCmd = GetKtxCmdPath();
		
		try
		{
			string ktxDir = System.IO.Path.GetDirectoryName(ktxCmd);
			var startInfo = new System.Diagnostics.ProcessStartInfo
			{
				FileName = ktxCmd,
				WorkingDirectory = ktxDir,
				Arguments = $"create --format R8G8B8A8_UNORM --layers 2 --encode uastc --generate-mipmap \"{globalTempL0}\" \"{globalTempL1}\" \"{globalOutput}\"",
				UseShellExecute = false,
				CreateNoWindow = true,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};
			
			using (var process = System.Diagnostics.Process.Start(startInfo))
			{
				string stdout = process.StandardOutput.ReadToEnd();
				string stderr = process.StandardError.ReadToEnd();
				process.WaitForExit();
				if (process.ExitCode != 0)
				{
					throw new System.Exception($"ktx create failed with exit code {process.ExitCode}. Stderr: {stderr}. Stdout: {stdout}");
				}
			}
		}
		catch (System.Exception ex)
		{
			System.Console.Error.WriteLine($"Failed to execute ktx create: {ex.Message}");
			Godot.GD.PrintErr($"Failed to execute ktx create: {ex.Message}");
			throw;
		}
		finally
		{
			if (System.IO.File.Exists(globalTempL0)) System.IO.File.Delete(globalTempL0);
			if (System.IO.File.Exists(globalTempL1)) System.IO.File.Delete(globalTempL1);
		}
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

	private TerrainState GetTerrainStateSafe()
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
		private set
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
		private set
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
		private set
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
		private set
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

	private TerrainCell[,] _localCells;
	private int[,] _localPathingCodes;

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
				}
				return state.PathingCodes;
			}
			if (_localPathingCodes == null)
			{
				_localPathingCodes = new int[Width, Depth];
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
				int targetWidth = value != null ? value.GetLength(0) : state.Width;
				int targetDepth = value != null ? value.GetLength(1) : state.Depth;
				GameHost.Instance.EcsWorld.Set(GameHost.Instance.WorldEntity, new TerrainState(
					targetWidth, targetDepth, state.QuadSize, state.CellSize,
					state.Cells, value, state.NavMesh, state.NavMeshQuery
				));
			}
			_localPathingCodes = value;
		}
	}

	private const int CHUNK_SIZE = 32;
	private int _chunkedWidth;
	private int _chunkedDepth;
	
	private class TerrainChunk
	{
		public MeshInstance3D MeshInstance;
		public CollisionShape3D CollisionShape;
		public ArrayMesh ArrayMesh;
		public ArrayMesh ShallowWaterArrayMesh;
		public MeshInstance3D ShallowWaterMesh;
		public ArrayMesh DeepWaterArrayMesh;
		public MeshInstance3D DeepWaterMesh;
		public Aabb WorldAabb;
		public float MinY;
		public float MaxY;
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
	}
	
	private List<TerrainChunk> _chunks = new List<TerrainChunk>();
	private ShaderMaterial _material;
	public ShaderMaterial Material => _material;
	private ShaderMaterial _shallowWaterMaterial;
	private ShaderMaterial _deepWaterMaterial;

	private void CreateWater()
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

	// --- FLAT WATER SURFACES ---
	vec2 p1 = v_world_pos.xz * 0.25 + vec2(TIME * wave_speed * 0.12, TIME * wave_speed * 0.08);
	vec2 p2 = v_world_pos.xz * 0.35 - vec2(TIME * wave_speed * 0.09, TIME * wave_speed * 0.14);

	float n1 = sin(p1.x + sin(p1.y * 1.2)) * 0.5 + 0.5;
	float n2 = cos(p2.x - cos(p2.y * 1.3)) * 0.5 + 0.5;
	float combined_wave = (n1 + n2) * 0.5;

	vec3 flat_perturbed_normal = normalize(vec3((n1 - 0.5) * 0.25, 1.0, (n2 - 0.5) * 0.25));

	// --- VERTICAL WATERFALL SHADING ---
	float flow_x = (v_world_pos.x + v_world_pos.z) * 0.3;
	float flow_y = v_world_pos.y * 1.2 + TIME * wave_speed * 1.1;

	float streak1 = sin(flow_y * 2.2 + sin(flow_x * 1.8)) * 0.5 + 0.5;
	float streak2 = cos(flow_y * 4.2 - cos(flow_x * 3.1)) * 0.5 + 0.5;
	float waterfall_foam_raw = streak1 * 0.5 + streak2 * 0.5;
	float waterfall_foam = smoothstep(0.25, 0.95, waterfall_foam_raw) * 0.28;

	vec3 wf_normal = normalize(vec3((streak1 - 0.5) * 0.08, (streak2 - 0.5) * 0.08, (streak1 - 0.5) * 0.08));

	vec3 local_normal = mix(wf_normal, flat_perturbed_normal, flat_factor);
	NORMAL = normalize(TANGENT * local_normal.x + BINORMAL * local_normal.z + NORMAL * local_normal.y);

	// --- DEPTH & SHORELINE CALCULATIONS ---
	float depth_raw = texture(depth_texture, SCREEN_UV).r;
	vec4 upos = INV_PROJECTION_MATRIX * vec4(SCREEN_UV * 2.0 - 1.0, depth_raw, 1.0);
	float pixel_z = -upos.z / upos.w;
	float water_z = -VERTEX.z;
	float water_depth = max(0.0, pixel_z - water_z);

	float depth_factor = clamp(water_depth / max_depth, 0.0, 1.0);
	vec4 water_col = mix(shallow_color, deep_color, smoothstep(0.0, 0.85, depth_factor));

	// --- CLEAN SHORELINE FOAM ---
	float shore_fade = mix(1.0, smoothstep(0.001, 0.02, water_depth), flat_factor);
	float shore_proximity = (1.0 - smoothstep(0.0, foam_depth, water_depth)) * shore_fade;

	float pulse = sin(water_depth * 18.0 - TIME * wave_speed * 3.5) * 0.5 + 0.5;
	float flat_foam_mask = smoothstep(0.2, 0.8, shore_proximity * (0.6 + 0.4 * combined_wave) + pulse * shore_proximity * 0.4);

	float final_foam_mask = mix(waterfall_foam, flat_foam_mask, flat_factor);

	// --- STRATEGY SHROUD INTEGRATION ---
	vec2 shroud_uv = (v_world_pos.xz - shroud_world_min) / shroud_world_size;
	float shroud_factor = texture(shroud_texture, clamp(shroud_uv, 0.0, 1.0)).r;

	vec3 final_albedo = mix(water_col.rgb, foam_color.rgb, final_foam_mask * foam_color.a) * (1.0 - shroud_factor * 0.98);

	ALBEDO = final_albedo;
	ALPHA = mix(water_col.a, 0.75, final_foam_mask) * shore_fade;

	ROUGHNESS = mix(mix(0.1, 0.35, waterfall_factor), 0.9, shroud_factor);
	METALLIC = 0.0;
	SPECULAR = mix(0.05, 0.10, waterfall_factor) * (1.0 - shroud_factor * 0.98);
}
";

		_shallowWaterMaterial = new ShaderMaterial();
		_shallowWaterMaterial.Shader = waterShader;
		_shallowWaterMaterial.SetShaderParameter("shallow_color", new Color(0.05f, 0.30f, 0.35f, 0.50f));
		_shallowWaterMaterial.SetShaderParameter("deep_color", new Color(0.03f, 0.20f, 0.25f, 0.70f));
		_shallowWaterMaterial.SetShaderParameter("max_depth", 1.5f);

		_deepWaterMaterial = new ShaderMaterial();
		_deepWaterMaterial.Shader = waterShader;

		_deepWaterMaterial.SetShaderParameter("shallow_color", new Color(0.02f, 0.12f, 0.28f, 0.70f));
		_deepWaterMaterial.SetShaderParameter("deep_color", new Color(0.005f, 0.03f, 0.12f, 0.99f));
		_deepWaterMaterial.SetShaderParameter("max_depth", 1.5f);

		foreach (var chunk in _chunks)
		{
			if (chunk.ShallowWaterMesh != null) chunk.ShallowWaterMesh.MaterialOverride = _shallowWaterMaterial;
			if (chunk.DeepWaterMesh != null) chunk.DeepWaterMesh.MaterialOverride = _deepWaterMaterial;
		}

		RegenerateWaterMesh();
	}

	private (float waterY, WaterType waterMode) GetCellWaterInfo(int x, int z, TerrainCell[,] cells, int w, int d)
	{
		var cell = cells[x, z];
		if (cell.WaterMode != WaterType.None)
		{
			return ((cell.MacroTier * TerrainCell.TIER_HEIGHT) + WATER_DELTA, cell.WaterMode);
		}

		float minCornerH = Math.Min(Math.Min(cell.Y_NW, cell.Y_NE), Math.Min(cell.Y_SE, cell.Y_SW));
		float maxWaterY = -9999f;
		WaterType bestWaterMode = WaterType.None;

		for (int nz = Math.Max(0, z - 1); nz <= Math.Min(d - 1, z + 1); nz++)
		{
			for (int nx = Math.Max(0, x - 1); nx <= Math.Min(w - 1, x + 1); nx++)
			{
				if (nx == x && nz == z) continue;
				var nCell = cells[nx, nz];
				if (nCell.WaterMode != WaterType.None)
				{
					float nWaterY = (nCell.MacroTier * TerrainCell.TIER_HEIGHT) + WATER_DELTA;
					if (nWaterY > maxWaterY)
					{
						maxWaterY = nWaterY;
						bestWaterMode = nCell.WaterMode;
					}
				}
			}
		}

		if (bestWaterMode != WaterType.None && minCornerH <= maxWaterY)
		{
			return (maxWaterY, bestWaterMode);
		}

		return (0f, WaterType.None);
	}

	public void RegenerateWaterMesh()
	{
		var cells = Cells;
		if (cells == null) return;

		int w = Width;
		int d = Depth;
		float quadSize = QuadSize;
		float halfWQuadSize = (w / 2.0f) * quadSize;
		float halfDQuadSize = (d / 2.0f) * quadSize;

		foreach (var chunk in _chunks)
		{
			if (chunk.ShallowWaterMesh == null)
			{
				chunk.ShallowWaterArrayMesh = new ArrayMesh();
				chunk.ShallowWaterMesh = new MeshInstance3D();
				chunk.ShallowWaterMesh.Name = $"ShallowWaterChunk_{chunk.StartX}_{chunk.StartZ}";
				chunk.ShallowWaterMesh.Mesh = chunk.ShallowWaterArrayMesh;
				if (_shallowWaterMaterial != null) chunk.ShallowWaterMesh.MaterialOverride = _shallowWaterMaterial;
				AddChild(chunk.ShallowWaterMesh);
			}

			if (chunk.DeepWaterMesh == null)
			{
				chunk.DeepWaterArrayMesh = new ArrayMesh();
				chunk.DeepWaterMesh = new MeshInstance3D();
				chunk.DeepWaterMesh.Name = $"DeepWaterChunk_{chunk.StartX}_{chunk.StartZ}";
				chunk.DeepWaterMesh.Mesh = chunk.DeepWaterArrayMesh;
				if (_deepWaterMaterial != null) chunk.DeepWaterMesh.MaterialOverride = _deepWaterMaterial;
				AddChild(chunk.DeepWaterMesh);
			}

			List<Vector3> shallowVerts = new();
			List<Vector3> shallowNorms = new();
			List<Vector2> shallowUVs = new();
			List<int> shallowIndices = new();

			List<Vector3> deepVerts = new();
			List<Vector3> deepNorms = new();
			List<Vector2> deepUVs = new();
			List<int> deepIndices = new();

			for (int z = chunk.StartZ; z < chunk.EndZ; z++)
			{
				for (int x = chunk.StartX; x < chunk.EndX; x++)
				{
					var (waterY1, activeWaterMode1) = GetCellWaterInfo(x, z, cells, w, d);
					if (activeWaterMode1 == WaterType.None) continue;

					Vector3 nw = new Vector3(x * quadSize - halfWQuadSize, waterY1, z * quadSize - halfDQuadSize);
					Vector3 ne = new Vector3((x + 1) * quadSize - halfWQuadSize, waterY1, z * quadSize - halfDQuadSize);
					Vector3 se = new Vector3((x + 1) * quadSize - halfWQuadSize, waterY1, (z + 1) * quadSize - halfDQuadSize);
					Vector3 sw = new Vector3(x * quadSize - halfWQuadSize, waterY1, (z + 1) * quadSize - halfDQuadSize);

					bool isShallow1 = (activeWaterMode1 == WaterType.Shallow);
					var targetVerts = isShallow1 ? shallowVerts : deepVerts;
					var targetNorms = isShallow1 ? shallowNorms : deepNorms;
					var targetUVs = isShallow1 ? shallowUVs : deepUVs;
					var targetIndices = isShallow1 ? shallowIndices : deepIndices;

					int baseIdx = targetVerts.Count;

					targetVerts.Add(nw);
					targetVerts.Add(ne);
					targetVerts.Add(se);
					targetVerts.Add(sw);

					Vector3 normal = Vector3.Up;
					targetNorms.Add(normal);
					targetNorms.Add(normal);
					targetNorms.Add(normal);
					targetNorms.Add(normal);

					targetUVs.Add(new Vector2(0, 0));
					targetUVs.Add(new Vector2(1, 0));
					targetUVs.Add(new Vector2(1, 1));
					targetUVs.Add(new Vector2(0, 1));

					targetIndices.Add(baseIdx + 0);
					targetIndices.Add(baseIdx + 1);
					targetIndices.Add(baseIdx + 2);

					targetIndices.Add(baseIdx + 0);
					targetIndices.Add(baseIdx + 2);
					targetIndices.Add(baseIdx + 3);

					// Build water ramp wall quads along boundaries between water cells of differing height levels
					if (x + 1 < w)
					{
						var (waterY2, activeWaterMode2) = GetCellWaterInfo(x + 1, z, cells, w, d);
						if (activeWaterMode2 != WaterType.None && MathF.Abs(waterY1 - waterY2) > 0.01f)
						{
							float yMin = MathF.Min(waterY1, waterY2);
							float yMax = MathF.Max(waterY1, waterY2);
							float wallX = (x + 1) * quadSize - halfWQuadSize;
							float z0 = z * quadSize - halfDQuadSize;
							float z1 = (z + 1) * quadSize - halfDQuadSize;

							bool wallShallow = isShallow1 || (activeWaterMode2 == WaterType.Shallow);
							var wVerts = wallShallow ? shallowVerts : deepVerts;
							var wNorms = wallShallow ? shallowNorms : deepNorms;
							var wUVs = wallShallow ? shallowUVs : deepUVs;
							var wIndices = wallShallow ? shallowIndices : deepIndices;

							int wBase = wVerts.Count;
							wVerts.Add(new Vector3(wallX, yMin, z0));
							wVerts.Add(new Vector3(wallX, yMin, z1));
							wVerts.Add(new Vector3(wallX, yMax, z1));
							wVerts.Add(new Vector3(wallX, yMax, z0));

							Vector3 wallNorm = (waterY1 > waterY2) ? Vector3.Right : Vector3.Left;
							wNorms.Add(wallNorm); wNorms.Add(wallNorm); wNorms.Add(wallNorm); wNorms.Add(wallNorm);
							wUVs.Add(new Vector2(0, 0)); wUVs.Add(new Vector2(1, 0)); wUVs.Add(new Vector2(1, 1)); wUVs.Add(new Vector2(0, 1));

							wIndices.Add(wBase + 0); wIndices.Add(wBase + 1); wIndices.Add(wBase + 2);
							wIndices.Add(wBase + 0); wIndices.Add(wBase + 2); wIndices.Add(wBase + 3);
							wIndices.Add(wBase + 0); wIndices.Add(wBase + 2); wIndices.Add(wBase + 1);
							wIndices.Add(wBase + 0); wIndices.Add(wBase + 3); wIndices.Add(wBase + 2);
						}
					}

					if (z + 1 < d)
					{
						var (waterY2, activeWaterMode2) = GetCellWaterInfo(x, z + 1, cells, w, d);
						if (activeWaterMode2 != WaterType.None && MathF.Abs(waterY1 - waterY2) > 0.01f)
						{
							float yMin = MathF.Min(waterY1, waterY2);
							float yMax = MathF.Max(waterY1, waterY2);
							float wallZ = (z + 1) * quadSize - halfDQuadSize;
							float x0 = x * quadSize - halfWQuadSize;
							float x1 = (x + 1) * quadSize - halfWQuadSize;

							bool wallShallow = isShallow1 || (activeWaterMode2 == WaterType.Shallow);
							var wVerts = wallShallow ? shallowVerts : deepVerts;
							var wNorms = wallShallow ? shallowNorms : deepNorms;
							var wUVs = wallShallow ? shallowUVs : deepUVs;
							var wIndices = wallShallow ? shallowIndices : deepIndices;

							int wBase = wVerts.Count;
							wVerts.Add(new Vector3(x0, yMin, wallZ));
							wVerts.Add(new Vector3(x1, yMin, wallZ));
							wVerts.Add(new Vector3(x1, yMax, wallZ));
							wVerts.Add(new Vector3(x0, yMax, wallZ));

							Vector3 wallNorm = (waterY1 > waterY2) ? Vector3.Back : Vector3.Forward;
							wNorms.Add(wallNorm); wNorms.Add(wallNorm); wNorms.Add(wallNorm); wNorms.Add(wallNorm);
							wUVs.Add(new Vector2(0, 0)); wUVs.Add(new Vector2(1, 0)); wUVs.Add(new Vector2(1, 1)); wUVs.Add(new Vector2(0, 1));

							wIndices.Add(wBase + 0); wIndices.Add(wBase + 1); wIndices.Add(wBase + 2);
							wIndices.Add(wBase + 0); wIndices.Add(wBase + 2); wIndices.Add(wBase + 3);
							wIndices.Add(wBase + 0); wIndices.Add(wBase + 2); wIndices.Add(wBase + 1);
							wIndices.Add(wBase + 0); wIndices.Add(wBase + 3); wIndices.Add(wBase + 2);
						}
					}
				}
			}

			UpdateSingleWaterMesh(chunk.ShallowWaterMesh, chunk.ShallowWaterArrayMesh, shallowVerts, shallowNorms, shallowUVs, shallowIndices);
			UpdateSingleWaterMesh(chunk.DeepWaterMesh, chunk.DeepWaterArrayMesh, deepVerts, deepNorms, deepUVs, deepIndices);
		}
	}

	private void UpdateSingleWaterMesh(MeshInstance3D meshInstance, ArrayMesh arrayMesh, List<Vector3> verts, List<Vector3> norms, List<Vector2> uvs, List<int> indices)
	{
		if (meshInstance == null || arrayMesh == null) return;
		arrayMesh.ClearSurfaces();
		if (verts.Count == 0) return;

		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = verts.ToArray();
		arrays[(int)Mesh.ArrayType.Normal] = norms.ToArray();
		arrays[(int)Mesh.ArrayType.TexUV] = uvs.ToArray();
		arrays[(int)Mesh.ArrayType.Index] = indices.ToArray();

		arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
	}

	private void UpdateWaterTransform()
	{
		RegenerateWaterMesh();
	}

	public void UpdateWaterSize()
	{
		RegenerateWaterMesh();
	}

	public override void _Ready()
	{
		Instance = this;
		CollisionLayer = 1U | TerrainCollisionLayer;
		var cells = Cells;
		if (cells == null || cells.GetLength(0) != Width || cells.GetLength(1) != Depth)
		{
			var newCells = new TerrainCell[Width, Depth];
			Cells = newCells;
			cells = newCells;
		}

		if (SplatMap == null || SplatMap.GetLength(0) < Width + 1 || SplatMap.GetLength(1) < Depth + 1)
		{
			var newSplatMap = new TerrainSplatWeights[Width + 1, Depth + 1];
			for (int z = 0; z <= Depth; z++)
			{
				for (int x = 0; x <= Width; x++)
				{
					if (SplatMap != null && x < SplatMap.GetLength(0) && z < SplatMap.GetLength(1))
						newSplatMap[x, z] = SplatMap[x, z];
					else
						newSplatMap[x, z] = TerrainSplatWeights.CreateSolid(0);
				}
			}
			SplatMap = newSplatMap;
		}

		if (CliffSplatMap == null || CliffSplatMap.GetLength(0) < Width + 1 || CliffSplatMap.GetLength(1) < Depth + 1)
		{
			var newCliffSplatMap = new TerrainSplatWeights[Width + 1, Depth + 1];
			for (int z = 0; z <= Depth; z++)
			{
				for (int x = 0; x <= Width; x++)
				{
					if (CliffSplatMap != null && x < CliffSplatMap.GetLength(0) && z < CliffSplatMap.GetLength(1))
						newCliffSplatMap[x, z] = CliffSplatMap[x, z];
					else
						newCliffSplatMap[x, z] = TerrainSplatWeights.CreateSolid(0);
				}
			}
			CliffSplatMap = newCliffSplatMap;
		}

		var state = GetTerrainStateSafe();
		if (state.PathingCodes == null || state.PathingCodes.GetLength(0) != Width || state.PathingCodes.GetLength(1) != Depth)
		{
			var newPathing = new int[Width, Depth];
			for (int z = 0; z < Depth; z++)
				for (int x = 0; x < Width; x++)
				{
					newPathing[x, z] = GetDefaultPathingCode(cells[x, z]);
				}
			PathingCodes = newPathing;
		}

		var shader = new Shader();
		shader.Code = @"
shader_type spatial;
render_mode blend_mix;

uniform sampler2DArray terrain_textures : source_color, filter_linear_mipmap_anisotropic;
uniform sampler2DArray terrain_normals_pbr : hint_default_white, filter_linear_mipmap_anisotropic;
uniform float blend_softness = 0.04;
uniform float blend_noise_strength = 0.22;
uniform float blend_noise_scale = 0.22;
uniform float cliff_jitter_strength = 1.0;
uniform float cliff_jitter_scale = 0.20;
uniform sampler2D shroud_texture : hint_default_white;
uniform vec2 shroud_world_min = vec2(-125.0, -125.0);
uniform vec2 shroud_world_size = vec2(250.0, 250.0);

uniform sampler2D pathing_texture : hint_default_transparent, filter_nearest;
uniform bool pathing_visible = false;

uniform bool grid_visible = false;
uniform vec4 grid_color_thick = vec4(1.0, 0.9, 0.0, 0.85);
uniform vec4 grid_color_thin = vec4(1.0, 0.9, 0.0, 0.25);
uniform float grid_spacing = 2.0;
uniform vec2 terrain_size = vec2(1.0, 1.0);

uniform float texture_scale = 0.5;
uniform float macro_scale = 0.035;

uniform float uv_warp_strength = 0.8;
uniform float macro_albedo_contrast = 0.10;
uniform float macro_roughness_contrast = 0.08;
uniform float macro_normal_strength = 0.15;
uniform float macro_lacunarity = 2.0;
uniform float macro_gain = 0.5;

uniform bool enable_macro_noise = true;
uniform bool enable_height_blend = true;
uniform bool enable_normal_mapping = true;
uniform float specular_amt = 0.2;

uniform vec4 swatch_params[32];
uniform vec4 swatch_height_params[32];

varying flat vec4 v_tex_indices;
varying vec4 v_tex_weights;
varying flat vec4 v_cliff_tex_indices;
varying vec4 v_cliff_tex_weights;
varying vec3 v_world_pos;
varying vec3 v_world_normal;
varying vec4 v_color;
varying flat float v_cliff_factor;

float macro_hash(vec2 p) {
	ivec2 ip = ivec2(floor(p));
	uvec2 q = uvec2(ip);
	q = q * uvec2(1597334677u, 3812015801u);
	uint n = (q.x ^ q.y) * 1597334677u;
	return float(n) * (1.0 / 4294967295.0);
}

float macro_noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(macro_hash(i + vec2(0.0, 0.0)), macro_hash(i + vec2(1.0, 0.0)), u.x),
	           mix(macro_hash(i + vec2(0.0, 1.0)), macro_hash(i + vec2(1.0, 1.0)), u.x), u.y);
}

float macro_fbm(vec2 p) {
	float v = 0.0;
	float a = 0.5;
	mat2 rot = mat2(vec2(0.8, -0.6), vec2(0.6, 0.8));
	for (int i = 0; i < 4; ++i) {
		v += a * macro_noise(p);
		p = rot * p * 2.0 + vec2(100.0);
		a *= 0.5;
	}
	return v;
}

vec4 sample_quad_array(sampler2DArray tex_array, float layer, vec2 uv) {
	return texture(tex_array, vec3(uv, layer));
}

vec2 stochastic_hash(vec2 p) {
	ivec2 ip = ivec2(floor(p));
	uvec2 q = uvec2(ip);
	q = q * uvec2(1597334677u, 3812015801u);
	uint n = (q.x ^ q.y) * 1597334677u;
	return vec2(float(n & 0xFFFFu), float((n >> 16u) & 0xFFFFu)) * (1.0 / 65535.0);
}

vec4 sample_semi_grid(sampler2DArray tex_array, float layer, vec2 uv, vec2 dx, vec2 dy, float blend_width) {
	if (blend_width <= 0.0001) {
		return textureGrad(tex_array, vec3(uv, layer), dx, dy);
	}
	vec2 f = fract(uv);
	vec2 edge = smoothstep(vec2(0.0), vec2(blend_width), f) * 
	            smoothstep(vec2(0.0), vec2(blend_width), vec2(1.0) - f);
	float center_weight = edge.x * edge.y;

	vec4 col_center = textureGrad(tex_array, vec3(uv, layer), dx, dy);

	if (center_weight > 0.99) {
		return col_center;
	}

	vec2 cell = floor(uv);
	vec2 offset = stochastic_hash(cell);
	vec4 col_border = textureGrad(tex_array, vec3(uv + offset, layer), dx, dy);

	return mix(col_border, col_center, center_weight);
}

vec4 sample_stochastic_layer(sampler2DArray tex_array, float layer, vec2 uv, vec2 dx, vec2 dy, float tile_mode, float stoch_tile_size, float cross_fade, bool is_vector_data) {
	if (tile_mode < 0.5) {
		return textureGrad(tex_array, vec3(uv, layer), dx, dy);
	}

	const float F2 = 0.36602540378;
	const float G2 = 0.2113248654;

	vec2 stoch_uv = uv / max(0.01, stoch_tile_size);
	vec2 skew_uv = stoch_uv + (stoch_uv.x + stoch_uv.y) * F2;
	vec2 i = floor(skew_uv);
	vec2 f = fract(skew_uv);

	vec2 i0, i1, i2;
	vec3 w;

	if (f.x > f.y) {
		i0 = i;
		i1 = i + vec2(1.0, 0.0);
		i2 = i + vec2(1.0, 1.0);
		w = vec3(1.0 - f.x, f.x - f.y, f.y);
	} else {
		i0 = i;
		i1 = i + vec2(0.0, 1.0);
		i2 = i + vec2(1.0, 1.0);
		w = vec3(1.0 - f.y, f.y - f.x, f.x);
	}

	vec2 off0 = stochastic_hash(i0);
	vec2 off1 = stochastic_hash(i1);
	vec2 off2 = stochastic_hash(i2);

	vec4 col0 = textureGrad(tex_array, vec3(uv + off0, layer), dx, dy);
	vec4 col1 = textureGrad(tex_array, vec3(uv + off1, layer), dx, dy);
	vec4 col2 = textureGrad(tex_array, vec3(uv + off2, layer), dx, dy);

	if (is_vector_data) {
		return col0 * w.x + col1 * w.y + col2 * w.z;
	}

	vec3 mean_color = col0.rgb * w.x + col1.rgb * w.y + col2.rgb * w.z;
	return vec4(mean_color, col0.a * w.x + col1.a * w.y + col2.a * w.z);
}

vec4 sample_planar_layer(sampler2DArray tex_array, float layer, vec2 uv_y, vec2 dx_y, vec2 dy_y, bool is_vector_data) {
	int layer_idx = int(clamp(round(layer), 0.0, 31.0));
	vec4 params = swatch_params[layer_idx];
	float tile_mode = params.x;
	float uv_scale = params.y > 0.001 ? params.y : 1.0;
	float stoch_tile_size = params.z > 0.001 ? params.z : 1.0;
	float cross_fade = clamp(params.w, 0.0, 0.10);

	vec2 scaled_uv_y = uv_y * uv_scale;
	vec2 scaled_dx_y = dx_y * uv_scale;
	vec2 scaled_dy_y = dy_y * uv_scale;

	return sample_stochastic_layer(tex_array, layer, scaled_uv_y, scaled_dx_y, scaled_dy_y, tile_mode, stoch_tile_size, cross_fade, is_vector_data);
}

vec3 get_active_weights(float layer, vec3 weights) {
	int layer_idx = int(clamp(round(layer), 0.0, 31.0));
	float tile_mode = swatch_params[layer_idx].x;
	if (tile_mode < 0.5) {
		float max_w = max(weights.x, max(weights.y, weights.z));
		if (weights.y >= max_w) return vec3(0.0, 1.0, 0.0);
		if (weights.x >= max_w) return vec3(1.0, 0.0, 0.0);
		return vec3(0.0, 0.0, 1.0);
	}
	return weights;
}

vec4 sample_triplanar_layer(sampler2DArray tex_array, float layer, vec2 uv_x, vec2 uv_y, vec2 uv_z, vec2 dx_x, vec2 dy_x, vec2 dx_y, vec2 dy_y, vec2 dx_z, vec2 dy_z, vec3 weights, bool is_vector_data) {
	int layer_idx = int(clamp(round(layer), 0.0, 31.0));
	vec4 params = swatch_params[layer_idx];
	float tile_mode = params.x;
	float uv_scale = params.y > 0.001 ? params.y : 1.0;
	float stoch_tile_size = params.z > 0.001 ? params.z : 1.0;
	float cross_fade = clamp(params.w, 0.0, 0.10);

	vec2 scaled_uv_x = uv_x * uv_scale;
	vec2 scaled_uv_y = uv_y * uv_scale;
	vec2 scaled_uv_z = uv_z * uv_scale;

	vec2 scaled_dx_x = dx_x * uv_scale;
	vec2 scaled_dy_x = dy_x * uv_scale;
	vec2 scaled_dx_y = dx_y * uv_scale;
	vec2 scaled_dy_y = dy_y * uv_scale;
	vec2 scaled_dx_z = dx_z * uv_scale;
	vec2 scaled_dy_z = dy_z * uv_scale;

	vec3 active_weights = get_active_weights(layer, weights);

	if (active_weights.y > 0.99) {
		return sample_stochastic_layer(tex_array, layer, scaled_uv_y, scaled_dx_y, scaled_dy_y, tile_mode, stoch_tile_size, cross_fade, is_vector_data);
	}
	if (active_weights.x > 0.99) {
		return sample_stochastic_layer(tex_array, layer, scaled_uv_x, scaled_dx_x, scaled_dy_x, tile_mode, stoch_tile_size, cross_fade, is_vector_data);
	}
	if (active_weights.z > 0.99) {
		return sample_stochastic_layer(tex_array, layer, scaled_uv_z, scaled_dx_z, scaled_dy_z, tile_mode, stoch_tile_size, cross_fade, is_vector_data);
	}

	vec4 col_x = sample_stochastic_layer(tex_array, layer, scaled_uv_x, scaled_dx_x, scaled_dy_x, tile_mode, stoch_tile_size, cross_fade, is_vector_data);
	vec4 col_y = sample_stochastic_layer(tex_array, layer, scaled_uv_y, scaled_dx_y, scaled_dy_y, tile_mode, stoch_tile_size, cross_fade, is_vector_data);
	vec4 col_z = sample_stochastic_layer(tex_array, layer, scaled_uv_z, scaled_dx_z, scaled_dy_z, tile_mode, stoch_tile_size, cross_fade, is_vector_data);
	return col_x * active_weights.x + col_y * active_weights.y + col_z * active_weights.z;
}

vec3 unpack_triplanar_normal(vec4 norm_pbr, vec3 blend_w, vec3 geom_norm) {
	vec2 t_xy = norm_pbr.rg * 2.0 - 1.0;
	float t_z = sqrt(max(0.0, 1.0 - dot(t_xy, t_xy)));

	vec3 sign_n = sign(geom_norm);
	sign_n.x = sign_n.x == 0.0 ? 1.0 : sign_n.x;
	sign_n.y = sign_n.y == 0.0 ? 1.0 : sign_n.y;
	sign_n.z = sign_n.z == 0.0 ? 1.0 : sign_n.z;

	vec3 n_x = vec3(t_z * sign_n.x, t_xy.y, t_xy.x * sign_n.x);
	vec3 n_y = vec3(t_xy.x, t_z * sign_n.y, t_xy.y);
	vec3 n_z = vec3(t_xy.x * sign_n.z, t_xy.y, t_z * sign_n.z);

	vec3 world_n = n_x * blend_w.x + n_y * blend_w.y + n_z * blend_w.z;
	return normalize(world_n);
}

void vertex() {
	v_tex_indices = CUSTOM0;
	v_tex_weights = CUSTOM1;
	v_cliff_tex_indices = CUSTOM2;
	v_cliff_tex_weights = CUSTOM3;
	v_color = vec4(1.0, 1.0, 1.0, 1.0);
	v_cliff_factor = COLOR.r;

	vec3 world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	float cliff_mask = clamp(COLOR.a, 0.0, 1.0);

	if (cliff_jitter_strength > 0.0 && cliff_mask > 0.001) {
		vec2 jitter_uv_x = (world_pos.xz + vec2(world_pos.y * 0.75, world_pos.y * 1.25)) * cliff_jitter_scale;
		vec2 jitter_uv_z = (world_pos.xz + vec2(world_pos.y * 1.35 + 43.1, world_pos.y * 0.65 + 19.7)) * cliff_jitter_scale;
		float noise_x = (macro_fbm(jitter_uv_x) - 0.5) * 2.0;
		float noise_z = (macro_fbm(jitter_uv_z) - 0.5) * 2.0;
		vec3 world_disp = vec3(noise_x, 0.0, noise_z) * (cliff_jitter_strength * cliff_mask);
		world_pos += world_disp;
		VERTEX = (inverse(MODEL_MATRIX) * vec4(world_pos, 1.0)).xyz;
	}

	v_world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
	v_world_pos = world_pos;
}

void fragment() {
	vec3 geom_normal = normalize(v_world_normal);

	vec3 pos_warped = v_world_pos;
	float base_fbm_val = 0.5;

	if (enable_macro_noise) {
		base_fbm_val = macro_fbm(v_world_pos.xz * macro_scale);
		if (uv_warp_strength > 0.0) {
			float warp_x = (base_fbm_val - 0.5) * 2.0;
			float warp_y = (macro_fbm((v_world_pos.xz + vec2(17.3, 31.7)) * macro_scale) - 0.5) * 2.0;
			vec2 warp_offset = vec2(warp_x, warp_y);
			pos_warped += vec3(warp_offset.x, warp_offset.y, warp_offset.y) * uv_warp_strength * 0.05 / max(0.001, macro_scale);
		}
	}

	float slope_cliff_factor = v_cliff_factor > 0.5 ? 1.0 : 0.0;

	vec3 terrain_color = vec3(0.0);
	vec3 blended_world_normal = geom_normal;
	float blended_ao = 1.0;
	float blended_roughness = 0.85;

	if (slope_cliff_factor < 0.5) {
		// --- GROUND PIPELINE ---
		vec2 uv_y = pos_warped.xz * texture_scale;
		vec2 dx_y = dFdx(uv_y);
		vec2 dy_y = dFdy(uv_y);

		vec4 raw_weights = v_tex_weights;
		raw_weights.x = raw_weights.x < 0.001 ? 0.0 : raw_weights.x;
		raw_weights.y = raw_weights.y < 0.001 ? 0.0 : raw_weights.y;
		raw_weights.z = raw_weights.z < 0.001 ? 0.0 : raw_weights.z;
		raw_weights.w = raw_weights.w < 0.001 ? 0.0 : raw_weights.w;

		float weight_sum = raw_weights.x + raw_weights.y + raw_weights.z + raw_weights.w;
		vec4 norm_weights = weight_sum > 0.0001 ? raw_weights / weight_sum : vec4(1.0, 0.0, 0.0, 0.0);

		float max_incoming_weight = max(max(norm_weights.x, norm_weights.y), max(norm_weights.z, norm_weights.w));
		float blend_transition_factor = clamp((1.0 - max_incoming_weight) * 2.5, 0.0, 1.0);

		if (blend_noise_strength > 0.0 && blend_transition_factor > 0.001) {
			vec2 blend_noise_uv = v_world_pos.xz * blend_noise_scale;
			float noise_ch_a = (macro_noise(blend_noise_uv) * 0.70 + macro_noise(blend_noise_uv * 2.13 + vec2(11.7, 7.3)) * 0.30) - 0.5;
			float noise_ch_b = (macro_noise(blend_noise_uv + vec2(19.3, 37.7)) * 0.70 + macro_noise(blend_noise_uv * 2.13 + vec2(43.3, 19.7)) * 0.30) - 0.5;
			vec4 weight_noise_offset = vec4(noise_ch_a, -noise_ch_a, noise_ch_b, -noise_ch_b) * (blend_noise_strength * blend_transition_factor);

			vec4 active_channel_mask = step(vec4(0.001), norm_weights);
			vec4 perturbed_weights = max(vec4(0.0), norm_weights + weight_noise_offset * active_channel_mask);
			float perturbed_weight_sum = perturbed_weights.x + perturbed_weights.y + perturbed_weights.z + perturbed_weights.w;
			norm_weights = perturbed_weight_sum > 0.0001 ? perturbed_weights / perturbed_weight_sum : norm_weights;
		}

		vec4 c0 = raw_weights.x > 0.001 ? sample_planar_layer(terrain_textures, round(v_tex_indices.x), uv_y, dx_y, dy_y, false) : vec4(0.0);
		vec4 c1 = raw_weights.y > 0.001 ? sample_planar_layer(terrain_textures, round(v_tex_indices.y), uv_y, dx_y, dy_y, false) : vec4(0.0);
		vec4 c2 = raw_weights.z > 0.001 ? sample_planar_layer(terrain_textures, round(v_tex_indices.z), uv_y, dx_y, dy_y, false) : vec4(0.0);
		vec4 c3 = raw_weights.w > 0.001 ? sample_planar_layer(terrain_textures, round(v_tex_indices.w), uv_y, dx_y, dy_y, false) : vec4(0.0);

		vec4 blend_layer_weights = norm_weights;
		vec3 ground_albedo = vec3(0.0);

		if (enable_height_blend) {
			float edge_noise = 0.0;
			if (enable_macro_noise) {
				float max_w = max(max(norm_weights.x, norm_weights.y), max(norm_weights.z, norm_weights.w));
				float transition_factor = clamp((1.0 - max_w) * 2.0, 0.0, 1.0);
				edge_noise = (base_fbm_val - 0.5) * 0.12 * transition_factor;
			}

			vec4 heights = vec4(c0.a, c1.a, c2.a, c3.a);
			vec4 noise_offsets = vec4(edge_noise, -edge_noise, edge_noise * 0.75, -edge_noise * 0.75);

			int idx0 = int(clamp(round(v_tex_indices.x), 0.0, 31.0));
			int idx1 = int(clamp(round(v_tex_indices.y), 0.0, 31.0));
			int idx2 = int(clamp(round(v_tex_indices.z), 0.0, 31.0));
			int idx3 = int(clamp(round(v_tex_indices.w), 0.0, 31.0));

			vec4 hp0 = swatch_height_params[idx0];
			vec4 hp1 = swatch_height_params[idx1];
			vec4 hp2 = swatch_height_params[idx2];
			vec4 hp3 = swatch_height_params[idx3];

			vec4 h_scales = vec4(
				hp0.x > 0.001 ? hp0.x : 1.0,
				hp1.x > 0.001 ? hp1.x : 1.0,
				hp2.x > 0.001 ? hp2.x : 1.0,
				hp3.x > 0.001 ? hp3.x : 1.0
			);

			vec4 h_biases = vec4(hp0.y, hp1.y, hp2.y, hp3.y);

			vec4 h_powers = vec4(
				hp0.z > 0.001 ? hp0.z : 1.0,
				hp1.z > 0.001 ? hp1.z : 1.0,
				hp2.z > 0.001 ? hp2.z : 1.0,
				hp3.z > 0.001 ? hp3.z : 1.0
			);

			vec4 shaped_heights = vec4(
				pow(clamp(heights.x, 0.0, 1.0), h_powers.x),
				pow(clamp(heights.y, 0.0, 1.0), h_powers.y),
				pow(clamp(heights.z, 0.0, 1.0), h_powers.z),
				pow(clamp(heights.w, 0.0, 1.0), h_powers.w)
			);

			vec4 scaled_ground_heights = vec4(
				shaped_heights.x * h_scales.x + h_biases.x,
				shaped_heights.y * h_scales.y + h_biases.y,
				shaped_heights.z * h_scales.z + h_biases.z,
				shaped_heights.w * h_scales.w + h_biases.w
			);

			vec4 height_scores = vec4(
				norm_weights.x > 0.001 ? (norm_weights.x + scaled_ground_heights.x + noise_offsets.x) : -100.0,
				norm_weights.y > 0.001 ? (norm_weights.y + scaled_ground_heights.y + noise_offsets.y) : -100.0,
				norm_weights.z > 0.001 ? (norm_weights.z + scaled_ground_heights.z + noise_offsets.z) : -100.0,
				norm_weights.w > 0.001 ? (norm_weights.w + scaled_ground_heights.w + noise_offsets.w) : -100.0
			);

			float max_score = max(max(height_scores.x, height_scores.y), max(height_scores.z, height_scores.w));
			float transition_depth = max(0.001, blend_softness);

			vec4 raw_blend = vec4(
				smoothstep(max_score - transition_depth, max_score, height_scores.x) * step(0.001, norm_weights.x),
				smoothstep(max_score - transition_depth, max_score, height_scores.y) * step(0.001, norm_weights.y),
				smoothstep(max_score - transition_depth, max_score, height_scores.z) * step(0.001, norm_weights.z),
				smoothstep(max_score - transition_depth, max_score, height_scores.w) * step(0.001, norm_weights.w)
			);

			float blend_sum = raw_blend.x + raw_blend.y + raw_blend.z + raw_blend.w;
			blend_layer_weights = blend_sum > 0.0001 ? raw_blend / blend_sum : norm_weights;

			ground_albedo = (c0.rgb * blend_layer_weights.x +
			                 c1.rgb * blend_layer_weights.y +
			                 c2.rgb * blend_layer_weights.z +
			                 c3.rgb * blend_layer_weights.w);
		} else {
			ground_albedo = (c0.rgb * norm_weights.x +
			                 c1.rgb * norm_weights.y +
			                 c2.rgb * norm_weights.z +
			                 c3.rgb * norm_weights.w);
		}

		vec3 ground_normal = geom_normal;
		float ground_ao = 1.0;
		float ground_roughness = 0.85;

		float gw0 = blend_layer_weights.x;
		float gw1 = blend_layer_weights.y;
		float gw2 = blend_layer_weights.z;
		float gw3 = blend_layer_weights.w;

		if (enable_normal_mapping) {
			vec4 gn0 = gw0 > 0.001 ? sample_planar_layer(terrain_normals_pbr, round(v_tex_indices.x), uv_y, dx_y, dy_y, true) : vec4(0.5, 0.5, 1.0, 1.0);
			vec4 gn1 = gw1 > 0.001 ? sample_planar_layer(terrain_normals_pbr, round(v_tex_indices.y), uv_y, dx_y, dy_y, true) : vec4(0.5, 0.5, 1.0, 1.0);
			vec4 gn2 = gw2 > 0.001 ? sample_planar_layer(terrain_normals_pbr, round(v_tex_indices.z), uv_y, dx_y, dy_y, true) : vec4(0.5, 0.5, 1.0, 1.0);
			vec4 gn3 = gw3 > 0.001 ? sample_planar_layer(terrain_normals_pbr, round(v_tex_indices.w), uv_y, dx_y, dy_y, true) : vec4(0.5, 0.5, 1.0, 1.0);

			vec3 gn0_vec = unpack_triplanar_normal(gn0, vec3(0.0, 1.0, 0.0), geom_normal);
			vec3 gn1_vec = unpack_triplanar_normal(gn1, vec3(0.0, 1.0, 0.0), geom_normal);
			vec3 gn2_vec = unpack_triplanar_normal(gn2, vec3(0.0, 1.0, 0.0), geom_normal);
			vec3 gn3_vec = unpack_triplanar_normal(gn3, vec3(0.0, 1.0, 0.0), geom_normal);

			ground_normal = normalize(gn0_vec * gw0 + gn1_vec * gw1 + gn2_vec * gw2 + gn3_vec * gw3);
			ground_ao = (gn0.b * gw0 + gn1.b * gw1 + gn2.b * gw2 + gn3.b * gw3);
			ground_roughness = (gn0.a * gw0 + gn1.a * gw1 + gn2.a * gw2 + gn3.a * gw3);
		} else {
			float max_gw = max(max(gw0, gw1), max(gw2, gw3));
			float dom_layer = round(v_tex_indices.x);
			if (gw1 >= max_gw) dom_layer = round(v_tex_indices.y);
			else if (gw2 >= max_gw) dom_layer = round(v_tex_indices.z);
			else if (gw3 >= max_gw) dom_layer = round(v_tex_indices.w);

			int dom_idx = int(clamp(dom_layer, 0.0, 31.0));
			float uv_scale = swatch_params[dom_idx].y > 0.001 ? swatch_params[dom_idx].y : 1.0;
			vec2 dom_uv = uv_y * uv_scale;
			vec2 dom_dx = dx_y * uv_scale;
			vec2 dom_dy = dy_y * uv_scale;

			vec4 gn = textureGrad(terrain_normals_pbr, vec3(dom_uv, dom_layer), dom_dx, dom_dy);
			ground_normal = unpack_triplanar_normal(gn, vec3(0.0, 1.0, 0.0), geom_normal);
			ground_ao = gn.b;
			ground_roughness = gn.a;
		}

		if (enable_macro_noise && macro_normal_strength > 0.0) {
			float n_noise_y_x = (macro_fbm((v_world_pos.xz + vec2(0.1, 0.0)) * macro_scale) - macro_fbm((v_world_pos.xz - vec2(0.1, 0.0)) * macro_scale));
			float n_noise_y_z = (macro_fbm((v_world_pos.xz + vec2(0.0, 0.1)) * macro_scale) - macro_fbm((v_world_pos.xz - vec2(0.0, 0.1)) * macro_scale));
			vec3 noise_norm_y = vec3(n_noise_y_x, 0.0, n_noise_y_z);
			ground_normal = normalize(ground_normal + noise_norm_y * macro_normal_strength);
		}

		terrain_color = ground_albedo;
		blended_world_normal = ground_normal;
		blended_ao = ground_ao;
		blended_roughness = ground_roughness;
	} else {
		// --- CLIFF PIPELINE ---
		vec3 dX = dFdx(v_world_pos);
		vec3 dY = dFdy(v_world_pos);
		vec3 cliff_geom_normal = cross(dY, dX);
		float geom_len = length(cliff_geom_normal);
		cliff_geom_normal = geom_len > 0.00001 ? (cliff_geom_normal / geom_len) : geom_normal;
		if (cliff_geom_normal.y < 0.0 && abs(cliff_geom_normal.y) > 0.001) {
			cliff_geom_normal = -cliff_geom_normal;
		}

		vec3 triplanar_normal = abs(cliff_geom_normal);
		vec3 blend_weights = pow(triplanar_normal, vec3(4.0));
		float bw_sum = blend_weights.x + blend_weights.y + blend_weights.z;
		blend_weights = bw_sum > 0.0001 ? blend_weights / bw_sum : vec3(0.0, 1.0, 0.0);

		vec2 uv_x = pos_warped.zy * texture_scale;
		vec2 uv_y = pos_warped.xz * texture_scale;
		vec2 uv_z = pos_warped.xy * texture_scale;

		vec2 dx_x = dFdx(uv_x);
		vec2 dy_x = dFdy(uv_x);
		vec2 dx_y = dFdx(uv_y);
		vec2 dy_y = dFdy(uv_y);
		vec2 dx_z = dFdx(uv_z);
		vec2 dy_z = dFdy(uv_z);

		vec4 raw_c_weights = v_cliff_tex_weights;
		raw_c_weights.x = raw_c_weights.x < 0.001 ? 0.0 : raw_c_weights.x;
		raw_c_weights.y = raw_c_weights.y < 0.001 ? 0.0 : raw_c_weights.y;
		raw_c_weights.z = raw_c_weights.z < 0.001 ? 0.0 : raw_c_weights.z;
		raw_c_weights.w = raw_c_weights.w < 0.001 ? 0.0 : raw_c_weights.w;

		float c_weight_sum = raw_c_weights.x + raw_c_weights.y + raw_c_weights.z + raw_c_weights.w;
		vec4 norm_c_weights = c_weight_sum > 0.0001 ? raw_c_weights / c_weight_sum : vec4(1.0, 0.0, 0.0, 0.0);

		float max_c_weight = max(max(norm_c_weights.x, norm_c_weights.y), max(norm_c_weights.z, norm_c_weights.w));
		float c_transition_factor = clamp((1.0 - max_c_weight) * 2.5, 0.0, 1.0);

		if (blend_noise_strength > 0.0 && c_transition_factor > 0.001) {
			vec2 c_noise_uv = v_world_pos.xz * blend_noise_scale + vec2(43.7, 19.3);
			float noise_c_a = (macro_noise(c_noise_uv) * 0.70 + macro_noise(c_noise_uv * 2.13 + vec2(11.7, 7.3)) * 0.30) - 0.5;
			float noise_c_b = (macro_noise(c_noise_uv + vec2(19.3, 37.7)) * 0.70 + macro_noise(c_noise_uv * 2.13 + vec2(43.3, 19.7)) * 0.30) - 0.5;
			vec4 c_noise_offset = vec4(noise_c_a, -noise_c_a, noise_c_b, -noise_c_b) * (blend_noise_strength * c_transition_factor);

			vec4 c_channel_mask = step(vec4(0.001), norm_c_weights);
			vec4 perturbed_c_weights = max(vec4(0.0), norm_c_weights + c_noise_offset * c_channel_mask);
			float perturbed_c_sum = perturbed_c_weights.x + perturbed_c_weights.y + perturbed_c_weights.z + perturbed_c_weights.w;
			norm_c_weights = perturbed_c_sum > 0.0001 ? perturbed_c_weights / perturbed_c_sum : norm_c_weights;
		}

		vec4 cliff0 = raw_c_weights.x > 0.001 ? sample_triplanar_layer(terrain_textures, round(v_cliff_tex_indices.x), uv_x, uv_y, uv_z, dx_x, dy_x, dx_y, dy_y, dx_z, dy_z, blend_weights, false) : vec4(0.0);
		vec4 cliff1 = raw_c_weights.y > 0.001 ? sample_triplanar_layer(terrain_textures, round(v_cliff_tex_indices.y), uv_x, uv_y, uv_z, dx_x, dy_x, dx_y, dy_y, dx_z, dy_z, blend_weights, false) : vec4(0.0);
		vec4 cliff2 = raw_c_weights.z > 0.001 ? sample_triplanar_layer(terrain_textures, round(v_cliff_tex_indices.z), uv_x, uv_y, uv_z, dx_x, dy_x, dx_y, dy_y, dx_z, dy_z, blend_weights, false) : vec4(0.0);
		vec4 cliff3 = raw_c_weights.w > 0.001 ? sample_triplanar_layer(terrain_textures, round(v_cliff_tex_indices.w), uv_x, uv_y, uv_z, dx_x, dy_x, dx_y, dy_y, dx_z, dy_z, blend_weights, false) : vec4(0.0);

		vec4 cliff_blend_weights = norm_c_weights;
		vec3 cliff_albedo = vec3(0.0);

		if (enable_height_blend) {
			float cliff_edge_noise = 0.0;
			if (enable_macro_noise) {
				float max_w = max(max(norm_c_weights.x, norm_c_weights.y), max(norm_c_weights.z, norm_c_weights.w));
				float transition_factor = clamp((1.0 - max_w) * 2.0, 0.0, 1.0);
				cliff_edge_noise = (base_fbm_val - 0.5) * 0.12 * transition_factor;
			}

			vec4 c_heights = vec4(cliff0.a, cliff1.a, cliff2.a, cliff3.a);
			vec4 c_noise_offsets = vec4(cliff_edge_noise, -cliff_edge_noise, cliff_edge_noise * 0.75, -cliff_edge_noise * 0.75);

			int c_idx0 = int(clamp(round(v_cliff_tex_indices.x), 0.0, 31.0));
			int c_idx1 = int(clamp(round(v_cliff_tex_indices.y), 0.0, 31.0));
			int c_idx2 = int(clamp(round(v_cliff_tex_indices.z), 0.0, 31.0));
			int c_idx3 = int(clamp(round(v_cliff_tex_indices.w), 0.0, 31.0));

			vec4 c_hp0 = swatch_height_params[c_idx0];
			vec4 c_hp1 = swatch_height_params[c_idx1];
			vec4 c_hp2 = swatch_height_params[c_idx2];
			vec4 c_hp3 = swatch_height_params[c_idx3];

			vec4 c_h_scales = vec4(
				c_hp0.x > 0.001 ? c_hp0.x : 1.0,
				c_hp1.x > 0.001 ? c_hp1.x : 1.0,
				c_hp2.x > 0.001 ? c_hp2.x : 1.0,
				c_hp3.x > 0.001 ? c_hp3.x : 1.0
			);

			vec4 c_h_biases = vec4(c_hp0.y, c_hp1.y, c_hp2.y, c_hp3.y);

			vec4 c_h_powers = vec4(
				c_hp0.z > 0.001 ? c_hp0.z : 1.0,
				c_hp1.z > 0.001 ? c_hp1.z : 1.0,
				c_hp2.z > 0.001 ? c_hp2.z : 1.0,
				c_hp3.z > 0.001 ? c_hp3.z : 1.0
			);

			vec4 c_shaped_heights = vec4(
				pow(clamp(c_heights.x, 0.0, 1.0), c_h_powers.x),
				pow(clamp(c_heights.y, 0.0, 1.0), c_h_powers.y),
				pow(clamp(c_heights.z, 0.0, 1.0), c_h_powers.z),
				pow(clamp(c_heights.w, 0.0, 1.0), c_h_powers.w)
			);

			vec4 scaled_cliff_heights = vec4(
				c_shaped_heights.x * c_h_scales.x + c_h_biases.x,
				c_shaped_heights.y * c_h_scales.y + c_h_biases.y,
				c_shaped_heights.z * c_h_scales.z + c_h_biases.z,
				c_shaped_heights.w * c_h_scales.w + c_h_biases.w
			);

			vec4 c_height_scores = vec4(
				norm_c_weights.x > 0.001 ? (norm_c_weights.x + scaled_cliff_heights.x + c_noise_offsets.x) : -100.0,
				norm_c_weights.y > 0.001 ? (norm_c_weights.y + scaled_cliff_heights.y + c_noise_offsets.y) : -100.0,
				norm_c_weights.z > 0.001 ? (norm_c_weights.z + scaled_cliff_heights.z + c_noise_offsets.z) : -100.0,
				norm_c_weights.w > 0.001 ? (norm_c_weights.w + scaled_cliff_heights.w + c_noise_offsets.w) : -100.0
			);

			float max_c_score = max(max(c_height_scores.x, c_height_scores.y), max(c_height_scores.z, c_height_scores.w));
			float c_transition_depth = max(0.001, blend_softness);

			vec4 raw_c_blend = vec4(
				smoothstep(max_c_score - c_transition_depth, max_c_score, c_height_scores.x) * step(0.001, norm_c_weights.x),
				smoothstep(max_c_score - c_transition_depth, max_c_score, c_height_scores.y) * step(0.001, norm_c_weights.y),
				smoothstep(max_c_score - c_transition_depth, max_c_score, c_height_scores.z) * step(0.001, norm_c_weights.z),
				smoothstep(max_c_score - c_transition_depth, max_c_score, c_height_scores.w) * step(0.001, norm_c_weights.w)
			);

			float c_blend_sum = raw_c_blend.x + raw_c_blend.y + raw_c_blend.z + raw_c_blend.w;
			cliff_blend_weights = c_blend_sum > 0.0001 ? raw_c_blend / c_blend_sum : norm_c_weights;

			cliff_albedo = (cliff0.rgb * cliff_blend_weights.x +
			                cliff1.rgb * cliff_blend_weights.y +
			                cliff2.rgb * cliff_blend_weights.z +
			                cliff3.rgb * cliff_blend_weights.w);
		} else {
			cliff_albedo = (cliff0.rgb * norm_c_weights.x +
			                cliff1.rgb * norm_c_weights.y +
			                cliff2.rgb * norm_c_weights.z +
			                cliff3.rgb * norm_c_weights.w);
		}

		vec3 cliff_normal = cliff_geom_normal;
		float cliff_ao = 1.0;
		float cliff_roughness = 0.85;

		float cw0 = cliff_blend_weights.x;
		float cw1 = cliff_blend_weights.y;
		float cw2 = cliff_blend_weights.z;
		float cw3 = cliff_blend_weights.w;

		if (enable_normal_mapping) {
			vec4 cn0 = cw0 > 0.001 ? sample_triplanar_layer(terrain_normals_pbr, round(v_cliff_tex_indices.x), uv_x, uv_y, uv_z, dx_x, dy_x, dx_y, dy_y, dx_z, dy_z, blend_weights, true) : vec4(0.5, 0.5, 1.0, 1.0);
			vec4 cn1 = cw1 > 0.001 ? sample_triplanar_layer(terrain_normals_pbr, round(v_cliff_tex_indices.y), uv_x, uv_y, uv_z, dx_x, dy_x, dx_y, dy_y, dx_z, dy_z, blend_weights, true) : vec4(0.5, 0.5, 1.0, 1.0);
			vec4 cn2 = cw2 > 0.001 ? sample_triplanar_layer(terrain_normals_pbr, round(v_cliff_tex_indices.z), uv_x, uv_y, uv_z, dx_x, dy_x, dx_y, dy_y, dx_z, dy_z, blend_weights, true) : vec4(0.5, 0.5, 1.0, 1.0);
			vec4 cn3 = cw3 > 0.001 ? sample_triplanar_layer(terrain_normals_pbr, round(v_cliff_tex_indices.w), uv_x, uv_y, uv_z, dx_x, dy_x, dx_y, dy_y, dx_z, dy_z, blend_weights, true) : vec4(0.5, 0.5, 1.0, 1.0);

			vec3 cn0_vec = unpack_triplanar_normal(cn0, get_active_weights(round(v_cliff_tex_indices.x), blend_weights), cliff_geom_normal);
			vec3 cn1_vec = unpack_triplanar_normal(cn1, get_active_weights(round(v_cliff_tex_indices.y), blend_weights), cliff_geom_normal);
			vec3 cn2_vec = unpack_triplanar_normal(cn2, get_active_weights(round(v_cliff_tex_indices.z), blend_weights), cliff_geom_normal);
			vec3 cn3_vec = unpack_triplanar_normal(cn3, get_active_weights(round(v_cliff_tex_indices.w), blend_weights), cliff_geom_normal);

			cliff_normal = normalize(cn0_vec * cw0 + cn1_vec * cw1 + cn2_vec * cw2 + cn3_vec * cw3);
			cliff_ao = (cn0.b * cw0 + cn1.b * cw1 + cn2.b * cw2 + cn3.b * cw3);
			cliff_roughness = (cn0.a * cw0 + cn1.a * cw1 + cn2.a * cw2 + cn3.a * cw3);
		} else {
			float max_cw = max(max(cw0, cw1), max(cw2, cw3));
			float dom_c_layer = round(v_cliff_tex_indices.x);
			if (cw1 >= max_cw) dom_c_layer = round(v_cliff_tex_indices.y);
			else if (cw2 >= max_cw) dom_c_layer = round(v_cliff_tex_indices.z);
			else if (cw3 >= max_cw) dom_c_layer = round(v_cliff_tex_indices.w);

			int dom_c_idx = int(clamp(dom_c_layer, 0.0, 31.0));
			float c_uv_scale = swatch_params[dom_c_idx].y > 0.001 ? swatch_params[dom_c_idx].y : 1.0;

			vec2 c_uv = uv_y * c_uv_scale;
			vec2 c_dx = dx_y * c_uv_scale;
			vec2 c_dy = dy_y * c_uv_scale;
			if (blend_weights.x > blend_weights.y && blend_weights.x > blend_weights.z) {
				c_uv = uv_x * c_uv_scale;
				c_dx = dx_x * c_uv_scale;
				c_dy = dy_x * c_uv_scale;
			} else if (blend_weights.z > blend_weights.y) {
				c_uv = uv_z * c_uv_scale;
				c_dx = dx_z * c_uv_scale;
				c_dy = dy_z * c_uv_scale;
			}

			vec4 cn = textureGrad(terrain_normals_pbr, vec3(c_uv, dom_c_layer), c_dx, c_dy);
			cliff_normal = unpack_triplanar_normal(cn, get_active_weights(dom_c_layer, blend_weights), cliff_geom_normal);
			cliff_ao = cn.b;
			cliff_roughness = cn.a;
		}

		if (enable_macro_noise && macro_normal_strength > 0.0) {
			float n_noise_y_x = (macro_fbm((v_world_pos.xz + vec2(0.1, 0.0)) * macro_scale) - macro_fbm((v_world_pos.xz - vec2(0.1, 0.0)) * macro_scale));
			float n_noise_y_z = (macro_fbm((v_world_pos.xz + vec2(0.0, 0.1)) * macro_scale) - macro_fbm((v_world_pos.xz - vec2(0.0, 0.1)) * macro_scale));
			vec3 noise_norm_y = vec3(n_noise_y_x, 0.0, n_noise_y_z);

			float n_noise_x_y = (macro_fbm((v_world_pos.zy + vec2(0.1, 0.0)) * macro_scale) - macro_fbm((v_world_pos.zy - vec2(0.1, 0.0)) * macro_scale));
			float n_noise_x_z = (macro_fbm((v_world_pos.zy + vec2(0.0, 0.1)) * macro_scale) - macro_fbm((v_world_pos.zy - vec2(0.0, 0.1)) * macro_scale));
			vec3 noise_norm_x = vec3(0.0, n_noise_x_z, n_noise_x_y);

			float n_noise_z_x = (macro_fbm((v_world_pos.xy + vec2(0.1, 0.0)) * macro_scale) - macro_fbm((v_world_pos.xy - vec2(0.1, 0.0)) * macro_scale));
			float n_noise_z_y = (macro_fbm((v_world_pos.xy + vec2(0.0, 0.1)) * macro_scale) - macro_fbm((v_world_pos.xy - vec2(0.0, 0.1)) * macro_scale));
			vec3 noise_norm_z = vec3(n_noise_z_x, n_noise_z_y, 0.0);

			vec3 triplanar_cliff_noise = noise_norm_x * blend_weights.x + noise_norm_y * blend_weights.y + noise_norm_z * blend_weights.z;
			float effective_cliff_normal_strength = macro_normal_strength * 3.5;
			cliff_normal = normalize(cliff_normal + triplanar_cliff_noise * effective_cliff_normal_strength);
		}

		terrain_color = cliff_albedo;
		blended_world_normal = cliff_normal;
		blended_ao = cliff_ao;
		blended_roughness = cliff_roughness;
	}

	float macro_var = 1.0;
	if (enable_macro_noise) {
		macro_var = mix(1.0 - macro_albedo_contrast, 1.0 + macro_albedo_contrast, base_fbm_val);
	}

	vec3 final_albedo = terrain_color * macro_var * v_color.rgb;
	float terrain_lum = dot(final_albedo, vec3(0.2126, 0.7152, 0.0722));
	final_albedo = mix(vec3(terrain_lum), final_albedo, 0.82);
	vec3 emission_color = vec3(0.0);
	
	if (pathing_visible) {
		vec2 pathing_uv = (v_world_pos.xz + terrain_size / 2.0) / terrain_size;
		int code = int(round(texture(pathing_texture, pathing_uv).r * 255.0));

		vec2 cell_frac = fract(v_world_pos.xz / grid_spacing);
		int sx = int(floor(cell_frac.x * 3.0));
		int sz = int(floor(cell_frac.y * 3.0));
		int box_idx = sz * 3 + sx;

		vec4 pathing_color = vec4(0.0);
		if (box_idx == 0) {
			if (code == 0) {
				pathing_color = vec4(0.9, 0.1, 0.1, 0.75);
			}
		} else if (box_idx == 1) {
			if ((code & 1) != 0) {
				pathing_color = vec4(0.2, 0.6, 1.0, 0.75);
			}
		} else if (box_idx == 2) {
			if ((code & 2) != 0) {
				pathing_color = vec4(0.0, 0.15, 0.7, 0.75);
			}
		} else if (box_idx == 3) {
			if ((code & 4) != 0) {
				pathing_color = vec4(0.85, 0.85, 0.0, 0.75);
			}
		} else if (box_idx == 4) {
			if ((code & 8) != 0) {
				pathing_color = vec4(0.2, 0.85, 0.2, 0.75);
			}
		} else if (box_idx == 5) {
			if ((code & 32) != 0) {
				pathing_color = vec4(0.6, 0.2, 0.8, 0.75);
			}
		}

		if (pathing_color.a > 0.0) {
			final_albedo = mix(final_albedo, pathing_color.rgb, pathing_color.a);
		}
	}
	
	if (grid_visible) {
		vec2 grid_uv = v_world_pos.xz / grid_spacing;
		
		vec2 df = fwidth(grid_uv) * 3.0;
		vec2 grid_lines = smoothstep(vec2(1.0) - df, vec2(1.0), fract(grid_uv)) + 
						  (1.0 - smoothstep(vec2(0.0), df, fract(grid_uv)));
		
		float thin_line = max(grid_lines.x, grid_lines.y);
		
		vec2 thick_grid_uv = grid_uv / 10.0;
		vec2 df_thick = fwidth(thick_grid_uv) * 3.0;
		vec2 thick_grid_lines = smoothstep(vec2(1.0) - df_thick, vec2(1.0), fract(thick_grid_uv)) + 
								(1.0 - smoothstep(vec2(0.0), df_thick, fract(thick_grid_uv)));
		
		float thick_line = max(thick_grid_lines.x, thick_grid_lines.y);
		
		if (thick_line > 0.0) {
			final_albedo = mix(final_albedo, grid_color_thick.rgb, grid_color_thick.a * thick_line);
			emission_color = mix(emission_color, grid_color_thick.rgb, grid_color_thick.a * thick_line);
		} else if (thin_line > 0.0) {
			final_albedo = mix(final_albedo, grid_color_thin.rgb, grid_color_thin.a * thin_line);
			emission_color = mix(emission_color, grid_color_thin.rgb, grid_color_thin.a * thin_line);
		}
	}

	float final_roughness = 0.9;

	if (enable_macro_noise) {
		float macro_roughness_var = mix(1.0 - macro_roughness_contrast, 1.0 + macro_roughness_contrast, base_fbm_val);
		final_roughness = clamp(blended_roughness * macro_roughness_var, 0.05, 1.0);
	} else {
		final_roughness = clamp(blended_roughness, 0.05, 1.0);
	}

	vec2 shroud_uv = (v_world_pos.xz - shroud_world_min) / shroud_world_size;
	float shroud_factor = texture(shroud_texture, clamp(shroud_uv, 0.0, 1.0)).r;
	final_albedo *= (1.0 - shroud_factor * 0.98);
	emission_color *= (1.0 - shroud_factor * 0.98);

	ALBEDO = final_albedo;
	NORMAL = normalize((VIEW_MATRIX * vec4(blended_world_normal, 0.0)).xyz);
	AO = blended_ao * (1.0 - shroud_factor * 0.98);
	ROUGHNESS = mix(final_roughness, 1.0, shroud_factor);
	METALLIC = 0.0;                 
	SPECULAR = specular_amt * (1.0 - shroud_factor * 0.98);
	EMISSION = emission_color;
}
";

		_material = new ShaderMaterial();
		_material.Shader = shader;

		ReloadTerrainTextures();

		var defaultShroudImage = Image.CreateEmpty(32, 32, false, Image.Format.Rf);
		defaultShroudImage.Fill(new Color(0f, 0f, 0f, 1f));
		var defaultShroudTexture = ImageTexture.CreateFromImage(defaultShroudImage);
		_material.SetShaderParameter("shroud_texture", defaultShroudTexture);

		_material.SetShaderParameter("grid_spacing", QuadSize);
		_material.SetShaderParameter("terrain_size", new Vector2(Width * QuadSize, Depth * QuadSize));
		_material.SetShaderParameter("texture_scale", 1.0f / QuadSize);
		_material.SetShaderParameter("cliff_jitter_strength", CliffJitterStrength);
		_material.SetShaderParameter("cliff_jitter_scale", CliffJitterScale);
		_material.SetShaderParameter("blend_softness", BlendSoftness);
		_material.SetShaderParameter("blend_noise_strength", BlendNoiseStrength);
		_material.SetShaderParameter("blend_noise_scale", BlendNoiseScale);

		ApplyQualitySettings(GameSettings.QualityIdx);

		CreateChunks();
		CreateWater();

		if (GameHost.Instance == null || !GameHost.Instance.IsLoadingMap)
		{
			UpdateMeshAndPhysics();
		}
	}

	public void ApplyQualitySettings(GraphicsQuality quality)
	{
		ApplyQualitySettings((int)quality);
	}

	public void ApplyQualitySettings(int qualityIdx)
	{
		if (_material == null) return;

		switch (qualityIdx)
		{
			case 0:
				_material.SetShaderParameter("enable_macro_noise", false);
				_material.SetShaderParameter("enable_height_blend", false);
				_material.SetShaderParameter("enable_normal_mapping", false);
				_material.SetShaderParameter("specular_amt", 0.0f);
				break;
			case 1:
				_material.SetShaderParameter("enable_macro_noise", false);
				_material.SetShaderParameter("enable_height_blend", false);
				_material.SetShaderParameter("enable_normal_mapping", false);
				_material.SetShaderParameter("specular_amt", 0.0f);
				break;
			case 2:
				_material.SetShaderParameter("enable_macro_noise", true);
				_material.SetShaderParameter("enable_height_blend", true);
				_material.SetShaderParameter("enable_normal_mapping", true);
				_material.SetShaderParameter("specular_amt", 0.2f);
				break;
			case 3:
			default:
				_material.SetShaderParameter("enable_macro_noise", true);
				_material.SetShaderParameter("enable_height_blend", true);
				_material.SetShaderParameter("enable_normal_mapping", true);
				_material.SetShaderParameter("specular_amt", 0.2f);
				break;
		}

		_material.SetShaderParameter("cliff_jitter_strength", CliffJitterStrength);
		_material.SetShaderParameter("blend_noise_strength", BlendNoiseStrength);
		_material.SetShaderParameter("cliff_jitter_scale", CliffJitterScale);
		_material.SetShaderParameter("blend_noise_scale", BlendNoiseScale);
	}

	public static string GetKtxCmdPath()
	{
		string found = PathUtils.FindPath("ktx_tools/v5.0.0-rc1/bin/ktx.exe");
		if (System.IO.File.Exists(found)) return found;
		return System.IO.Path.GetFullPath(System.IO.Path.Combine(PathUtils.GetProjectRoot(), "..", "ktx_tools", "v5.0.0-rc1", "bin", "ktx.exe"));
	}

	private (Image AlbedoHeight, Image NormalRoughness) LoadKtx2LayersDynamic(string ktx2Path)
	{
		string globalKtx2Path = Godot.ProjectSettings.GlobalizePath(ktx2Path);
		string cacheDir = Godot.ProjectSettings.GlobalizePath("user://ktx_layer_cache");
		System.IO.Directory.CreateDirectory(cacheDir);

		string baseName = System.IO.Path.GetFileNameWithoutExtension(ktx2Path);
		string globalTempOut0 = System.IO.Path.Combine(cacheDir, $"{baseName}_layer0.png");
		string globalTempOut1 = System.IO.Path.Combine(cacheDir, $"{baseName}_layer1.png");

		if (System.IO.File.Exists(globalKtx2Path))
		{
			DateTime ktxTime = System.IO.File.GetLastWriteTimeUtc(globalKtx2Path);
			if (System.IO.File.Exists(globalTempOut0) && System.IO.File.Exists(globalTempOut1) &&
				System.IO.File.GetLastWriteTimeUtc(globalTempOut0) >= ktxTime &&
				System.IO.File.GetLastWriteTimeUtc(globalTempOut1) >= ktxTime)
			{
				var cached0 = Image.LoadFromFile(globalTempOut0);
				var cached1 = Image.LoadFromFile(globalTempOut1);
				if (cached0 != null && cached1 != null)
				{
					return (cached0, cached1);
				}
			}
		}

		string ktxCmd = GetKtxCmdPath();
		var startInfo0 = new System.Diagnostics.ProcessStartInfo
		{
			FileName = ktxCmd,
			WorkingDirectory = System.IO.Path.GetDirectoryName(ktxCmd),
			Arguments = $"extract --layer 0 --level 0 --transcode rgba8 \"{globalKtx2Path}\" \"{globalTempOut0}\"",
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};
		using (var process = System.Diagnostics.Process.Start(startInfo0))
		{
			process.WaitForExit();
			if (process.ExitCode != 0)
			{
				string err = process.StandardError.ReadToEnd();
				throw new System.Exception($"ktx extract layer 0 failed: {err}");
			}
		}
		var startInfo1 = new System.Diagnostics.ProcessStartInfo
		{
			FileName = ktxCmd,
			WorkingDirectory = System.IO.Path.GetDirectoryName(ktxCmd),
			Arguments = $"extract --layer 1 --level 0 --transcode rgba8 \"{globalKtx2Path}\" \"{globalTempOut1}\"",
			UseShellExecute = false,
			CreateNoWindow = true,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};
		using (var process = System.Diagnostics.Process.Start(startInfo1))
		{
			process.WaitForExit();
			if (process.ExitCode != 0)
			{
				string err = process.StandardError.ReadToEnd();
				throw new System.Exception($"ktx extract layer 1 failed: {err}");
			}
		}
		var img0 = Image.LoadFromFile(globalTempOut0);
		var img1 = Image.LoadFromFile(globalTempOut1);
		return (img0, img1);
	}

	private static Texture2DArray? _cachedAlbedoTextureArray;
	private static Texture2DArray? _cachedNormalRoughnessTextureArray;
	private static string? _cachedMapDir;
	private List<string> _loadedTextureList = new List<string>();
	private Godot.Vector4[] _swatchParamsCache = new Godot.Vector4[32];
	private Godot.Vector4[] _swatchHeightParamsCache = new Godot.Vector4[32];

	public void UpdateTextureParamDirect(
		string swatchName,
		string tileMode,
		float uvScale,
		float stochasticTileSize,
		float crossFade = 5.0f,
		float? brightness = null,
		string? tintStr = null,
		float heightScale = 1.0f,
		float heightOffset = 0.0f,
		float crevicePower = 1.0f,
		float edgeNoiseInfluence = 1.0f)
	{
		if (_material == null) return;
		string cleanName = System.IO.Path.GetFileNameWithoutExtension(swatchName);

		int targetIndex = -1;
		for (int i = 0; i < _loadedTextureList.Count && i < 32; i++)
		{
			if (string.Equals(_loadedTextureList[i], cleanName, StringComparison.OrdinalIgnoreCase))
			{
				targetIndex = i;
				break;
			}
		}

		if (targetIndex >= 0)
		{
			float tm = string.Equals(tileMode, "Grid", StringComparison.OrdinalIgnoreCase) ? 0.0f : 1.0f;
			float uv = Math.Clamp(uvScale, 0.1f, 4.0f);
			float stoch = Math.Clamp(stochasticTileSize, 0.5f, 3.0f);
			float cf = crossFade > 0.10f ? Math.Clamp(crossFade, 0.0f, 10.0f) * 0.01f : Math.Clamp(crossFade, 0.0f, 0.10f);

			float hs = Math.Clamp(heightScale, 0.1f, 3.0f);
			float ho = Math.Clamp(heightOffset, -1.0f, 1.0f);
			float cp = Math.Clamp(crevicePower, 0.5f, 4.0f);
			float en = 1.0f;

			_swatchParamsCache[targetIndex] = new Godot.Vector4(tm, uv, stoch, cf);
			_swatchHeightParamsCache[targetIndex] = new Godot.Vector4(hs, ho, cp, en);

			_material.SetShaderParameter("swatch_params", _swatchParamsCache);
			_material.SetShaderParameter("swatch_height_params", _swatchHeightParamsCache);

			if (GameHost.Instance != null && GameHost.Instance.EcsWorld != null && GameHost.Instance.EcsWorld.IsAlive(GameHost.Instance.WorldEntity) && GameHost.Instance.EcsWorld.Has<Realm.Ecs.Components.Terrain.TerrainState>(GameHost.Instance.WorldEntity))
			{
				ref var ts = ref GameHost.Instance.EcsWorld.Get<Realm.Ecs.Components.Terrain.TerrainState>(GameHost.Instance.WorldEntity);
				if (ts.SwatchConfigs == null || ts.SwatchConfigs.Length != 32)
				{
					ts.SwatchConfigs = new Realm.Ecs.Components.Terrain.TerrainSwatchConfig[32];
				}
				ts.SwatchConfigs[targetIndex] = new Realm.Ecs.Components.Terrain.TerrainSwatchConfig(hs, ho, cp, en);
			}
		}

		if (brightness.HasValue || !string.IsNullOrEmpty(tintStr))
		{
			ReloadTerrainTextures(true);
		}
	}

	public void ReloadTerrainTextures(bool forceReload = false)
	{
		if (_material == null) return;
		string mapDir = GameHost.Instance != null && !string.IsNullOrEmpty(GameHost.Instance.CurrentMapDirectory)
			? GameHost.Instance.CurrentMapDirectory
			: Godot.ProjectSettings.GlobalizePath("user://temp_map_workspace");

		var textureList = new List<string>();
		System.Text.Json.Nodes.JsonObject? texturesObj = null;

		try
		{
			string metadataPath = System.IO.Path.Combine(mapDir, "metadata.json");
			if (System.IO.File.Exists(metadataPath))
			{
				string text = System.IO.File.ReadAllText(metadataPath);
				var root = System.Text.Json.Nodes.JsonNode.Parse(text) as System.Text.Json.Nodes.JsonObject;
				if (root != null)
				{
					if (root.ContainsKey("Assets") && root["Assets"] is System.Text.Json.Nodes.JsonObject assets && assets.ContainsKey("textures") && assets["textures"] is System.Text.Json.Nodes.JsonObject tObj1)
					{
						texturesObj = tObj1;
					}
					else if (root.ContainsKey("MapProperties") && root["MapProperties"] is System.Text.Json.Nodes.JsonObject mp && mp.ContainsKey("Assets") && mp["Assets"] is System.Text.Json.Nodes.JsonObject mpAssets && mpAssets.ContainsKey("textures") && mpAssets["textures"] is System.Text.Json.Nodes.JsonObject tObj2)
					{
						texturesObj = tObj2;
					}
					else if (root.ContainsKey("textures") && root["textures"] is System.Text.Json.Nodes.JsonObject tObj3)
					{
						texturesObj = tObj3;
					}

					if (texturesObj != null)
					{
						var baseSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
						foreach (var kvp in texturesObj)
						{
							string baseName = System.IO.Path.GetFileNameWithoutExtension(kvp.Key);
							if (!baseSet.Contains(baseName))
							{
								baseSet.Add(baseName);
								textureList.Add(baseName);
							}
						}
					}
				}
			}
		}
		catch { }

		var swatchParams = new Godot.Vector4[32];
		var swatchHeightParams = new Godot.Vector4[32];
		for (int i = 0; i < 32; i++)
		{
			swatchParams[i] = new Godot.Vector4(1.0f, 1.0f, 1.0f, 0.05f);
			swatchHeightParams[i] = new Godot.Vector4(1.0f, 0.0f, 1.0f, 1.0f);
		}

		if (texturesObj != null)
		{
			for (int i = 0; i < textureList.Count && i < 32; i++)
			{
				string name = textureList[i];
				System.Text.Json.Nodes.JsonNode? swatchNode = null;
				foreach (var kvp in texturesObj)
				{
					string baseName = System.IO.Path.GetFileNameWithoutExtension(kvp.Key);
					if (string.Equals(baseName, name, StringComparison.OrdinalIgnoreCase))
					{
						swatchNode = kvp.Value;
						break;
					}
				}

				float tileMode = 1.0f;
				float uvScale = 1.0f;
				float stochasticTileSize = 1.0f;
				float crossFade = 0.05f;
				float heightScale = 1.0f;
				float heightOffset = 0.0f;
				float crevicePower = 1.0f;

				if (swatchNode is System.Text.Json.Nodes.JsonObject sObj)
				{
					string tm = sObj["Tile_Mode"]?.ToString() ?? sObj["tile_mode"]?.ToString() ?? "Stochastic";
					if (string.Equals(tm, "Grid", StringComparison.OrdinalIgnoreCase))
					{
						tileMode = 0.0f;
					}

					string uvStr = sObj["UV_Scale"]?.ToString() ?? sObj["uv_scale"]?.ToString();
					if (!string.IsNullOrEmpty(uvStr) && float.TryParse(uvStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedUv))
					{
						uvScale = Math.Clamp(parsedUv, 0.1f, 4.0f);
					}

					string stochStr = sObj["Stochastic_Tile_Size"]?.ToString() ?? sObj["stochastic_tile_size"]?.ToString();
					if (!string.IsNullOrEmpty(stochStr) && float.TryParse(stochStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedStoch))
					{
						stochasticTileSize = Math.Clamp(parsedStoch, 0.5f, 3.0f);
					}

					string cfStr = sObj["Cross_Fade"]?.ToString() ?? sObj["cross_fade"]?.ToString() ?? sObj["Grid_Cross_Fade"]?.ToString() ?? sObj["grid_cross_fade"]?.ToString();
					if (!string.IsNullOrEmpty(cfStr) && float.TryParse(cfStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedCf))
					{
						crossFade = parsedCf > 0.10f ? Math.Clamp(parsedCf, 0.0f, 10.0f) * 0.01f : Math.Clamp(parsedCf, 0.0f, 0.10f);
					}

					string hsStr = sObj["Height_Scale"]?.ToString() ?? sObj["height_scale"]?.ToString() ?? sObj["heightScale"]?.ToString();
					if (!string.IsNullOrEmpty(hsStr) && float.TryParse(hsStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedHs))
					{
						heightScale = Math.Clamp(parsedHs, 0.1f, 3.0f);
					}

					string hoStr = sObj["Height_Offset"]?.ToString() ?? sObj["height_offset"]?.ToString() ?? sObj["heightOffset"]?.ToString();
					if (!string.IsNullOrEmpty(hoStr) && float.TryParse(hoStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedHo))
					{
						heightOffset = Math.Clamp(parsedHo, -1.0f, 1.0f);
					}

					string cpStr = sObj["Crevice_Power"]?.ToString() ?? sObj["crevice_power"]?.ToString() ?? sObj["crevicePower"]?.ToString();
					if (!string.IsNullOrEmpty(cpStr) && float.TryParse(cpStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedCp))
					{
						crevicePower = Math.Clamp(parsedCp, 0.5f, 4.0f);
					}
				}

				swatchParams[i] = new Godot.Vector4(tileMode, uvScale, stochasticTileSize, crossFade);
				swatchHeightParams[i] = new Godot.Vector4(heightScale, heightOffset, crevicePower, 1.0f);
			}
		}

		_loadedTextureList = textureList;
		_swatchParamsCache = swatchParams;
		_swatchHeightParamsCache = swatchHeightParams;
		_material.SetShaderParameter("swatch_params", swatchParams);
		_material.SetShaderParameter("swatch_height_params", swatchHeightParams);

		if (GameHost.Instance != null && GameHost.Instance.EcsWorld != null && GameHost.Instance.EcsWorld.IsAlive(GameHost.Instance.WorldEntity) && GameHost.Instance.EcsWorld.Has<Realm.Ecs.Components.Terrain.TerrainState>(GameHost.Instance.WorldEntity))
		{
			ref var ts = ref GameHost.Instance.EcsWorld.Get<Realm.Ecs.Components.Terrain.TerrainState>(GameHost.Instance.WorldEntity);
			ts.SwatchConfigs = new Realm.Ecs.Components.Terrain.TerrainSwatchConfig[32];
			for (int i = 0; i < 32; i++)
			{
				ts.SwatchConfigs[i] = new Realm.Ecs.Components.Terrain.TerrainSwatchConfig(
					swatchHeightParams[i].X,
					swatchHeightParams[i].Y,
					swatchHeightParams[i].Z,
					swatchHeightParams[i].W
				);
			}
		}

		if (!forceReload && _cachedAlbedoTextureArray != null && _cachedNormalRoughnessTextureArray != null && _cachedMapDir == mapDir)
		{
			_material.SetShaderParameter("terrain_textures", _cachedAlbedoTextureArray);
			_material.SetShaderParameter("terrain_normals_pbr", _cachedNormalRoughnessTextureArray);
			return;
		}

		var albedoHeightImages = new Godot.Collections.Array<Image>();
		var normalRoughnessImages = new Godot.Collections.Array<Image>();
		foreach (var name in textureList)
		{
			float texBrightness = 1.0f;
			Color texTint = new Color(1.0f, 1.0f, 1.0f);
			if (texturesObj != null)
			{
				foreach (var kvp in texturesObj)
				{
					string baseName = System.IO.Path.GetFileNameWithoutExtension(kvp.Key);
					if (string.Equals(baseName, name, StringComparison.OrdinalIgnoreCase) && kvp.Value is System.Text.Json.Nodes.JsonObject sObj)
					{
						string brightStr = sObj["Brightness"]?.ToString() ?? sObj["brightness"]?.ToString();
						if (!string.IsNullOrEmpty(brightStr) && float.TryParse(brightStr, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedBright))
						{
							texBrightness = Math.Clamp(parsedBright, 0.10f, 2.0f);
						}
						string tintStr = sObj["Tint"]?.ToString() ?? sObj["tint"]?.ToString();
						if (!string.IsNullOrEmpty(tintStr) && Color.HtmlIsValid(tintStr))
						{
							texTint = Color.FromHtml(tintStr);
						}
						break;
					}
				}
			}

			string ktx2Path = System.IO.Path.Combine(mapDir, "Assets", "textures", name + ".ktx2");
			if (!System.IO.File.Exists(ktx2Path))
			{
				ktx2Path = System.IO.Path.Combine(mapDir, name + ".ktx2");
			}
			if (!System.IO.File.Exists(ktx2Path))
			{
				ktx2Path = ProjectSettings.GlobalizePath($"res://Assets/2d/TileSheets/{name}.ktx2");
			}
			if (!System.IO.File.Exists(ktx2Path))
			{
				string pngPath = ProjectSettings.GlobalizePath($"res://Assets/2d/TileSheets/{name}.png");
				if (System.IO.File.Exists(pngPath))
				{
					ProcessAndSaveRawTexture(pngPath, ktx2Path);
				}
			}
			Image imgLayer0 = null;
			Image imgLayer1 = null;
			if (System.IO.File.Exists(ktx2Path))
			{
				try
				{
					var layers = LoadKtx2LayersDynamic(ktx2Path);
					imgLayer0 = layers.AlbedoHeight;
					imgLayer1 = layers.NormalRoughness;
				}
				catch (Exception ex)
				{
					GD.PrintErr($"Failed to load dynamic KTX2 layers for {name}: {ex.Message}");
				}
			}
			if (imgLayer0 == null || imgLayer1 == null)
			{
				imgLayer0 = Godot.Image.CreateEmpty(TargetTextureResolution, TargetTextureResolution, false, Godot.Image.Format.Rgba8);
				imgLayer0.Fill(new Color(1f, 0f, 1f, 0.99f));
				imgLayer1 = Godot.Image.CreateEmpty(TargetTextureResolution, TargetTextureResolution, false, Godot.Image.Format.Rgba8);
				imgLayer1.Fill(new Color(0.5f, 0.5f, 1.0f, 0.8f));
			}

			imgLayer0 = AutoCropToPowerOfTwoSquare(imgLayer0);
			imgLayer1 = AutoCropToPowerOfTwoSquare(imgLayer1);

			Realm.Godot.Utils.PlayerColorShaderManager.ApplyBrightnessAndTintToAlbedoImage(imgLayer0, texBrightness, texTint);

			List<Image> layer0SubImages = new List<Image>();
			List<Image> layer1SubImages = new List<Image>();

			layer0SubImages.Add(ProcessAndResizeImage(imgLayer0, TargetTextureResolution));
			layer1SubImages.Add(ProcessAndResizeImage(imgLayer1, TargetTextureResolution));

			for (int subIdx = 0; subIdx < layer0SubImages.Count; subIdx++)
			{
				var sub0 = layer0SubImages[subIdx];
				var sub1 = layer1SubImages[subIdx];

				if (sub0.GetFormat() != Godot.Image.Format.Rgba8) sub0.Convert(Godot.Image.Format.Rgba8);
				if (sub1.GetFormat() != Godot.Image.Format.Rgba8) sub1.Convert(Godot.Image.Format.Rgba8);

				sub0.GenerateMipmaps();
				sub1.GenerateMipmaps();

				albedoHeightImages.Add(sub0);
				normalRoughnessImages.Add(sub1);
			}
		}
		if (albedoHeightImages.Count == 0 || normalRoughnessImages.Count == 0)
		{
			var fb0 = Godot.Image.CreateEmpty(TargetTextureResolution, TargetTextureResolution, false, Godot.Image.Format.Rgba8);
			fb0.Fill(new Color(0.2f, 0.5f, 0.2f, 0.99f));
			fb0.GenerateMipmaps();

			var fb1 = Godot.Image.CreateEmpty(TargetTextureResolution, TargetTextureResolution, false, Godot.Image.Format.Rgba8);
			fb1.Fill(new Color(0.5f, 0.5f, 1.0f, 0.8f));
			fb1.GenerateMipmaps();

			albedoHeightImages.Add(fb0);
			normalRoughnessImages.Add(fb1);
		}
		var albedoTextureArray = new Texture2DArray();
		albedoTextureArray.CreateFromImages(albedoHeightImages);
		var normalTextureArray = new Texture2DArray();
		normalTextureArray.CreateFromImages(normalRoughnessImages);
		_cachedAlbedoTextureArray = albedoTextureArray;
		_cachedNormalRoughnessTextureArray = normalTextureArray;
		_cachedMapDir = mapDir;

		_material.SetShaderParameter("terrain_textures", albedoTextureArray);
		_material.SetShaderParameter("terrain_normals_pbr", normalTextureArray);
	}

	private void CreateChunks()
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

	private static readonly StringName ShroudTextureParam = "shroud_texture";
	private ImageTexture _currentShroudTexture = null;
	private static ImageTexture _clearShroudTexture = null;

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

	public void SetPathingVisible(bool visible)
	{
		if (_material != null)
		{
			_material.SetShaderParameter("pathing_visible", visible);
		}
	}

	public void SetGridVisible(bool visible)
	{
		if (_material != null)
		{
			_material.SetShaderParameter("grid_visible", visible);
		}
	}

	public void SetWireframeMode(bool enabled)
	{
		Viewport viewport = GetViewport();
		if (viewport != null)
		{
			viewport.DebugDraw = enabled ? Viewport.DebugDrawEnum.Wireframe : Viewport.DebugDrawEnum.Disabled;
		}
	}

	public void ToggleWireframeMode()
	{
		Viewport viewport = GetViewport();
		if (viewport != null)
		{
			bool isWireframe = viewport.DebugDraw == Viewport.DebugDrawEnum.Wireframe;
			viewport.DebugDraw = isWireframe ? Viewport.DebugDrawEnum.Disabled : Viewport.DebugDrawEnum.Wireframe;
		}
	}

	public void UpdatePathingTexture()
	{
		if (_material == null || PathingCodes == null) return;
		
		int w = Width;
		int d = Depth;
		var img = Image.CreateEmpty(w, d, false, Image.Format.Rgba8);
		
		for (int z = 0; z < d; z++)
		{
			for (int x = 0; x < w; x++)
			{
				int code = PathingCodes[x, z];
				img.SetPixel(x, z, new Color(code / 255.0f, 0f, 0f, 0f));
			}
		}
		
		var tex = ImageTexture.CreateFromImage(img);
		_material.SetShaderParameter("pathing_texture", tex);
	}

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

	public static float GetGridNodeHeight(int gx, int gz, TerrainCell[,] cells, int w, int d)
	{
		if (cells == null || w <= 0 || d <= 0) return 0f;
		int cellX = Math.Clamp(gx, 0, w - 1);
		int cellZ = Math.Clamp(gz, 0, d - 1);
		if (gx < w && gz < d) return cells[cellX, cellZ].Y_NW;
		if (gx == w && gz < d) return cells[w - 1, cellZ].Y_NE;
		if (gx < w && gz == d) return cells[cellX, d - 1].Y_SW;
		return cells[w - 1, d - 1].Y_SE;
	}

	public float GetGridNodeHeight(int gx, int gz)
	{
		return GetGridNodeHeight(gx, gz, Cells, Width, Depth);
	}

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

	private void UpdateChunkPhysics(TerrainChunk chunk)
	{
		int cellWidth = chunk.EndX - chunk.StartX;
		int cellDepth = chunk.EndZ - chunk.StartZ;

		int mapDataCount = (cellWidth + 1) * (cellDepth + 1);
		if (chunk.MapDataCache == null || chunk.MapDataCache.Length != mapDataCount)
		{
			chunk.MapDataCache = new float[mapDataCount];
		}

		bool mapDataChanged = false;
		int idx = 0;

		for (int z = 0; z <= cellDepth; z++)
		{
			for (int x = 0; x <= cellWidth; x++)
			{
				int vx = chunk.StartX + x;
				int vz = chunk.StartZ + z;
				float h = GetGridNodeHeight(vx, vz);

				if (Math.Abs(chunk.MapDataCache[idx] - h) > 0.0001f)
				{
					chunk.MapDataCache[idx] = h;
					mapDataChanged = true;
				}
				idx++;
			}
		}

		if (mapDataChanged || chunk.CollisionShape.Shape == null)
		{
			var heightMapShape = new HeightMapShape3D();
			heightMapShape.MapWidth = cellWidth + 1;
			heightMapShape.MapDepth = cellDepth + 1;
			heightMapShape.MapData = chunk.MapDataCache;

			chunk.CollisionShape.Shape = heightMapShape;

			float offsetX = (chunk.StartX + cellWidth / 2.0f - Width / 2.0f) * QuadSize;
			float offsetZ = (chunk.StartZ + cellDepth / 2.0f - Depth / 2.0f) * QuadSize;
			chunk.CollisionShape.Position = new Vector3(offsetX, 0, offsetZ);
			chunk.CollisionShape.Scale = new Vector3(QuadSize, 1.0f, QuadSize);
		}
	}

	private void UpdateChunkMesh(TerrainChunk chunk, bool rebuildPhysics)
	{
		var cells = Cells;
		if (cells == null) return;
		int w = Width;
		int d = Depth;
		float quadSize = QuadSize;
		float halfWQuadSize = (w / 2.0f) * quadSize;
		float halfDQuadSize = (d / 2.0f) * quadSize;
		var splatMap = SplatMap;
		var cliffSplatMap = CliffSplatMap ?? splatMap;

		int maxVertices = 0;
		int maxIndices = 0;

		for (int z = chunk.StartZ; z < chunk.EndZ; z++)
		{
			for (int x = chunk.StartX; x < chunk.EndX; x++)
			{
				CountQuadElements(x, z, cells, w, d, ref maxVertices, ref maxIndices);
			}
		}

		if (chunk.VerticesCache == null || chunk.VerticesCache.Length < maxVertices)
		{
			chunk.VerticesCache = new Vector3[maxVertices];
			chunk.NormalsCache = new Vector3[maxVertices];
			chunk.UvsCache = new Vector2[maxVertices];
			chunk.ColorsCache = new Color[maxVertices];
			chunk.TexIndicesCache = new float[maxVertices * 4];
			chunk.TexWeightsCache01 = new float[maxVertices * 4];
			chunk.CliffIndicesCache = new float[maxVertices * 4];
			chunk.CliffWeightsCache = new float[maxVertices * 4];
		}

		if (chunk.IndicesCache == null || chunk.IndicesCache.Length < maxIndices)
		{
			chunk.IndicesCache = new int[maxIndices];
		}

		int vertexIndex = 0;
		int indexIndex = 0;

		for (int z = chunk.StartZ; z < chunk.EndZ; z++)
		{
			for (int x = chunk.StartX; x < chunk.EndX; x++)
			{
				ProcessCellQuad(chunk, x, z, cells, w, d, quadSize, halfWQuadSize, halfDQuadSize, splatMap, cliffSplatMap, ref vertexIndex, ref indexIndex);
			}
		}

		WeldChunkVertices(chunk, ref vertexIndex, indexIndex);

		Vector3[] finalVertices = chunk.VerticesCache;
		Color[] finalColors = chunk.ColorsCache;
		Vector3[] finalNormals = chunk.NormalsCache;
		Vector2[] finalUvs = chunk.UvsCache;
		float[] finalTexIndices = chunk.TexIndicesCache;
		float[] finalTexWeights = chunk.TexWeightsCache01;
		float[] finalCliffIndices = chunk.CliffIndicesCache;
		float[] finalCliffWeights = chunk.CliffWeightsCache;
		int[] finalIndices = chunk.IndicesCache;

		if (vertexIndex < chunk.VerticesCache.Length)
		{
			finalVertices = new Vector3[vertexIndex];
			Array.Copy(chunk.VerticesCache, finalVertices, vertexIndex);

			finalColors = new Color[vertexIndex];
			Array.Copy(chunk.ColorsCache, finalColors, vertexIndex);

			finalNormals = new Vector3[vertexIndex];
			Array.Copy(chunk.NormalsCache, finalNormals, vertexIndex);

			finalUvs = new Vector2[vertexIndex];
			Array.Copy(chunk.UvsCache, finalUvs, vertexIndex);

			finalTexIndices = new float[vertexIndex * 4];
			Array.Copy(chunk.TexIndicesCache, finalTexIndices, vertexIndex * 4);

			finalTexWeights = new float[vertexIndex * 4];
			Array.Copy(chunk.TexWeightsCache01, finalTexWeights, vertexIndex * 4);

			finalCliffIndices = new float[vertexIndex * 4];
			Array.Copy(chunk.CliffIndicesCache, finalCliffIndices, vertexIndex * 4);

			finalCliffWeights = new float[vertexIndex * 4];
			Array.Copy(chunk.CliffWeightsCache, finalCliffWeights, vertexIndex * 4);
		}

		if (indexIndex < chunk.IndicesCache.Length)
		{
			finalIndices = new int[indexIndex];
			Array.Copy(chunk.IndicesCache, finalIndices, indexIndex);
		}

		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = finalVertices;
		arrays[(int)Mesh.ArrayType.Color] = finalColors;
		arrays[(int)Mesh.ArrayType.Normal] = finalNormals;
		arrays[(int)Mesh.ArrayType.TexUV] = finalUvs;
		arrays[(int)Mesh.ArrayType.Custom0] = finalTexIndices;
		arrays[(int)Mesh.ArrayType.Custom1] = finalTexWeights;
		arrays[(int)Mesh.ArrayType.Custom2] = finalCliffIndices;
		arrays[(int)Mesh.ArrayType.Custom3] = finalCliffWeights;
		arrays[(int)Mesh.ArrayType.Index] = finalIndices;

		chunk.ArrayMesh.ClearSurfaces();
		if (vertexIndex > 0)
		{
			int custom0Format = (int)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift;
			int custom1Format = (int)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom1Shift;
			int custom2Format = (int)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom2Shift;
			int custom3Format = (int)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom3Shift;
			chunk.ArrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays,
				new Godot.Collections.Array<Godot.Collections.Array>(),
				null,
				(Mesh.ArrayFormat)((int)(Mesh.ArrayFormat.FormatCustom0 | Mesh.ArrayFormat.FormatCustom1 | Mesh.ArrayFormat.FormatCustom2 | Mesh.ArrayFormat.FormatCustom3) | custom0Format | custom1Format | custom2Format | custom3Format));
		}

		float minY = float.MaxValue;
		float maxY = float.MinValue;
		for (int i = 0; i < vertexIndex; i++)
		{
			float y = finalVertices[i].Y;
			if (y < minY) minY = y;
			if (y > maxY) maxY = y;
		}
		if (vertexIndex == 0) { minY = -2f; maxY = 2f; }
		chunk.MinY = minY;
		chunk.MaxY = maxY;

		float halfW = (w / 2.0f) * quadSize;
		float halfD = (d / 2.0f) * quadSize;
		float minX = chunk.StartX * quadSize - halfW;
		float maxX = chunk.EndX * quadSize - halfW;
		float minZ = chunk.StartZ * quadSize - halfD;
		float maxZ = chunk.EndZ * quadSize - halfD;
		chunk.WorldAabb = new Aabb(new Vector3(minX, minY - 2f, minZ), new Vector3(maxX - minX, Math.Max(0.5f, maxY - minY + 10f), maxZ - minZ));
		chunk.MeshInstance.CustomAabb = chunk.WorldAabb;
		if (chunk.ShallowWaterMesh != null) chunk.ShallowWaterMesh.CustomAabb = chunk.WorldAabb;
		if (chunk.DeepWaterMesh != null) chunk.DeepWaterMesh.CustomAabb = chunk.WorldAabb;

		if (rebuildPhysics || chunk.CollisionShape.Shape == null)
		{
			UpdateChunkPhysics(chunk);
		}
	}

	public override void _Process(double delta)
	{
		base._Process(delta);
		UpdateFrustumCulling();
	}

	private void UpdateFrustumCulling()
	{
		if (IsMinimapRendering)
		{
			SetAllChunksVisible(true);
			return;
		}

		var viewport = GetViewport();
		if (viewport == null) return;
		var camera = viewport.GetCamera3D();
		if (camera == null || !GodotObject.IsInstanceValid(camera)) return;

		if (camera.Projection == Camera3D.ProjectionType.Orthogonal)
		{
			SetAllChunksVisible(true);
			return;
		}

		var frustum = camera.GetFrustum();
		if (frustum == null || frustum.Count < 6) return;

		foreach (var chunk in _chunks)
		{
			bool visible = IntersectsFrustum(frustum, chunk.WorldAabb);
			if (GodotObject.IsInstanceValid(chunk.MeshInstance))
			{
				chunk.MeshInstance.Visible = visible;
			}
			if (GodotObject.IsInstanceValid(chunk.ShallowWaterMesh))
			{
				chunk.ShallowWaterMesh.Visible = visible;
			}
			if (GodotObject.IsInstanceValid(chunk.DeepWaterMesh))
			{
				chunk.DeepWaterMesh.Visible = visible;
			}
		}
	}

	public void SetAllChunksVisible(bool visible)
	{
		foreach (var chunk in _chunks)
		{
			if (GodotObject.IsInstanceValid(chunk.MeshInstance)) chunk.MeshInstance.Visible = visible;
			if (GodotObject.IsInstanceValid(chunk.ShallowWaterMesh)) chunk.ShallowWaterMesh.Visible = visible;
			if (GodotObject.IsInstanceValid(chunk.DeepWaterMesh)) chunk.DeepWaterMesh.Visible = visible;
		}
	}

	public static bool IntersectsFrustum(Godot.Collections.Array<Plane> frustumPlanes, Aabb aabb)
	{
		aabb = aabb.Grow(8.0f);
		Vector3 min = aabb.Position;
		Vector3 max = aabb.End;

		int count = frustumPlanes.Count;
		for (int i = 0; i < count; i++)
		{
			Plane plane = frustumPlanes[i];
			Vector3 n = new Vector3(
				plane.Normal.X >= 0 ? min.X : max.X,
				plane.Normal.Y >= 0 ? min.Y : max.Y,
				plane.Normal.Z >= 0 ? min.Z : max.Z
			);

			if (plane.DistanceTo(n) > 0)
			{
				return false;
			}
		}
		return true;
	}

	private Vector3 GetWorldPosition(float gridX, float gridZ, float height, float halfWQuadSize, float halfDQuadSize, float quadSize)
	{
		float lx = gridX * quadSize - halfWQuadSize;
		float lz = gridZ * quadSize - halfDQuadSize;
		return new Vector3(lx, height, lz);
	}

	private Vector3 GetWorldPosition(float gridX, float gridZ, float height)
	{
		return GetWorldPosition(gridX, gridZ, height, (Width / 2.0f) * QuadSize, (Depth / 2.0f) * QuadSize, QuadSize);
	}

	private void CountQuadElements(
		int x, int z,
		TerrainCell[,] cells,
		int w, int d,
		ref int totalVertices,
		ref int totalIndices)
	{
		if (cells == null) return;
		int cellW = cells.GetLength(0);
		int cellD = cells.GetLength(1);
		if (x < 0 || x >= cellW || z < 0 || z >= cellD) return;

		totalVertices += 12;
		totalIndices += 12;
	}

	private (int tex0, int tex1, int tex2, int tex3) GetQuadDominantTextures(ReadOnlySpan<TerrainSplatWeights> splats)
	{
		Span<int> indices = stackalloc int[16];
		Span<float> weights = stackalloc float[16];
		int count = 0;

		for (int sIdx = 0; sIdx < splats.Length; sIdx++)
		{
			TerrainSplatWeights s = splats[sIdx];
			for (int k = 0; k < 4; k++)
			{
				int idx = k switch { 0 => s.Index0, 1 => s.Index1, 2 => s.Index2, _ => s.Index3 };
				float w = k switch { 0 => s.Weight0, 1 => s.Weight1, 2 => s.Weight2, _ => s.Weight3 };
				if (w <= 0.0001f) continue;

				bool found = false;
				for (int i = 0; i < count; i++)
				{
					if (indices[i] == idx)
					{
						weights[i] += w;
						found = true;
						break;
					}
				}
				if (!found && count < 16)
				{
					indices[count] = idx;
					weights[count] = w;
					count++;
				}
			}
		}

		if (count == 0)
		{
			int defaultTex = splats.Length > 0 ? splats[0].Index0 : 0;
			return (defaultTex, defaultTex, defaultTex, defaultTex);
		}

		for (int i = 0; i < count - 1; i++)
		{
			for (int j = i + 1; j < count; j++)
			{
				if (weights[j] > weights[i])
				{
					float tempW = weights[i];
					weights[i] = weights[j];
					weights[j] = tempW;

					int tempIdx = indices[i];
					indices[i] = indices[j];
					indices[j] = tempIdx;
				}
			}
		}

		int res0 = indices[0];
		int res1 = count > 1 ? indices[1] : res0;
		int res2 = count > 2 ? indices[2] : res0;
		int res3 = count > 3 ? indices[3] : res0;

		return (res0, res1, res2, res3);
	}

	private (int tex0, int tex1, int tex2, int tex3) GetQuadDominantTextures(TerrainSplatWeights s0, TerrainSplatWeights s1, TerrainSplatWeights s2, TerrainSplatWeights s3)
	{
		Span<TerrainSplatWeights> splats = stackalloc TerrainSplatWeights[4] { s0, s1, s2, s3 };
		return GetQuadDominantTextures(splats);
	}

	private (float w0, float w1, float w2, float w3) GetSplatWeightsForQuad(TerrainSplatWeights s, int tex0, int tex1, int tex2, int tex3)
	{
		float GetWeightForTexture(in TerrainSplatWeights splat, int targetTexIndex)
		{
			float w = 0.0f;
			if (splat.Index0 == targetTexIndex) w += splat.Weight0;
			if (splat.Index1 == targetTexIndex) w += splat.Weight1;
			if (splat.Index2 == targetTexIndex) w += splat.Weight2;
			if (splat.Index3 == targetTexIndex) w += splat.Weight3;
			return w;
		}

		float w0 = GetWeightForTexture(s, tex0);
		float w1 = (tex1 != tex0) ? GetWeightForTexture(s, tex1) : 0.0f;
		float w2 = (tex2 != tex0 && tex2 != tex1) ? GetWeightForTexture(s, tex2) : 0.0f;
		float w3 = (tex3 != tex0 && tex3 != tex1 && tex3 != tex2) ? GetWeightForTexture(s, tex3) : 0.0f;

		float sumW = w0 + w1 + w2 + w3;
		if (sumW > 0.0001f)
		{
			float invSum = 1.0f / sumW;
			w0 *= invSum;
			w1 *= invSum;
			w2 *= invSum;
			w3 *= invSum;
		}
		else
		{
			w0 = 1.0f;
			w1 = 0.0f;
			w2 = 0.0f;
			w3 = 0.0f;
		}

		return (w0, w1, w2, w3);
	}

	private TerrainSplatWeights BlendSplatWeights(TerrainSplatWeights s0, TerrainSplatWeights s1, TerrainSplatWeights s2, TerrainSplatWeights s3)
	{
		var (tex0, tex1, tex2, tex3) = GetQuadDominantTextures(s0, s1, s2, s3);
		var (w00, w01, w02, w03) = GetSplatWeightsForQuad(s0, tex0, tex1, tex2, tex3);
		var (w10, w11, w12, w13) = GetSplatWeightsForQuad(s1, tex0, tex1, tex2, tex3);
		var (w20, w21, w22, w23) = GetSplatWeightsForQuad(s2, tex0, tex1, tex2, tex3);
		var (w30, w31, w32, w33) = GetSplatWeightsForQuad(s3, tex0, tex1, tex2, tex3);

		float avg0 = (w00 + w10 + w20 + w30) * 0.25f;
		float avg1 = (w01 + w11 + w21 + w31) * 0.25f;
		float avg2 = (w02 + w12 + w22 + w32) * 0.25f;
		float avg3 = (w03 + w13 + w23 + w33) * 0.25f;

		float sum = avg0 + avg1 + avg2 + avg3;
		if (sum > 0.0001f)
		{
			float invSum = 1.0f / sum;
			avg0 *= invSum;
			avg1 *= invSum;
			avg2 *= invSum;
			avg3 *= invSum;
		}
		else
		{
			avg0 = 1.0f; avg1 = 0f; avg2 = 0f; avg3 = 0f;
		}

		return new TerrainSplatWeights
		{
			Index0 = tex0,
			Index1 = tex1,
			Index2 = tex2,
			Index3 = tex3,
			Weight0 = avg0,
			Weight1 = avg1,
			Weight2 = avg2,
			Weight3 = avg3
		};
	}

	private TerrainSplatWeights BlendSplatWeights(TerrainSplatWeights s0, TerrainSplatWeights s1, TerrainSplatWeights s2)
	{
		var (tex0, tex1, tex2, tex3) = GetQuadDominantTextures(s0, s1, s2, s0);
		var (w00, w01, w02, w03) = GetSplatWeightsForQuad(s0, tex0, tex1, tex2, tex3);
		var (w10, w11, w12, w13) = GetSplatWeightsForQuad(s1, tex0, tex1, tex2, tex3);
		var (w20, w21, w22, w23) = GetSplatWeightsForQuad(s2, tex0, tex1, tex2, tex3);

		float avg0 = (w00 + w10 + w20) / 3.0f;
		float avg1 = (w01 + w11 + w21) / 3.0f;
		float avg2 = (w02 + w12 + w22) / 3.0f;
		float avg3 = (w03 + w13 + w23) / 3.0f;

		float sum = avg0 + avg1 + avg2 + avg3;
		if (sum > 0.0001f)
		{
			float invSum = 1.0f / sum;
			avg0 *= invSum;
			avg1 *= invSum;
			avg2 *= invSum;
			avg3 *= invSum;
		}
		else
		{
			avg0 = 1.0f; avg1 = 0f; avg2 = 0f; avg3 = 0f;
		}

		return new TerrainSplatWeights
		{
			Index0 = tex0,
			Index1 = tex1,
			Index2 = tex2,
			Index3 = tex3,
			Weight0 = avg0,
			Weight1 = avg1,
			Weight2 = avg2,
			Weight3 = avg3
		};
	}

	public static float Smoothstep(float edge0, float edge1, float x)
	{
		float t = Mathf.Clamp((x - edge0) / (edge1 - edge0), 0.0f, 1.0f);
		return t * t * (3.0f - 2.0f * t);
	}

	public static float GetCliffFactor(Vector3 normal)
	{
		float ny = Mathf.Clamp(normal.Y, 0.0f, 1.0f);
		return 1.0f - Smoothstep(0.70f, 0.90f, ny);
	}

	public static float GetVertexCliffWeight(int gx, int gz, TerrainCell[,] cells, int w, int d, float quadSize)
	{
		if (cells == null || w <= 0 || d <= 0) return 0f;
		float h = GetGridNodeHeight(gx, gz, cells, w, d);
		float maxDelta = 0f;
		if (gx > 0) maxDelta = Math.Max(maxDelta, Math.Abs(h - GetGridNodeHeight(gx - 1, gz, cells, w, d)));
		if (gx < w) maxDelta = Math.Max(maxDelta, Math.Abs(h - GetGridNodeHeight(gx + 1, gz, cells, w, d)));
		if (gz > 0) maxDelta = Math.Max(maxDelta, Math.Abs(h - GetGridNodeHeight(gx, gz - 1, cells, w, d)));
		if (gz < d) maxDelta = Math.Max(maxDelta, Math.Abs(h - GetGridNodeHeight(gx, gz + 1, cells, w, d)));

		sbyte minTier = sbyte.MaxValue;
		sbyte maxTier = sbyte.MinValue;
		int cellCount = 0;

		void CheckCell(int cx, int cz)
		{
			if (cx >= 0 && cx < w && cz >= 0 && cz < d)
			{
				sbyte t = cells[cx, cz].MacroTier;
				if (t < minTier) minTier = t;
				if (t > maxTier) maxTier = t;
				cellCount++;
			}
		}

		CheckCell(gx - 1, gz - 1);
		CheckCell(gx, gz - 1);
		CheckCell(gx - 1, gz);
		CheckCell(gx, gz);

		float tierCliffDeltaThreshold = TerrainCell.TIER_HEIGHT * 0.70f;
		bool hasTierDifference = cellCount > 1 && (maxTier - minTier) >= 1;
		bool hasStepHeightDelta = maxDelta >= tierCliffDeltaThreshold;

		if (!hasTierDifference && !hasStepHeightDelta)
		{
			return 0.0f;
		}

		float deltaCliff = Smoothstep(TerrainCell.TIER_HEIGHT * 0.50f, TerrainCell.TIER_HEIGHT * 0.85f, maxDelta);
		if (hasTierDifference)
		{
			deltaCliff = Math.Max(deltaCliff, 1.0f);
		}

		return Math.Clamp(deltaCliff, 0.0f, 1.0f);
	}

	private void ProcessCellQuad(
		TerrainChunk chunk,
		int x, int z,
		TerrainCell[,] cells,
		int w, int d,
		float quadSize,
		float halfWQuadSize,
		float halfDQuadSize,
		TerrainSplatWeights[,] splatMap,
		TerrainSplatWeights[,] cliffSplatMap,
		ref int vertexIndex,
		ref int indexIndex)
	{
		var cell = cells[x, z];
		sbyte cellMacro = cell.MacroTier;

		float hNW = cell.Y_NW;
		float hNE = cell.Y_NE;
		float hSE = cell.Y_SE;
		float hSW = cell.Y_SW;
		float hC = cell.CenterHeight;

		Vector3 gPNW = GetWorldPosition(x, z, hNW, halfWQuadSize, halfDQuadSize, quadSize);
		Vector3 gPNE = GetWorldPosition(x + 1, z, hNE, halfWQuadSize, halfDQuadSize, quadSize);
		Vector3 gPSE = GetWorldPosition(x + 1, z + 1, hSE, halfWQuadSize, halfDQuadSize, quadSize);
		Vector3 gPSW = GetWorldPosition(x, z + 1, hSW, halfWQuadSize, halfDQuadSize, quadSize);
		Vector3 gPC = GetWorldPosition(x + 0.5f, z + 0.5f, hC, halfWQuadSize, halfDQuadSize, quadSize);

		Vector3 normNW = GetVertexNormal(x, z, cells, w, d, quadSize);
		Vector3 normNE = GetVertexNormal(x + 1, z, cells, w, d, quadSize);
		Vector3 normSE = GetVertexNormal(x + 1, z + 1, cells, w, d, quadSize);
		Vector3 normSW = GetVertexNormal(x, z + 1, cells, w, d, quadSize);
		Vector3 normC = (normNW + normNE + normSE + normSW).Normalized();

		sbyte currentTier = cell.MacroTier;
		bool bordersDifferentTier = false;
		if (x > 0 && Math.Abs(cells[x - 1, z].MacroTier - currentTier) >= 1) bordersDifferentTier = true;
		if (x < w - 1 && Math.Abs(cells[x + 1, z].MacroTier - currentTier) >= 1) bordersDifferentTier = true;
		if (z > 0 && Math.Abs(cells[x, z - 1].MacroTier - currentTier) >= 1) bordersDifferentTier = true;
		if (z < d - 1 && Math.Abs(cells[x, z + 1].MacroTier - currentTier) >= 1) bordersDifferentTier = true;

		float cliffNW = GetVertexCliffWeight(x, z, cells, w, d, quadSize);
		float cliffNE = GetVertexCliffWeight(x + 1, z, cells, w, d, quadSize);
		float cliffSE = GetVertexCliffWeight(x + 1, z + 1, cells, w, d, quadSize);
		float cliffSW = GetVertexCliffWeight(x, z + 1, cells, w, d, quadSize);
		float cliffC = (cliffNW + cliffNE + cliffSE + cliffSW) * 0.25f;

		float maxDelta = Math.Max(
			Math.Max(Math.Abs(hNW - hNE), Math.Abs(hNE - hSE)),
			Math.Max(Math.Abs(hSE - hSW), Math.Abs(hSW - hNW))
		);
		float maxCenterDelta = Math.Max(
			Math.Max(Math.Abs(hC - hNW), Math.Abs(hC - hNE)),
			Math.Max(Math.Abs(hC - hSE), Math.Abs(hC - hSW))
		);
		float totalQuadDelta = Math.Max(maxDelta, maxCenterDelta);

		float tierStepThreshold = TerrainCell.TIER_HEIGHT * 0.70f;
		bool isCliffQuad = (bordersDifferentTier && totalQuadDelta >= tierStepThreshold * 0.5f) || totalQuadDelta >= tierStepThreshold;
		float quadCliffWeight = isCliffQuad ? 1.0f : 0.0f;

		int mapSplatW = splatMap != null ? splatMap.GetLength(0) : 0;
		int mapSplatD = splatMap != null ? splatMap.GetLength(1) : 0;

		TerrainSplatWeights floorS00 = splatMap != null ? splatMap[Math.Clamp(x, 0, mapSplatW - 1), Math.Clamp(z, 0, mapSplatD - 1)] : default;
		TerrainSplatWeights floorS10 = splatMap != null ? splatMap[Math.Clamp(x + 1, 0, mapSplatW - 1), Math.Clamp(z, 0, mapSplatD - 1)] : floorS00;
		TerrainSplatWeights floorS11 = splatMap != null ? splatMap[Math.Clamp(x + 1, 0, mapSplatW - 1), Math.Clamp(z + 1, 0, mapSplatD - 1)] : floorS00;
		TerrainSplatWeights floorS01 = splatMap != null ? splatMap[Math.Clamp(x, 0, mapSplatW - 1), Math.Clamp(z + 1, 0, mapSplatD - 1)] : floorS00;
		TerrainSplatWeights floorSC = BlendSplatWeights(floorS00, floorS10, floorS11, floorS01);

		int mapCliffW = cliffSplatMap != null ? cliffSplatMap.GetLength(0) : 0;
		int mapCliffD = cliffSplatMap != null ? cliffSplatMap.GetLength(1) : 0;

		TerrainSplatWeights cliffS00 = cliffSplatMap[Math.Clamp(x, 0, mapCliffW - 1), Math.Clamp(z, 0, mapCliffD - 1)];
		TerrainSplatWeights cliffS10 = (cliffSplatMap != null && mapCliffW > 0 && mapCliffD > 0)
			? cliffSplatMap[Math.Clamp(x + 1, 0, mapCliffW - 1), Math.Clamp(z, 0, mapCliffD - 1)]
			: cliffS00;
		TerrainSplatWeights cliffS11 = (cliffSplatMap != null && mapCliffW > 0 && mapCliffD > 0)
			? cliffSplatMap[Math.Clamp(x + 1, 0, mapCliffW - 1), Math.Clamp(z + 1, 0, mapCliffD - 1)]
			: cliffS00;
		TerrainSplatWeights cliffS01 = (cliffSplatMap != null && mapCliffW > 0 && mapCliffD > 0)
			? cliffSplatMap[Math.Clamp(x, 0, mapCliffW - 1), Math.Clamp(z + 1, 0, mapCliffD - 1)]
			: cliffS00;
		TerrainSplatWeights cliffSC = BlendSplatWeights(cliffS00, cliffS10, cliffS11, cliffS01);

		int cliffTex0 = BlendSplatWeights(cliffS00, cliffS10, cliffSC).GetDominantIndex();
		int cliffTex1 = BlendSplatWeights(cliffS10, cliffS11, cliffSC).GetDominantIndex();
		int cliffTex2 = BlendSplatWeights(cliffS11, cliffS01, cliffSC).GetDominantIndex();
		int cliffTex3 = BlendSplatWeights(cliffS01, cliffS00, cliffSC).GetDominantIndex();

		Vector2 uvNW = new Vector2(gPNW.X, gPNW.Z);
		Vector2 uvNE = new Vector2(gPNE.X, gPNE.Z);
		Vector2 uvSE = new Vector2(gPSE.X, gPSE.Z);
		Vector2 uvSW = new Vector2(gPSW.X, gPSW.Z);
		Vector2 uvC = new Vector2(gPC.X, gPC.Z);

		Color col = new Color(1.0f, 1.0f, 1.0f, 1.0f);

		var (gTex0, gTex1, gTex2, gTex3) = GetQuadDominantTextures(floorS00, floorS10, floorS11, floorS01);
		var (cTex0, cTex1, cTex2, cTex3) = GetQuadDominantTextures(cliffS00, cliffS10, cliffS11, cliffS01);

		ProcessSubTriangleGround(chunk, gPNW, gPNE, gPC, normNW, normNE, normC, uvNW, uvNE, uvC, cliffNW, cliffNE, cliffC, quadCliffWeight, floorS00, floorS10, floorSC, gTex0, gTex1, gTex2, gTex3, cliffS00, cliffS10, cliffSC, cTex0, cTex1, cTex2, cTex3, ref vertexIndex, ref indexIndex);
		ProcessSubTriangleGround(chunk, gPNE, gPSE, gPC, normNE, normSE, normC, uvNE, uvSE, uvC, cliffNE, cliffSE, cliffC, quadCliffWeight, floorS10, floorS11, floorSC, gTex0, gTex1, gTex2, gTex3, cliffS10, cliffS11, cliffSC, cTex0, cTex1, cTex2, cTex3, ref vertexIndex, ref indexIndex);
		ProcessSubTriangleGround(chunk, gPSE, gPSW, gPC, normSE, normSW, normC, uvSE, uvSW, uvC, cliffSE, cliffSW, cliffC, quadCliffWeight, floorS11, floorS01, floorSC, gTex0, gTex1, gTex2, gTex3, cliffS11, cliffS01, cliffSC, cTex0, cTex1, cTex2, cTex3, ref vertexIndex, ref indexIndex);
		ProcessSubTriangleGround(chunk, gPSW, gPNW, gPC, normSW, normNW, normC, uvSW, uvNW, uvC, cliffSW, cliffNW, cliffC, quadCliffWeight, floorS01, floorS00, floorSC, gTex0, gTex1, gTex2, gTex3, cliffS01, cliffS00, cliffSC, cTex0, cTex1, cTex2, cTex3, ref vertexIndex, ref indexIndex);
	}

	private void ProcessCellQuad(
		TerrainChunk chunk,
		int x, int z,
		ref int vertexIndex,
		ref int indexIndex)
	{
		ProcessCellQuad(chunk, x, z, Cells, Width, Depth, QuadSize, (Width / 2.0f) * QuadSize, (Depth / 2.0f) * QuadSize, SplatMap, CliffSplatMap ?? SplatMap, ref vertexIndex, ref indexIndex);
	}

	private void ProcessSubTriangleGround(
		TerrainChunk chunk,
		Vector3 pos0, Vector3 pos1, Vector3 pos2,
		Vector3 norm0, Vector3 norm1, Vector3 norm2,
		Vector2 uv0, Vector2 uv1, Vector2 uv2,
		float cliff0, float cliff1, float cliff2,
		float quadCliff,
		TerrainSplatWeights gS0, TerrainSplatWeights gS1, TerrainSplatWeights gS2,
		int gTex0, int gTex1, int gTex2, int gTex3,
		TerrainSplatWeights cS0, TerrainSplatWeights cS1, TerrainSplatWeights cS2,
		int cTex0, int cTex1, int cTex2, int cTex3,
		ref int vertexIndex,
		ref int indexIndex)
	{
		int baseIndex = vertexIndex;

		var (gw00, gw01, gw02, gw03) = GetSplatWeightsForQuad(gS0, gTex0, gTex1, gTex2, gTex3);
		var (gw10, gw11, gw12, gw13) = GetSplatWeightsForQuad(gS1, gTex0, gTex1, gTex2, gTex3);
		var (gw20, gw21, gw22, gw23) = GetSplatWeightsForQuad(gS2, gTex0, gTex1, gTex2, gTex3);

		var (cw00, cw01, cw02, cw03) = GetSplatWeightsForQuad(cS0, cTex0, cTex1, cTex2, cTex3);
		var (cw10, cw11, cw12, cw13) = GetSplatWeightsForQuad(cS1, cTex0, cTex1, cTex2, cTex3);
		var (cw20, cw21, cw22, cw23) = GetSplatWeightsForQuad(cS2, cTex0, cTex1, cTex2, cTex3);

		PopulateExplicitVertex(chunk, pos0, uv0, norm0, cliff0, quadCliff, gTex0, gTex1, gTex2, gTex3, gw00, gw01, gw02, gw03, cTex0, cTex1, cTex2, cTex3, cw00, cw01, cw02, cw03, ref vertexIndex);
		PopulateExplicitVertex(chunk, pos1, uv1, norm1, cliff1, quadCliff, gTex0, gTex1, gTex2, gTex3, gw10, gw11, gw12, gw13, cTex0, cTex1, cTex2, cTex3, cw10, cw11, cw12, cw13, ref vertexIndex);
		PopulateExplicitVertex(chunk, pos2, uv2, norm2, cliff2, quadCliff, gTex0, gTex1, gTex2, gTex3, gw20, gw21, gw22, gw23, cTex0, cTex1, cTex2, cTex3, cw20, cw21, cw22, cw23, ref vertexIndex);

		chunk.IndicesCache[indexIndex++] = baseIndex;
		chunk.IndicesCache[indexIndex++] = baseIndex + 1;
		chunk.IndicesCache[indexIndex++] = baseIndex + 2;
	}

	private void PopulateExplicitVertex(
		TerrainChunk chunk,
		Vector3 position,
		Vector2 uv,
		Vector3 faceNormal,
		float cliffWeight,
		float quadCliffWeight,
		int gTex0, int gTex1, int gTex2, int gTex3,
		float gw0, float gw1, float gw2, float gw3,
		int cTex0, int cTex1, int cTex2, int cTex3,
		float cw0, float cw1, float cw2, float cw3,
		ref int vertexIndex)
	{
		chunk.VerticesCache[vertexIndex] = position;
		chunk.NormalsCache[vertexIndex] = faceNormal;
		chunk.UvsCache[vertexIndex] = uv;
		chunk.ColorsCache[vertexIndex] = new Color(quadCliffWeight, 1.0f, 1.0f, cliffWeight);

		int sIdx = vertexIndex * 4;
		chunk.TexIndicesCache[sIdx + 0] = gTex0;
		chunk.TexIndicesCache[sIdx + 1] = gTex1;
		chunk.TexIndicesCache[sIdx + 2] = gTex2;
		chunk.TexIndicesCache[sIdx + 3] = gTex3;

		chunk.TexWeightsCache01[sIdx + 0] = gw0 > 0.0001f ? gw0 : 0.0f;
		chunk.TexWeightsCache01[sIdx + 1] = gw1 > 0.0001f ? gw1 : 0.0f;
		chunk.TexWeightsCache01[sIdx + 2] = gw2 > 0.0001f ? gw2 : 0.0f;
		chunk.TexWeightsCache01[sIdx + 3] = gw3 > 0.0001f ? gw3 : 0.0f;

		chunk.CliffIndicesCache[sIdx + 0] = cTex0;
		chunk.CliffIndicesCache[sIdx + 1] = cTex1;
		chunk.CliffIndicesCache[sIdx + 2] = cTex2;
		chunk.CliffIndicesCache[sIdx + 3] = cTex3;

		chunk.CliffWeightsCache[sIdx + 0] = cw0 > 0.0001f ? cw0 : 0.0f;
		chunk.CliffWeightsCache[sIdx + 1] = cw1 > 0.0001f ? cw1 : 0.0f;
		chunk.CliffWeightsCache[sIdx + 2] = cw2 > 0.0001f ? cw2 : 0.0f;
		chunk.CliffWeightsCache[sIdx + 3] = cw3 > 0.0001f ? cw3 : 0.0f;

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
		var cells = Cells;
		if (cells == null)
		{
			height = 0.0f;
			normal = Vector3.Up;
			return;
		}

		int cellW = cells.GetLength(0);
		int cellD = cells.GetLength(1);
		if (cellW <= 0 || cellD <= 0)
		{
			height = 0.0f;
			normal = Vector3.Up;
			return;
		}

		float halfW = (cellW / 2.0f) * QuadSize;
		float halfD = (cellD / 2.0f) * QuadSize;

		float localX = (worldX + halfW) / QuadSize;
		float localZ = (worldZ + halfD) / QuadSize;

		int x0 = Math.Clamp((int)MathF.Floor(localX), 0, cellW - 1);
		int z0 = Math.Clamp((int)MathF.Floor(localZ), 0, cellD - 1);

		float tx = Math.Clamp(localX - x0, 0.0f, 1.0f);
		float tz = Math.Clamp(localZ - z0, 0.0f, 1.0f);

		var cell = cells[x0, z0];
		float h00 = cell.Y_NW;
		float h10 = cell.Y_NE;
		float h01 = cell.Y_SW;
		float h11 = cell.Y_SE;
		height = (1 - tx) * (1 - tz) * h00 + tx * (1 - tz) * h10 + (1 - tx) * tz * h01 + tx * tz * h11;
		Vector3 n00 = GetVertexNormal(x0, z0);
		Vector3 n10 = GetVertexNormal(x0 + 1, z0);
		Vector3 n01 = GetVertexNormal(x0, z0 + 1);
		Vector3 n11 = GetVertexNormal(x0 + 1, z0 + 1);
		normal = ((1 - tx) * (1 - tz) * n00 + tx * (1 - tz) * n10 + (1 - tx) * tz * n01 + tx * tz * n11).Normalized();
	}

	public static Vector3 GetVertexNormal(int x, int z, TerrainCell[,] cells, int w, int d, float quadSize)
	{
		if (cells == null || w <= 0 || d <= 0) return Vector3.Up;

		float dx;
		if (x > 0 && x < w)
		{
			dx = (GetGridNodeHeight(x + 1, z, cells, w, d) - GetGridNodeHeight(x - 1, z, cells, w, d)) / (2.0f * quadSize);
		}
		else if (x < w)
		{
			dx = (GetGridNodeHeight(x + 1, z, cells, w, d) - GetGridNodeHeight(x, z, cells, w, d)) / quadSize;
		}
		else if (x > 0)
		{
			dx = (GetGridNodeHeight(x, z, cells, w, d) - GetGridNodeHeight(x - 1, z, cells, w, d)) / quadSize;
		}
		else
		{
			dx = 0.0f;
		}

		float dz;
		if (z > 0 && z < d)
		{
			dz = (GetGridNodeHeight(x, z + 1, cells, w, d) - GetGridNodeHeight(x, z - 1, cells, w, d)) / (2.0f * quadSize);
		}
		else if (z < d)
		{
			dz = (GetGridNodeHeight(x, z + 1, cells, w, d) - GetGridNodeHeight(x, z, cells, w, d)) / quadSize;
		}
		else if (z > 0)
		{
			dz = (GetGridNodeHeight(x, z, cells, w, d) - GetGridNodeHeight(x, z - 1, cells, w, d)) / quadSize;
		}
		else
		{
			dz = 0.0f;
		}

		if (Math.Abs(dx) < 0.0001f && Math.Abs(dz) < 0.0001f)
		{
			return Vector3.Up;
		}

		Vector3 tangentX = new Vector3(quadSize, dx * quadSize, 0.0f).Normalized();
		Vector3 tangentZ = new Vector3(0.0f, dz * quadSize, quadSize).Normalized();
		return tangentZ.Cross(tangentX).Normalized();
	}

	private Vector3 GetVertexNormal(int x, int z)
	{
		return GetVertexNormal(x, z, Cells, Width, Depth, QuadSize);
	}

	public void ResizeTerrain(int newWidth, int newDepth)
	{
		if (GameHost.Instance == null || GameHost.Instance.EcsWorld == null || !GameHost.Instance.EcsWorld.IsAlive(GameHost.Instance.WorldEntity)) return;
		if (!GameHost.Instance.EcsWorld.Has<TerrainState>(GameHost.Instance.WorldEntity)) return;

		newWidth = Math.Clamp((int)Math.Round(newWidth / 32.0) * 32, 32, 512);
		newDepth = Math.Clamp((int)Math.Round(newDepth / 32.0) * 32, 32, 512);

		ref var state = ref GameHost.Instance.EcsWorld.Get<TerrainState>(GameHost.Instance.WorldEntity);
		
		int oldWidth = state.Width;
		int oldDepth = state.Depth;
		var oldCells = Cells;
		int[,] oldPathing = state.PathingCodes;
		TerrainSplatWeights[,] oldSplatMap = SplatMap;

		var newCells = new TerrainCell[newWidth, newDepth];
		int[,] newPathing = new int[newWidth, newDepth];
		TerrainSplatWeights[,] newSplatMap = new TerrainSplatWeights[newWidth + 1, newDepth + 1];

		int offsetX = (newWidth - oldWidth) / 2;
		int offsetZ = (newDepth - oldDepth) / 2;

		for (int z = 0; z <= newDepth; z++)
		{
			for (int x = 0; x <= newWidth; x++)
			{
				int oldX = x - offsetX;
				int oldZ = z - offsetZ;
				if (x < newWidth && z < newDepth)
				{
					if (oldCells != null && oldX >= 0 && oldX < oldWidth && oldZ >= 0 && oldZ < oldDepth)
					{
						newCells[x, z] = oldCells[oldX, oldZ];
					}
					if (oldPathing != null && oldX >= 0 && oldX < oldWidth && oldZ >= 0 && oldZ < oldDepth)
					{
						newPathing[x, z] = oldPathing[oldX, oldZ];
					}
					else
					{
						newPathing[x, z] = GetDefaultPathingCode(newCells[x, z]);
					}
				}
				if (oldSplatMap != null && oldX >= 0 && oldX < oldSplatMap.GetLength(0) && oldZ >= 0 && oldZ < oldSplatMap.GetLength(1))
				{
					newSplatMap[x, z] = oldSplatMap[oldX, oldZ];
				}
				else
				{
					newSplatMap[x, z] = TerrainSplatWeights.CreateSolid(0);
				}
			}
		}

		GameHost.Instance.EcsWorld.Set(GameHost.Instance.WorldEntity, new TerrainState(
			newWidth, newDepth, state.QuadSize, state.CellSize,
			newCells, newPathing, state.NavMesh, state.NavMeshQuery
		));

		_localCells = newCells;
		_localPathingCodes = newPathing;
		SplatMap = newSplatMap;
		
		if (_material != null)
		{
			_material.SetShaderParameter("terrain_size", new Vector2(newWidth * state.QuadSize, newDepth * state.QuadSize));
		}

		CreateChunks();
		UpdateWaterSize();
		UpdateMeshAndPhysics();
	}

	public void ScaleTerrainData(int newWidth, int newDepth)
	{
		if (GameHost.Instance == null || GameHost.Instance.EcsWorld == null || !GameHost.Instance.EcsWorld.IsAlive(GameHost.Instance.WorldEntity)) return;
		if (!GameHost.Instance.EcsWorld.Has<TerrainState>(GameHost.Instance.WorldEntity)) return;

		newWidth = Math.Clamp((int)Math.Round(newWidth / 32.0) * 32, 32, 512);
		newDepth = Math.Clamp((int)Math.Round(newDepth / 32.0) * 32, 32, 512);

		ref var state = ref GameHost.Instance.EcsWorld.Get<TerrainState>(GameHost.Instance.WorldEntity);

		int oldWidth = state.Width;
		int oldDepth = state.Depth;
		var oldCells = Cells;
		int[,] oldPathing = state.PathingCodes;
		TerrainSplatWeights[,] oldSplatMap = SplatMap;

		var newCells = new TerrainCell[newWidth, newDepth];
		int[,] newPathing = new int[newWidth, newDepth];
		TerrainSplatWeights[,] newSplatMap = new TerrainSplatWeights[newWidth + 1, newDepth + 1];

		for (int z = 0; z <= newDepth; z++)
		{
			for (int x = 0; x <= newWidth; x++)
			{
				int x0 = oldSplatMap != null ? Math.Clamp((int)Math.Floor(x * (float)(oldSplatMap.GetLength(0) - 1) / newWidth), 0, oldSplatMap.GetLength(0) - 1) : 0;
				int z0 = oldSplatMap != null ? Math.Clamp((int)Math.Floor(z * (float)(oldSplatMap.GetLength(1) - 1) / newDepth), 0, oldSplatMap.GetLength(1) - 1) : 0;

				if (x < newWidth && z < newDepth)
				{
					int cellX0 = Math.Clamp((int)Math.Floor(x * (float)oldWidth / newWidth), 0, oldWidth - 1);
					int cellZ0 = Math.Clamp((int)Math.Floor(z * (float)oldDepth / newDepth), 0, oldDepth - 1);
					if (oldCells != null) newCells[x, z] = oldCells[cellX0, cellZ0];

					if (oldPathing != null)
					{
						newPathing[x, z] = oldPathing[cellX0, cellZ0];
					}
					else
					{
						newPathing[x, z] = GetDefaultPathingCode(newCells[x, z]);
					}
				}

				newSplatMap[x, z] = oldSplatMap != null ? oldSplatMap[x0, z0] : TerrainSplatWeights.CreateSolid(0);
			}
		}

		GameHost.Instance.EcsWorld.Set(GameHost.Instance.WorldEntity, new TerrainState(
			newWidth, newDepth, state.QuadSize, state.CellSize,
			newCells, newPathing, state.NavMesh, state.NavMeshQuery
		));

		_localCells = newCells;
		_localPathingCodes = newPathing;
		SplatMap = newSplatMap;
		
		if (_material != null)
		{
			_material.SetShaderParameter("terrain_size", new Vector2(newWidth * state.QuadSize, newDepth * state.QuadSize));
		}

		CreateChunks();
		UpdateWaterSize();
		UpdateMeshAndPhysics();
	}

	public void RestoreTerrainFromSnapshot(int newWidth, int newDepth, float quadSize, TerrainCell[,] cells, int[,] pathingCodes, TerrainSplatWeights[,] splatMap)
	{
		if (GameHost.Instance == null || GameHost.Instance.EcsWorld == null || !GameHost.Instance.EcsWorld.IsAlive(GameHost.Instance.WorldEntity)) return;
		if (!GameHost.Instance.EcsWorld.Has<TerrainState>(GameHost.Instance.WorldEntity)) return;

		newWidth = Math.Clamp((int)Math.Round(newWidth / 32.0) * 32, 32, 512);
		newDepth = Math.Clamp((int)Math.Round(newDepth / 32.0) * 32, 32, 512);

		ref var state = ref GameHost.Instance.EcsWorld.Get<TerrainState>(GameHost.Instance.WorldEntity);

		TerrainCell[,] clonedCells = cells != null ? (TerrainCell[,])cells.Clone() : new TerrainCell[newWidth, newDepth];
		int[,] clonedPathing = pathingCodes != null ? (int[,])pathingCodes.Clone() : new int[newWidth, newDepth];
		TerrainSplatWeights[,] clonedSplatMap = splatMap != null ? (TerrainSplatWeights[,])splatMap.Clone() : new TerrainSplatWeights[newWidth, newDepth];

		GameHost.Instance.EcsWorld.Set(GameHost.Instance.WorldEntity, new TerrainState(
			newWidth, newDepth, quadSize, state.CellSize,
			clonedCells, clonedPathing, state.NavMesh, state.NavMeshQuery
		));

		_localCells = clonedCells;
		_localPathingCodes = clonedPathing;
		SplatMap = clonedSplatMap;

		CreateChunks();
		UpdateWaterTransform();
		UpdateWaterSize();
		UpdateMeshAndPhysics();
	}

	public void RestoreTerrainFromSnapshot(int newWidth, int newDepth, float quadSize, float[,] heights, int[,] pathingCodes, TerrainSplatWeights[,] splatMap)
	{
		if (GameHost.Instance == null || GameHost.Instance.EcsWorld == null || !GameHost.Instance.EcsWorld.IsAlive(GameHost.Instance.WorldEntity)) return;
		if (!GameHost.Instance.EcsWorld.Has<TerrainState>(GameHost.Instance.WorldEntity)) return;

		newWidth = Math.Clamp((int)Math.Round(newWidth / 32.0) * 32, 32, 512);
		newDepth = Math.Clamp((int)Math.Round(newDepth / 32.0) * 32, 32, 512);

		ref var state = ref GameHost.Instance.EcsWorld.Get<TerrainState>(GameHost.Instance.WorldEntity);

		float[,] clonedSource = (float[,])heights.Clone();
		int[,] clonedPathing = pathingCodes != null ? (int[,])pathingCodes.Clone() : new int[newWidth, newDepth];
		TerrainSplatWeights[,] clonedSplatMap = splatMap != null ? (TerrainSplatWeights[,])splatMap.Clone() : new TerrainSplatWeights[newWidth, newDepth];

		var calculatedCells = TerrainState.CalculateCells(newWidth, newDepth, clonedSource);

		GameHost.Instance.EcsWorld.Set(GameHost.Instance.WorldEntity, new TerrainState(
			newWidth, newDepth, quadSize, state.CellSize,
			calculatedCells, clonedPathing, state.NavMesh, state.NavMeshQuery
		));

		_localCells = calculatedCells;
		_localPathingCodes = clonedPathing;
		SplatMap = clonedSplatMap;

		CreateChunks();
		
		UpdateWaterTransform();
		UpdateWaterSize();
		UpdateMeshAndPhysics();
	}

	private void WeldChunkVertices(TerrainChunk chunk, ref int vertexIndex, int indexIndex)
	{
		if (vertexIndex == 0)
		{
			return;
		}

		Dictionary<TerrainVertexKey, int> vertexMap = new Dictionary<TerrainVertexKey, int>(vertexIndex);
		int[] remapTable = new int[vertexIndex];
		int uniqueVertexCount = 0;

		for (int v = 0; v < vertexIndex; v++)
		{
			int texIdx = v * 4;
			TerrainVertexKey key = new TerrainVertexKey(
				chunk.VerticesCache[v],
				chunk.NormalsCache[v],
				chunk.UvsCache[v],
				chunk.ColorsCache[v],
				chunk.TexIndicesCache[texIdx],
				chunk.TexIndicesCache[texIdx + 1],
				chunk.TexIndicesCache[texIdx + 2],
				chunk.TexIndicesCache[texIdx + 3],
				chunk.TexWeightsCache01[texIdx],
				chunk.TexWeightsCache01[texIdx + 1],
				chunk.TexWeightsCache01[texIdx + 2],
				chunk.TexWeightsCache01[texIdx + 3],
				chunk.CliffIndicesCache[texIdx],
				chunk.CliffIndicesCache[texIdx + 1],
				chunk.CliffIndicesCache[texIdx + 2],
				chunk.CliffIndicesCache[texIdx + 3],
				chunk.CliffWeightsCache[texIdx],
				chunk.CliffWeightsCache[texIdx + 1],
				chunk.CliffWeightsCache[texIdx + 2],
				chunk.CliffWeightsCache[texIdx + 3]
			);

			if (vertexMap.TryGetValue(key, out int existingIndex))
			{
				remapTable[v] = existingIndex;
			}
			else
			{
				int newIndex = uniqueVertexCount++;
				vertexMap[key] = newIndex;
				remapTable[v] = newIndex;

				if (newIndex != v)
				{
					chunk.VerticesCache[newIndex] = chunk.VerticesCache[v];
					chunk.NormalsCache[newIndex] = chunk.NormalsCache[v];
					chunk.UvsCache[newIndex] = chunk.UvsCache[v];
					chunk.ColorsCache[newIndex] = chunk.ColorsCache[v];

					int newTexIdx = newIndex * 4;
					chunk.TexIndicesCache[newTexIdx] = chunk.TexIndicesCache[texIdx];
					chunk.TexIndicesCache[newTexIdx + 1] = chunk.TexIndicesCache[texIdx + 1];
					chunk.TexIndicesCache[newTexIdx + 2] = chunk.TexIndicesCache[texIdx + 2];
					chunk.TexIndicesCache[newTexIdx + 3] = chunk.TexIndicesCache[texIdx + 3];

					chunk.TexWeightsCache01[newTexIdx] = chunk.TexWeightsCache01[texIdx];
					chunk.TexWeightsCache01[newTexIdx + 1] = chunk.TexWeightsCache01[texIdx + 1];
					chunk.TexWeightsCache01[newTexIdx + 2] = chunk.TexWeightsCache01[texIdx + 2];
					chunk.TexWeightsCache01[newTexIdx + 3] = chunk.TexWeightsCache01[texIdx + 3];

					chunk.CliffIndicesCache[newTexIdx] = chunk.CliffIndicesCache[texIdx];
					chunk.CliffIndicesCache[newTexIdx + 1] = chunk.CliffIndicesCache[texIdx + 1];
					chunk.CliffIndicesCache[newTexIdx + 2] = chunk.CliffIndicesCache[texIdx + 2];
					chunk.CliffIndicesCache[newTexIdx + 3] = chunk.CliffIndicesCache[texIdx + 3];

					chunk.CliffWeightsCache[newTexIdx] = chunk.CliffWeightsCache[texIdx];
					chunk.CliffWeightsCache[newTexIdx + 1] = chunk.CliffWeightsCache[texIdx + 1];
					chunk.CliffWeightsCache[newTexIdx + 2] = chunk.CliffWeightsCache[texIdx + 2];
					chunk.CliffWeightsCache[newTexIdx + 3] = chunk.CliffWeightsCache[texIdx + 3];
				}
			}
		}

		for (int i = 0; i < indexIndex; i++)
		{
			chunk.IndicesCache[i] = remapTable[chunk.IndicesCache[i]];
		}

		vertexIndex = uniqueVertexCount;
	}

	private readonly struct TerrainVertexKey : IEquatable<TerrainVertexKey>
	{
		private readonly int _positionX;
		private readonly int _positionY;
		private readonly int _positionZ;
		private readonly int _normalX;
		private readonly int _normalY;
		private readonly int _normalZ;
		private readonly int _textureU;
		private readonly int _textureV;
		private readonly int _colorAlpha;
		private readonly int _quadCliff;
		private readonly int _gTex0;
		private readonly int _gTex1;
		private readonly int _gTex2;
		private readonly int _gTex3;
		private readonly int _gw0;
		private readonly int _gw1;
		private readonly int _gw2;
		private readonly int _gw3;
		private readonly int _cTex0;
		private readonly int _cTex1;
		private readonly int _cTex2;
		private readonly int _cTex3;
		private readonly int _cw0;
		private readonly int _cw1;
		private readonly int _cw2;
		private readonly int _cw3;

		public TerrainVertexKey(
			Vector3 position,
			Vector3 normal,
			Vector2 uv,
			Color color,
			float gTex0, float gTex1, float gTex2, float gTex3,
			float gw0, float gw1, float gw2, float gw3,
			float cTex0, float cTex1, float cTex2, float cTex3,
			float cw0, float cw1, float cw2, float cw3)
		{
			_positionX = (int)MathF.Round(position.X * 2000.0f);
			_positionY = (int)MathF.Round(position.Y * 2000.0f);
			_positionZ = (int)MathF.Round(position.Z * 2000.0f);
			_normalX = (int)MathF.Round(normal.X * 1000.0f);
			_normalY = (int)MathF.Round(normal.Y * 1000.0f);
			_normalZ = (int)MathF.Round(normal.Z * 1000.0f);
			_textureU = (int)MathF.Round(uv.X * 1000.0f);
			_textureV = (int)MathF.Round(uv.Y * 1000.0f);
			_colorAlpha = (int)MathF.Round(color.A * 1000.0f);
			_quadCliff = (int)MathF.Round(color.R);
			_gTex0 = (int)MathF.Round(gTex0);
			_gTex1 = (int)MathF.Round(gTex1);
			_gTex2 = (int)MathF.Round(gTex2);
			_gTex3 = (int)MathF.Round(gTex3);
			_gw0 = (int)MathF.Round(gw0 * 1000.0f);
			_gw1 = (int)MathF.Round(gw1 * 1000.0f);
			_gw2 = (int)MathF.Round(gw2 * 1000.0f);
			_gw3 = (int)MathF.Round(gw3 * 1000.0f);
			_cTex0 = (int)MathF.Round(cTex0);
			_cTex1 = (int)MathF.Round(cTex1);
			_cTex2 = (int)MathF.Round(cTex2);
			_cTex3 = (int)MathF.Round(cTex3);
			_cw0 = (int)MathF.Round(cw0 * 1000.0f);
			_cw1 = (int)MathF.Round(cw1 * 1000.0f);
			_cw2 = (int)MathF.Round(cw2 * 1000.0f);
			_cw3 = (int)MathF.Round(cw3 * 1000.0f);
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
				   _textureV == other._textureV &&
				   _colorAlpha == other._colorAlpha &&
				   _quadCliff == other._quadCliff &&
				   _gTex0 == other._gTex0 &&
				   _gTex1 == other._gTex1 &&
				   _gTex2 == other._gTex2 &&
				   _gTex3 == other._gTex3 &&
				   _gw0 == other._gw0 &&
				   _gw1 == other._gw1 &&
				   _gw2 == other._gw2 &&
				   _gw3 == other._gw3 &&
				   _cTex0 == other._cTex0 &&
				   _cTex1 == other._cTex1 &&
				   _cTex2 == other._cTex2 &&
				   _cTex3 == other._cTex3 &&
				   _cw0 == other._cw0 &&
				   _cw1 == other._cw1 &&
				   _cw2 == other._cw2 &&
				   _cw3 == other._cw3;
		}

		public override bool Equals(object? obj)
		{
			return obj is TerrainVertexKey other && Equals(other);
		}

		public override int GetHashCode()
		{
			HashCode hashCode = new HashCode();
			hashCode.Add(_positionX);
			hashCode.Add(_positionY);
			hashCode.Add(_positionZ);
			hashCode.Add(_normalX);
			hashCode.Add(_normalY);
			hashCode.Add(_normalZ);
			hashCode.Add(_textureU);
			hashCode.Add(_textureV);
			hashCode.Add(_colorAlpha);
			hashCode.Add(_quadCliff);
			hashCode.Add(_gTex0);
			hashCode.Add(_gw0);
			hashCode.Add(_cTex0);
			hashCode.Add(_cw0);
			return hashCode.ToHashCode();
		}
	}
}
