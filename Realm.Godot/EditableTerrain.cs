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

	public float[,] Heights { get; private set; }
	public Color[,] Colors { get; private set; }

	private MeshInstance3D _meshInstance;
	private CollisionShape3D _collisionShape;
	private ArrayMesh _arrayMesh;
	private StandardMaterial3D _material;

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

		// Initialize heights to 0, colors to green (default grass texture color)
		for (int z = 0; z < Depth; z++)
		{
			for (int x = 0; x < Width; x++)
			{
				Heights[x, z] = 0.0f;
				Colors[x, z] = new Color(0.2f, 0.6f, 0.2f); // Default Grass Green
			}
		}

		_meshInstance = new MeshInstance3D();
		_meshInstance.Name = "TerrainMesh";
		AddChild(_meshInstance);

		_collisionShape = new CollisionShape3D();
		_collisionShape.Name = "TerrainCollision";
		AddChild(_collisionShape);

		_arrayMesh = new ArrayMesh();
		
		_material = new StandardMaterial3D();
		_material.VertexColorUseAsAlbedo = true;
		_material.Roughness = 0.9f;
		
		// Attempt to load the grass texture as details for shading if available
		var texture = GD.Load<Texture2D>("res://Assets/terrain_grass.jpg");
		if (texture != null)
		{
			_material.AlbedoTexture = texture;
			_material.Uv1Scale = new Vector3(25, 25, 1);
		}
		
		_meshInstance.MaterialOverride = _material;

		CreateWater();
		UpdateMeshAndPhysics();
	}

	public void UpdateMeshAndPhysics()
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
		BakeNavMesh();
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
				pars.polyFlags[i] = 1;
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
