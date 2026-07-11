using Arch.Core;
using Realm.Ecs.Components.Terrain;
using DotRecast.Detour;
using Godot;
using System;
using System.Collections.Generic;

public partial class EditableTerrain : StaticBody3D
{
	public void ProcessAndSaveRawTexture(string rawPngPath, string outputKtx2Path)
	{
		var img = Godot.Image.LoadFromFile(rawPngPath);
		if (img == null) return;
		
		int w = img.GetWidth();
		int h = img.GetHeight();
		if (img.GetFormat() != Godot.Image.Format.Rgba8)
		{
			img.Convert(Godot.Image.Format.Rgba8);
		}
		
		var layer0 = Godot.Image.CreateEmpty(w, h, false, Godot.Image.Format.Rgba8);
		var layer1 = Godot.Image.CreateEmpty(w, h, false, Godot.Image.Format.Rgba8);
		
		for (int y = 0; y < h; y++)
		{
			for (int x = 0; x < w; x++)
			{
				var color = img.GetPixel(x, y);
				float luma = 0.299f * color.R + 0.587f * color.G + 0.114f * color.B;
				layer0.SetPixel(x, y, new Godot.Color(color.R, color.G, color.B, luma));
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
				
				float h00 = layer0.GetPixel(px, py).A;
				float h10 = layer0.GetPixel(x, py).A;
				float h20 = layer0.GetPixel(nx, py).A;
				
				float h01 = layer0.GetPixel(px, y).A;
				float h21 = layer0.GetPixel(nx, y).A;
				
				float h02 = layer0.GetPixel(px, ny).A;
				float h12 = layer0.GetPixel(x, ny).A;
				float h22 = layer0.GetPixel(nx, ny).A;
				
				float dx = (h20 + 2.0f * h21 + h22) - (h00 + 2.0f * h01 + h02);
				float dy = (h02 + 2.0f * h12 + h22) - (h00 + 2.0f * h10 + h20);
				float dz = 1.0f / 5.0f;
				
				var normal = new Godot.Vector3(-dx, -dy, dz).Normalized();
				
				float laplacian = (h10 + h01 + h21 + h12) - 4.0f * layer0.GetPixel(x, y).A;
				float height = layer0.GetPixel(x, y).A;
				float ao = 1.0f - Godot.Mathf.Clamp(laplacian * 4.0f, 0.0f, 0.5f);
				ao *= (0.7f + 0.3f * height);
				ao = Godot.Mathf.Clamp(ao, 0.0f, 1.0f);
				
				float r = (normal.X * 0.5f + 0.5f);
				float g = (normal.Y * 0.5f + 0.5f);
				float b = ao;
				
				float contrastHeight = (height - 0.5f) * 1.5f + 0.5f;
				contrastHeight = Godot.Mathf.Clamp(contrastHeight, 0.0f, 1.0f);
				float roughness = Godot.Mathf.Lerp(0.5f, 0.8f, contrastHeight);
				
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
		
		string ktxCmd = "ktx";
		string localPath = System.IO.Path.Combine(Godot.ProjectSettings.GlobalizePath("res://"), "ktx_tools", "bin", "ktx.exe");
		if (System.IO.File.Exists(localPath))
		{
			ktxCmd = localPath;
		}
		else
		{
			string workspacePath = @"C:\temp\Realm\ktx_tools\v5.0.0-rc1\bin\ktx.exe";
			if (!System.IO.File.Exists(workspacePath))
			{
				workspacePath = @"C:\temp\Realm\ktx_tools\bin\ktx.exe";
			}
			if (System.IO.File.Exists(workspacePath))
			{
				ktxCmd = workspacePath;
			}
		}
		
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

	public static int GetDefaultPathingCode(float height, float waterHeight, bool waterEnabled)
	{
		if (!waterEnabled)
		{
			return PATHING_GROUND | PATHING_BUILDABLE | PATHING_FLYING;
		}

		if (height >= waterHeight)
		{
			return PATHING_GROUND | PATHING_BUILDABLE | PATHING_FLYING;
		}

		float depth = waterHeight - height;
		if (depth < 4.0f)
		{
			return PATHING_SHALLOW_WATER | PATHING_FLYING;
		}

		return PATHING_DEEP_WATER | PATHING_FLYING;
	}

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

	private const int CHUNK_SIZE = 32;
	private int _chunkedWidth;
	private int _chunkedDepth;
	
	private class TerrainChunk
	{
		public MeshInstance3D MeshInstance;
		public CollisionShape3D CollisionShape;
		public ArrayMesh ArrayMesh;
		public int StartX;
		public int StartZ;
		public int EndX;
		public int EndZ;
		public Vector3[] VerticesCache;
		public float[] TexIndicesCache;
		public float[] TexWeightsCache01;
		public Vector3[] NormalsCache;
		public Vector2[] UvsCache;
		public int[] IndicesCache;
		public float[] MapDataCache;
	}
	
	private List<TerrainChunk> _chunks = new List<TerrainChunk>();
	private ShaderMaterial _material;
	private MeshInstance3D _waterMesh;

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
			float[,] h = Heights;
			for (int z = 0; z < Depth; z++)
				for (int x = 0; x < Width; x++)
					newPathing[x, z] = GetDefaultPathingCode(h[x, z], WaterHeight, WaterEnabled);
			PathingCodes = newPathing;
		}

		var shader = new Shader();
		shader.Code = @"
shader_type spatial;
render_mode blend_mix;

uniform sampler2DArray terrain_textures : source_color;
uniform sampler2DArray terrain_normals_pbr : hint_default_white;
uniform float blend_softness = 0.2;
uniform sampler2D fog_texture : hint_default_white;
uniform vec2 fog_world_min = vec2(-125.0, -125.0);
uniform vec2 fog_world_size = vec2(250.0, 250.0);

uniform sampler2D pathing_texture : hint_default_transparent, filter_nearest;
uniform bool pathing_visible = false;

uniform bool grid_visible = false;
uniform vec4 grid_color_thick = vec4(1.0, 0.9, 0.0, 0.85);
uniform vec4 grid_color_thin = vec4(1.0, 0.9, 0.0, 0.25);
uniform float grid_spacing = 2.0;
uniform vec2 terrain_size = vec2(1.0, 1.0);

varying vec4 v_tex_indices;
varying vec4 v_tex_weights;
varying vec3 v_world_pos;

void vertex() {
	v_tex_indices = CUSTOM0;
	v_tex_weights = CUSTOM1;
	v_world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	vec4 c0 = texture(terrain_textures, vec3(UV, v_tex_indices.x));
	vec4 c1 = texture(terrain_textures, vec3(UV, v_tex_indices.y));
	vec4 c2 = texture(terrain_textures, vec3(UV, v_tex_indices.z));
	vec4 c3 = texture(terrain_textures, vec3(UV, v_tex_indices.w));

	float h0 = c0.a + v_tex_weights.x;
	float h1 = c1.a + v_tex_weights.y;
	float h2 = c2.a + v_tex_weights.z;
	float h3 = c3.a + v_tex_weights.w;
	
	float max_h = max(max(h0, h1), max(h2, h3));
	float t = max_h - blend_softness;
	
	float w0 = max(h0 - t, 0.0);
	float w1 = max(h1 - t, 0.0);
	float w2 = max(h2 - t, 0.0);
	float w3 = max(h3 - t, 0.0);
	
	float sum = w0 + w1 + w2 + w3;
	if (sum > 0.0) {
		w0 /= sum; w1 /= sum; w2 /= sum; w3 /= sum;
	} else {
		w0 = 1.0; w1 = 0.0; w2 = 0.0; w3 = 0.0;
	}
	
	vec3 terrain_color = (c0.rgb * w0 +
	                      c1.rgb * w1 +
	                      c2.rgb * w2 +
	                      c3.rgb * w3);

	vec3 final_albedo = terrain_color;
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
			if (code == 0 || (code & 16) != 0) {
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

	vec4 n0 = texture(terrain_normals_pbr, vec3(UV, v_tex_indices.x));
	vec4 n1 = texture(terrain_normals_pbr, vec3(UV, v_tex_indices.y));
	vec4 n2 = texture(terrain_normals_pbr, vec3(UV, v_tex_indices.z));
	vec4 n3 = texture(terrain_normals_pbr, vec3(UV, v_tex_indices.w));
	
	vec2 n0_xy = vec2(n0.r * 2.0 - 1.0, (1.0 - n0.g) * 2.0 - 1.0);
	vec3 n0_vec = vec3(n0_xy, sqrt(max(0.0, 1.0 - dot(n0_xy, n0_xy))));
	
	vec2 n1_xy = vec2(n1.r * 2.0 - 1.0, (1.0 - n1.g) * 2.0 - 1.0);
	vec3 n1_vec = vec3(n1_xy, sqrt(max(0.0, 1.0 - dot(n1_xy, n1_xy))));
	
	vec2 n2_xy = vec2(n2.r * 2.0 - 1.0, (1.0 - n2.g) * 2.0 - 1.0);
	vec3 n2_vec = vec3(n2_xy, sqrt(max(0.0, 1.0 - dot(n2_xy, n2_xy))));
	
	vec2 n3_xy = vec2(n3.r * 2.0 - 1.0, (1.0 - n3.g) * 2.0 - 1.0);
	vec3 n3_vec = vec3(n3_xy, sqrt(max(0.0, 1.0 - dot(n3_xy, n3_xy))));
	
	vec3 blended_normal_tangent = normalize(n0_vec * w0 + n1_vec * w1 + n2_vec * w2 + n3_vec * w3);
	float blended_ao = (n0.b * w0 + n1.b * w1 + n2.b * w2 + n3.b * w3);
	float blended_roughness = (n0.a * w0 + n1.a * w1 + n2.a * w2 + n3.a * w3);
	float final_roughness = clamp(blended_roughness, 0.5, 1.0);

	ALBEDO = final_albedo;
	NORMAL = TANGENT * blended_normal_tangent.x + BINORMAL * blended_normal_tangent.y + NORMAL * blended_normal_tangent.z;
	AO = mix(1.0, blended_ao, 0.5);
	ROUGHNESS = final_roughness;
	METALLIC = 0.0;                 
	SPECULAR = 0.2;                 
	EMISSION = emission_color;
}
";

		_material = new ShaderMaterial();
		_material.Shader = shader;
		ReloadTerrainTextures();

		var defaultFogImage = Image.CreateEmpty(32, 32, false, Image.Format.Rf);
		defaultFogImage.Fill(new Color(0f, 0f, 0f, 1f));
		var defaultFogTexture = ImageTexture.CreateFromImage(defaultFogImage);
		_material.SetShaderParameter("fog_texture", defaultFogTexture);

		_material.SetShaderParameter("grid_spacing", Spacing);
		_material.SetShaderParameter("terrain_size", new Vector2(Width * Spacing, Depth * Spacing));

		CreateChunks();
		CreateWater();
		UpdateMeshAndPhysics();
	}

	private string GetKtxCmdPath()
	{
		string ktxCmd = "ktx";
		string localPath = System.IO.Path.Combine(Godot.ProjectSettings.GlobalizePath("res://"), "ktx_tools", "bin", "ktx.exe");
		if (System.IO.File.Exists(localPath))
		{
			ktxCmd = localPath;
		}
		else
		{
			string workspacePath = @"C:\temp\Realm\ktx_tools\v5.0.0-rc1\bin\ktx.exe";
			if (!System.IO.File.Exists(workspacePath))
			{
				workspacePath = @"C:\temp\Realm\ktx_tools\bin\ktx.exe";
			}
			if (System.IO.File.Exists(workspacePath))
			{
				ktxCmd = workspacePath;
			}
		}
		return ktxCmd;
	}

	private (Image AlbedoHeight, Image NormalRoughness) LoadKtx2LayersDynamic(string ktx2Path)
	{
		string globalKtx2Path = Godot.ProjectSettings.GlobalizePath(ktx2Path);
		string tempOut0 = $"user://temp_ext0_{System.Guid.NewGuid()}.png";
		string tempOut1 = $"user://temp_ext1_{System.Guid.NewGuid()}.png";
		string globalTempOut0 = Godot.ProjectSettings.GlobalizePath(tempOut0);
		string globalTempOut1 = Godot.ProjectSettings.GlobalizePath(tempOut1);
		string ktxCmd = GetKtxCmdPath();
		try
		{
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
		finally
		{
			if (System.IO.File.Exists(globalTempOut0)) System.IO.File.Delete(globalTempOut0);
			if (System.IO.File.Exists(globalTempOut1)) System.IO.File.Delete(globalTempOut1);
		}
	}

	public void ReloadTerrainTextures()
	{
		if (_material == null) return;
		string mapDir = GameHost.Instance != null && !string.IsNullOrEmpty(GameHost.Instance.CurrentMapDirectory)
			? GameHost.Instance.CurrentMapDirectory
			: Godot.ProjectSettings.GlobalizePath("user://temp_map_workspace");
		var albedoHeightImages = new Godot.Collections.Array<Image>();
		var normalRoughnessImages = new Godot.Collections.Array<Image>();
		int texWidth = 0;
		int texHeight = 0;
		var names = new[]
		{
			"ancient_ruin", "deep_moss", "grey_slate", "iron_dust",
			"lava_vein", "mossy_stone", "pale_sand", "river_silt",
			"royal_marble", "tarn_mud", "dark_wood", "mist_grove"
		};
		foreach (var name in names)
		{
			string ktx2Path = System.IO.Path.Combine(mapDir, name + ".ktx2");
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
				int fbWidth = texWidth != 0 ? texWidth : 512;
				int fbHeight = texHeight != 0 ? texHeight : 512;
				imgLayer0 = Godot.Image.CreateEmpty(fbWidth, fbHeight, false, Godot.Image.Format.Rgba8);
				imgLayer0.Fill(new Color(1f, 0f, 1f, 0.99f));
				imgLayer1 = Godot.Image.CreateEmpty(fbWidth, fbHeight, false, Godot.Image.Format.Rgba8);
				imgLayer1.Fill(new Color(0.5f, 0.5f, 1.0f, 0.8f));
			}
			if (texWidth == 0)
			{
				texWidth = imgLayer0.GetWidth();
				texHeight = imgLayer0.GetHeight();
			}
			else
			{
				if (imgLayer0.GetWidth() != texWidth || imgLayer0.GetHeight() != texHeight)
				{
					imgLayer0.Resize(texWidth, texHeight);
				}
				if (imgLayer1.GetWidth() != texWidth || imgLayer1.GetHeight() != texHeight)
				{
					imgLayer1.Resize(texWidth, texHeight);
				}
			}
			if (imgLayer0.GetFormat() != Godot.Image.Format.Rgba8) imgLayer0.Convert(Godot.Image.Format.Rgba8);
			if (imgLayer1.GetFormat() != Godot.Image.Format.Rgba8) imgLayer1.Convert(Godot.Image.Format.Rgba8);

			var p0 = imgLayer0.GetPixel(0, 0);
			if (p0.A >= 1.0f) imgLayer0.SetPixel(0, 0, new Color(p0.R, p0.G, p0.B, 0.99f));
			var p1 = imgLayer1.GetPixel(0, 0);
			if (p1.A >= 1.0f) imgLayer1.SetPixel(0, 0, new Color(p1.R, p1.G, p1.B, 0.99f));

			imgLayer0.GenerateMipmaps(false);
			imgLayer1.GenerateMipmaps(true);
			imgLayer0.Compress(Godot.Image.CompressMode.S3Tc, Godot.Image.CompressSource.Generic);
			imgLayer1.Compress(Godot.Image.CompressMode.S3Tc, Godot.Image.CompressSource.Generic);
			albedoHeightImages.Add(imgLayer0);
			normalRoughnessImages.Add(imgLayer1);
		}
		var albedoTextureArray = new Texture2DArray();
		albedoTextureArray.CreateFromImages(albedoHeightImages);
		var normalTextureArray = new Texture2DArray();
		normalTextureArray.CreateFromImages(normalRoughnessImages);
		_material.SetShaderParameter("terrain_textures", albedoTextureArray);
		_material.SetShaderParameter("terrain_normals_pbr", normalTextureArray);
	}

	private void CreateChunks()
	{
		foreach (var chunk in _chunks)
		{
			if (GodotObject.IsInstanceValid(chunk.MeshInstance)) chunk.MeshInstance.QueueFree();
			if (GodotObject.IsInstanceValid(chunk.CollisionShape)) chunk.CollisionShape.QueueFree();
		}
		_chunks.Clear();

		int w = Width;
		int d = Depth;
		_chunkedWidth = w;
		_chunkedDepth = d;

		for (int z = 0; z < d - 1; z += CHUNK_SIZE)
		{
			for (int x = 0; x < w - 1; x += CHUNK_SIZE)
			{
				int ex = Math.Min(x + CHUNK_SIZE, w - 1);
				int ez = Math.Min(z + CHUNK_SIZE, d - 1);
				
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

				chunk.CollisionShape = new CollisionShape3D();
				chunk.CollisionShape.Name = $"TerrainCollision_{x}_{z}";
				
				// Position collision shape correctly for the chunk
				float lx = (x + (ex - x) / 2.0f - (w - 1) / 2.0f) * Spacing;
				float lz = (z + (ez - z) / 2.0f - (d - 1) / 2.0f) * Spacing;
				chunk.CollisionShape.Position = new Vector3(lx, 0.0f, lz);
				
				AddChild(chunk.CollisionShape);
				
				_chunks.Add(chunk);
			}
		}
	}

	public void SetFogTexture(ImageTexture fogTexture)
	{
		if (_material != null && fogTexture != null)
		{
			_material.SetShaderParameter("fog_texture", fogTexture);
		}
	}

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

	public void UpdateMeshAndPhysics(bool rebuildPhysics = true, bool rebuildNavMesh = true, Rect2I? affectedRegion = null)
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

		if (_chunks.Count == 0 || _chunkedWidth != w || _chunkedDepth != d)
		{
			CreateChunks();
		}

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

			UpdateChunk(chunk, rebuildPhysics);
		}
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

		var heightMapShape = new HeightMapShape3D();
		heightMapShape.MapWidth = cellWidth + 1;
		heightMapShape.MapDepth = cellDepth + 1;
		
		int mapDataCount = (cellWidth + 1) * (cellDepth + 1);
		if (chunk.MapDataCache == null || chunk.MapDataCache.Length != mapDataCount)
		{
			chunk.MapDataCache = new float[mapDataCount];
		}
		
		for (int z = 0; z <= cellDepth; z++)
		{
			for (int x = 0; x <= cellWidth; x++)
			{
				chunk.MapDataCache[z * (cellWidth + 1) + x] = Heights[chunk.StartX + x, chunk.StartZ + z];
			}
		}
		heightMapShape.MapData = chunk.MapDataCache;
		chunk.CollisionShape.Shape = heightMapShape;
		chunk.CollisionShape.Scale = new Vector3(Spacing, 1.0f, Spacing);
	}

