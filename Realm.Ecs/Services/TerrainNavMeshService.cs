using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Recast;
using DotRecast.Recast.Geom;
using Realm.Ecs.Components.Terrain;
using System;
using System.Collections.Generic;

namespace Realm.Ecs.Services;

internal class TerrainNavMeshService
{
	public void BakeNavMesh(ref TerrainState state)
	{
		int width = state.Width;
		int depth = state.Depth;
		float spacing = state.Spacing;
		var verts = new List<float>();
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				float lx = (x - (width - 1) / 2.0f) * spacing;
				float lz = (z - (depth - 1) / 2.0f) * spacing;
				verts.Add(lx);
				verts.Add(state.Heights[x, z]);
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

				int xGrid = Math.Clamp((int)Math.Round(avgX / spacing + (width - 1) / 2.0f), 0, width - 1);
				int zGrid = Math.Clamp((int)Math.Round(avgZ / spacing + (depth - 1) / 2.0f), 0, depth - 1);

				int pathFlags = (state.PathingCodes != null) ? state.PathingCodes[xGrid, zGrid] : (8 | 4);
				if ((pathFlags & 16) != 0)
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
				state.NavMesh = new DtNavMesh();
				state.NavMesh.Init(navMeshData, pars.nvp, 0);
				state.NavMeshQuery = new DtNavMeshQuery(state.NavMesh);
			}
		}
	}

	public void GetHeightAndNormal(in TerrainState state, float worldX, float worldZ, out float height, out System.Numerics.Vector3 normal)
	{
		float halfW = (state.Width - 1) / 2.0f * state.Spacing;
		float halfD = (state.Depth - 1) / 2.0f * state.Spacing;
		float gridX = (worldX + halfW) / state.Spacing;
		float gridZ = (worldZ + halfD) / state.Spacing;
		int x0 = (int)Math.Floor(gridX);
		int z0 = (int)Math.Floor(gridZ);
		x0 = Math.Max(0, Math.Min(state.Width - 2, x0));
		z0 = Math.Max(0, Math.Min(state.Depth - 2, z0));
		float tx = gridX - x0;
		float tz = gridZ - z0;
		float h00 = state.Heights[x0, z0];
		float h10 = state.Heights[x0 + 1, z0];
		float h01 = state.Heights[x0, z0 + 1];
		float h11 = state.Heights[x0 + 1, z0 + 1];
		height = (1 - tx) * (1 - tz) * h00 + tx * (1 - tz) * h10 + (1 - tx) * tz * h01 + tx * tz * h11;
		System.Numerics.Vector3 n00 = GetVertexNormal(in state, x0, z0);
		System.Numerics.Vector3 n10 = GetVertexNormal(in state, x0 + 1, z0);
		System.Numerics.Vector3 n01 = GetVertexNormal(in state, x0, z0 + 1);
		System.Numerics.Vector3 n11 = GetVertexNormal(in state, x0 + 1, z0 + 1);
		normal = System.Numerics.Vector3.Normalize((1 - tx) * (1 - tz) * n00 + tx * (1 - tz) * n10 + (1 - tx) * tz * n01 + tx * tz * n11);
	}

	private System.Numerics.Vector3 GetVertexNormal(in TerrainState state, int x, int z)
	{
		float hl = state.Heights[Math.Max(0, x - 1), z];
		float hr = state.Heights[Math.Min(state.Width - 1, x + 1), z];
		float hd = state.Heights[x, Math.Max(0, z - 1)];
		float hu = state.Heights[x, Math.Min(state.Depth - 1, z + 1)];
		System.Numerics.Vector3 tangentX = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(2.0f * state.Spacing, hr - hl, 0.0f));
		System.Numerics.Vector3 tangentZ = System.Numerics.Vector3.Normalize(new System.Numerics.Vector3(0.0f, hu - hd, 2.0f * state.Spacing));
		return System.Numerics.Vector3.Normalize(System.Numerics.Vector3.Cross(tangentZ, tangentX));
	}
}
