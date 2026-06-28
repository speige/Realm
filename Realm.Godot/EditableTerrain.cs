using Godot;
using System;
using System.Collections.Generic;
using DotRecast.Core.Numerics;
using DotRecast.Recast;
using DotRecast.Recast.Geom;
using DotRecast.Detour;

public partial class EditableTerrain : StaticBody3D
{
	public float CellSize { get; private set; } = 5.0f / 2.5f / 10.0f;
	public DtNavMesh NavMesh { get; private set; }
	public DtNavMeshQuery NavMeshQuery { get; private set; }

	public int Width { get; private set; } = 126;
	public int Depth { get; private set; } = 126;
	public float Spacing { get; private set; } = 2.0f;

	public const int PATHING_SHALLOW_WATER = 1;
	public const int PATHING_DEEP_WATER = 2;
	public const int PATHING_FLYING = 4;
	public const int PATHING_GROUND = 8;
	public const int PATHING_UNPATHABLE = 16;

	public float[,] Heights { get; private set; }
	public Color[,] Colors { get; private set; }
	public int[,] PathingCodes { get; private set; }

	private MeshInstance3D _meshInstance;
	private CollisionShape3D _collisionShape;
	private ArrayMesh _arrayMesh;
	private ShaderMaterial _material;

	private MeshInstance3D _waterMesh;
	private float _waterHeight = -2.0f;
	private bool _waterEnabled = true;

	public float WaterHeight
	{
		get => _waterHeight;
		set
		{
			_waterHeight = value;
			UpdateWaterTransform();
		}
	}

