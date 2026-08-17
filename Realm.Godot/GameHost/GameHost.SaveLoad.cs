using Godot;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Components.Meta;
using Arch.Core;
using System.Collections.Generic;
using System.Linq;

public partial class GameHost
{
	public string CurrentMapDirectory { get; set; } = ProjectSettings.GlobalizePath("user://temp_map_workspace");

	public void SaveMapToFile(string customPath = "")
	{
		if (GroundTerrain == null) return;

		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;
		int splatW = GroundTerrain.SplatMap.GetLength(0);
		int splatD = GroundTerrain.SplatMap.GetLength(1);
		string[] splatData = new string[splatW * splatD];
		for (int z = 0; z < splatD; z++)
		{
			for (int x = 0; x < splatW; x++)
			{
				splatData[z * splatW + x] = GroundTerrain.SplatMap[x, z].Serialize();
			}
		}

		var unitsData = new List<(Entity Entity, float RotationY, float Scale)>();
		foreach (var unit in AllUnits)
		{
			if (GodotObject.IsInstanceValid(unit) && EcsWorld.IsAlive(unit.Entity))
			{
				unitsData.Add((unit.Entity, unit.RotationDegrees.Y, unit.Scale.X));
			}
		}

		var propsData = new List<(Entity Entity, float RotationY, float Scale)>();
		foreach (var prop in AllProps)
		{
			if (GodotObject.IsInstanceValid(prop) && EcsWorld.IsAlive(prop.Entity))
			{
				propsData.Add((prop.Entity, prop.RotationDegrees.Y, prop.Scale.X));
			}
		}

		var decalsData = new List<(string DecalId, System.Numerics.Vector3 Position, float RotationY, float Scale)>();
		foreach (var child in GetChildren())
		{
			if (child is Decal decal && GodotObject.IsInstanceValid(decal))
			{
				string decalId = decal is Decal3D decal3D ? decal3D.DecalId : "logo";
				decalsData.Add((decalId, new System.Numerics.Vector3(decal.Position.X, decal.Position.Y, decal.Position.Z), decal.RotationDegrees.Y, decal.Scale.X));
			}
		}

		var coordinatesData = EditorCoordinates.Select(r => new CoordinateSaveData
		{
			Name = r.Name,
			MinX = r.MinX,
			MinZ = r.MinZ,
			MaxX = r.MaxX,
			MaxZ = r.MaxZ
		}).ToList();

		string path = string.IsNullOrEmpty(customPath) ? "user://terrain.json" : customPath;
		string absolutePath = ProjectSettings.GlobalizePath(path);
		CurrentMapDirectory = System.IO.Path.GetDirectoryName(absolutePath);

		_saveLoadService.SaveMapToFile(absolutePath, splatData, unitsData.ToArray(), propsData.ToArray(), decalsData.ToArray(), coordinatesData);
	}

