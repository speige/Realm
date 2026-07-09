using System;

public class TerrainModifyAction : IEditorAction
{
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
	}

	public void Undo()
	{
		if (GameHost.Instance?.GroundTerrain == null) return;
		if (_beforeHeights != null && GameHost.Instance.GroundTerrain.Heights != null)
		{
			Array.Copy(_beforeHeights, GameHost.Instance.GroundTerrain.Heights, _beforeHeights.Length);
		}
		if (_beforeSplatMap != null && GameHost.Instance.GroundTerrain.SplatMap != null)
		{
			Array.Copy(_beforeSplatMap, GameHost.Instance.GroundTerrain.SplatMap, _beforeSplatMap.Length);
		}
		if (_beforePathing != null && GameHost.Instance.GroundTerrain.PathingCodes != null)
		{
			Array.Copy(_beforePathing, GameHost.Instance.GroundTerrain.PathingCodes, _beforePathing.Length);
		}
		GameHost.Instance.GroundTerrain.UpdateMeshAndPhysics();
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
			Array.Copy(_afterHeights, GameHost.Instance.GroundTerrain.Heights, _afterHeights.Length);
		}
		if (_afterSplatMap != null && GameHost.Instance.GroundTerrain.SplatMap != null)
		{
			Array.Copy(_afterSplatMap, GameHost.Instance.GroundTerrain.SplatMap, _afterSplatMap.Length);
		}
		if (_afterPathing != null && GameHost.Instance.GroundTerrain.PathingCodes != null)
		{
			Array.Copy(_afterPathing, GameHost.Instance.GroundTerrain.PathingCodes, _afterPathing.Length);
		}
		GameHost.Instance.GroundTerrain.UpdateMeshAndPhysics();
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