	public bool WaterEnabled
	{
		get => _waterEnabled;
		set
		{
			_waterEnabled = value;
			if (_waterMesh != null)
			{
				_waterMesh.Visible = _waterEnabled;
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
		_waterMesh.Visible = _waterEnabled;
		UpdateWaterTransform();
	}

	private void UpdateWaterTransform()
	{
		if (_waterMesh != null)
		{
			_waterMesh.Position = new Vector3(0.0f, _waterHeight, 0.0f);
		}
	}

	public override void _Ready()
	{
		Heights = new float[Width, Depth];
		Colors = new Color[Width, Depth];
		PathingCodes = new int[Width, Depth];

		// Initialize heights to 0, colors to green (default grass texture color), pathing to ground | flying
		for (int z = 0; z < Depth; z++)
		{
			for (int x = 0; x < Width; x++)
			{
				Heights[x, z] = 0.0f;
				Colors[x, z] = new Color(0.2f, 0.6f, 0.2f); // Default Grass Green
				PathingCodes[x, z] = PATHING_GROUND | PATHING_FLYING;
			}
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
			"res://Assets/2d/TileSheets/river_silt.png",
			"res://Assets/2d/TileSheets/cinder_rock.png",
			"res://Assets/2d/TileSheets/arid_dust.png",
			"res://Assets/2d/TileSheets/deep_moss.png",
			"res://Assets/2d/TileSheets/crag_stone.png",
			"res://Assets/2d/TileSheets/ash_soil.png",
			"res://Assets/2d/TileSheets/fern_grove.png",
			"res://Assets/2d/TileSheets/mossy_stone.png",
			"res://Assets/2d/TileSheets/holy_moss.png",
			"res://Assets/2d/TileSheets/void_shard.png",
			"res://Assets/terrain_grass.jpg",
			"res://Assets/2d/TileSheets/ash_soil.png"
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
				tex = GD.Load<Texture2D>("res://Assets/terrain_grass.jpg");
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
		// 1. Rebuild ArrayMesh surface
		int vertexCount = Width * Depth;
		var vertices = new Vector3[vertexCount];
		var colors = new Color[vertexCount];
		var normals = new Vector3[vertexCount];
		var uvs = new Vector2[vertexCount];

		// Compute vertices, colors, uvs
		for (int z = 0; z < Depth; z++)
		{
			for (int x = 0; x < Width; x++)
			{
				int idx = z * Width + x;
				float lx = (x - (Width - 1) / 2.0f) * Spacing;
				float lz = (z - (Depth - 1) / 2.0f) * Spacing;
				vertices[idx] = new Vector3(lx, Heights[x, z], lz);
				colors[idx] = Colors[x, z];
				uvs[idx] = new Vector2((float)x / (Width - 1) * 25f, (float)z / (Depth - 1) * 25f);
			}
		}

		// Compute Normals for nice shading
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
				normals[idx] = tangentZ.Cross(tangentX).Normalized();
			}
		}

		// Indices
		int cellWidth = Width - 1;
		int cellDepth = Depth - 1;
		int indexCount = cellWidth * cellDepth * 6;
		var indices = new int[indexCount];
		int iIdx = 0;
		for (int z = 0; z < cellDepth; z++)
		{
			for (int x = 0; x < cellWidth; x++)
			{
				int v00 = z * Width + x;
				int v10 = z * Width + (x + 1);
				int v01 = (z + 1) * Width + x;
				int v11 = (z + 1) * Width + (x + 1);
				
				indices[iIdx++] = v00;
				indices[iIdx++] = v10;
				indices[iIdx++] = v01;
				
				indices[iIdx++] = v10;
				indices[iIdx++] = v11;
				indices[iIdx++] = v01;
			}
		}

		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices;
		arrays[(int)Mesh.ArrayType.Normal] = normals;
		arrays[(int)Mesh.ArrayType.Color] = colors;
		arrays[(int)Mesh.ArrayType.TexUV] = uvs;
		arrays[(int)Mesh.ArrayType.Index] = indices;

		_arrayMesh.ClearSurfaces();
		_arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		_meshInstance.Mesh = _arrayMesh;

		if (!rebuildPhysics) return;

		// 2. Rebuild Collision Shape
		var heightMapShape = new HeightMapShape3D();
		heightMapShape.MapWidth = Width;
		heightMapShape.MapDepth = Depth;
		
		float[] mapData = new float[Width * Depth];
		for (int z = 0; z < Depth; z++)
		{
			for (int x = 0; x < Width; x++)
			{
				mapData[z * Width + x] = Heights[x, z];
			}
		}
		heightMapShape.MapData = mapData;
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
		int width = Width;
		int depth = Depth;
		float spacing = Spacing;
		var verts = new List<float>();
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				float lx = (x - (width - 1) / 2.0f) * spacing;
				float lz = (z - (depth - 1) / 2.0f) * spacing;
				verts.Add(lx);
				verts.Add(Heights[x, z]);
				verts.Add(lz);
			}
		}
		var indices = new List<int>();
		for (int z = 0; z < depth - 1; z++)
		{
			for (int x = 0; x < width - 1; x++)
			{
				int v00 = z * width + x;
				int v10 = z * width + (x + 1);
				int v01 = (z + 1) * width + x;
				int v11 = (z + 1) * width + (x + 1);
				indices.Add(v00);
				indices.Add(v01);
				indices.Add(v10);
				indices.Add(v10);
				indices.Add(v01);
				indices.Add(v11);
			}
		}
		var geom = new SimpleInputGeomProvider(verts, indices);
		float minHeightBrushAdjustment = 5.0f;
		float maxUnitHeight = 2.5f;
		float cellSize = minHeightBrushAdjustment / maxUnitHeight / 10.0f;
		float cellHeight = cellSize * 0.5f;
		float agentRadius = 1.0f;
		float agentHeight = 2.5f;
		float agentMaxClimb = 0.9f;
		float agentMaxSlope = 45.0f;
		RcConfig cfg = new RcConfig(
			RcPartition.WATERSHED,
			cellSize, cellHeight,
			agentMaxSlope, agentHeight, agentRadius, agentMaxClimb,
			8, 20,
			12.0f, 1.3f,
			6,
			6.0f, 1.0f,
			true, true, true,
			new RcAreaModification(1),
			true
		);
		RcVec3f bmin = geom.GetMeshBoundsMin();
		RcVec3f bmax = geom.GetMeshBoundsMax();
		bmin.Y -= 10f;
		bmax.Y += 50f;
		var bcfg = new RcBuilderConfig(cfg, bmin, bmax);
		var builder = new RcBuilder();
		var result = builder.Build(geom, bcfg, true);
		if (result.Mesh != null)
		{
			var pars = new DtNavMeshCreateParams();
			pars.verts = result.Mesh.verts;
			pars.vertCount = result.Mesh.nverts;
			pars.polys = result.Mesh.polys;
			pars.polyCount = result.Mesh.npolys;
			pars.nvp = result.Mesh.nvp;
			pars.bmin = result.Mesh.bmin;
			pars.bmax = result.Mesh.bmax;
			pars.cs = result.Mesh.cs;
			pars.ch = result.Mesh.ch;
			pars.buildBvTree = true;
			pars.walkableHeight = agentHeight;
			pars.walkableRadius = agentRadius;
			pars.walkableClimb = agentMaxClimb;
			pars.polyAreas = new int[result.Mesh.npolys];
			pars.polyFlags = new int[result.Mesh.npolys];
			for (int i = 0; i < result.Mesh.npolys; i++)
			{
				pars.polyAreas[i] = result.Mesh.areas[i];
				
				float sumX = 0f;
				float sumZ = 0f;
				int nv = 0;
				for (int j = 0; j < result.Mesh.nvp; j++)
				{
					int vIdx = result.Mesh.polys[i * result.Mesh.nvp * 2 + j];
					if (vIdx < 0 || vIdx >= result.Mesh.nverts)
						break;
					float wx = bmin.X + result.Mesh.verts[vIdx * 3] * result.Mesh.cs;
					float wz = bmin.Z + result.Mesh.verts[vIdx * 3 + 2] * result.Mesh.cs;
					sumX += wx;
					sumZ += wz;
					nv++;
				}
				float avgX = nv > 0 ? sumX / nv : 0f;
				float avgZ = nv > 0 ? sumZ / nv : 0f;

				int xGrid = Mathf.Clamp((int)Math.Round(avgX / spacing + (width - 1) / 2.0f), 0, width - 1);
				int zGrid = Mathf.Clamp((int)Math.Round(avgZ / spacing + (depth - 1) / 2.0f), 0, depth - 1);

				int pathFlags = (PathingCodes != null) ? PathingCodes[xGrid, zGrid] : (PATHING_GROUND | PATHING_FLYING);
				if ((pathFlags & PATHING_UNPATHABLE) != 0)
				{
					pars.polyFlags[i] = 0;
				}
				else
				{
					pars.polyFlags[i] = pathFlags;
				}
			}
			if (result.MeshDetail != null)
			{
				pars.detailMeshes = result.MeshDetail.meshes;
				pars.detailVerts = result.MeshDetail.verts;
				pars.detailVertsCount = result.MeshDetail.nverts;
				pars.detailTris = result.MeshDetail.tris;
				pars.detailTriCount = result.MeshDetail.ntris;
			}
			var navMeshData = DtNavMeshBuilder.CreateNavMeshData(pars);
			if (navMeshData != null)
			{
				NavMesh = new DtNavMesh();
				NavMesh.Init(navMeshData, pars.nvp, 0);
				NavMeshQuery = new DtNavMeshQuery(NavMesh);
			}
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
