using System;
using System.Collections.Generic;
using Godot;

public class MapEditorHUDViewModel
{
	public enum EditorModule
	{
		Terrain,
		TextureDeco,
		Pathing,
		Objects,
		Coordinates,
		Clipboard
	}

	public bool LeftPanelExpanded { get; set; } = false;
	public bool RightPanelExpanded { get; set; } = true;
	public EditorModule ActiveModule { get; set; } = EditorModule.Terrain;

	public float BrushSize { get; set; } = 5f;
	public float BrushStrength { get; set; } = 20f;
	public float BlockStep { get; set; } = 0.5f;
	public float WaterHeight { get; set; } = 1.0f;

	public float PlacementRotate { get; set; } = 0f;
	public float PlacementScale { get; set; } = 1.0f;
	public bool SpawnAsEnemy { get; set; } = false;
	public bool RandomRotation { get; set; } = false;
	public bool RandomScale { get; set; } = false;
	public bool ClumpMode { get; set; } = false;
	public float ClumpDensity { get; set; } = 5f;
	public float ClumpScaleVar { get; set; } = 0.2f;

	public string CurrentCategory { get; set; } = "Characters";
	public List<string> CategoryFiles { get; } = new();
	public int SelectedCategoryItemIndex { get; set; } = -1;

	public bool SnapToGrid { get; set; } = false;
	public bool GridOverlayVisible { get; set; } = false;
	public bool CameraBoundsOverlayVisible { get; set; } = false;
	public bool WaterEnabled { get; set; } = false;
	public string SkyboxSelected { get; set; } = "";
	public bool PathingOverlayVisible { get; set; } = false;
	public bool BrushShapeSquare { get; set; } = false;

	public string StatusText { get; set; } = "";
	public string FeedbackText { get; set; } = "";

	public bool HasInspectorSelection { get; set; } = false;
	public string InspectorTitle { get; set; } = "No Selection";
	public string InspectorPos { get; set; } = "Position: (0, 0)";

	public bool ShallowWater { get; set; } = false;
	public bool DeepWater { get; set; } = false;
	public bool Flying { get; set; } = false;
	public bool Ground { get; set; } = true;
	public bool Buildable { get; set; } = false;
	public bool Unpathable { get; set; } = false;
	public int PathingModeIndex { get; set; } = 0;

	public void UpdateFromHost()
	{
		if (GameHost.Instance != null)
		{
			BrushSize = GameHost.Instance.EditorBrushRadius;
			BrushStrength = GameHost.Instance.EditorBrushStrength;
			BlockStep = GameHost.Instance.EditorBlockLevelHeight;
			WaterHeight = GameHost.Instance.GroundTerrain != null ? GameHost.Instance.GroundTerrain.WaterHeight : -2.0f;

			PlacementRotate = GameHost.Instance.EditorPlacementRotation;
			PlacementScale = GameHost.Instance.EditorPlacementScale;
			SpawnAsEnemy = GameHost.Instance.PlaceUnitIsEnemy;
			RandomRotation = GameHost.Instance.EditorRandomRotation;
			RandomScale = GameHost.Instance.EditorRandomScale;
			ClumpMode = GameHost.Instance.EditorClumpMode;
			ClumpDensity = GameHost.Instance.EditorClumpDensity;
			ClumpScaleVar = GameHost.Instance.EditorClumpScaleVar;

			SnapToGrid = GameHost.Instance.EditorSnapToGrid;
			GridOverlayVisible = GameHost.Instance.EditorGridVisible;
			CameraBoundsOverlayVisible = GameHost.Instance.EditorCameraBoundsVisible;
			WaterEnabled = GameHost.Instance.GroundTerrain != null && GameHost.Instance.GroundTerrain.WaterHeight > -100f;
			PathingOverlayVisible = GameHost.Instance.PathingOverlayVisible;
			BrushShapeSquare = GameHost.Instance.EditorBrushIsSquare;
		}
	}
}
