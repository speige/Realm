using System;
using Godot;

public class TerrainModifyAction : IEditorAction
{
	private readonly int _minX;
	private readonly int _minZ;
	private readonly int _width;
	private readonly int _depth;

	private readonly float[,] _beforeHeights;
	private readonly float[,] _afterHeights;
	private readonly TerrainSplatWeights[,] _beforeSplatMap;
	private readonly TerrainSplatWeights[,] _afterSplatMap;
	private readonly int[,] _beforePathing;
	private readonly int[,] _afterPathing;

	public TerrainModifyAction(float[,] beforeHeights, float[,] afterHeights, TerrainSplatWeights[,] beforeSplatMap, TerrainSplatWeights[,] afterSplatMap, int[,] beforePathing = null, int[,] afterPathing = null)
	{
		_beforeHeights = beforeHeights;
		_afterHeights = afterHeights;
		_beforeSplatMap = beforeSplatMap;
		_afterSplatMap = afterSplatMap;
		_beforePathing = beforePathing;
		_afterPathing = afterPathing;
		_minX = 0;
		_minZ = 0;
		if (beforeHeights != null) {
			_width = beforeHeights.GetLength(0);
			_depth = beforeHeights.GetLength(1);
		} else if (beforeSplatMap != null) {
			_width = beforeSplatMap.GetLength(0);
			_depth = beforeSplatMap.GetLength(1);
		} else if (beforePathing != null) {
			_width = beforePathing.GetLength(0);
			_depth = beforePathing.GetLength(1);
		}
	}

	public TerrainModifyAction(int minX, int minZ, int width, int depth, float[,] beforeHeights, float[,] afterHeights, TerrainSplatWeights[,] beforeSplatMap, TerrainSplatWeights[,] afterSplatMap, int[,] beforePathing = null, int[,] afterPathing = null)
	{
		_minX = minX;
		_minZ = minZ;
		_width = width;
		_depth = depth;
		_beforeHeights = beforeHeights;
		_afterHeights = afterHeights;
		_beforeSplatMap = beforeSplatMap;
		_afterSplatMap = afterSplatMap;
		_beforePathing = beforePathing;
		_afterPathing = afterPathing;
	}

	public void Undo()
	{
		if (GameHost.Instance?.GroundTerrain == null) return;
		
		if (_beforeHeights != null && GameHost.Instance.GroundTerrain.Heights != null)
		{
			for (int z = 0; z < _depth; z++)
				for (int x = 0; x < _width; x++)
					GameHost.Instance.GroundTerrain.Heights[_minX + x, _minZ + z] = _beforeHeights[x, z];
		}
		if (_beforeSplatMap != null && GameHost.Instance.GroundTerrain.SplatMap != null)
		{
			for (int z = 0; z < _depth; z++)
				for (int x = 0; x < _width; x++)
					GameHost.Instance.GroundTerrain.SplatMap[_minX + x, _minZ + z] = _beforeSplatMap[x, z];
		}
		if (_beforePathing != null && GameHost.Instance.GroundTerrain.PathingCodes != null)
		{
			for (int z = 0; z < _depth; z++)
				for (int x = 0; x < _width; x++)
					GameHost.Instance.GroundTerrain.PathingCodes[_minX + x, _minZ + z] = _beforePathing[x, z];
		}

		Rect2I affected = new Rect2I(_minX - 2, _minZ - 2, _width + 4, _depth + 4);
		GameHost.Instance.GroundTerrain.UpdateMeshAndPhysics(true, false, affected);
		if (_beforeSplatMap != null && GameHost.Instance.GroundTerrain.SplatMap != null)
		{
			ServiceLocator.Get<EditorService>()?.AlignSplatMapSlots(_minX - 2, _minZ - 2, _minX + _width + 2, _minZ + _depth + 2);
		}
		GameHost.Instance.AlignAllEntitiesToTerrainExternal();
		GameHost.Instance.RebuildGridOverlayMeshExternal();
		if (_beforeHeights != null || _beforePathing != null)
		{
			GameHost.Instance.GroundTerrain.BakeNavMesh();
		}
		if (_beforePathing != null)
		{
			GameHost.Instance.UpdatePathingOverlay();
		}
	}

	public void Redo()
	{
		if (GameHost.Instance?.GroundTerrain == null) return;

		if (_afterHeights != null && GameHost.Instance.GroundTerrain.Heights != null)
		{
			for (int z = 0; z < _depth; z++)
				for (int x = 0; x < _width; x++)
					GameHost.Instance.GroundTerrain.Heights[_minX + x, _minZ + z] = _afterHeights[x, z];
		}
		if (_afterSplatMap != null && GameHost.Instance.GroundTerrain.SplatMap != null)
		{
			for (int z = 0; z < _depth; z++)
				for (int x = 0; x < _width; x++)
					GameHost.Instance.GroundTerrain.SplatMap[_minX + x, _minZ + z] = _afterSplatMap[x, z];
		}
		if (_afterPathing != null && GameHost.Instance.GroundTerrain.PathingCodes != null)
		{
			for (int z = 0; z < _depth; z++)
				for (int x = 0; x < _width; x++)
					GameHost.Instance.GroundTerrain.PathingCodes[_minX + x, _minZ + z] = _afterPathing[x, z];
		}

		Rect2I affected = new Rect2I(_minX - 2, _minZ - 2, _width + 4, _depth + 4);
		GameHost.Instance.GroundTerrain.UpdateMeshAndPhysics(true, false, affected);
		if (_afterSplatMap != null && GameHost.Instance.GroundTerrain.SplatMap != null)
		{
			ServiceLocator.Get<EditorService>()?.AlignSplatMapSlots(_minX - 2, _minZ - 2, _minX + _width + 2, _minZ + _depth + 2);
		}
		GameHost.Instance.AlignAllEntitiesToTerrainExternal();
		GameHost.Instance.RebuildGridOverlayMeshExternal();
		if (_afterHeights != null || _afterPathing != null)
		{
			GameHost.Instance.GroundTerrain.BakeNavMesh();
		}
		if (_afterPathing != null)
		{
			GameHost.Instance.UpdatePathingOverlay();
		}
	}
}
