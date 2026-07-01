using DotRecast.Detour;

namespace Realm.Ecs.Components.Terrain
{
	/// <summary>
	/// Represents the simulation state and configuration of the map terrain, heightfield, pathing flags, and navigation mesh.
	/// </summary>
	internal struct TerrainState
	{
		public int Width;
		public int Depth;
		public float Spacing;
		public float CellSize;
		public float WaterHeight;
		public bool WaterEnabled;
		public float[,] Heights;
		public int[,] PathingCodes;
		public DtNavMesh NavMesh;
		public DtNavMeshQuery NavMeshQuery;

		public TerrainState(
			int width,
			int depth,
			float spacing,
			float cellSize,
			float waterHeight,
			bool waterEnabled,
			float[,] heights,
			int[,] pathingCodes,
			DtNavMesh navMesh,
			DtNavMeshQuery navMeshQuery)
		{
			Width = width;
			Depth = depth;
			Spacing = spacing;
			CellSize = cellSize;
			WaterHeight = waterHeight;
			WaterEnabled = waterEnabled;
			Heights = heights;
			PathingCodes = pathingCodes;
			NavMesh = navMesh;
			NavMeshQuery = navMeshQuery;
		}
	}
}
