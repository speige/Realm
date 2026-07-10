using Arch.Core;
using Realm.Ecs.Components.Terrain;
using DotRecast.Detour;
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
	vec3 terrain_color = (c0.rgb * v_tex_weights.x +
	                      c1.rgb * v_tex_weights.y +
	                      c2.rgb * v_tex_weights.z +
	                      c3.rgb * v_tex_weights.w);

	vec3 final_albedo = terrain_color;
	vec3 emission_color = vec3(0.0);
	
	if (pathing_visible) {
		vec2 pathing_uv = (v_world_pos.xz + terrain_size / 2.0) / terrain_size;
		int code = int(round(texture(pathing_texture, pathing_uv).r * 255.0));
		
		int bit_0 = code % 2;
		int bit_1 = (code / 2) % 2;
		int bit_2 = (code / 4) % 2;
		int bit_3 = (code / 8) % 2;
		int bit_4 = (code / 16) % 2;
		int bit_5 = (code / 32) % 2;
		
		int active_count = 0;
		int flag_0 = -1;
		int flag_1 = -1;
		int flag_2 = -1;
		int flag_3 = -1;
		int flag_4 = -1;
		int flag_5 = -1;
		
		if (bit_4 != 0) {
			if (active_count == 0) flag_0 = 16;
			else if (active_count == 1) flag_1 = 16;
			else if (active_count == 2) flag_2 = 16;
			else if (active_count == 3) flag_3 = 16;
			else if (active_count == 4) flag_4 = 16;
			else if (active_count == 5) flag_5 = 16;
			active_count++;
		}
		if (bit_3 != 0) {
			if (active_count == 0) flag_0 = 8;
			else if (active_count == 1) flag_1 = 8;
			else if (active_count == 2) flag_2 = 8;
			else if (active_count == 3) flag_3 = 8;
			else if (active_count == 4) flag_4 = 8;
			else if (active_count == 5) flag_5 = 8;
			active_count++;
		}
		if (bit_5 != 0) {
			if (active_count == 0) flag_0 = 32;
			else if (active_count == 1) flag_1 = 32;
			else if (active_count == 2) flag_2 = 32;
			else if (active_count == 3) flag_3 = 32;
			else if (active_count == 4) flag_4 = 32;
			else if (active_count == 5) flag_5 = 32;
			active_count++;
		}
		if (bit_0 != 0) {
			if (active_count == 0) flag_0 = 1;
			else if (active_count == 1) flag_1 = 1;
			else if (active_count == 2) flag_2 = 1;
			else if (active_count == 3) flag_3 = 1;
			else if (active_count == 4) flag_4 = 1;
			else if (active_count == 5) flag_5 = 1;
			active_count++;
		}
		if (bit_1 != 0) {
			if (active_count == 0) flag_0 = 2;
			else if (active_count == 1) flag_1 = 2;
			else if (active_count == 2) flag_2 = 2;
			else if (active_count == 3) flag_3 = 2;
			else if (active_count == 4) flag_4 = 2;
			else if (active_count == 5) flag_5 = 2;
			active_count++;
		}
		if (bit_2 != 0) {
			if (active_count == 0) flag_0 = 4;
			else if (active_count == 1) flag_1 = 4;
			else if (active_count == 2) flag_2 = 4;
			else if (active_count == 3) flag_3 = 4;
			else if (active_count == 4) flag_4 = 4;
			else if (active_count == 5) flag_5 = 4;
			active_count++;
		}
		
		if (active_count == 0) {
			flag_0 = 0;
			active_count = 1;
		}
		
		vec2 cell_frac = fract(v_world_pos.xz / grid_spacing);
		int sx = int(floor(cell_frac.x * 2.0));
		int sz = int(floor(cell_frac.y * 2.0));
		int flag_idx = (sx + sz) % active_count;
		
		int active_flag = flag_0;
		if (flag_idx == 1) active_flag = flag_1;
		else if (flag_idx == 2) active_flag = flag_2;
		else if (flag_idx == 3) active_flag = flag_3;
		else if (flag_idx == 4) active_flag = flag_4;
		else if (flag_idx == 5) active_flag = flag_5;
		
		vec4 pathing_color = vec4(0.0);
		if (active_flag == 16 || active_flag == 0) {
			pathing_color = vec4(0.9, 0.1, 0.1, 0.25);
		} else if (active_flag == 32) {
			pathing_color = vec4(0.6, 0.2, 0.8, 0.25);
		} else if (active_flag == 8) {
			pathing_color = vec4(0.2, 0.85, 0.2, 0.25);
		} else if (active_flag == 1) {
			pathing_color = vec4(0.2, 0.6, 1.0, 0.25);
		} else if (active_flag == 2) {
			pathing_color = vec4(0.0, 0.15, 0.7, 0.25);
		} else if (active_flag == 4) {
			pathing_color = vec4(0.85, 0.85, 0.0, 0.25);
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

	ALBEDO = final_albedo;
	EMISSION = emission_color;
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
