using DotRecast.Detour;

namespace Realm.Ecs.Components.Terrain
{
	/// <summary>
	/// Represents the simulation state and configuration of the map terrain, heightfield, pathing flags, and navigation mesh.
	/// </summary>
	internal struct TerrainState
	{
		public const float DefaultQuadSize = 2.0f;
		public const float DefaultCellSize = 0.5f;

		public int Width;
		public int Depth;
		public float QuadSize;
		public float CellSize;
		public TerrainCell[,]? Cells;
		public int[,]? PathingCodes;
		public DtNavMesh NavMesh;
		public DtNavMeshQuery NavMeshQuery;

		public float[,]? Heights => CalculateHeights(Width, Depth, Cells);

		public static float[,]? CalculateHeights(int width, int depth, TerrainCell[,] cells)
		{
			if (cells == null) return null;
			int w = System.Math.Max(1, width);
			int d = System.Math.Max(1, depth);
			float[,] result = new float[w + 1, d + 1];
			for (int z = 0; z <= d; z++)
			{
				for (int x = 0; x <= w; x++)
				{
					int cellX = System.Math.Clamp(x, 0, w - 1);
					int cellZ = System.Math.Clamp(z, 0, d - 1);
					if (x < w && z < d)
						result[x, z] = cells[cellX, cellZ].Y_NW;
					else if (x == w && z < d)
						result[x, z] = cells[w - 1, cellZ].Y_NE;
					else if (x < w && z == d)
						result[x, z] = cells[cellX, d - 1].Y_SW;
					else
						result[x, z] = cells[w - 1, d - 1].Y_SE;
				}
			}
			return result;
		}

		public static TerrainCell[,]? CalculateCells(int width, int depth, float[,] heights, TerrainCell[,] existingCells = null)
		{
			if (heights == null)
			{
				return null;
			}
			var cells = new TerrainCell[width, depth];
			int sW = heights.GetLength(0);
			int sD = heights.GetLength(1);
			for (int z = 0; z < depth; z++)
			{
				for (int x = 0; x < width; x++)
				{
					int x0 = System.Math.Clamp(x, 0, sW - 1);
					int x1 = System.Math.Clamp(x + 1, 0, sW - 1);
					int z0 = System.Math.Clamp(z, 0, sD - 1);
					int z1 = System.Math.Clamp(z + 1, 0, sD - 1);

					float nw = heights[x0, z0];
					float ne = heights[x1, z0];
					float sw = heights[x0, z1];
					float se = heights[x1, z1];

					WaterType wMode = WaterType.None;
					if (existingCells != null && x < existingCells.GetLength(0) && z < existingCells.GetLength(1))
					{
						wMode = existingCells[x, z].WaterMode;
					}

					cells[x, z] = new TerrainCell(nw, ne, se, sw, wMode);
				}
			}
			return cells;
		}

		public void SetHeights(float[,] heights)
		{
			if (heights == null) return;
			Cells = CalculateCells(Width, Depth, heights, Cells);
		}

		public TerrainState(
			int width,
			int depth,
			float quadSize,
			float cellSize,
			TerrainCell[,] cells,
			int[,] pathingCodes,
			DtNavMesh navMesh,
			DtNavMeshQuery navMeshQuery)
		{
			Width = width;
			Depth = depth;
			QuadSize = quadSize;
			CellSize = cellSize;
			Cells = cells;
			PathingCodes = pathingCodes;
			NavMesh = navMesh;
			NavMeshQuery = navMeshQuery;
		}

		public TerrainState(
			int width,
			int depth,
			float quadSize,
			float cellSize,
			float[,] heights,
			int[,] pathingCodes,
			DtNavMesh navMesh,
			DtNavMeshQuery navMeshQuery)
		{
			Width = width;
			Depth = depth;
			QuadSize = quadSize;
			CellSize = cellSize;
			PathingCodes = pathingCodes;
			NavMesh = navMesh;
			NavMeshQuery = navMeshQuery;
			Cells = (heights != null) ? CalculateCells(width, depth, heights) : null;
		}
	}
}