	public bool LoadMapFromFile(string customPath = "", bool terrainOnly = false, bool clearUnits = true)
	{
		IsLoadingMap = true;
		try
		{
			string path = string.IsNullOrEmpty(customPath) ? "user://terrain.json" : customPath;
			string absolutePath = ProjectSettings.GlobalizePath(path);
			CurrentMapDirectory = System.IO.Path.GetDirectoryName(absolutePath);



			if (clearUnits)
			{
				ClearAllUnits();
			}

			bool success = _saveLoadService.LoadMapFromFile(absolutePath, terrainOnly);
			if (!success)
			{
				int defaultWidth = 128;
				int defaultDepth = 128;
				float[,] defaultHeights = new float[defaultWidth, defaultDepth];
				int[,] defaultPathing = new int[defaultWidth, defaultDepth];
				for (int z = 0; z < defaultDepth; z++)
				{
					for (int x = 0; x < defaultWidth; x++)
					{
						defaultHeights[x, z] = 0.0f;
						defaultPathing[x, z] = EditableTerrain.GetDefaultPathingCode(WaterType.None);
					}
				}
				if (EcsWorld.Has<TerrainState>(_worldEntity))
				{
					ref var ts = ref EcsWorld.Get<TerrainState>(_worldEntity);
					ts.SetHeights(defaultHeights);
					ts.PathingCodes = defaultPathing;
					EcsWorld.Set(_worldEntity, ts);
				}
				if (GroundTerrain != null)
				{
					for (int z = 0; z < defaultDepth; z++)
					{
						for (int x = 0; x < defaultWidth; x++)
						{
							GroundTerrain.SplatMap[x, z] = TerrainSplatWeights.CreateSolid(0);
						}
					}
					GroundTerrain.UpdateMeshAndPhysics();
				}
				MapEditorHUD.Instance?.UpdateBlockModeExternal(true);
				MapEditorHUD.Instance?.UpdateBlockLevelHeightExternal(2.0f);
				MapEditorHUD.Instance?.UpdateCameraBoundsUI();
				UpdateCameraBoundsOverlayVisibility();
				UpdateGridOverlayVisibility();
				UpdatePathingOverlay();

				EditorCoordinates.Clear();
				RebuildAllCoordinatePersistentMeshes();
				MapEditorHUD.Instance?.RefreshCoordinateListExternal();

				return false;
			}

			if (GroundTerrain == null)
			{
				var toRemove = new List<Node>();
				foreach (var child in GetChildren())
				{
					if (child.Name.ToString().StartsWith("Ground"))
					{
						toRemove.Add(child);
					}
				}
				foreach (var child in toRemove)
				{
					RemoveChild(child);
					child.QueueFree();
				}

				var terrainNode = new EditableTerrain();
				terrainNode.Name = "Ground";
				AddChild(terrainNode);
				GroundTerrain = terrainNode;
			}

			TerrainState terrain = default;
			EditorState editor = default;
			bool foundWorld = false;

			var worldQuery = Realm.Ecs.Common.QueryCache.AllTerrainStateAndEditorStateQuery;
			EcsWorld.Query(in worldQuery, (ref TerrainState t, ref EditorState e) =>
			{
				terrain = t;
				editor = e;
				foundWorld = true;
			});

			if (!foundWorld) return false;

			MapEditorHUD.Instance?.UpdateBlockModeExternal(editor.BlockMode);
			MapEditorHUD.Instance?.UpdateBlockLevelHeightExternal(editor.BlockLevelHeight);
			MapEditorHUD.Instance?.UpdateCameraBoundsUI();
			UpdateCameraBoundsOverlayVisibility();
			UpdateGridOverlayVisibility();
			UpdatePathingOverlay();

			if (!string.IsNullOrEmpty(editor.SkyboxPath))
			{
				SetSkyboxTexture(editor.SkyboxPath);
				MapEditorHUD.Instance?.UpdateSelectedSkyboxExternal(editor.SkyboxPath);
			}

			int width = GroundTerrain.Width;
			int depth = GroundTerrain.Depth;

			if (GroundTerrain.SplatMap == null || GroundTerrain.SplatMap.GetLength(0) != width || GroundTerrain.SplatMap.GetLength(1) != depth)
			{
				GroundTerrain.UpdateMeshAndPhysics(false, false);
			}
			GroundTerrain.UpdateWaterSize();

			TerrainColorsState colorsState = default;
			bool foundColors = false;
			EcsWorld.Query(in worldQuery, (Entity entity) =>
			{
				if (EcsWorld.Has<TerrainColorsState>(entity))
				{
					colorsState = EcsWorld.Get<TerrainColorsState>(entity);
					foundColors = true;
				}
			});

			if (foundColors && colorsState.Colors != null)
			{
				int splatW = GroundTerrain.SplatMap.GetLength(0);
				int splatD = GroundTerrain.SplatMap.GetLength(1);
				int colorLen = colorsState.Colors.Length;

				for (int z = 0; z < splatD; z++)
				{
					for (int x = 0; x < splatW; x++)
					{
						int idx;
						if (colorLen == splatW * splatD)
						{
							idx = z * splatW + x;
						}
						else
						{
							int srcX = System.Math.Clamp(x, 0, width - 1);
							int srcZ = System.Math.Clamp(z, 0, depth - 1);
							idx = srcZ * width + srcX;
						}
						if (idx < colorLen)
						{
							string serialized = colorsState.Colors[idx];
							GroundTerrain.SplatMap[x, z] = TerrainSplatWeights.Deserialize(serialized);
						}
					}
				}
			}

			if (terrain.Cells != null && GroundTerrain != null)
			{
				GroundTerrain.Cells = (Realm.Ecs.Components.Terrain.TerrainCell[,])terrain.Cells.Clone();
			}

			if (terrain.PathingCodes != null && terrain.PathingCodes.Length == width * depth)
			{
				for (int z = 0; z < depth; z++)
				{
					for (int x = 0; x < width; x++)
					{
						GroundTerrain.PathingCodes[x, z] = terrain.PathingCodes[x, z];
					}
				}
			}

			AlignTerrainSplatMapExternal();

			if (!terrainOnly)
			{
				var unitSpawnQuery = Realm.Ecs.Common.QueryCache.AllUnitSpawnRequestQuery;
				var unitRequests = new List<Entity>();
				EcsWorld.Query(in unitSpawnQuery, (Entity entity) => unitRequests.Add(entity));
				foreach (var reqEnt in unitRequests)
				{
					ref var req = ref EcsWorld.Get<UnitSpawnRequest>(reqEnt);
					SpawnUnitExternal(req.UnitId, new Vector3(req.Position.X, req.Position.Y, req.Position.Z), req.IsEnemy, req.RotationY, req.Scale, req.Player);
					EcsWorld.Destroy(reqEnt);
				}

				var propSpawnQuery = Realm.Ecs.Common.QueryCache.AllPropSpawnRequestQuery;
				var propRequests = new List<Entity>();
				EcsWorld.Query(in propSpawnQuery, (Entity entity) => propRequests.Add(entity));
				foreach (var reqEnt in propRequests)
				{
					ref var req = ref EcsWorld.Get<PropSpawnRequest>(reqEnt);
					SpawnPropExternalWithParams(req.PropId, new Vector3(req.Position.X, req.Position.Y, req.Position.Z), req.RotationY, req.Scale);
					EcsWorld.Destroy(reqEnt);
				}
				PropMultiMeshManager.Instance?.RebuildAll();

				var decalSpawnQuery = Realm.Ecs.Common.QueryCache.AllDecalSpawnRequestQuery;
				var decalRequests = new List<Entity>();
				EcsWorld.Query(in decalSpawnQuery, (Entity entity) => decalRequests.Add(entity));
				foreach (var reqEnt in decalRequests)
				{
					ref var req = ref EcsWorld.Get<DecalSpawnRequest>(reqEnt);
					SpawnDecalExternalWithParams(req.DecalId, new Vector3(req.Position.X, req.Position.Y, req.Position.Z), req.RotationY, req.Scale);
					EcsWorld.Destroy(reqEnt);
				}
			}

			GroundTerrain.UpdateMeshAndPhysics(false, true);

			MapEditorHUD.Instance?.RegenerateMinimap();

			var loadedCoordinates = _saveLoadService.GetLastLoadedCoordinates();
			EditorCoordinates.Clear();
			EditorCoordinates.AddRange(loadedCoordinates.Select(r => new EditorCoordinate { Name = r.Name, MinX = r.MinX, MinZ = r.MinZ, MaxX = r.MaxX, MaxZ = r.MaxZ }));
			RebuildAllCoordinatePersistentMeshes();
			MapEditorHUD.Instance?.RefreshCoordinateListExternal();

			return true;
		}
		finally
		{
			IsLoadingMap = false;
		}
	}
}
