using Godot;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Terrain;
using Realm.Ecs.Components.Meta;
using Arch.Core;
using System.Collections.Generic;

public partial class GameHost
{
	public void SaveMapToFile(string customPath = "")
	{
		if (GroundTerrain == null) return;

		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;
		string[] htmlColors = new string[width * depth];
		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				htmlColors[z * width + x] = GroundTerrain.Colors[x, z].ToHtml(true);
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

		string path = string.IsNullOrEmpty(customPath) ? "user://terrain.json" : customPath;
		string absolutePath = ProjectSettings.GlobalizePath(path);

		_saveLoadService.SaveMapToFile(absolutePath, htmlColors, unitsData.ToArray(), propsData.ToArray(), decalsData.ToArray());
	}

	public bool LoadMapFromFile(string customPath = "", bool terrainOnly = false)
	{
		string path = string.IsNullOrEmpty(customPath) ? "user://terrain.json" : customPath;
		string absolutePath = ProjectSettings.GlobalizePath(path);

		ClearAllUnits();

		bool success = _saveLoadService.LoadMapFromFile(absolutePath, terrainOnly);
		if (!success) return false;

		if (GroundTerrain == null)
		{
			var terrainNode = new EditableTerrain();
			terrainNode.Name = "Ground";
			AddChild(terrainNode);
			GroundTerrain = terrainNode;
		}

		TerrainState terrain = default;
		EditorState editor = default;
		bool foundWorld = false;

		var worldQuery = new QueryDescription().WithAll<TerrainState, EditorState>();
		EcsWorld.Query(in worldQuery, (ref TerrainState t, ref EditorState e) =>
		{
			terrain = t;
			editor = e;
			foundWorld = true;
		});

		if (!foundWorld) return false;

		GroundTerrain.WaterEnabled = terrain.WaterEnabled;
		MapEditorHUD.Instance?.UpdateWaterEnabledExternal(terrain.WaterEnabled);
		
		GroundTerrain.WaterHeight = terrain.WaterHeight;
		MapEditorHUD.Instance?.UpdateWaterHeightExternal(terrain.WaterHeight);

		MapEditorHUD.Instance?.UpdateBlockModeExternal(editor.BlockMode);
		MapEditorHUD.Instance?.UpdateBlockLevelHeightExternal(editor.BlockLevelHeight);
		MapEditorHUD.Instance?.UpdateCameraBoundsUI();
		RebuildCameraBoundsOverlay();

		if (!string.IsNullOrEmpty(editor.SkyboxPath))
		{
			SetSkyboxTexture(editor.SkyboxPath);
			MapEditorHUD.Instance?.UpdateSelectedSkyboxExternal(editor.SkyboxPath);
		}

		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;

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

		if (foundColors && colorsState.Colors != null && colorsState.Colors.Length == width * depth)
		{
			for (int z = 0; z < depth; z++)
			{
				for (int x = 0; x < width; x++)
				{
					GroundTerrain.Colors[x, z] = Color.FromHtml(colorsState.Colors[z * width + x]);
				}
			}
		}

		GroundTerrain.UpdateMeshAndPhysics();

		if (!terrainOnly)
		{
			var unitSpawnQuery = new QueryDescription().WithAll<UnitSpawnRequest>();
			var unitRequests = new List<Entity>();
			EcsWorld.Query(in unitSpawnQuery, (Entity entity) => unitRequests.Add(entity));
			foreach (var reqEnt in unitRequests)
			{
				ref var req = ref EcsWorld.Get<UnitSpawnRequest>(reqEnt);
				SpawnUnitExternal(req.UnitId, new Vector3(req.Position.X, req.Position.Y, req.Position.Z), req.IsEnemy, req.RotationY, req.Scale);
				EcsWorld.Destroy(reqEnt);
			}

			var propSpawnQuery = new QueryDescription().WithAll<PropSpawnRequest>();
			var propRequests = new List<Entity>();
			EcsWorld.Query(in propSpawnQuery, (Entity entity) => propRequests.Add(entity));
			foreach (var reqEnt in propRequests)
			{
				ref var req = ref EcsWorld.Get<PropSpawnRequest>(reqEnt);
				SpawnPropExternalWithParams(req.PropId, new Vector3(req.Position.X, req.Position.Y, req.Position.Z), req.RotationY, req.Scale);
				EcsWorld.Destroy(reqEnt);
			}

			var decalSpawnQuery = new QueryDescription().WithAll<DecalSpawnRequest>();
			var decalRequests = new List<Entity>();
			EcsWorld.Query(in decalSpawnQuery, (Entity entity) => decalRequests.Add(entity));
			foreach (var reqEnt in decalRequests)
			{
				ref var req = ref EcsWorld.Get<DecalSpawnRequest>(reqEnt);
				SpawnDecalExternalWithParams(req.DecalId, new Vector3(req.Position.X, req.Position.Y, req.Position.Z), req.RotationY, req.Scale);
				EcsWorld.Destroy(reqEnt);
			}
		}

		MapEditorHUD.Instance?.RegenerateMinimap();
		return true;
	}
}