	private void UpdateChunk(TerrainChunk chunk, bool rebuildPhysics)
	{
		int cellWidth = chunk.EndX - chunk.StartX;
		int cellDepth = chunk.EndZ - chunk.StartZ;
		int triangleCount = cellWidth * cellDepth * 2;
		int vertexCount = triangleCount * 3;

		if (chunk.VerticesCache == null || chunk.VerticesCache.Length != vertexCount)
		{
			chunk.VerticesCache = new Vector3[vertexCount];
			chunk.TexIndicesCache = new float[vertexCount * 4];
			chunk.TexWeightsCache01 = new float[vertexCount * 4];
			chunk.NormalsCache = new Vector3[vertexCount];
			chunk.UvsCache = new Vector2[vertexCount];
			chunk.IndicesCache = new int[vertexCount];
		}

		int vertexIndex = 0;
		for (int z = chunk.StartZ; z < chunk.EndZ; z++)
		{
			for (int x = chunk.StartX; x < chunk.EndX; x++)
			{
				ProcessTriangle(chunk, x, z, x + 1, z, x, z + 1, ref vertexIndex);
				ProcessTriangle(chunk, x + 1, z, x + 1, z + 1, x, z + 1, ref vertexIndex);
			}
		}

		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = chunk.VerticesCache;
		arrays[(int)Mesh.ArrayType.Normal] = chunk.NormalsCache;
		arrays[(int)Mesh.ArrayType.TexUV] = chunk.UvsCache;
		arrays[(int)Mesh.ArrayType.Custom0] = chunk.TexIndicesCache;
		arrays[(int)Mesh.ArrayType.Custom1] = chunk.TexWeightsCache01;
		arrays[(int)Mesh.ArrayType.Index] = chunk.IndicesCache;

		chunk.ArrayMesh.ClearSurfaces();
		if (vertexCount > 0)
		{
			int custom0Format = (int)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom0Shift;
			int custom1Format = (int)Mesh.ArrayCustomFormat.RgbaFloat << (int)Mesh.ArrayFormat.FormatCustom1Shift;
			chunk.ArrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays,
				new Godot.Collections.Array<Godot.Collections.Array>(),
				null,
				(Mesh.ArrayFormat)((int)(Mesh.ArrayFormat.FormatCustom0 | Mesh.ArrayFormat.FormatCustom1) | custom0Format | custom1Format));
		}

