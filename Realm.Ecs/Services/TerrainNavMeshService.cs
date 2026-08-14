using Arch.Core;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Recast;
using DotRecast.Recast.Geom;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Terrain;
using System;
using System.Collections.Generic;

namespace Realm.Ecs.Services;

internal class TerrainNavMeshService
{
	// Recast/Detour bake tuning. Smaller cell sizes improve path fidelity but increase bake cost.
	private const float NavMeshCellHeight = 0.1f;
	private const float AgentRadius = 0.1f;
	private const float AgentHeight = 2.5f;
	private const float AgentMaxClimb = 0.9f;
	private const float AgentMaxSlope = 55.0f;

	private readonly WorldAccessor _ecsWorldAccessor;

	public TerrainNavMeshService(WorldAccessor ecsWorldAccessor)
	{
		_ecsWorldAccessor = ecsWorldAccessor;
	}

	public void BakeNavMesh(ref TerrainState state)
	{
		int width = state.Width;
		int depth = state.Depth;
		float quadSize = state.QuadSize;

		var obstacles = new List<(System.Numerics.Vector3 Pos, float Radius)>();
		var obstacleQuery = QueryCache.AllPositionAndCollisionRadiusQuery;
		_ecsWorldAccessor.Current.Query(in obstacleQuery, (Entity ent, ref Position pos, ref CollisionRadius colRad) =>
		{
			float scale = _ecsWorldAccessor.Current.Has<CollisionScale>(ent) ? _ecsWorldAccessor.Current.Get<CollisionScale>(ent).Value : 1.0f;
			float radius = colRad.Value * scale;
			obstacles.Add((pos.Value, radius));
		});

		var cellsState = state.Cells;
		float halfW = width / 2.0f * quadSize;
		float halfD = depth / 2.0f * quadSize;

		var bakeHeights = new float[width + 1, depth + 1];
		for (int z = 0; z <= depth; z++)
		{
			for (int x = 0; x <= width; x++)
			{
				bakeHeights[x, z] = GetVertexHeight(in state, x, z);
			}
		}

		foreach (var obs in obstacles)
		{
			int nearestX = Math.Clamp((int)Math.Round((obs.Pos.X + halfW) / quadSize), 0, width);
			int nearestZ = Math.Clamp((int)Math.Round((obs.Pos.Z + halfD) / quadSize), 0, depth);
			bakeHeights[nearestX, nearestZ] = 20.0f;

			float radius = obs.Radius;
			float effectiveRadius = Math.Max(quadSize * 0.5f, radius - quadSize * 0.51f);
			int minX = Math.Clamp((int)Math.Floor((obs.Pos.X - radius + halfW) / quadSize), 0, width);
			int maxX = Math.Clamp((int)Math.Ceiling((obs.Pos.X + radius + halfW) / quadSize), 0, width);
			int minZ = Math.Clamp((int)Math.Floor((obs.Pos.Z - radius + halfD) / quadSize), 0, depth);
			int maxZ = Math.Clamp((int)Math.Ceiling((obs.Pos.Z + radius + halfD) / quadSize), 0, depth);

			for (int z = minZ; z <= maxZ; z++)
			{
				for (int x = minX; x <= maxX; x++)
				{
					float lx = (x - width / 2.0f) * quadSize;
					float lz = (z - depth / 2.0f) * quadSize;
					float dx = lx - obs.Pos.X;
					float dz = lz - obs.Pos.Z;
					if (dx * dx + dz * dz <= effectiveRadius * effectiveRadius)
					{
						bakeHeights[x, z] = 20.0f;
					}
				}
			}
		}

		var meshVerts = new List<float>();
		for (int z = 0; z <= depth; z++)
		{
			for (int x = 0; x <= width; x++)
			{
				float lx = (x - width / 2.0f) * quadSize;
				float lz = (z - depth / 2.0f) * quadSize;
				meshVerts.Add(lx);
				meshVerts.Add(bakeHeights[x, z]);
				meshVerts.Add(lz);
			}
		}

		var indices = new List<int>();
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				int v00 = z * (width + 1) + x;
				int v10 = z * (width + 1) + (x + 1);
				int v01 = (z + 1) * (width + 1) + x;
				int v11 = (z + 1) * (width + 1) + (x + 1);
				indices.Add(v00);
				indices.Add(v01);
				indices.Add(v10);
				indices.Add(v10);
				indices.Add(v01);
				indices.Add(v11);
			}
		}

		var geom = new SimpleInputGeomProvider(meshVerts, indices);
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				var pathingCode = state.PathingCodes != null ? (TerrainPathingFlags)state.PathingCodes[x, z] : TerrainPathingFlags.Ground | TerrainPathingFlags.Buildable;
				bool isWalkable = pathingCode != TerrainPathingFlags.None;
				if (!isWalkable)
				{
					float lx = (x + 0.5f - width / 2.0f) * quadSize;
					float lz = (z + 0.5f - depth / 2.0f) * quadSize;
					float h = GetVertexHeight(in state, x, z);
					float halfS = quadSize * 0.45f;
					var vol = new RcConvexVolume
					{
						verts = new float[]
						{
							lx - halfS, h, lz - halfS,
							lx + halfS, h, lz - halfS,
							lx + halfS, h, lz + halfS,
							lx - halfS, h, lz + halfS
						},
						hmin = h - 5.0f,
						hmax = h + 5.0f,
						areaMod = new RcAreaModification(0)
					};
					geom.AddConvexVolume(vol);
				}
			}
		}


		RcConfig cfg = new RcConfig(
			RcPartition.WATERSHED,
			state.CellSize > 0.0001f ? state.CellSize : TerrainState.DefaultCellSize, NavMeshCellHeight,
			AgentMaxSlope, AgentHeight, AgentRadius, AgentMaxClimb,
			8, 20,
			3.0f, 1.3f,
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
		if (result.Mesh == null || result.Mesh.npolys == 0)
		{
			Console.Error.WriteLine($"[BakeNavMesh] BUILDER FAILED: no polys generated. width={width} depth={depth} quadSize={quadSize}");
		}
		if (result.Mesh != null && result.Mesh.npolys > 0)
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
			pars.walkableHeight = AgentHeight;
			pars.walkableRadius = AgentRadius;
			pars.walkableClimb = AgentMaxClimb;
			pars.polyAreas = new int[result.Mesh.npolys];
			pars.polyFlags = new int[result.Mesh.npolys];

			Span<System.Numerics.Vector2> polyVerts = stackalloc System.Numerics.Vector2[12];
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
					if (nv < polyVerts.Length)
					{
						polyVerts[nv++] = new System.Numerics.Vector2(wx, wz);
					}
				}
				float avgX = nv > 0 ? sumX / nv : 0f;
				float avgZ = nv > 0 ? sumZ / nv : 0f;

				int xGrid = Math.Clamp((int)Math.Floor(avgX / quadSize + width / 2.0f), 0, width - 1);
				int zGrid = Math.Clamp((int)Math.Floor(avgZ / quadSize + depth / 2.0f), 0, depth - 1);
				var pathFlags = state.PathingCodes != null ? (TerrainPathingFlags)state.PathingCodes[xGrid, zGrid] : TerrainPathingFlags.Ground | TerrainPathingFlags.Buildable;
				pars.polyFlags[i] = (int)pathFlags;
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
				state.NavMesh = new DtNavMesh();
				state.NavMesh.Init(navMeshData, pars.nvp, 0);
				state.NavMeshQuery = new DtNavMeshQuery(state.NavMesh);
			}
		}
	}

	private float GetVertexHeight(in TerrainState state, int x, int z)
	{
		var cells = state.Cells;
		if (cells == null) return 0.0f;
		int w = state.Width;
		int d = state.Depth;
		if (w <= 0 || d <= 0) return 0.0f;
		int cellX = Math.Clamp(x, 0, w - 1);
		int cellZ = Math.Clamp(z, 0, d - 1);
		if (x < w && z < d) return cells[cellX, cellZ].Y_NW;
		if (x == w && z < d) return cells[w - 1, cellZ].Y_NE;
		if (x < w && z == d) return cells[cellX, d - 1].Y_SW;
		return cells[w - 1, d - 1].Y_SE;
	}

	public void GetHeightAndNormal(in TerrainState state, float worldX, float worldZ, out float height, out System.Numerics.Vector3 normal)
	{
		float halfW = state.Width / 2.0f * state.QuadSize;
		float halfD = state.Depth / 2.0f * state.QuadSize;
		float gridX = (worldX + halfW) / state.QuadSize;
		float gridZ = (worldZ + halfD) / state.QuadSize;
		int x0 = (int)Math.Floor(gridX);
		int z0 = (int)Math.Floor(gridZ);
		x0 = Math.Max(0, Math.Min(state.Width - 1, x0));
		z0 = Math.Max(0, Math.Min(state.Depth - 1, z0));
		float tx = gridX - x0;
		float tz = gridZ - z0;
		float h00 = GetVertexHeight(in state, x0, z0);
		float h10 = GetVertexHeight(in state, Math.Min(state.Width, x0 + 1), z0);
		float h01 = GetVertexHeight(in state, x0, Math.Min(state.Depth, z0 + 1));
		float h11 = GetVertexHeight(in state, Math.Min(state.Width, x0 + 1), Math.Min(state.Depth, z0 + 1));
		height = (1 - tx) * (1 - tz) * h00 + tx * (1 - tz) * h10 + (1 - tx) * tz * h01 + tx * tz * h11;
		System.Numerics.Vector3 n00 = GetVertexNormal(in state, x0, z0);
		System.Numerics.Vector3 n10 = GetVertexNormal(in state, Math.Min(state.Width, x0 + 1), z0);
		System.Numerics.Vector3 n01 = GetVertexNormal(in state, x0, Math.Min(state.Depth, z0 + 1));
		System.Numerics.Vector3 n11 = GetVertexNormal(in state, Math.Min(state.Width, x0 + 1), Math.Min(state.Depth, z0 + 1));
		normal = System.Numerics.Vector3.Normalize((1 - tx) * (1 - tz) * n00 + tx * (1 - tz) * n10 + (1 - tx) * tz * n01 + tx * tz * n11);
	}

	private System.Numerics.Vector3 GetVertexNormal(in TerrainState state, int x, int z)
	{
		var cells = state.Cells;
		if (cells == null) return System.Numerics.Vector3.UnitY;
		float h = GetVertexHeight(in state, x, z);
		float cliffThreshold = 0.95f * Math.Max(0.1f, state.QuadSize);

		float deltaRight = x < state.Width ? GetVertexHeight(in state, x + 1, z) - h : 0.0f;
		float deltaLeft = x > 0 ? h - GetVertexHeight(in state, x - 1, z) : 0.0f;
		bool rightIsCliff = x < state.Width && Math.Abs(deltaRight) >= cliffThreshold;
		bool leftIsCliff = x > 0 && Math.Abs(deltaLeft) >= cliffThreshold;

		float dx;
		if (rightIsCliff && leftIsCliff)
		{
			dx = 0.0f;
		}
		else if (rightIsCliff)
		{
			dx = (x > 0 && !leftIsCliff) ? deltaLeft : 0.0f;
		}
		else if (leftIsCliff)
		{
			dx = (x < state.Width && !rightIsCliff) ? deltaRight : 0.0f;
		}
		else
		{
			if (x > 0 && x < state.Width)
			{
				dx = (GetVertexHeight(in state, x + 1, z) - GetVertexHeight(in state, x - 1, z)) * 0.5f;
			}
			else if (x < state.Width)
			{
				dx = deltaRight;
			}
			else if (x > 0)
			{
				dx = deltaLeft;
			}
			else
			{
				dx = 0.0f;
			}
		}

		float deltaUp = z < state.Depth ? GetVertexHeight(in state, x, z + 1) - h : 0.0f;
		float deltaDown = z > 0 ? h - GetVertexHeight(in state, x, z - 1) : 0.0f;
		bool upIsCliff = z < state.Depth && Math.Abs(deltaUp) >= cliffThreshold;
		bool downIsCliff = z > 0 && Math.Abs(deltaDown) >= cliffThreshold;

		float dz;
		if (upIsCliff && downIsCliff)
		{
			dz = 0.0f;
		}
		else if (upIsCliff)
		{
			dz = (z > 0 && !downIsCliff) ? deltaDown : 0.0f;
		}
		else if (downIsCliff)
		{
			dz = (z < state.Depth && !upIsCliff) ? deltaUp : 0.0f;
		}
		else
		{
			if (z > 0 && z < state.Depth)
			{
				dz = (GetVertexHeight(in state, x, z + 1) - GetVertexHeight(in state, x, z - 1)) * 0.5f;
			}
			else if (z < state.Depth)
			{
				dz = deltaUp;
			}
			else if (z > 0)
			{
				dz = deltaDown;
			}
			else
			{
				dz = 0.0f;
			}
		}

		if (Math.Abs(dx) < 0.001f && Math.Abs(dz) < 0.001f)
		{
			return System.Numerics.Vector3.UnitY;
		}

		System.Numerics.Vector3 tangentX = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(state.QuadSize, dx, 0.0f));
		System.Numerics.Vector3 tangentZ = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(0.0f, dz, state.QuadSize));
		return System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Cross(tangentZ, tangentX));
	}
}
