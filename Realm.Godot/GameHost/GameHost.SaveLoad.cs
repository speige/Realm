using Godot;
using Realm.MapAPI;
using System;
using System.Collections.Generic;
using System.Text.Json;

public partial class GameHost
{


	public void SaveMapToFile(string customPath = "")
	{
		if (GroundTerrain == null) return;

		var saveData = new MapSaveData();
		saveData.WaterEnabled = GroundTerrain.WaterEnabled;
		saveData.WaterHeight = GroundTerrain.WaterHeight;
		saveData.BlockMode = EditorBlockMode;
		saveData.BlockLevelHeight = EditorBlockLevelHeight;
		saveData.WC3BlockMode = EditorBlockMode;
		saveData.WC3LevelHeight = EditorBlockLevelHeight;
		saveData.CameraBoundsLeft = EditorCameraBoundsLeft;
		saveData.CameraBoundsRight = EditorCameraBoundsRight;
		saveData.CameraBoundsTop = EditorCameraBoundsTop;
		saveData.CameraBoundsBottom = EditorCameraBoundsBottom;
		saveData.SkyboxPath = EditorSkyboxPath;
		
		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;
		saveData.Heights = new float[width * depth];
		saveData.Colors = new string[width * depth];
		saveData.Pathing = new int[width * depth];

		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				int idx = z * width + x;
				saveData.Heights[idx] = GroundTerrain.Heights[x, z];
				saveData.Colors[idx] = GroundTerrain.Colors[x, z].ToHtml(true);
				saveData.Pathing[idx] = GroundTerrain.PathingCodes != null ? GroundTerrain.PathingCodes[x, z] : (EditableTerrain.PATHING_GROUND | EditableTerrain.PATHING_FLYING);
			}
		}

		saveData.Units = new List<UnitSaveData>();
		foreach (var unit in AllUnits)
		{
			if (GodotObject.IsInstanceValid(unit))
			{
				saveData.Units.Add(new UnitSaveData
				{
					UnitId = unit.UnitId,
					PosX = unit.Position.X,
					PosY = unit.Position.Y,
					PosZ = unit.Position.Z,
					RotationY = unit.RotationDegrees.Y,
					Scale = unit.Scale.X,
					IsEnemy = unit.IsEnemy
				});
			}
		}

		saveData.Props = new List<PropSaveData>();
		saveData.Decals = new List<DecalSaveData>();
		foreach (var child in GetChildren())
		{
			if (child is Prop3D prop && GodotObject.IsInstanceValid(prop))
			{
				saveData.Props.Add(new PropSaveData
				{
					PropId = prop.PropId,
					PosX = prop.Position.X,
					PosY = prop.Position.Y,
					PosZ = prop.Position.Z,
					RotationY = prop.RotationDegrees.Y,
					Scale = prop.Scale.X
				});
			}
			else if (child is Decal decal && GodotObject.IsInstanceValid(decal))
			{
				saveData.Decals.Add(new DecalSaveData
				{
					DecalId = decal.HasMeta("DecalId") ? decal.GetMeta("DecalId").AsString() : "logo",
					PosX = decal.Position.X,
					PosY = decal.Position.Y,
					PosZ = decal.Position.Z,
					RotationY = decal.RotationDegrees.Y,
					Scale = decal.Scale.X
				});
			}
		}

		try
		{
			string json = System.Text.Json.JsonSerializer.Serialize(saveData);
			string path = string.IsNullOrEmpty(customPath) ? "user://terrain.json" : customPath;
			using var file = FileAccess.Open(path, FileAccess.ModeFlags.Write);
			if (file != null)
			{
				file.StoreString(json);
				EditorHasUnsavedChanges = false;
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr(ex.Message);
		}
	}

	public bool LoadMapFromFile(string customPath = "", bool terrainOnly = false)
	{
		string path = string.IsNullOrEmpty(customPath) ? "user://terrain.json" : customPath;
		if (!FileAccess.FileExists(path)) return false;

		try
		{
			using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
			if (file == null) return false;

			string json = file.GetAsText();
			var saveData = System.Text.Json.JsonSerializer.Deserialize<MapSaveData>(json);
			if (saveData == null) return false;

			ClearAllUnits();

			if (GroundTerrain == null)
			{
				var terrainNode = new EditableTerrain();
				terrainNode.Name = "Ground";
				AddChild(terrainNode);
				GroundTerrain = terrainNode;
			}

			bool waterEnabled = saveData.WaterEnabled ?? true;
			GroundTerrain.WaterEnabled = waterEnabled;
			MapEditorHUD.Instance?.UpdateWaterEnabledExternal(waterEnabled);
			if (saveData.WaterHeight.HasValue)
			{
				GroundTerrain.WaterHeight = saveData.WaterHeight.Value;
				MapEditorHUD.Instance?.UpdateWaterHeightExternal(saveData.WaterHeight.Value);
			}
			bool isBlock = saveData.BlockMode ?? saveData.WC3BlockMode ?? false;
			EditorBlockMode = isBlock;
			MapEditorHUD.Instance?.UpdateBlockModeExternal(isBlock);

			float step = saveData.BlockLevelHeight ?? saveData.WC3LevelHeight ?? 4.0f;
			EditorBlockLevelHeight = step;
			MapEditorHUD.Instance?.UpdateBlockLevelHeightExternal(step);

			if (saveData.CameraBoundsLeft.HasValue) EditorCameraBoundsLeft = saveData.CameraBoundsLeft.Value;
			if (saveData.CameraBoundsRight.HasValue) EditorCameraBoundsRight = saveData.CameraBoundsRight.Value;
			if (saveData.CameraBoundsTop.HasValue) EditorCameraBoundsTop = saveData.CameraBoundsTop.Value;
			if (saveData.CameraBoundsBottom.HasValue) EditorCameraBoundsBottom = saveData.CameraBoundsBottom.Value;
			MapEditorHUD.Instance?.UpdateCameraBoundsUI();
			RebuildCameraBoundsOverlay();

			if (!string.IsNullOrEmpty(saveData.SkyboxPath))
			{
				SetSkyboxTexture(saveData.SkyboxPath);
				MapEditorHUD.Instance?.UpdateSelectedSkyboxExternal(saveData.SkyboxPath);
			}

			int width = GroundTerrain.Width;
			int depth = GroundTerrain.Depth;

			if (saveData.Heights != null && saveData.Heights.Length == width * depth)
			{
				for (int z = 0; z < depth; z++)
				{
					for (int x = 0; x < width; x++)
					{
						int idx = z * width + x;
						GroundTerrain.Heights[x, z] = saveData.Heights[idx];
					}
				}
			}

			if (saveData.Colors != null && saveData.Colors.Length == width * depth)
			{
				for (int z = 0; z < depth; z++)
				{
					for (int x = 0; x < width; x++)
					{
						int idx = z * width + x;
						GroundTerrain.Colors[x, z] = Color.FromHtml(saveData.Colors[idx]);
					}
				}
			}

			if (saveData.Pathing != null && saveData.Pathing.Length == width * depth)
			{
				for (int z = 0; z < depth; z++)
				{
					for (int x = 0; x < width; x++)
					{
						int idx = z * width + x;
						GroundTerrain.PathingCodes[x, z] = saveData.Pathing[idx];
					}
				}
			}
			else
			{
				for (int z = 0; z < depth; z++)
				{
					for (int x = 0; x < width; x++)
					{
						GroundTerrain.PathingCodes[x, z] = EditableTerrain.PATHING_GROUND | EditableTerrain.PATHING_FLYING;
					}
				}
			}

			GroundTerrain.UpdateMeshAndPhysics();

			if (!terrainOnly)
			{
				if (saveData.Units != null)
				{
					foreach (var u in saveData.Units)
					{
						SpawnUnitExternal(u.UnitId, new Vector3(u.PosX, u.PosY, u.PosZ), u.IsEnemy, u.RotationY, u.Scale);
					}
				}

				if (saveData.Props != null)
				{
					foreach (var p in saveData.Props)
					{
						SpawnPropExternalWithParams(p.PropId, new Vector3(p.PosX, p.PosY, p.PosZ), p.RotationY, p.Scale);
					}
				}

				if (saveData.Decals != null)
				{
					foreach (var d in saveData.Decals)
					{
						SpawnDecalExternalWithParams(d.DecalId, new Vector3(d.PosX, d.PosY, d.PosZ), d.RotationY, d.Scale);
					}
				}
			}

			EditorHasUnsavedChanges = false;
			MapEditorHUD.Instance?.RegenerateMinimap();
			return true;
		}
		catch (Exception ex)
		{
			GD.PrintErr(ex.Message);
			return false;
		}
	}
}