		if (rebuildPhysics)
		{
			UpdateChunkPhysics(chunk);
		}
	}

	private void ProcessTriangle(
		TerrainChunk chunk,
		int x0, int z0,
		int x1, int z1,
		int x2, int z2,
		ref int vertexIndex)
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

		PopulateTriangleVertex(chunk, x0, z0, p0, p1, p2, p3, s0, ref vertexIndex);
		PopulateTriangleVertex(chunk, x1, z1, p0, p1, p2, p3, s1, ref vertexIndex);
		PopulateTriangleVertex(chunk, x2, z2, p0, p1, p2, p3, s2, ref vertexIndex);
	}

	private void PopulateTriangleVertex(
		TerrainChunk chunk,
		int x, int z,
		int p0, int p1, int p2, int p3,
		TerrainSplatWeights srcSplat,
		ref int vertexIndex)
	{
		float lx = (x - (Width - 1) / 2.0f) * Spacing;
		float lz = (z - (Depth - 1) / 2.0f) * Spacing;
		chunk.VerticesCache[vertexIndex] = new Vector3(lx, Heights[x, z], lz);

		chunk.NormalsCache[vertexIndex] = GetVertexNormal(x, z);

		chunk.UvsCache[vertexIndex] = new Vector2((float)x / (Width - 1) * 25f, (float)z / (Depth - 1) * 25f);

		chunk.IndicesCache[vertexIndex] = vertexIndex;

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
		chunk.TexIndicesCache[sIdx + 0] = p0;
		chunk.TexIndicesCache[sIdx + 1] = p1;
		chunk.TexIndicesCache[sIdx + 2] = p2;
		chunk.TexIndicesCache[sIdx + 3] = p3;

		chunk.TexWeightsCache01[sIdx + 0] = w0;
		chunk.TexWeightsCache01[sIdx + 1] = w1;
		chunk.TexWeightsCache01[sIdx + 2] = w2;
		chunk.TexWeightsCache01[sIdx + 3] = w3;

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
					newPathing[x, z] = GetDefaultPathingCode(0.0f, state.WaterHeight, state.WaterEnabled);
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
		
		if (_material != null)
		{
			_material.SetShaderParameter("terrain_size", new Vector2(newWidth * state.Spacing, newDepth * state.Spacing));
		}

		CreateChunks();

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
					newPathing[x, z] = GetDefaultPathingCode(newHeights[x, z], state.WaterHeight, state.WaterEnabled);
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
		
		if (_material != null)
		{
			_material.SetShaderParameter("terrain_size", new Vector2(newWidth * state.Spacing, newDepth * state.Spacing));
		}

		CreateChunks();

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
		
		if (newWidth != Width || newDepth != Depth) {
			CreateChunks();
		}
		
		UpdateWaterTransform();
		UpdateWaterSize();
		UpdateMeshAndPhysics();
	}
}
