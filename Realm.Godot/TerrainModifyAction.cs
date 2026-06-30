using Godot;
using System;

public class TerrainModifyAction : IEditorAction
{
	private readonly float[,] _beforeHeights;
	private readonly float[,] _afterHeights;
	private readonly Color[,] _beforeColors;
	private readonly Color[,] _afterColors;
	private readonly int[,] _beforePathing;
	private readonly int[,] _afterPathing;

	public TerrainModifyAction(float[,] beforeHeights, float[,] afterHeights, Color[,] beforeColors, Color[,] afterColors, int[,] beforePathing = null, int[,] afterPathing = null)
	{
		_beforeHeights = beforeHeights;
		_afterHeights = afterHeights;
		_beforeColors = beforeColors;
		_afterColors = afterColors;
		_beforePathing = beforePathing;
		_afterPathing = afterPathing;
	}

	public void Undo()
	{
		if (GameHost.Instance?.GroundTerrain == null) return;
		Array.Copy(_beforeHeights, GameHost.Instance.GroundTerrain.Heights, _beforeHeights.Length);
		Array.Copy(_beforeColors, GameHost.Instance.GroundTerrain.Colors, _beforeColors.Length);
		if (_beforePathing != null && GameHost.Instance.GroundTerrain.PathingCodes != null)
		{
			Array.Copy(_beforePathing, GameHost.Instance.GroundTerrain.PathingCodes, _beforePathing.Length);
		}
		GameHost.Instance.GroundTerrain.UpdateMeshAndPhysics();
		GameHost.Instance.AlignAllEntitiesToTerrainExternal();
		GameHost.Instance.RebuildGridOverlayMeshExternal();
	}

	public void Redo()
	{
		if (GameHost.Instance?.GroundTerrain == null) return;
		Array.Copy(_afterHeights, GameHost.Instance.GroundTerrain.Heights, _afterHeights.Length);
		Array.Copy(_afterColors, GameHost.Instance.GroundTerrain.Colors, _afterColors.Length);
		if (_afterPathing != null && GameHost.Instance.GroundTerrain.PathingCodes != null)
		{
			Array.Copy(_afterPathing, GameHost.Instance.GroundTerrain.PathingCodes, _afterPathing.Length);
		}
		GameHost.Instance.GroundTerrain.UpdateMeshAndPhysics();
		GameHost.Instance.AlignAllEntitiesToTerrainExternal();
		GameHost.Instance.RebuildGridOverlayMeshExternal();
	}
}