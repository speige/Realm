using Arch.Core;
using Godot;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using System;
using System.Collections.Generic;
using System.Linq;

public partial class GameHost
{
	public readonly Dictionary<string, float> ModelYOffsets = new(StringComparer.OrdinalIgnoreCase);
	public readonly Dictionary<string, float> ModelCollisionCircleRatios = new(StringComparer.OrdinalIgnoreCase);
	public readonly Dictionary<string, float> ModelBrightness = new(StringComparer.OrdinalIgnoreCase);
	public readonly Dictionary<string, Color> ModelColorTint = new(StringComparer.OrdinalIgnoreCase);
	public readonly Dictionary<string, bool> ModelGenerateNormals = new(StringComparer.OrdinalIgnoreCase);
	private bool _modelYOffsetSavePending = false;
	private bool _modelCollisionCircleSavePending = false;

	private readonly Dictionary<string, string> _normalizedAssetKeyCache = new(StringComparer.OrdinalIgnoreCase);

	public string NormalizeModelAssetKey(string pathOrId)
	{
		if (string.IsNullOrEmpty(pathOrId)) return "";
		if (_normalizedAssetKeyCache.TryGetValue(pathOrId, out string cached))
		{
			return cached;
		}

		string filename = System.IO.Path.GetFileName(pathOrId);
		if (!filename.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) && !filename.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase))
		{
			filename += ".glb";
		}
		string result = filename.ToLowerInvariant();
		_normalizedAssetKeyCache[pathOrId] = result;
		return result;
	}

	public string GetModelAssetKey(object objOrId)
	{
		if (objOrId == null) return "";
		if (objOrId is Prop3D prop)
		{
			return NormalizeModelAssetKey(prop.ModelAssetPath);
		}
		if (objOrId is Unit3D unit)
		{
			if (!string.IsNullOrEmpty(unit.ModelPath))
				return NormalizeModelAssetKey(unit.ModelPath);
			if (UnitRegistry.TryGetValue(unit.UnitId, out var meta) && !string.IsNullOrEmpty(meta.ModelPath))
				return NormalizeModelAssetKey(meta.ModelPath);
			return NormalizeModelAssetKey(unit.UnitId);
		}
		if (objOrId is Node node)
		{
			return NormalizeModelAssetKey(node.Name.ToString());
		}
		if (objOrId is string str)
		{
			return NormalizeModelAssetKey(str);
		}
		return "";
	}

	public float GetModelYOffset(string assetKey)
	{
		string norm = NormalizeModelAssetKey(assetKey);
		if (!string.IsNullOrEmpty(norm) && ModelYOffsets.TryGetValue(norm, out float val))
		{
			return val;
		}
		return 0f;
	}

	public void SetModelYOffset(string assetKey, float offset)
	{
		string norm = NormalizeModelAssetKey(assetKey);
		if (string.IsNullOrEmpty(norm)) return;

		ModelYOffsets[norm] = offset;

		foreach (var prop in AllProps)
		{
			if (GodotObject.IsInstanceValid(prop) && GetModelAssetKey(prop) == norm)
			{
				prop.Position = new Vector3(prop.Position.X, _editorService.GetTerrainHeightAt(prop.Position) + offset, prop.Position.Z);
			}
		}

		foreach (var unit in AllUnits)
		{
			if (GodotObject.IsInstanceValid(unit) && GetModelAssetKey(unit) == norm)
			{
				unit.UpdateModelYOffset(offset);
			}
		}

		if (_editorPreviewNode != null && GodotObject.IsInstanceValid(_editorPreviewNode))
		{
			if (_editorPreviewNode is Prop3D previewProp && GetModelAssetKey(previewProp) == norm)
			{
				previewProp.UpdateVisualYOffset(offset);
			}
			else if (_editorPreviewNode is Unit3D previewUnit && GetModelAssetKey(previewUnit) == norm)
			{
				previewUnit.UpdateModelYOffset(offset);
			}
		}

		_modelYOffsetSavePending = true;
		EditorHasUnsavedChanges = true;
	}

	public void FlushModelYOffsetSave()
	{
		if (_modelYOffsetSavePending || _modelCollisionCircleSavePending)
		{
			_modelYOffsetSavePending = false;
			_modelCollisionCircleSavePending = false;
			SaveModelYOffsetsToMetadataJson();
		}
	}

	public float GetModelCollisionCircleRatio(string assetKey)
	{
		string norm = NormalizeModelAssetKey(assetKey);
		if (!string.IsNullOrEmpty(norm) && ModelCollisionCircleRatios.TryGetValue(norm, out float val))
		{
			return val;
		}
		return 1.0f;
	}

	public void SetModelCollisionCircleRatio(string assetKey, float ratio)
	{
		string norm = NormalizeModelAssetKey(assetKey);
		if (string.IsNullOrEmpty(norm)) return;

		ModelCollisionCircleRatios[norm] = ratio;
		UpdateCollisionRadiiForAsset(norm);

		_modelCollisionCircleSavePending = true;
		EditorHasUnsavedChanges = true;
	}

	public float GetModelBrightness(string assetKey)
	{
		string norm = NormalizeModelAssetKey(assetKey);
		if (!string.IsNullOrEmpty(norm) && ModelBrightness.TryGetValue(norm, out float bVal))
		{
			return Mathf.Clamp(bVal, 0.0f, 1.0f);
		}
		return 1.0f;
	}

	public void SetModelBrightness(string assetKey, float brightness)
	{
		string norm = NormalizeModelAssetKey(assetKey);
		if (string.IsNullOrEmpty(norm)) return;

		float k = Mathf.Clamp(brightness, 0.0f, 1.0f);
		ModelBrightness[norm] = k;
		UpdateMaterialOverridesForAsset(norm);

		_modelYOffsetSavePending = true;
		EditorHasUnsavedChanges = true;
	}

	public Color GetModelColorTint(string assetKey)
	{
		string norm = NormalizeModelAssetKey(assetKey);
		if (!string.IsNullOrEmpty(norm) && ModelColorTint.TryGetValue(norm, out Color cVal))
		{
			return cVal;
		}
		return new Color(1.0f, 1.0f, 1.0f);
	}

	public void SetModelColorTint(string assetKey, Color color)
	{
		string norm = NormalizeModelAssetKey(assetKey);
		if (string.IsNullOrEmpty(norm)) return;

		Color clamped = new Color(
			Mathf.Clamp(color.R, 0.0f, 1.0f),
			Mathf.Clamp(color.G, 0.0f, 1.0f),
			Mathf.Clamp(color.B, 0.0f, 1.0f),
			1.0f
		);
		ModelColorTint[norm] = clamped;
		UpdateMaterialOverridesForAsset(norm);

		_modelYOffsetSavePending = true;
		EditorHasUnsavedChanges = true;
	}

	public bool GetModelGenerateNormals(string assetKey)
	{
		string norm = NormalizeModelAssetKey(assetKey);
		if (!string.IsNullOrEmpty(norm) && ModelGenerateNormals.TryGetValue(norm, out bool val))
		{
			return val;
		}
		return false;
	}

	public void SetModelGenerateNormals(string assetKey, bool generateNormals)
	{
		string norm = NormalizeModelAssetKey(assetKey);
		if (string.IsNullOrEmpty(norm)) return;

		ModelGenerateNormals[norm] = generateNormals;
		UpdateMaterialOverridesForAsset(norm);

		_modelYOffsetSavePending = true;
		EditorHasUnsavedChanges = true;
	}

	public void UpdateMaterialOverridesForAsset(string normAssetKey)
	{
		if (string.IsNullOrEmpty(normAssetKey)) return;

		float brightness = GetModelBrightness(normAssetKey);
		Color tint = GetModelColorTint(normAssetKey);
		bool generateNormals = GetModelGenerateNormals(normAssetKey);

		foreach (var prop in AllProps)
		{
			if (GodotObject.IsInstanceValid(prop) && GetModelAssetKey(prop) == normAssetKey)
			{
				ApplyMaterialOverridesToNode(prop, brightness, tint, generateNormals);
			}
		}

		foreach (var unit in AllUnits)
		{
			if (GodotObject.IsInstanceValid(unit) && GetModelAssetKey(unit) == normAssetKey)
			{
				ApplyMaterialOverridesToNode(unit, brightness, tint, generateNormals);
			}
		}

		if (_editorPreviewNode != null && GodotObject.IsInstanceValid(_editorPreviewNode) && GetModelAssetKey(_editorPreviewNode) == normAssetKey)
		{
			ApplyMaterialOverridesToNode(_editorPreviewNode, brightness, tint, generateNormals);
		}
	}

	public static void ApplyMaterialOverridesToNode(
		Node node,
		float brightness = 1.0f,
		Color? colorTint = null,
		bool generateNormals = false)
	{
		if (node == null || !GodotObject.IsInstanceValid(node)) return;

		Color tint = colorTint ?? new Color(1.0f, 1.0f, 1.0f);
		float multR = brightness * tint.R;
		float multG = brightness * tint.G;
		float multB = brightness * tint.B;

		var meshNodes = FindMeshInstancesRecursive(node);
		foreach (var meshInst in meshNodes)
		{
			string nameStr = meshInst.Name.ToString();
			if (nameStr.StartsWith("_selection") || nameStr.StartsWith("_hover") || nameStr.StartsWith("BrushIndicator")) continue;

			if (generateNormals)
			{
				if (!meshInst.HasMeta("original_mesh") && meshInst.Mesh != null)
				{
					meshInst.SetMeta("original_mesh", meshInst.Mesh);
				}

				Mesh baseMesh = meshInst.HasMeta("original_mesh") ? meshInst.GetMeta("original_mesh").As<Mesh>() : meshInst.Mesh;
				if (baseMesh is ArrayMesh arrayMesh)
				{
					var toolMesh = new ArrayMesh();
					var surfaceTool = new SurfaceTool();
					for (int i = 0; i < arrayMesh.GetSurfaceCount(); i++)
					{
						surfaceTool.CreateFrom(arrayMesh, i);
						surfaceTool.GenerateNormals();
						toolMesh = surfaceTool.Commit(toolMesh);
					}
					meshInst.Mesh = toolMesh;
				}
			}
			else
			{
				if (meshInst.HasMeta("original_mesh"))
				{
					meshInst.Mesh = meshInst.GetMeta("original_mesh").As<Mesh>();
				}
			}

			int surfaceCount = meshInst.Mesh != null ? meshInst.Mesh.GetSurfaceCount() : 0;
			for (int i = 0; i < surfaceCount; i++)
			{
				Material mat = meshInst.GetSurfaceOverrideMaterial(i);
				if (mat == null && meshInst.Mesh != null)
				{
					mat = meshInst.Mesh.SurfaceGetMaterial(i);
				}

				if (mat is BaseMaterial3D baseMat)
				{
					if (meshInst.GetSurfaceOverrideMaterial(i) == null)
					{
						baseMat = (BaseMaterial3D)baseMat.Duplicate();
						meshInst.SetSurfaceOverrideMaterial(i, baseMat);
					}

					baseMat.AlbedoColor = new Color(multR, multG, multB, baseMat.AlbedoColor.A);
				}
			}

			if (meshInst.MaterialOverride is BaseMaterial3D overrideMat)
			{
				overrideMat.AlbedoColor = new Color(multR, multG, multB, overrideMat.AlbedoColor.A);
			}
		}
	}

	private static void FindMeshInstancesRecursive(Node parent, List<MeshInstance3D> result)
	{
		if (parent == null) return;
		if (parent is MeshInstance3D mi)
		{
			result.Add(mi);
		}
		int childCount = parent.GetChildCount();
		for (int i = 0; i < childCount; i++)
		{
			FindMeshInstancesRecursive(parent.GetChild(i), result);
		}
	}

	private static List<MeshInstance3D> FindMeshInstancesRecursive(Node parent)
	{
		var list = new List<MeshInstance3D>();
		FindMeshInstancesRecursive(parent, list);
		return list;
	}

	public void FlushModelCollisionCircleSave()
	{
		FlushModelYOffsetSave();
	}

	public void UpdateCollisionRadiiForAsset(string normAssetKey)
	{
		float ratio = GetModelCollisionCircleRatio(normAssetKey);

		foreach (var prop in AllProps)
		{
			if (GodotObject.IsInstanceValid(prop) && GetModelAssetKey(prop) == normAssetKey)
			{
				prop.UpdateCollisionCircleScale(ratio);
				if (prop.Entity != default && EcsWorld.IsAlive(prop.Entity))
				{
					float autoDetected = GetOrCalculateObstacleRadius(prop.PropId, prop);
					float baseRadius = autoDetected * ratio;
					if (EcsWorld.Has<Realm.Ecs.Components.Core.CollisionRadius>(prop.Entity))
					{
						EcsWorld.Set(prop.Entity, new Realm.Ecs.Components.Core.CollisionRadius(baseRadius));
					}
					else
					{
						EcsWorld.Add(prop.Entity, new Realm.Ecs.Components.Core.CollisionRadius(baseRadius));
					}
				}
			}
		}

		foreach (var unit in AllUnits)
		{
			if (GodotObject.IsInstanceValid(unit) && GetModelAssetKey(unit) == normAssetKey)
			{
				unit.UpdateCollisionCircleScale(ratio);
				if (unit.Entity != default && EcsWorld.IsAlive(unit.Entity))
				{
					float autoDetected = GetOrCalculateObstacleRadius(unit.UnitId, unit);
					float baseRadius = autoDetected * ratio;
					if (EcsWorld.Has<Realm.Ecs.Components.Core.CollisionRadius>(unit.Entity))
					{
						EcsWorld.Set(unit.Entity, new Realm.Ecs.Components.Core.CollisionRadius(baseRadius));
					}
					else
					{
						EcsWorld.Add(unit.Entity, new Realm.Ecs.Components.Core.CollisionRadius(baseRadius));
					}
				}
			}
		}

		if (_editorPreviewNode != null && GodotObject.IsInstanceValid(_editorPreviewNode))
		{
			if (_editorPreviewNode is Prop3D previewProp && GetModelAssetKey(previewProp) == normAssetKey)
			{
				previewProp.UpdateCollisionCircleScale(ratio);
			}
			else if (_editorPreviewNode is Unit3D previewUnit && GetModelAssetKey(previewUnit) == normAssetKey)
			{
				previewUnit.UpdateCollisionCircleScale(ratio);
			}
		}
	}

	public void LoadModelYOffsetsFromMetadataJson(string directory = null)
	{
		try
		{
			string mapDir = !string.IsNullOrEmpty(directory) ? directory : CurrentMapDirectory;
			if (string.IsNullOrEmpty(mapDir))
			{
				mapDir = Godot.ProjectSettings.GlobalizePath("user://temp_map_workspace");
			}
			string metadataPath = System.IO.Path.Combine(mapDir, "metadata.json");
			if (!System.IO.File.Exists(metadataPath)) return;

			string jsonText = System.IO.File.ReadAllText(metadataPath);
			if (string.IsNullOrWhiteSpace(jsonText)) return;

			var root = System.Text.Json.Nodes.JsonNode.Parse(jsonText) as System.Text.Json.Nodes.JsonObject;
			if (root == null) return;

			ModelYOffsets.Clear();
			ModelCollisionCircleRatios.Clear();
			ModelBrightness.Clear();
			ModelGenerateNormals.Clear();

			if (root.ContainsKey("ModelOffsets") && root["ModelOffsets"] is System.Text.Json.Nodes.JsonObject offsetsObj)
			{
				foreach (var kvp in offsetsObj)
				{
					if (kvp.Value != null && float.TryParse(kvp.Value.ToString(), out float val))
					{
						ModelYOffsets[NormalizeModelAssetKey(kvp.Key)] = val;
					}
				}
			}

			if (root.ContainsKey("ModelCollisionCircleRatios") && root["ModelCollisionCircleRatios"] is System.Text.Json.Nodes.JsonObject circlesObj)
			{
				foreach (var kvp in circlesObj)
				{
					if (kvp.Value != null && float.TryParse(kvp.Value.ToString(), out float val))
					{
						ModelCollisionCircleRatios[NormalizeModelAssetKey(kvp.Key)] = val;
					}
				}
			}

			if (root.ContainsKey("ModelBrightness") && root["ModelBrightness"] is System.Text.Json.Nodes.JsonObject mbObj)
			{
				foreach (var kvp in mbObj)
				{
					if (kvp.Value != null && float.TryParse(kvp.Value.ToString(), out float val))
					{
						ModelBrightness[NormalizeModelAssetKey(kvp.Key)] = val;
					}
				}
			}

			if (root.ContainsKey("ModelGenerateNormals") && root["ModelGenerateNormals"] is System.Text.Json.Nodes.JsonObject gnObj)
			{
				foreach (var kvp in gnObj)
				{
					if (kvp.Value != null && bool.TryParse(kvp.Value.ToString(), out bool val))
					{
						ModelGenerateNormals[NormalizeModelAssetKey(kvp.Key)] = val;
					}
				}
			}

			if (root.ContainsKey("Assets") && root["Assets"] is System.Text.Json.Nodes.JsonObject assetsObj && assetsObj.ContainsKey("glb") && assetsObj["glb"] is System.Text.Json.Nodes.JsonObject glbObj)
			{
				foreach (var catKvp in glbObj)
				{
					if (catKvp.Value is System.Text.Json.Nodes.JsonObject catDict)
					{
						foreach (var itemKvp in catDict)
						{
							if (itemKvp.Value is System.Text.Json.Nodes.JsonObject itemObj)
							{
								if (itemObj.ContainsKey("y_offset") && float.TryParse(itemObj["y_offset"]?.ToString(), out float yVal))
								{
									ModelYOffsets[NormalizeModelAssetKey(itemKvp.Key)] = yVal;
								}
								if (itemObj.ContainsKey("collision_circle_ratio") && float.TryParse(itemObj["collision_circle_ratio"]?.ToString(), out float rVal))
								{
									ModelCollisionCircleRatios[NormalizeModelAssetKey(itemKvp.Key)] = rVal;
								}
								if (itemObj.ContainsKey("brightness") && float.TryParse(itemObj["brightness"]?.ToString(), out float brightVal))
								{
									ModelBrightness[NormalizeModelAssetKey(itemKvp.Key)] = brightVal;
								}
								if (itemObj.ContainsKey("generate_normals") && bool.TryParse(itemObj["generate_normals"]?.ToString(), out bool gnVal))
								{
									ModelGenerateNormals[NormalizeModelAssetKey(itemKvp.Key)] = gnVal;
								}
							}
						}
					}
				}
			}

			foreach (var key in ModelBrightness.Keys
				.Concat(ModelGenerateNormals.Keys)
				.Distinct())
			{
				UpdateMaterialOverridesForAsset(key);
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"LoadModelYOffsetsFromMetadataJson error: {ex.Message}");
		}
	}

	public void SaveModelYOffsetsToMetadataJson(string directory = null)
	{
		try
		{
			string mapDir = !string.IsNullOrEmpty(directory) ? directory : CurrentMapDirectory;
			if (string.IsNullOrEmpty(mapDir))
			{
				mapDir = Godot.ProjectSettings.GlobalizePath("user://temp_map_workspace");
			}
			string metadataPath = System.IO.Path.Combine(mapDir, "metadata.json");

			System.Text.Json.Nodes.JsonObject root = new System.Text.Json.Nodes.JsonObject();
			if (System.IO.File.Exists(metadataPath))
			{
				string text = System.IO.File.ReadAllText(metadataPath);
				if (!string.IsNullOrWhiteSpace(text))
				{
					root = System.Text.Json.Nodes.JsonNode.Parse(text) as System.Text.Json.Nodes.JsonObject ?? new System.Text.Json.Nodes.JsonObject();
				}
			}

			System.Text.Json.Nodes.JsonObject offsetsObj = new System.Text.Json.Nodes.JsonObject();
			foreach (var kvp in ModelYOffsets) offsetsObj[kvp.Key] = kvp.Value;
			root["ModelOffsets"] = offsetsObj;

			System.Text.Json.Nodes.JsonObject circlesObj = new System.Text.Json.Nodes.JsonObject();
			foreach (var kvp in ModelCollisionCircleRatios) circlesObj[kvp.Key] = kvp.Value;
			root["ModelCollisionCircleRatios"] = circlesObj;



			System.Text.Json.Nodes.JsonObject mbObj = new System.Text.Json.Nodes.JsonObject();
			foreach (var kvp in ModelBrightness) mbObj[kvp.Key] = kvp.Value;
			root["ModelBrightness"] = mbObj;

			System.Text.Json.Nodes.JsonObject gnObj = new System.Text.Json.Nodes.JsonObject();
			foreach (var kvp in ModelGenerateNormals) gnObj[kvp.Key] = kvp.Value;
			root["ModelGenerateNormals"] = gnObj;

			if (root.ContainsKey("Assets") && root["Assets"] is System.Text.Json.Nodes.JsonObject assetsObj && assetsObj.ContainsKey("glb") && assetsObj["glb"] is System.Text.Json.Nodes.JsonObject glbObj)
			{
				foreach (var catKvp in glbObj)
				{
					if (catKvp.Value is System.Text.Json.Nodes.JsonObject catDict)
					{
						foreach (var key in catDict.Select(kvp => kvp.Key).ToList())
						{
							string normKey = NormalizeModelAssetKey(key);
							bool hasY = ModelYOffsets.TryGetValue(normKey, out float yVal);
							bool hasRatio = ModelCollisionCircleRatios.TryGetValue(normKey, out float rVal);
							bool hasBright = ModelBrightness.TryGetValue(normKey, out float brightVal);
							bool hasGn = ModelGenerateNormals.TryGetValue(normKey, out bool gnVal);

							if (hasY || hasRatio || hasBright || hasGn)
							{
								var nodeVal = catDict[key];
								if (nodeVal is System.Text.Json.Nodes.JsonObject itemObj)
								{
									if (hasY) itemObj["y_offset"] = yVal;
									if (hasRatio) itemObj["collision_circle_ratio"] = rVal;
									if (hasBright) itemObj["brightness"] = brightVal;
									if (hasGn) itemObj["generate_normals"] = gnVal;
								}
								else if (nodeVal != null)
								{
									string hashStr = nodeVal.ToString();
									var newItemObj = new System.Text.Json.Nodes.JsonObject
									{
										["hash"] = hashStr
									};
									if (hasY) newItemObj["y_offset"] = yVal;
									if (hasRatio) newItemObj["collision_circle_ratio"] = rVal;
									if (hasBright) newItemObj["brightness"] = brightVal;
									if (hasGn) newItemObj["generate_normals"] = gnVal;
									catDict[key] = newItemObj;
								}
							}
						}
					}
				}
			}

			System.IO.File.WriteAllText(metadataPath, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
		}
		catch (Exception ex)
		{
			GD.PrintErr($"SaveModelYOffsetsToMetadataJson error: {ex.Message}");
		}
	}
	public void ClearMapEntirely()
	{
		if (GroundTerrain == null) return;
		
		var unitsCopy = new List<Unit3D>(AllUnits);
		foreach (var unit in unitsCopy)
		{
			if (GodotObject.IsInstanceValid(unit))
			{
				DeleteNodeExternal(unit);
			}
		}
		SelectedUnits.Clear();
		AllUnits.Clear();
		ClearAllBuildQueueGhosts();
		AllProps.Clear();
		AllDecals.Clear();
		ActivePings.Clear();
		EntityToUnit3D.Clear();
		EntityToProp3D.Clear();
		if (_controlGroups != null)
		{
			for (int i = 0; i < _controlGroups.Length; i++)
			{
				_controlGroups[i]?.Clear();
			}
		}
		
		var childrenCopy = new List<Node>(GetChildren());
		foreach (var child in childrenCopy)
		{
			if (child is Prop3D prop && GodotObject.IsInstanceValid(prop))
			{
				DeleteNodeExternal(prop);
			}
			else if (child is Decal decal && GodotObject.IsInstanceValid(decal))
			{
				DeleteNodeExternal(decal);
			}
		}
		
		if (GroundTerrain != null && GroundTerrain.Heights != null && GroundTerrain.SplatMap != null)
		{
			int width = GroundTerrain.Width;
			int depth = GroundTerrain.Depth;

			var heights = GroundTerrain.Heights;
			var splatMap = GroundTerrain.SplatMap;
			var cliffSplatMap = GroundTerrain.CliffSplatMap;
			var pathingCodes = GroundTerrain.PathingCodes;

			if (heights == null || heights.GetLength(0) != width || heights.GetLength(1) != depth)
			{
				heights = new float[width, depth];
			}
			if (splatMap == null || splatMap.GetLength(0) < width + 1 || splatMap.GetLength(1) < depth + 1)
			{
				splatMap = new TerrainSplatWeights[width + 1, depth + 1];
				GroundTerrain.SplatMap = splatMap;
			}
			if (cliffSplatMap == null || cliffSplatMap.GetLength(0) < width + 1 || cliffSplatMap.GetLength(1) < depth + 1)
			{
				cliffSplatMap = new TerrainSplatWeights[width + 1, depth + 1];
				GroundTerrain.CliffSplatMap = cliffSplatMap;
			}
			if (pathingCodes == null || pathingCodes.GetLength(0) != width || pathingCodes.GetLength(1) != depth)
			{
				pathingCodes = new int[width, depth];
			}

			for (int z = 0; z <= depth; z++)
			{
				for (int x = 0; x <= width; x++)
				{
					if (x < width && z < depth)
					{
						heights[x, z] = 0.0f;
						pathingCodes[x, z] = EditableTerrain.GetDefaultPathingCode(GroundTerrain.Cells[x, z]);
					}
					splatMap[x, z] = TerrainSplatWeights.CreateSolid(0);
					cliffSplatMap[x, z] = TerrainSplatWeights.CreateSolid(GroundTerrain.CliffTextureIndex);
				}
			}

			GroundTerrain.SetHeights(heights);

			if (EcsWorld != null && EcsWorld.IsAlive(WorldEntity) && EcsWorld.Has<Realm.Ecs.Components.Terrain.TerrainState>(WorldEntity))
			{
				ref var ts = ref EcsWorld.Get<Realm.Ecs.Components.Terrain.TerrainState>(WorldEntity);
				ts.SetHeights(heights);
				ts.PathingCodes = pathingCodes;
				EcsWorld.Set(WorldEntity, ts);
			}

			GroundTerrain.UpdateMeshAndPhysics(true, true);
			if (PathingOverlayVisible)
			{
				RebuildPathingOverlay();
			}
		}

		_editorService?.ResetAllState();
		if (_selectionHighlightMesh != null)
		{
			_selectionHighlightMesh.Visible = false;
		}

		EditorHistoryManager.Clear();
		EditorHasUnsavedChanges = false;
		RebuildGridOverlayMeshExternal();
		
		EditorCameraBoundsLeft = -95.0f;
		EditorCameraBoundsRight = 95.0f;
		EditorCameraBoundsTop = -95.0f;
		EditorCameraBoundsBottom = 125.0f;
		MapEditorHUD.Instance?.UpdateCameraBoundsUI();
		RebuildCameraBoundsOverlay();

		EditorCoordinates.Clear();
		RebuildAllCoordinatePersistentMeshes();
		MapEditorHUD.Instance?.RefreshCoordinateListExternal();

		MapEditorHUD.Instance?.ClearTempWorkspaceExternal();
		MapEditorHUD.Instance?.GenerateVSCodeFilesExternal();
		MapEditorHUD.Instance?.ShowFeedbackExternal("Map reset: cleared all entities & terrain");
		MapEditorHUD.Instance?.RegenerateMinimap();
	}

	private bool IsMouseOverUI()
	{
		if (GodotObject.IsInstanceValid(MapEditorHUD.Instance))
		{
			return MapEditorHUD.Instance.IsMouseOverUI(GetViewport().GetMousePosition());
		}
		var mousePos = GetViewport().GetMousePosition();
		var viewportSize = GetViewport().GetVisibleRect().Size;
		
		if (mousePos.Y < 75) return true;
		if (mousePos.Y > viewportSize.Y - 245) return true;
		if (mousePos.X < 225 || mousePos.X > viewportSize.X - 225) return true;
		
		return false;
	}

	private long _lastTerrainMeshRebuildMs = long.MinValue;
	private Rect2I? _terrainFlushRegion;
	private bool _terrainGeometryDirty;
	private const float TerrainMeshRebuildPeriodMs = 33.3f;

	private void ApplyContinuousTerrainEditing(Vector3 worldPos, float delta, bool isFirstClick = false)
	{
		if (GroundTerrain == null) return;

		int pathingMask = 0;
		bool pathingAdd = true;
		if (ActiveEditorTool == EditorTool.PaintPathing && MapEditorHUD.Instance != null)
		{
			pathingMask = MapEditorHUD.Instance.GetSelectedPathingMask();
			pathingAdd = MapEditorHUD.Instance.IsPathingAddMode();
		}

		bool applyGround = MapEditorHUD.Instance?.IsApplyGroundTextureEnabled() ?? true;
		bool applyCliff = MapEditorHUD.Instance?.IsApplyCliffTextureEnabled() ?? true;

		var result = _editorService.ApplyContinuousTerrainEditing(
			worldPos, delta,
			ActiveEditorTool,
			EditorBrushRadius, EditorBrushStrength,
			EditorBrushIsSquare,
			EditorBlockMode, EditorBlockLevelHeight,
			EditorPaintTextureIndex, EditorCliffPaintTextureIndex,
			pathingMask, pathingAdd,
			isFirstClick,
			applyGround, applyCliff);

		if (result.HeightsModified || result.SplatModified || result.PathingModified)
		{
			Rect2I affected = new Rect2I(result.MinX - 2, result.MinZ - 2, result.MaxX - result.MinX + 4, result.MaxZ - result.MinZ + 4);

			// Accumulate the affected region so the final flush at mouse-release covers the whole stroke.
			_terrainFlushRegion = _terrainFlushRegion.HasValue ? _terrainFlushRegion.Value.Merge(affected) : affected;
			_terrainGeometryDirty = true;

			// Limit the number of full mesh/water rebuilds while dragging so painting stays smooth
			// on large maps, while the terrain data itself is updated every frame.
			long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			if (isFirstClick || nowMs - _lastTerrainMeshRebuildMs >= TerrainMeshRebuildPeriodMs)
			{
				_lastTerrainMeshRebuildMs = nowMs;
				GroundTerrain.UpdateMeshAndPhysics(false, false, affected, result.HeightsModified); // false for physics rebuild during drag
				if (result.HeightsModified)
				{
					AlignAllEntitiesToTerrain(affected);
				}
				if (result.PathingModified && PathingOverlayVisible)
				{
					RebuildPathingOverlay();
				}
			}
			EditorHasUnsavedChanges = true;
		}
	}

	public Prop3D SpawnPropExternal(string propId, Vector3 position)
	{
		float defaultAmount = propId switch
		{
			"goldmine" => 2000f,
			"rock" => 1000f,
			"tree" => 500f,
			_ => 0f
		};

		var entity = EcsWorld.Create();
		EcsWorld.Add(entity, new PropIdentity(propId));
		if (defaultAmount > 0f)
		{
			EcsWorld.Add(entity, new ResourceNode(Guid.Empty, defaultAmount));
		}

		var prop = new Prop3D();
		prop.Entity = entity;
		prop.PropId = propId;
		AddChild(prop);
		AllProps.Add(prop);

		EntityToProp3D[entity] = prop;

		position.Y = _editorService.GetTerrainHeightAt(position);
		prop.Position = position;
		
		float actualScale = 1.0f;
		if (IsMapEditorMode)
		{
			prop.RotationDegrees = new Vector3(0.0f, EditorPlacementRotation, 0.0f);
			prop.Scale *= EditorPlacementScale;
			actualScale = EditorPlacementScale;
		}

		EcsWorld.Add(entity, new Realm.Ecs.Components.Tags.Prop());
		EcsWorld.Add(entity, new Realm.Ecs.Components.Core.Position(new System.Numerics.Vector3(position.X, position.Y, position.Z)));
		EcsWorld.Add(entity, new CollisionScale(actualScale));

		float autoDetectedRadius = GetOrCalculateObstacleRadius(propId, prop);
		string propAssetKey = GetModelAssetKey(prop);
		float baseRadius = autoDetectedRadius * GetModelCollisionCircleRatio(propAssetKey);
		EcsWorld.Add(entity, new Realm.Ecs.Components.Core.CollisionRadius(baseRadius));

		return prop;
	}

	public Texture2D LoadDecalTexture(string decalId)
	{
		if (string.IsNullOrEmpty(decalId)) decalId = "logo";

		string wsPath = ProjectSettings.GlobalizePath("user://temp_map_workspace");
		string wsDecalPath = System.IO.Path.Combine(wsPath, "Assets", "decals", decalId);
		if (!System.IO.File.Exists(wsDecalPath) && !decalId.Contains('.'))
		{
			wsDecalPath = System.IO.Path.Combine(wsPath, "Assets", "decals", decalId + ".png");
		}
		if (System.IO.File.Exists(wsDecalPath))
		{
			var img = Image.LoadFromFile(wsDecalPath);
			if (img != null) return ImageTexture.CreateFromImage(img);
		}

		if (System.IO.File.Exists(decalId) && !decalId.StartsWith("res://"))
		{
			var img = Image.LoadFromFile(decalId);
			if (img != null)
			{
				return ImageTexture.CreateFromImage(img);
			}
		}

		string texPath = GetDecalTexturePath(decalId);
		var texture = GD.Load<Texture2D>(texPath);
		return texture ?? GD.Load<Texture2D>("res://icon.svg");
	}

	public string GetDecalTexturePath(string decalId)
	{
		if (string.IsNullOrEmpty(decalId))
		{
			decalId = "logo";
		}
		string wsPath = ProjectSettings.GlobalizePath("user://temp_map_workspace");
		string wsDecalPath = System.IO.Path.Combine(wsPath, "Assets", "decals", decalId);
		if (!System.IO.File.Exists(wsDecalPath) && !decalId.Contains('.'))
		{
			wsDecalPath = System.IO.Path.Combine(wsPath, "Assets", "decals", decalId + ".png");
		}
		if (System.IO.File.Exists(wsDecalPath))
		{
			return wsDecalPath;
		}

		if (System.IO.File.Exists(decalId) && !decalId.StartsWith("res://"))
		{
			return decalId;
		}
		if (decalId.StartsWith("res://") || decalId.Contains('/') || decalId.Contains('\\'))
		{
			if (decalId.EndsWith(".glb") || decalId.EndsWith(".gltf"))
			{
				return "res://icon.svg";
			}
			return decalId;
		}
		return "res://icon.svg";
	}

	public Decal SpawnDecalExternal(Vector3 position)
	{
		var entity = EcsWorld.Create();
		var decal = new Decal3D();
		decal.Entity = entity;
		decal.DecalId = "logo";
		decal.TextureAlbedo = GD.Load<Texture2D>("res://icon.svg");
		decal.Size = new Vector3(6.0f, 20.0f, 6.0f);
		decal.AlbedoMix = 1.0f;
		AddChild(decal);
		AllDecals.Add(decal);
		
		position.Y = _editorService.GetTerrainHeightAt(position);
		decal.Position = position;
		
		EcsWorld.Add(entity, new Realm.Ecs.Components.Core.Position(new System.Numerics.Vector3(position.X, position.Y, position.Z)));
		EcsWorld.Add(entity, new RotationY(0.0f));
		EcsWorld.Add(entity, new ModelScale(1.0f));
		
		if (IsMapEditorMode)
		{
			decal.RotationDegrees = new Vector3(0.0f, EditorPlacementRotation, 0.0f);
			decal.Size = new Vector3(6.0f, 20.0f, 6.0f) * EditorPlacementScale;
			decal.Scale = Vector3.One;
			EcsWorld.Set(entity, new RotationY(EditorPlacementRotation));
			EcsWorld.Set(entity, new ModelScale(EditorPlacementScale));
		}
		return decal;
	}

	public float GetTerrainHeightAt(Vector3 worldPos)
	{
		return _editorService.GetTerrainHeightAt(worldPos);
	}

	private void AlignAllEntitiesToTerrain(Rect2I? affectedRegion = null)
	{
		float quadSize = GroundTerrain != null ? GroundTerrain.QuadSize : EditableTerrain.DefaultQuadSize;
		float halfW = GroundTerrain != null ? (GroundTerrain.Width - 1) / 2.0f * quadSize : 0f;
		float halfD = GroundTerrain != null ? (GroundTerrain.Depth - 1) / 2.0f * quadSize : 0f;

		bool IsInRegion(Vector3 pos)
		{
			if (!affectedRegion.HasValue) return true;
			var region = affectedRegion.Value;
			float gridX = pos.X / quadSize + halfW / quadSize;
			float gridZ = pos.Z / quadSize + halfD / quadSize;
			int x = (int)Mathf.Round(gridX);
			int z = (int)Mathf.Round(gridZ);
			return x >= region.Position.X - 2 && x <= region.Position.X + region.Size.X + 2 &&
				   z >= region.Position.Y - 2 && z <= region.Position.Y + region.Size.Y + 2;
		}

		foreach (var unit in AllUnits)
		{
			if (GodotObject.IsInstanceValid(unit))
			{
				var pos = unit.GlobalPosition;
				if (!IsInRegion(pos)) continue;
				pos.Y = _editorService.GetTerrainHeightAt(pos);
				unit.GlobalPosition = pos;
				if (EcsWorld.IsAlive(unit.Entity))
				{
					EcsWorld.Set(unit.Entity, new Realm.Ecs.Components.Core.Position(new System.Numerics.Vector3(pos.X, pos.Y, pos.Z)));
				}
			}
		}

		foreach (var child in GetChildren())
		{
			if (child is Prop3D prop && GodotObject.IsInstanceValid(prop))
			{
				var pos = prop.GlobalPosition;
				if (!IsInRegion(pos)) continue;
				pos.Y = _editorService.GetTerrainHeightAt(pos);
				prop.GlobalPosition = pos;
			}
			else if (child is Decal decal && GodotObject.IsInstanceValid(decal))
			{
				var pos = decal.GlobalPosition;
				if (!IsInRegion(pos)) continue;
				pos.Y = _editorService.GetTerrainHeightAt(pos);
				decal.GlobalPosition = pos;
			}
		}
	}

	private void DeleteObjectAt(Node collider, Vector3 hitPos)
	{
		var unit = FindUnit3DInParentChain(collider);
		if (unit != null)
		{
			SelectedUnits.Remove(unit);
			AllUnits.Remove(unit);
			EntityToUnit3D.Remove(unit.Entity);
			if (EcsWorld.IsAlive(unit.Entity))
			{
				EcsWorld.Destroy(unit.Entity);
			}
			unit.QueueFree();
			return;
		}
		
		Node current = collider;
		while (current != null && current != this)
		{
			if (current is Prop3D prop)
			{
				AllProps.Remove(prop);
				EntityToProp3D.Remove(prop.Entity);
				if (EcsWorld.IsAlive(prop.Entity))
				{
					EcsWorld.Destroy(prop.Entity);
				}
				prop.QueueFree();
				return;
			}
			current = current.GetParent();
		}


		var decal = FindDecal3DInParentChain(collider);
		if (decal != null)
		{
			if (EcsWorld.IsAlive(decal.Entity))
			{
				EcsWorld.Destroy(decal.Entity);
			}
			decal.QueueFree();
		}
	}

		private Decal3D FindDecal3DInParentChain(Node node)
	{
		Node current = node;
		while (current != null && current != this)
		{
			if (current is Decal3D d) return d;
			current = current.GetParent();
		}
		return null;
	}

	private void ProcessMapEditorPhysics(float fDelta)
	{
		if (_simulationService == null || EcsWorld == null) return;

		_simulationService.TickEditorPhysics(fDelta);
		var query = Realm.Ecs.Common.QueryCache.AllPositionAndMoveToAndMovementStatsNoneDeadQuery;
		var arrivedUnits = _simulationService.GetEditorArrivedUnits();
		arrivedUnits.Clear();
		EcsWorld.Query(in query, _simulationService.EditorMovementQueryDelegate);

		foreach (var entity in arrivedUnits)
		{
			if (EcsWorld.IsAlive(entity) && EcsWorld.Has<MoveTo>(entity))
			{
				EcsWorld.Remove<MoveTo>(entity);
			}
		}
	}
	


	public Unit3D SpawnUnitExternal(string unitId, Vector3 position, bool isEnemy, float rotationY, float scale)
	{
		position.Y = _editorService.GetTerrainHeightAt(position);
		if (!UnitRegistry.ContainsKey(unitId))
		{
			string resolvedModelPath = unitId;
			if (!unitId.StartsWith("res://") && !System.IO.File.Exists(unitId))
			{
				string wsPath = Godot.ProjectSettings.GlobalizePath("user://temp_map_workspace");
				string filename = System.IO.Path.GetFileName(unitId);
				if (!filename.EndsWith(".glb") && !filename.EndsWith(".gltf")) filename += ".glb";
				string[] subDirs = new[] { "character", "building", "environment", "props" };
				foreach (var sub in subDirs)
				{
					string cand = System.IO.Path.Combine(wsPath, "Assets", "models", sub, filename);
					if (System.IO.File.Exists(cand))
					{
						resolvedModelPath = cand;
						break;
					}
				}
			}
			var dynamicMeta = new UnitMetadata
			{
				Name = System.IO.Path.GetFileNameWithoutExtension(unitId).Replace("_", " "),
				MaxHp = 100f,
				Damage = 10f,
				Range = 2f,
				Armor = 2f,
				Speed = (unitId.Contains("Buildings") || unitId.Contains("castle") || unitId.Contains("tower")) ? 0f : 6.0f,
				ProductionTime = 10f,
				ModelPath = resolvedModelPath
			};
			UnitRegistry[unitId] = dynamicMeta;
		}

		if (!UnitRegistry.TryGetValue(unitId, out var meta)) return null;

		var playerOwner = isEnemy ? _enemyPlayerEntity.AsPlayerEntity(EcsWorld) : _playerEntity.AsPlayerEntity(EcsWorld);
		
		string modelPath;
		if (!string.IsNullOrEmpty(meta.ModelPath) && (FileAccess.FileExists(meta.ModelPath) || System.IO.File.Exists(meta.ModelPath)))
		{
			modelPath = meta.ModelPath;
		}
		else
		{
			modelPath = GetFallbackModelPath(unitId, meta.Speed == 0f);
		}

		string name = meta.Name;
		var entity = CreateEcsUnit(unitId, name, meta.MaxHp, meta.Damage, meta.Range, meta.Armor, meta.Speed, position, playerOwner);

		var unit3D = SpawnUnit3D(entity, unitId, modelPath, position, meta.Speed == 0f, isEnemy);
		unit3D.RotationDegrees = new Vector3(0.0f, rotationY, 0.0f);
		unit3D.Scale = Vector3.One * scale;

		if (EcsWorld.Has<CollisionScale>(entity))
		{
			EcsWorld.Set(entity, new CollisionScale(scale));
		}
		else
		{
			EcsWorld.Add(entity, new CollisionScale(scale));
		}

		string unitAssetKey = GetModelAssetKey(unit3D);
		if (!string.IsNullOrEmpty(unitAssetKey))
		{
			float brightness = GetModelBrightness(unitAssetKey);
			Color tint = GetModelColorTint(unitAssetKey);
			bool generateNormals = GetModelGenerateNormals(unitAssetKey);
			if (MathF.Abs(brightness - 1.0f) > 0.001f || generateNormals || tint != new Color(1.0f, 1.0f, 1.0f))
			{
				ApplyMaterialOverridesToNode(unit3D, brightness, tint, generateNormals);
			}
		}

		return unit3D;
	}

	public Prop3D SpawnPropExternalWithParams(string propId, Vector3 position, float rotationY, float scale)
	{
		float defaultAmount = propId switch
		{
			"goldmine" => 2000f,
			"rock" => 1000f,
			"tree" => 500f,
			_ => 0f
		};

		var entity = EcsWorld.Create();
		EcsWorld.Add(entity, new PropIdentity(propId));
		if (defaultAmount > 0f)
		{
			EcsWorld.Add(entity, new ResourceNode(Guid.Empty, defaultAmount));
		}

		var prop = new Prop3D();
		prop.Entity = entity;
		prop.PropId = propId;
		AddChild(prop);
		AllProps.Add(prop);

		EntityToProp3D[entity] = prop;

		position.Y = _editorService.GetTerrainHeightAt(position);
		prop.Position = position;
		prop.RotationDegrees = new Vector3(0.0f, rotationY, 0.0f);
		prop.Scale = Vector3.One * scale;
		
		EcsWorld.Add(entity, new Realm.Ecs.Components.Tags.Prop());
		EcsWorld.Add(entity, new Realm.Ecs.Components.Core.Position(new System.Numerics.Vector3(position.X, position.Y, position.Z)));
		EcsWorld.Add(entity, new CollisionScale(scale));

		string propAssetKey = GetModelAssetKey(prop);
		if (!string.IsNullOrEmpty(propAssetKey))
		{
			float brightness = GetModelBrightness(propAssetKey);
			Color tint = GetModelColorTint(propAssetKey);
			bool generateNormals = GetModelGenerateNormals(propAssetKey);
			if (MathF.Abs(brightness - 1.0f) > 0.001f || generateNormals || tint != new Color(1.0f, 1.0f, 1.0f))
			{
				ApplyMaterialOverridesToNode(prop, brightness, tint, generateNormals);
			}
		}

		return prop;
	}

	public Decal SpawnDecalExternalWithParams(string decalId, Vector3 position, float rotationY, float scale)
	{
		var entity = EcsWorld.Create();
		var decal = new Decal3D();
		decal.Entity = entity;
		decal.DecalId = string.IsNullOrEmpty(decalId) ? "logo" : decalId;
		decal.TextureAlbedo = LoadDecalTexture(decalId);
		decal.Size = new Vector3(6.0f, 20.0f, 6.0f) * scale;
		decal.AlbedoMix = 1.0f;
		AddChild(decal);
		AllDecals.Add(decal);
		
		position.Y = _editorService.GetTerrainHeightAt(position);
		decal.Position = position;
		decal.RotationDegrees = new Vector3(0.0f, rotationY, 0.0f);
		decal.Scale = Vector3.One;
		
		EcsWorld.Add(entity, new Realm.Ecs.Components.Core.Position(new System.Numerics.Vector3(position.X, position.Y, position.Z)));
		EcsWorld.Add(entity, new RotationY(rotationY));
		EcsWorld.Add(entity, new ModelScale(scale));
		
		return decal;
	}

	public void DeleteNodeExternal(Node node)
	{
		if (node == null || !GodotObject.IsInstanceValid(node))
		{
			MapEditorHUD.Instance?.ShowFeedbackExternal("[Debug] DeleteNodeExternal: node is NULL or invalid");
			return;
		}

		var unit = (node as Unit3D) ?? FindUnit3DInParentChain(node);
		if (unit != null && GodotObject.IsInstanceValid(unit))
		{
			if (unit == _selectedEditorObject || FindUnit3DInParentChain(_selectedEditorObject) == unit)
			{
				SelectedEditorObject = null;
			}
			SelectedUnits.Remove(unit);
			AllUnits.Remove(unit);
			EntityToUnit3D.Remove(unit.Entity);
			if (EcsWorld.IsAlive(unit.Entity))
			{
				EcsWorld.Destroy(unit.Entity);
			}
			unit.QueueFree();
			return;
		}
		var prop = (node as Prop3D) ?? FindProp3DInParentChain(node);
		if (prop != null && GodotObject.IsInstanceValid(prop))
		{
			if (prop == _selectedEditorObject || FindProp3DInParentChain(_selectedEditorObject) == prop)
			{
				SelectedEditorObject = null;
			}
			AllProps.Remove(prop);
			EntityToProp3D.Remove(prop.Entity);
			if (EcsWorld.IsAlive(prop.Entity))
			{
				EcsWorld.Destroy(prop.Entity);
			}
			prop.QueueFree();
			return;
		}
		var decal = (node as Decal) ?? FindDecalInParentChain(node);
		if (decal != null && GodotObject.IsInstanceValid(decal))
		{
			if (decal == _selectedEditorObject || FindDecalInParentChain(_selectedEditorObject) == decal)
			{
				SelectedEditorObject = null;
			}
			if (decal is Decal3D decal3D && EcsWorld.IsAlive(decal3D.Entity))
			{
				EcsWorld.Destroy(decal3D.Entity);
			}
			decal.QueueFree();
			return;
		}

		if (_selectedEditorObject == node)
		{
			SelectedEditorObject = null;
		}
		node.QueueFree();
	}

	public IEditorAction DeleteObjectAtWithUndo(Node collider, Vector3 hitPos)
	{
		if (collider == null || !GodotObject.IsInstanceValid(collider))
		{
			MapEditorHUD.Instance?.ShowFeedbackExternal("[Debug] DeleteObjectAtWithUndo: collider is NULL or invalid");
			return null;
		}

		// 1. Direct Parent Chain Check
		var unit = (collider as Unit3D) ?? FindUnit3DInParentChain(collider);
		if (unit != null && GodotObject.IsInstanceValid(unit))
		{
			if (unit == _selectedEditorObject || FindUnit3DInParentChain(_selectedEditorObject) == unit) SelectedEditorObject = null;
			var action = new ObjectDeleteAction("unit", unit.UnitId, unit.Position, unit.RotationDegrees.Y, unit.Scale.X, unit.IsEnemy, unit);
			SelectedUnits.Remove(unit);
			AllUnits.Remove(unit);
			EntityToUnit3D.Remove(unit.Entity);
			if (EcsWorld.IsAlive(unit.Entity)) EcsWorld.Destroy(unit.Entity);
			unit.QueueFree();
			return action;
		}

		var prop = (collider as Prop3D) ?? FindProp3DInParentChain(collider);
		if (prop != null && GodotObject.IsInstanceValid(prop))
		{
			if (prop == _selectedEditorObject || FindProp3DInParentChain(_selectedEditorObject) == prop) SelectedEditorObject = null;
			var action = new ObjectDeleteAction("prop", prop.PropId, prop.Position, prop.RotationDegrees.Y, prop.Scale.X, false, prop);
			AllProps.Remove(prop);
			EntityToProp3D.Remove(prop.Entity);
			if (EcsWorld.IsAlive(prop.Entity)) EcsWorld.Destroy(prop.Entity);
			prop.QueueFree();
			return action;
		}

		var decal = (collider as Decal) ?? FindDecalInParentChain(collider);
		if (decal != null && GodotObject.IsInstanceValid(decal))
		{
			if (decal == _selectedEditorObject || FindDecalInParentChain(_selectedEditorObject) == decal) SelectedEditorObject = null;
			string decalId = decal is Decal3D d3d ? d3d.DecalId : "logo";
			var action = new ObjectDeleteAction("decal", decalId, decal.Position, decal.RotationDegrees.Y, decal.Scale.X, false, decal);
			if (decal is Decal3D d3 && EcsWorld.IsAlive(d3.Entity)) EcsWorld.Destroy(d3.Entity);
			decal.QueueFree();
			return action;
		}

		// 2. Proximity Search (if clicking terrain ground near object)
		Unit3D closestUnit = null;
		float closestUnitDist = 2.0f;
		foreach (var u in AllUnits)
		{
			if (GodotObject.IsInstanceValid(u))
			{
				float d = u.Position.DistanceTo(hitPos);
				if (d < closestUnitDist)
				{
					closestUnitDist = d;
					closestUnit = u;
				}
			}
		}

		Prop3D closestProp = null;
		float closestPropDist = 2.0f;
		foreach (var child in GetChildren())
		{
			if (child is Prop3D p && GodotObject.IsInstanceValid(p))
			{
				float d = p.Position.DistanceTo(hitPos);
				if (d < closestPropDist)
				{
					closestPropDist = d;
					closestProp = p;
				}
			}
		}

		Decal closestDecal = null;
		float closestDecalDist = 2.0f;
		foreach (var child in GetChildren())
		{
			if (child is Decal dec && GodotObject.IsInstanceValid(dec))
			{
				float d = dec.GlobalPosition.DistanceTo(hitPos);
				if (d < closestDecalDist)
				{
					closestDecalDist = d;
					closestDecal = dec;
				}
			}
		}

		float minDistance = Mathf.Min(closestUnitDist, Mathf.Min(closestPropDist, closestDecalDist));
		if (minDistance < 2.0f)
		{
			if (closestUnit != null && minDistance == closestUnitDist)
			{
				if (closestUnit == _selectedEditorObject) SelectedEditorObject = null;
				var action = new ObjectDeleteAction("unit", closestUnit.UnitId, closestUnit.Position, closestUnit.RotationDegrees.Y, closestUnit.Scale.X, closestUnit.IsEnemy, closestUnit);
				SelectedUnits.Remove(closestUnit);
				AllUnits.Remove(closestUnit);
				EntityToUnit3D.Remove(closestUnit.Entity);
				if (EcsWorld.IsAlive(closestUnit.Entity)) EcsWorld.Destroy(closestUnit.Entity);
				closestUnit.QueueFree();
				return action;
			}
			else if (closestProp != null && minDistance == closestPropDist)
			{
				if (closestProp == _selectedEditorObject) SelectedEditorObject = null;
				var action = new ObjectDeleteAction("prop", closestProp.PropId, closestProp.Position, closestProp.RotationDegrees.Y, closestProp.Scale.X, false, closestProp);
				AllProps.Remove(closestProp);
				EntityToProp3D.Remove(closestProp.Entity);
				if (EcsWorld.IsAlive(closestProp.Entity)) EcsWorld.Destroy(closestProp.Entity);
				closestProp.QueueFree();
				return action;
			}
			else if (closestDecal != null && minDistance == closestDecalDist)
			{
				if (closestDecal == _selectedEditorObject) SelectedEditorObject = null;
				string decalId = closestDecal is Decal3D d3d ? d3d.DecalId : "logo";
				var action = new ObjectDeleteAction("decal", decalId, closestDecal.Position, closestDecal.RotationDegrees.Y, closestDecal.Scale.X, false, closestDecal);
				if (closestDecal is Decal3D d3 && EcsWorld.IsAlive(d3.Entity)) EcsWorld.Destroy(d3.Entity);
				closestDecal.QueueFree();
				return action;
			}
		}

		MapEditorHUD.Instance?.ShowFeedbackExternal("[Debug] DeleteObjectAtWithUndo returned NULL (no direct or proximity match)");
		return null;
	}

	public void AlignAllEntitiesToTerrainExternal()
	{
		AlignAllEntitiesToTerrain();
	}

	private void UpdateEditorPreview(Vector3 position)
	{
		bool needsPreview = ActiveEditorTool == EditorTool.PlaceUnit ||
							ActiveEditorTool == EditorTool.PlaceProp ||
							ActiveEditorTool == EditorTool.PlaceDecal;

		if (!needsPreview)
		{
			ClearEditorPreview();
			return;
		}

		string reqType = ActiveEditorTool.ToString();
		string reqId = ActivePlaceId;
		bool reqIsEnemy = PlaceUnitIsEnemy;

		if (_editorPreviewNode == null || !GodotObject.IsInstanceValid(_editorPreviewNode) || _editorPreviewType != reqType || _editorPreviewId != reqId || _editorPreviewIsEnemy != reqIsEnemy)
		{
			ClearEditorPreview();
			
			_editorPreviewType = reqType;
			_editorPreviewId = reqId;
			_editorPreviewIsEnemy = reqIsEnemy;

			if (ActiveEditorTool == EditorTool.PlaceUnit)
			{
				if (!UnitRegistry.ContainsKey(reqId))
				{
					string resolvedModelPath = reqId;
					if (!reqId.StartsWith("res://") && !System.IO.File.Exists(reqId))
					{
						resolvedModelPath = (reqId.Contains("Buildings") || reqId.Contains("castle") || reqId.Contains("tower"))
							? $"res://Assets/3d/Buildings/{reqId}"
							: $"res://Assets/3d/Characters/{reqId}";
					}
					var dynamicMeta = new UnitMetadata
					{
						Name = System.IO.Path.GetFileNameWithoutExtension(reqId).Replace("_", " "),
						MaxHp = 100f,
						Damage = 10f,
						Range = 2f,
						Armor = 2f,
						Speed = (reqId.Contains("Buildings") || reqId.Contains("castle") || reqId.Contains("tower")) ? 0f : 6.0f,
						ProductionTime = 10f,
						ModelPath = resolvedModelPath
					};
					UnitRegistry[reqId] = dynamicMeta;
				}

				if (UnitRegistry.TryGetValue(reqId, out var meta))
				{
 					string modelPath = !string.IsNullOrEmpty(meta.ModelPath) ? meta.ModelPath : GetFallbackModelPath(reqId, meta.Speed == 0f);

					var previewUnit = new Unit3D();
					previewUnit.UnitId = reqId;
					previewUnit.IsBuilding = meta.Speed == 0f;
					previewUnit.IsEnemy = reqIsEnemy;
					previewUnit.IsPreview = true;
					AddChild(previewUnit);
					previewUnit.LoadModel(modelPath);

					Color color = reqIsEnemy ? new Color(1.0f, 0.3f, 0.15f) : new Color(0.15f, 0.65f, 1.0f);
					MakeHologramRecursive(previewUnit, color);
					_editorPreviewNode = previewUnit;
				}
			}
			else if (ActiveEditorTool == EditorTool.PlaceProp)
			{
				var previewProp = new Prop3D();
				previewProp.PropId = reqId;
				previewProp.IsPreview = true;
				AddChild(previewProp);

				Color color = new Color(0.95f, 0.82f, 0.15f);
				MakeHologramRecursive(previewProp, color);
				_editorPreviewNode = previewProp;
			}
			else if (ActiveEditorTool == EditorTool.PlaceDecal)
			{
				var previewDecal = new Decal3D();
				previewDecal.TextureAlbedo = LoadDecalTexture(reqId);
				previewDecal.Size = new Vector3(6.0f, 20.0f, 6.0f) * EditorPlacementScale;
				AddChild(previewDecal);
				previewDecal.DecalId = string.IsNullOrEmpty(reqId) ? "logo" : reqId;

				Color color = new Color(1.0f, 1.0f, 1.0f);
				var mat = new StandardMaterial3D();
				mat.AlbedoColor = new Color(color.R, color.G, color.B, 0.4f);
				mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
				previewDecal.AlbedoMix = 0.5f;
				_editorPreviewNode = previewDecal;
			}
		}

		if (_editorPreviewNode != null && GodotObject.IsInstanceValid(_editorPreviewNode))
		{
			if (EditorClumpMode || ActiveEditorTool == EditorTool.PlacePropClump)
			{
				_editorPreviewNode.Visible = false;
				return;
			}

			if (!_editorService.HasCachedRandom) _editorService.GenerateNewRandomPlacementRotationAndScale();
			float previewRot = (EditorRandomRotation && !_editorService.IsPastingObject) ? _editorService.CachedRandomRotation : EditorPlacementRotation;
			float previewScaleVal = (EditorRandomScale && !_editorService.IsPastingObject) ? _editorService.CachedRandomScale : EditorPlacementScale;

			Vector3 previewPos = position;
			if (EditorSnapToGrid && GroundTerrain != null)
			{
				previewPos = _editorService.SnapToGrid(previewPos);
			}
			previewPos.Y = _editorService.GetTerrainHeightAt(previewPos);
			if (ActiveEditorTool == EditorTool.PlaceUnit || ActiveEditorTool == EditorTool.PlaceProp)
			{
				float radius = GetPlacementRadius(ActivePlaceId, previewScaleVal);
				var finalPos = FindNearestFreePosition(previewPos, radius);
				if (finalPos != null)
				{
					previewPos = finalPos.Value;
				}
			}
			_editorPreviewNode.Position = previewPos;
			_editorPreviewNode.RotationDegrees = new Vector3(0.0f, previewRot, 0.0f);
			if (_editorPreviewNode is Decal previewDecal)
			{
				previewDecal.Size = new Vector3(6.0f, 20.0f, 6.0f) * previewScaleVal;
				previewDecal.Scale = Vector3.One;
			}
			else
			{
				_editorPreviewNode.Scale = Vector3.One * previewScaleVal;
			}
			_editorPreviewNode.Visible = true;
		}
	}

	private void ClearEditorPreview()
	{
		if (_editorPreviewNode != null && GodotObject.IsInstanceValid(_editorPreviewNode))
		{
			_editorPreviewNode.QueueFree();
		}
		_editorPreviewNode = null;
		_editorPreviewType = "";
		_editorPreviewId = "";
		_editorPreviewIsEnemy = false;
	}

	private void MakeHologramRecursive(Node node, Color color)
	{
		if (node is MeshInstance3D meshInstance)
		{
			var mat = new StandardMaterial3D();
			mat.AlbedoColor = new Color(color.R, color.G, color.B, 0.4f);
			mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
			mat.EmissionEnabled = true;
			mat.Emission = new Color(color.R, color.G, color.B) * 0.5f;
			meshInstance.MaterialOverride = mat;
		}
		foreach (var child in node.GetChildren())
		{
			MakeHologramRecursive(child, color);
		}
	}

	public void SetUnitTeamExternal(Unit3D unit, bool isEnemy)
	{
		if (GodotObject.IsInstanceValid(unit) && EcsWorld.IsAlive(unit.Entity))
		{
			var playerOwner = isEnemy ? _enemyPlayerEntity.AsPlayerEntity(EcsWorld) : _playerEntity.AsPlayerEntity(EcsWorld);
			EcsWorld.Set(unit.Entity, new Owner(playerOwner));
			if (UnitRegistry.TryGetValue(unit.UnitId, out var meta))
			{
				string name = meta.Name;
				if (isEnemy)
				{
					if (unit.UnitId == "worker") name = "Orc Worker";
					else if (unit.UnitId == "soldier") name = "Orc Raider";
					else if (unit.UnitId == "archer") name = "Dark Archer";
					else if (unit.UnitId == "priest") name = "Orc Shaman";
					else if (unit.UnitId == "castle") name = "Orc Stronghold";
					else if (unit.UnitId == "tower") name = "Orc Totem Tower";
				}
				EcsWorld.Set(unit.Entity, new Name(name));
			}
			unit.IsEnemy = isEnemy;
			if (unit.UnitId == "priest")
			{
				Color priestColor = isEnemy ? new Color(0.8f, 0.2f, 0.8f) : new Color(1.0f, 0.85f, 0.2f);
				unit.ApplyModelTint(priestColor);
			}
			else if (unit.UnitId == "worker")
			{
				Color workerColor = isEnemy ? new Color(0.6f, 0.4f, 0.2f) : new Color(0.8f, 0.6f, 0.4f);
				unit.ApplyModelTint(workerColor);
			}
			unit.IsSelected = unit.IsSelected;
		}
	}

	private void UpdateDecalSelectionRing(Decal decal, bool selected)
	{
		if (!GodotObject.IsInstanceValid(decal)) return;
		var existing = decal.GetNodeOrNull<MeshInstance3D>("EditorSelectionRing");
		if (existing != null)
		{
			existing.QueueFree();
		}
		if (selected)
		{
			var ring = new MeshInstance3D();
			ring.Name = "EditorSelectionRing";
			var torusMesh = new TorusMesh();
			torusMesh.InnerRadius = 2.5f;
			torusMesh.OuterRadius = 2.8f;
			ring.Mesh = torusMesh;
			ring.Position = new Vector3(0, 0.05f, 0);
			var material = new StandardMaterial3D();
			material.AlbedoColor = new Color(0.1f, 0.7f, 0.95f);
			material.EmissionEnabled = true;
			material.Emission = new Color(0.1f, 0.7f, 0.95f);
			material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
			ring.MaterialOverride = material;
			decal.AddChild(ring);
		}
	}

	private void UpdateDecalHoverRing(Decal decal, bool hovered)
	{
		if (!GodotObject.IsInstanceValid(decal)) return;
		var existing = decal.GetNodeOrNull<MeshInstance3D>("EditorHoverRing");
		if (existing != null)
		{
			existing.QueueFree();
		}
		if (hovered && SelectedEditorObject != decal)
		{
			var ring = new MeshInstance3D();
			ring.Name = "EditorHoverRing";
			var torusMesh = new TorusMesh();
			torusMesh.InnerRadius = 2.5f;
			torusMesh.OuterRadius = 2.8f;
			ring.Mesh = torusMesh;
			ring.Position = new Vector3(0, 0.05f, 0);
			var material = new StandardMaterial3D();
			material.AlbedoColor = new Color(1.0f, 1.0f, 1.0f, 0.4f);
			material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
			material.EmissionEnabled = true;
			material.Emission = new Color(1.0f, 1.0f, 1.0f) * 0.3f;
			material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
			ring.MaterialOverride = material;
			decal.AddChild(ring);
		}
	}

	private void ProcessMapEditorTick(float fDelta)
	{
		_editorService.TickClumpCooldown(fDelta);

		var mousePos = GetViewport().GetMousePosition();
		var terrainHit = RaycastTerrainFromMouse(mousePos);
		Vector3 hitPos = Vector3.Zero;
		bool hasHit = false;
		if (terrainHit != null && terrainHit.ContainsKey("position"))
		{
			hitPos = terrainHit["position"].AsVector3();
			hasHit = true;
		}
		else
		{
			var camera = GetViewport().GetCamera3D();
			if (camera != null)
			{
				var from = camera.ProjectRayOrigin(mousePos);
				var normal = camera.ProjectRayNormal(mousePos);
				if (Mathf.Abs(normal.Y) > 0.0001f)
				{
					float t = (0.0f - from.Y) / normal.Y;
					hitPos = from + normal * t;
					hasHit = true;
				}
			}
		}
		if (hasHit)
		{
			UpdateBrushIndicator(hitPos);
			UpdateEditorPreview(hitPos);
			if (GroundTerrain != null)
			{
				if (ActiveEditorTool == EditorTool.SelectArea && _editorService.IsSelectingArea && _editorService.SelectionStart != null)
				{
					var (cx, cz) = _editorService.WorldPosToCellCoords(hitPos);
					_editorService.SetSelectionEnd(new Vector2I(cx, cz));
					int minX = Mathf.Min(_editorService.SelectionStart.Value.X, cx);
					int minZ = Mathf.Min(_editorService.SelectionStart.Value.Y, cz);
					int maxX = Mathf.Max(_editorService.SelectionStart.Value.X, cx);
					int maxZ = Mathf.Max(_editorService.SelectionStart.Value.Y, cz);
					CreateSelectionHighlight();
					RebuildSelectionHighlightMesh(minX, minZ, maxX, maxZ);
				}
				else if (ActiveEditorTool == EditorTool.DrawCoordinate && _editorService.IsSelectingArea && _editorService.SelectionStart != null)
				{
					var (rcx, rcz) = _editorService.WorldPosToCellCoords(hitPos);
					_editorService.SetSelectionEnd(new Vector2I(rcx, rcz));
					int rMinX = Mathf.Min(_editorService.SelectionStart.Value.X, rcx);
					int rMinZ = Mathf.Min(_editorService.SelectionStart.Value.Y, rcz);
					int rMaxX = Mathf.Max(_editorService.SelectionStart.Value.X, rcx);
					int rMaxZ = Mathf.Max(_editorService.SelectionStart.Value.Y, rcz);
					UpdateCoordinatePreviewMesh(rMinX, rMinZ, rMaxX, rMaxZ);
				}
				else if (ActiveEditorTool == EditorTool.PasteArea && _editorService.HasCopiedArea)
				{
					var (cx, cz) = _editorService.WorldPosToCellCoords(hitPos);
					float r = EditorPasteRotation % 360.0f;
					if (r < 0) r += 360.0f;
					int rotSteps = (int)Math.Round(r / 90.0f) % 4;

					int pasteWidth = _editorService.CopiedAreaWidth;
					int pasteDepth = _editorService.CopiedAreaDepth;

					int targetWidth = (rotSteps == 1 || rotSteps == 3) ? pasteDepth : pasteWidth;
					int targetDepth = (rotSteps == 1 || rotSteps == 3) ? pasteWidth : pasteDepth;

					int dX = 0;
					int dZ = 0;
					if (rotSteps == 1 || rotSteps == 3)
					{
						dX = (pasteWidth - pasteDepth) / 2;
						dZ = (pasteDepth - pasteWidth) / 2;
					}

					int minX = Mathf.Clamp(cx + dX, 0, GroundTerrain.Width - 1);
					int minZ = Mathf.Clamp(cz + dZ, 0, GroundTerrain.Depth - 1);
					int maxX = Mathf.Clamp(cx + dX + targetWidth - 1, 0, GroundTerrain.Width - 1);
					int maxZ = Mathf.Clamp(cz + dZ + targetDepth - 1, 0, GroundTerrain.Depth - 1);

					CreateSelectionHighlight();
					RebuildSelectionHighlightMesh(minX, minZ, maxX, maxZ);
				}
			}
			
			bool canHover = (ActiveEditorTool == EditorTool.SelectMove && !_isDraggingObject) ||
							ActiveEditorTool == EditorTool.DeleteObject ||
							ActiveEditorTool == EditorTool.Eyedropper;
			Node newHovered = null;
			if (canHover && !IsMouseOverUI())
			{
				var objectHit = RaycastFromMouse(mousePos);
				var collider = (objectHit != null && objectHit.ContainsKey("collider")) ? objectHit["collider"].As<Node>() : null;
				if (collider != null)
				{
					newHovered = FindUnit3DInParentChain(collider);
					if (newHovered == null)
					{
						newHovered = FindProp3DInParentChain(collider);
					}
				}
				if (newHovered == null)
				{
					newHovered = FindDecal3DInParentChain(collider);
				}
			}

			if (_hoveredEditorObject != newHovered)
			{
				if (GodotObject.IsInstanceValid(_hoveredEditorObject))
				{
					if (_hoveredEditorObject is Unit3D u) u.IsHovered = false;
					else if (_hoveredEditorObject is Prop3D p) p.IsHovered = false;
					else if (_hoveredEditorObject is Decal d) UpdateDecalHoverRing(d, false);
				}
				_hoveredEditorObject = newHovered;
				if (GodotObject.IsInstanceValid(_hoveredEditorObject))
				{
					if (_hoveredEditorObject is Unit3D u) u.IsHovered = true;
					else if (_hoveredEditorObject is Prop3D p) p.IsHovered = true;
					else if (_hoveredEditorObject is Decal d) UpdateDecalHoverRing(d, true);
				}
			}

			if (Input.IsMouseButtonPressed(MouseButton.Left) && !_leftClickInitiatedOverUI && !IsMouseOverUI())
			{
				if ((ActiveEditorTool == EditorTool.PlaceUnit || ActiveEditorTool == EditorTool.PlaceProp || ActiveEditorTool == EditorTool.PlaceDecal) && EditorClumpMode)
				{
					if (!_editorService.IsDrawingClump)
					{
						_editorService.BeginClumpSession();
					}
					if (_editorService.CanSpawnClump())
					{
						ApplyGeneralClumpSpawn(hitPos);
						_editorService.SetClumpCooldown(0.15f);
					}
				}

				if (ActiveEditorTool == EditorTool.SelectMove && _isDraggingObject && GodotObject.IsInstanceValid(SelectedEditorObject))
				{
					if (!_dragObjectHasMoved && hitPos.DistanceTo(_dragObjectStartHitPos) > 0.3f)
					{
						_dragObjectHasMoved = true;
					}

					if (_dragObjectHasMoved)
					{
						var node3D = SelectedEditorObject as Node3D;
						var dragPos = hitPos - (_dragObjectStartHitPos - _dragObjectStartPos);
						if (EditorSnapToGrid && GroundTerrain != null)
						{
							dragPos = _editorService.SnapToGrid(dragPos);
						}
						dragPos.Y = _editorService.GetTerrainHeightAt(dragPos);
						node3D.Position = dragPos;
						if (SelectedEditorObject is Unit3D unit && EcsWorld.IsAlive(unit.Entity))
						{
							EcsWorld.Set(unit.Entity, new Position(new System.Numerics.Vector3(dragPos.X, dragPos.Y, dragPos.Z)));
						}
						MapEditorHUD.Instance?.UpdateSelectedObjectInfo();
					}
				}

				bool isTerrainTool = ActiveEditorTool == EditorTool.Raise ||
									 ActiveEditorTool == EditorTool.Lower ||
									 ActiveEditorTool == EditorTool.Smooth ||
									 ActiveEditorTool == EditorTool.Plateau ||
									 ActiveEditorTool == EditorTool.PaintTexture ||
									 ActiveEditorTool == EditorTool.Noise ||
									 ActiveEditorTool == EditorTool.PaintPathing;

				bool firstClick = false;
				if (isTerrainTool && !_editorService.IsDrawingTerrain && GroundTerrain != null)
				{
					firstClick = true;
					_editorService.BeginTerrainDraw(
						hitPos,
						ActiveEditorTool,
						EditorBlockMode,
						EditorBlockLevelHeight,
						GroundTerrain.Heights,
						GroundTerrain.SplatMap,
						GroundTerrain.PathingCodes,
						GroundTerrain.CliffSplatMap);
				}


				ApplyContinuousTerrainEditing(hitPos, fDelta, firstClick);
				if (EditorMirrorMode != MirrorMode.None)
				{
					foreach (var t in GetMirroredTransforms(hitPos, 0.0f))
					{
						ApplyContinuousTerrainEditing(t.Position, fDelta, firstClick);
					}
				}
			}
			else
			{
				if (_editorService.IsDrawingClump)
				{
					var composite = _editorService.EndClumpSession();
					if (composite != null)
					{
						EditorHistoryManager.RecordAction(composite);
						EditorHasUnsavedChanges = true;
					}
				}

				_editorService.ResetDrawState();

				if (_isDraggingObject)
				{
					_isDraggingObject = false;
					if (GodotObject.IsInstanceValid(SelectedEditorObject))
					{
						var node3D = SelectedEditorObject as Node3D;
						bool isUnit = SelectedEditorObject is Unit3D;
						bool isEnemy = isUnit ? (SelectedEditorObject as Unit3D).IsEnemy : false;
						if (node3D.Position.DistanceTo(_dragObjectStartPos) > 0.05f)
						{
							var action = new ObjectTransformAction(
								node3D,
								_dragObjectStartPos, node3D.Position,
								_dragObjectStartRot, node3D.RotationDegrees,
								_dragObjectStartScale, node3D.Scale,
								_dragObjectStartIsEnemy, isEnemy
							);
							EditorHistoryManager.RecordAction(action);
							MapEditorHUD.Instance?.ShowFeedbackExternal("Moved Object");
							EditorHasUnsavedChanges = true;
						}
					}
				}
				if (_editorService.IsSelectingArea)
				{
					_editorService.SetIsSelectingArea(false);
				}
				if (_editorService.IsDrawingTerrain)
				{
					if (GroundTerrain != null && GroundTerrain.Heights != null && GroundTerrain.SplatMap != null && GroundTerrain.PathingCodes != null)
					{
						var action = _editorService.EndTerrainDraw(
							GroundTerrain.Heights,
							GroundTerrain.SplatMap,
							GroundTerrain.PathingCodes,
							GroundTerrain.CliffSplatMap);

						EditorHistoryManager.RecordAction(action);
						bool isHeightsTool = ActiveEditorTool == EditorTool.Raise ||
											 ActiveEditorTool == EditorTool.Lower ||
											 ActiveEditorTool == EditorTool.Smooth ||
											 ActiveEditorTool == EditorTool.Plateau ||
											 ActiveEditorTool == EditorTool.Noise ||
											 ActiveEditorTool == EditorTool.PaintPathing;
						if (isHeightsTool)
						{
							GroundTerrain.UpdatePhysics();
							RebuildGridOverlayMeshExternal();
							UpdatePathingOverlay();
						}
						EditorHasUnsavedChanges = true;
					}
					else
					{
						_editorService.EndTerrainDraw(null, null, null, null);
					}
				}
			}
		}
		else
		{
			if (_brushIndicatorMesh != null)
				_brushIndicatorMesh.Visible = false;
			ClearEditorPreview();
			if (_isDraggingObject)
			{
				_isDraggingObject = false;
				if (GodotObject.IsInstanceValid(SelectedEditorObject))
				{
					var node3D = SelectedEditorObject as Node3D;
					bool isUnit = SelectedEditorObject is Unit3D;
					bool isEnemy = isUnit ? (SelectedEditorObject as Unit3D).IsEnemy : false;
					if (node3D.Position.DistanceTo(_dragObjectStartPos) > 0.05f)
					{
						var action = new ObjectTransformAction(
							node3D,
							_dragObjectStartPos, node3D.Position,
							_dragObjectStartRot, node3D.RotationDegrees,
							_dragObjectStartScale, node3D.Scale,
							_dragObjectStartIsEnemy, isEnemy
						);
						EditorHistoryManager.RecordAction(action);
						EditorHasUnsavedChanges = true;
					}
				}
			}
			if (_editorService.IsDrawingClump)
			{
				var composite = _editorService.EndClumpSession();
				if (composite != null)
				{
					EditorHistoryManager.RecordAction(composite);
					EditorHasUnsavedChanges = true;
				}
			}
			if (_editorService.IsSelectingArea)
			{
				_editorService.SetIsSelectingArea(false);
			}
			if (_editorService.IsDrawingTerrain)
			{
				if (GroundTerrain != null && GroundTerrain.Heights != null && GroundTerrain.SplatMap != null && GroundTerrain.PathingCodes != null)
				{
					var action = _editorService.EndTerrainDraw(
						GroundTerrain.Heights,
						GroundTerrain.SplatMap,
						GroundTerrain.PathingCodes,
						GroundTerrain.CliffSplatMap);

					EditorHistoryManager.RecordAction(action);
					bool isHeightsTool = ActiveEditorTool == EditorTool.Raise ||
										 ActiveEditorTool == EditorTool.Lower ||
										 ActiveEditorTool == EditorTool.Smooth ||
										 ActiveEditorTool == EditorTool.Plateau ||
										 ActiveEditorTool == EditorTool.Noise ||
										 ActiveEditorTool == EditorTool.PaintPathing;
					if (isHeightsTool)
					{
							GroundTerrain.UpdatePhysics();
						RebuildGridOverlayMeshExternal();
						UpdatePathingOverlay();
					}

						// Final flush: guarantee the (throttled) mesh rebuilds caught up with the whole stroke.
						if (_terrainGeometryDirty)
						{
							GroundTerrain.UpdateMeshAndPhysics(false, false, _terrainFlushRegion ?? new Rect2I(0, 0, GroundTerrain.Width, GroundTerrain.Depth));
							_terrainGeometryDirty = false;
							_terrainFlushRegion = null;
							_lastTerrainMeshRebuildMs = long.MinValue;
						}
						EditorHasUnsavedChanges = true;
				}
				else
				{
					_editorService.EndTerrainDraw(null, null, null, null);
				}
			}
		}
		
		ProcessMapEditorPhysics(fDelta);
	}

	public void StartMapEditorMode()
	{
		IsMapEditorMode = true;
		ActiveEditorTool = EditorTool.None;
		EditorHistoryManager.Clear();
		
		if (!MapEditorHUD.ReturningFromTest)
		{
			string wsPath = Godot.ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
			System.IO.Directory.CreateDirectory(wsPath);
			MapWorkspaceService.SetupWorkspace(wsPath, "CustomMap");
		}

		if (MapEditorHUD.ReturningFromTest)
		{
			LoadMapFromFile(MapEditorHUD.TempWorkspaceGodotPath + "/terrain.json");

			EditorGridMode = MapEditorHUD.SavedGridMode;
			EditorCameraBoundsVisible = MapEditorHUD.SavedCameraBoundsVisible;

			var camera = MainCamera as CameraControl;
			if (camera != null)
			{
				camera.Position = MapEditorHUD.SavedCameraPosition;
				if (EcsWorld != null && EcsWorld.IsAlive(WorldEntity) && EcsWorld.Has<CameraState>(WorldEntity))
				{
					ref var state = ref EcsWorld.Get<CameraState>(WorldEntity);
					state.TargetHeight = MapEditorHUD.SavedTargetHeight;
					state.CurrentHeight = MapEditorHUD.SavedTargetHeight;
					state.TargetYaw = MapEditorHUD.SavedTargetYaw;
					state.CurrentYaw = MapEditorHUD.SavedTargetYaw;
					state.TargetPitch = MapEditorHUD.SavedTargetPitch;
					state.CurrentPitch = MapEditorHUD.SavedTargetPitch;
					state.IsTopDown = MapEditorHUD.SavedIsTopDown;
					state.YawSwing = MapEditorHUD.SavedYawSwing;
					state.PitchSwing = MapEditorHUD.SavedPitchSwing;
				}
			}
		}
		else
		{
			ClearMapEntirely();
		}
		
		CreateBrushIndicator();
		UpdateGridOverlayVisibility();
		InitializeCameraBoundsOverlay();
		UpdateDayNightVisuals(0.5f);
	}

	public void ExitMapEditorMode()
	{
		IsMapEditorMode = false;
		ActiveEditorTool = EditorTool.None;
		EditorHistoryManager.Clear();
		ClearEditorPreview();
		
		if (_brushIndicatorMesh != null)
		{
			_brushIndicatorMesh.QueueFree();
			_brushIndicatorMesh = null;
		}

		if (GroundTerrain != null)
		{
			GroundTerrain.SetGridVisible(false);
			GroundTerrain.SetPathingVisible(false);
		}

		if (_cameraBoundsOverlayMesh != null)
		{
			_cameraBoundsOverlayMesh.QueueFree();
			_cameraBoundsOverlayMesh = null;
		}
		
		var groundNode = GetNodeOrNull("Ground");
		if (groundNode != null)
		{
			groundNode.QueueFree();
			RemoveChild(groundNode);
		}
		
		CreateGround();
	}

	private void ClearAllUnits()
	{
		SelectedUnits.Clear();
		foreach (var unit in AllUnits)
		{
			if (GodotObject.IsInstanceValid(unit))
			{
				unit.QueueFree();
			}
		}
		AllUnits.Clear();
		_castlesList.Clear();
		CurrentPopulation = 0;
		MaxPopulation = 0;

		foreach (var child in GetChildren())
		{
			if (child is Prop3D prop)
			{
				prop.QueueFree();
			}
			else if (child is Decal decal)
			{
				decal.QueueFree();
			}
		}
		AllProps.Clear();
		AllDecals.Clear();
		EntityToUnit3D.Clear();
		EntityToProp3D.Clear();
		
		ReinitializeEcsAndServices();
		
		_playerEntity = EcsWorld.Create();
		EcsWorld.Add(_playerEntity, new Player());
		EcsWorld.Add(_playerEntity, new Name("Horaid_Topa"));
		InitializePlayerResources(_playerEntity);
		SetupPlayerEntityComponents(_playerEntity);

		_enemyPlayerEntity = EcsWorld.Create();
		EcsWorld.Add(_enemyPlayerEntity, new Player());
		EcsWorld.Add(_enemyPlayerEntity, new Name("Enemy_AI"));
		InitializePlayerResources(_enemyPlayerEntity);
		SetupPlayerEntityComponents(_enemyPlayerEntity);
	}

	private void CreateBrushIndicator()
	{
		if (_brushIndicatorMesh != null) return;
		
		_brushIndicatorMesh = new MeshInstance3D();
		_brushIndicatorMesh.Name = "BrushIndicator";
		
		var mat = new StandardMaterial3D();
		mat.AlbedoColor = new Color(0.15f, 0.65f, 1.0f, 0.3f);
		mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		mat.EmissionEnabled = true;
		mat.Emission = new Color(0.15f, 0.65f, 1.0f) * 0.5f;
		mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
		_brushIndicatorMesh.MaterialOverride = mat;
		
		AddChild(_brushIndicatorMesh);
		_brushIndicatorMesh.Visible = false;

		UpdateBrushMesh();
	}

	public void UpdateBrushMesh()
	{
		if (_brushIndicatorMesh == null) return;
		if (EditorBrushIsSquare)
		{
			var plane = new PlaneMesh();
			plane.Size = new Vector2(EditableTerrain.DefaultQuadSize, EditableTerrain.DefaultQuadSize);
			_brushIndicatorMesh.Mesh = plane;
		}
		else
		{
			var torus = new TorusMesh();
			torus.InnerRadius = 0.95f;
			torus.OuterRadius = 1.05f;
			_brushIndicatorMesh.Mesh = torus;
		}
	}

	private void UpdateBrushIndicator(Vector3 position)
	{
		if (_brushIndicatorMesh == null) return;
		
		_brushIndicatorMesh.Position = new Vector3(position.X, position.Y + 0.1f, position.Z);
		_brushIndicatorMesh.Scale = new Vector3(EditorBrushRadius, 0.1f, EditorBrushRadius);
		
		bool isTerrainTool = ActiveEditorTool == EditorTool.Raise ||
							 ActiveEditorTool == EditorTool.Lower ||
							 ActiveEditorTool == EditorTool.Smooth ||
							 ActiveEditorTool == EditorTool.Plateau ||
							 ActiveEditorTool == EditorTool.PaintTexture ||
							 ActiveEditorTool == EditorTool.Noise ||
							 ActiveEditorTool == EditorTool.Ramp ||
							 ActiveEditorTool == EditorTool.PlacePropClump ||
							 ActiveEditorTool == EditorTool.PaintPathing ||
							 ((ActiveEditorTool == EditorTool.PlaceUnit || ActiveEditorTool == EditorTool.PlaceProp || ActiveEditorTool == EditorTool.PlaceDecal) && EditorClumpMode);
							 
		_brushIndicatorMesh.Visible = isTerrainTool;
	}

	public MeshInstance3D BrushIndicatorMesh => _brushIndicatorMesh;
	public MeshInstance3D? GridOverlayMesh => null;
	public MeshInstance3D? PathingOverlayMesh => null;

	public void ClearRampStartPosExternal()
	{
		_editorService.SetRampStartPos(null);
	}

	public struct MirroredTransform
	{
		public Vector3 Position;
		public float Rotation;
	}

	public List<MirroredTransform> GetMirroredTransforms(Vector3 pos, float rotation)
	{
		return _editorService.GetMirroredTransforms(pos, rotation, EditorMirrorMode);
	}

	private Node3D FindObjectNearPosition(Vector3 position, float searchRadius = 1.5f)
	{
		foreach (var child in GetChildren())
		{
			if (child is Node3D n3d && GodotObject.IsInstanceValid(n3d))
			{
				if (n3d is Unit3D || n3d is Prop3D || n3d is Decal)
				{
					float dist = new Vector2(n3d.GlobalPosition.X - position.X, n3d.GlobalPosition.Z - position.Z).Length();
					if (dist <= searchRadius)
					{
						return n3d;
					}
				}
			}
		}
		return null;
	}

	private bool ApplyRampInternal(Vector3 start, Vector3 end)
	{
		return _editorService.ApplyRamp(start, end, EditorBrushRadius, EditorBlockMode, EditorBlockLevelHeight);
	}

	private float GetMinHeightInBrushBounds(Vector3 worldPos)
	{
		return _editorService.GetMinHeightInBrushBounds(worldPos, EditorBrushRadius, EditorBrushIsSquare);
	}

	private void ApplyGeneralClumpSpawn(Vector3 centerPos)
	{
		float autoDetectedRadius = GetOrCalculateObstacleRadius(ActivePlaceId, _editorPreviewNode);
		string assetKey = GetModelAssetKey(_editorPreviewNode ?? (object)ActivePlaceId);
		float ratio = GetModelCollisionCircleRatio(assetKey);
		float assetBaseCollisionRadius = Mathf.Max(0.1f, autoDetectedRadius * ratio);

		var requests = _editorService.BuildClumpSpawnRequests(
			centerPos,
			ActiveEditorTool,
			ActivePlaceId,
			PlaceUnitIsEnemy,
			EditorPlacementScale,
			EditorClumpCount,
			EditorClumpScale,
			EditorBrushRadius,
			EditorBrushIsSquare,
			EditorRandomRotation,
			EditorRandomScale,
			EditorPlacementRotation,
			EditorMirrorMode,
			assetBaseCollisionRadius);

		foreach (var req in requests)
		{
			Node spawnedNode = null;
			if (req.Type == "unit")
				spawnedNode = SpawnUnitExternal(req.Id, req.Position, req.IsEnemy, req.Rotation, req.Scale);
			else if (req.Type == "prop")
				spawnedNode = SpawnPropExternalWithParams(req.Id, req.Position, req.Rotation, req.Scale);
			else if (req.Type == "decal")
				spawnedNode = SpawnDecalExternalWithParams(req.Id, req.Position, req.Rotation, req.Scale);

			if (spawnedNode != null)
			{
				_editorService.RecordClumpSpawnAction(new ObjectSpawnAction(req.Type, req.Id, req.Position, req.Rotation, req.Scale, req.IsEnemy, spawnedNode));
			}
		}
	}

	public void SwapTexturesExternal(int indexA, int indexB)
	{
		if (GroundTerrain == null || GroundTerrain.SplatMap == null) return;
		if (indexA == indexB) return;

		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;

		float[,] heightsBefore = (float[,])GroundTerrain.Heights.Clone();
		TerrainSplatWeights[,] splatBefore = (TerrainSplatWeights[,])GroundTerrain.SplatMap.Clone();

		bool anyChanged = false;

		for (int z = 0; z < depth; z++)
		{
			for (int x = 0; x < width; x++)
			{
				var s = GroundTerrain.SplatMap[x, z];
				if (s.Index0 == indexA || s.Index0 == indexB ||
					s.Index1 == indexA || s.Index1 == indexB ||
					s.Index2 == indexA || s.Index2 == indexB ||
					s.Index3 == indexA || s.Index3 == indexB)
				{
					GroundTerrain.SplatMap[x, z] = new TerrainSplatWeights
					{
						Index0 = s.Index0 == indexA ? indexB : (s.Index0 == indexB ? indexA : s.Index0),
						Index1 = s.Index1 == indexA ? indexB : (s.Index1 == indexB ? indexA : s.Index1),
						Index2 = s.Index2 == indexA ? indexB : (s.Index2 == indexB ? indexA : s.Index2),
						Index3 = s.Index3 == indexA ? indexB : (s.Index3 == indexB ? indexA : s.Index3),
						Weight0 = s.Weight0,
						Weight1 = s.Weight1,
						Weight2 = s.Weight2,
						Weight3 = s.Weight3
					};
					anyChanged = true;
				}
			}
		}

		if (anyChanged)
		{
			float[,] heightsAfter = (float[,])GroundTerrain.Heights.Clone();
			TerrainSplatWeights[,] splatAfter = (TerrainSplatWeights[,])GroundTerrain.SplatMap.Clone();
			
			var action = new TerrainModifyAction(heightsBefore, heightsAfter, splatBefore, splatAfter);
			EditorHistoryManager.RecordAction(action);
			EditorHasUnsavedChanges = true;
			
			GroundTerrain.UpdateMeshAndPhysics(false, false);
			MapEditorHUD.Instance?.ShowFeedbackExternal("Textures swapped successfully!");
		}
	}

	public void AlignTerrainSplatMapExternal()
	{
		if (GroundTerrain != null)
		{
			_editorService.SetTerrainSplatMap(GroundTerrain.SplatMap);
		}
	}

	public void ResizeMapExternal(int newWidth, int newDepth)
	{
		if (GroundTerrain == null) return;

		newWidth = Math.Clamp((int)Math.Round(newWidth / 32.0) * 32, 32, 512);
		newDepth = Math.Clamp((int)Math.Round(newDepth / 32.0) * 32, 32, 512);

		var before = MapStateSnapshot.CreateSnapshot();

		int oldWidth = GroundTerrain.Width;
		int oldDepth = GroundTerrain.Depth;

		float diffWidth = (newWidth - oldWidth) * GroundTerrain.QuadSize;
		float diffDepth = (newDepth - oldDepth) * GroundTerrain.QuadSize;

		EditorCameraBoundsLeft -= diffWidth / 2.0f;
		EditorCameraBoundsRight += diffWidth / 2.0f;
		EditorCameraBoundsTop -= diffDepth / 2.0f;
		EditorCameraBoundsBottom += diffDepth / 2.0f;

		GroundTerrain.ResizeTerrain(newWidth, newDepth);

		_editorService.SetTerrainSplatMap(GroundTerrain.SplatMap);
		DeleteEntitiesOutsideBounds();

		RebuildCameraBoundsOverlay();
		MapEditorHUD.Instance?.UpdateCameraBoundsUI();
		MapEditorHUD.Instance?.RegenerateMinimap();

		EditorHasUnsavedChanges = true;
		MapEditorHUD.Instance?.ShowFeedbackExternal($"Map resized to {newWidth}x{newDepth}");

		var after = MapStateSnapshot.CreateSnapshot();
		EditorHistoryManager.RecordAction(new MapResizeAction(before, after));
	}

	public void ScaleMapExternal(int newWidth, int newDepth)
	{
		if (GroundTerrain == null) return;

		newWidth = Math.Clamp((int)Math.Round(newWidth / 32.0) * 32, 32, 512);
		newDepth = Math.Clamp((int)Math.Round(newDepth / 32.0) * 32, 32, 512);

		var before = MapStateSnapshot.CreateSnapshot();

		int oldWidth = GroundTerrain.Width;
		int oldDepth = GroundTerrain.Depth;
		float quadSize = GroundTerrain.QuadSize;

		float oldHalfW = oldWidth / 2.0f * quadSize;
		float oldHalfD = oldDepth / 2.0f * quadSize;
		float newHalfW = newWidth / 2.0f * quadSize;
		float newHalfD = newDepth / 2.0f * quadSize;
		float scaleX = oldHalfW > 0f ? newHalfW / oldHalfW : 1f;
		float scaleZ = oldHalfD > 0f ? newHalfD / oldHalfD : 1f;

		GroundTerrain.ScaleTerrainData(newWidth, newDepth);

		foreach (var unit in AllUnits)
		{
			if (GodotObject.IsInstanceValid(unit))
			{
				unit.Position = new Godot.Vector3(unit.Position.X * scaleX, unit.Position.Y, unit.Position.Z * scaleZ);
			}
		}

		foreach (var prop in AllProps)
		{
			if (GodotObject.IsInstanceValid(prop))
			{
				prop.Position = new Godot.Vector3(prop.Position.X * scaleX, prop.Position.Y, prop.Position.Z * scaleZ);
			}
		}

		foreach (var child in GetChildren())
		{
			if (child is Decal decal && GodotObject.IsInstanceValid(decal))
			{
				decal.Position = new Godot.Vector3(decal.Position.X * scaleX, decal.Position.Y, decal.Position.Z * scaleZ);
			}
		}

		float diffWidth = (newWidth - oldWidth) * quadSize;
		float diffDepth = (newDepth - oldDepth) * quadSize;
		EditorCameraBoundsLeft -= diffWidth / 2.0f;
		EditorCameraBoundsRight += diffWidth / 2.0f;
		EditorCameraBoundsTop -= diffDepth / 2.0f;
		EditorCameraBoundsBottom += diffDepth / 2.0f;

		DeleteEntitiesOutsideBounds();

		_editorService.SetTerrainSplatMap(GroundTerrain.SplatMap);
		RebuildCameraBoundsOverlay();
		MapEditorHUD.Instance?.UpdateCameraBoundsUI();
		MapEditorHUD.Instance?.RegenerateMinimap();

		EditorHasUnsavedChanges = true;
		MapEditorHUD.Instance?.ShowFeedbackExternal($"Map scaled to {newWidth}x{newDepth}");

		var after = MapStateSnapshot.CreateSnapshot();
		EditorHistoryManager.RecordAction(new MapResizeAction(before, after));
	}

	private void DeleteEntitiesOutsideBounds()
	{
		if (GroundTerrain == null) return;

		float halfW = (GroundTerrain.Width - 1) / 2.0f * GroundTerrain.QuadSize;
		float halfD = (GroundTerrain.Depth - 1) / 2.0f * GroundTerrain.QuadSize;

		var unitsToDelete = new List<Unit3D>();
		foreach (var unit in AllUnits)
		{
			if (GodotObject.IsInstanceValid(unit))
			{
				var pos = unit.Position;
				if (pos.X < -halfW || pos.X > halfW || pos.Z < -halfD || pos.Z > halfD)
				{
					unitsToDelete.Add(unit);
				}
			}
		}
		foreach (var unit in unitsToDelete)
		{
			DeleteNodeExternal(unit);
		}

		var propsToDelete = new List<Prop3D>();
		foreach (var prop in AllProps)
		{
			if (GodotObject.IsInstanceValid(prop))
			{
				var pos = prop.Position;
				if (pos.X < -halfW || pos.X > halfW || pos.Z < -halfD || pos.Z > halfD)
				{
					propsToDelete.Add(prop);
				}
			}
		}
		foreach (var prop in propsToDelete)
		{
			DeleteNodeExternal(prop);
		}
	}

	private MeshInstance3D _scaleMapSilhouetteMesh;

	public void ShowScaleMapSilhouette(int previewWidth, int previewDepth)
	{
		if (GroundTerrain == null) return;

		float targetWidthSize = previewWidth * GroundTerrain.QuadSize;
		float targetDepthSize = previewDepth * GroundTerrain.QuadSize;

		if (_scaleMapSilhouetteMesh != null && GodotObject.IsInstanceValid(_scaleMapSilhouetteMesh))
		{
			if (_scaleMapSilhouetteMesh.Mesh is PlaneMesh existingPlane)
			{
				existingPlane.Size = new Godot.Vector2(targetWidthSize, targetDepthSize);
				return;
			}
		}

		HideScaleMapSilhouette();

		_scaleMapSilhouetteMesh = new MeshInstance3D();
		_scaleMapSilhouetteMesh.Name = "ScaleMapSilhouette";

		var plane = new PlaneMesh();
		plane.Size = new Godot.Vector2(targetWidthSize, targetDepthSize);
		plane.SubdivideWidth = 0;
		plane.SubdivideDepth = 0;
		_scaleMapSilhouetteMesh.Mesh = plane;

		var mat = new StandardMaterial3D();
		mat.AlbedoColor = new Color(0.8f, 0.5f, 0.05f, 0.25f);
		mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
		mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
		mat.NoDepthTest = true;
		mat.RenderPriority = 10;
		mat.EmissionEnabled = true;
		mat.Emission = new Color(1.0f, 0.6f, 0.1f) * 0.4f;
		_scaleMapSilhouetteMesh.MaterialOverride = mat;

		_scaleMapSilhouetteMesh.Position = new Godot.Vector3(0f, 1.0f, 0f);

		AddChild(_scaleMapSilhouetteMesh);
	}

	public void HideScaleMapSilhouette()
	{
		if (_scaleMapSilhouetteMesh != null)
		{
			_scaleMapSilhouetteMesh.QueueFree();
			_scaleMapSilhouetteMesh = null;
		}
	}

	public void RebuildCameraBoundsOverlay()
	{
		if (_cameraBoundsOverlayMesh == null || GroundTerrain == null || GroundTerrain.Heights == null) return;
		if (!EditorCameraBoundsVisible) return;

		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;
		float quadSize = GroundTerrain.QuadSize;

		float halfW = width / 2.0f;
		float halfD = depth / 2.0f;

		float minWorldX = -halfW * quadSize;
		float maxWorldX = halfW * quadSize;
		float minWorldZ = -halfD * quadSize;
		float maxWorldZ = halfD * quadSize;

		float left = Mathf.Clamp(EditorCameraBoundsLeft, minWorldX, maxWorldX);
		float right = Mathf.Clamp(EditorCameraBoundsRight, minWorldX, maxWorldX);
		float top = Mathf.Clamp(EditorCameraBoundsTop, minWorldZ, maxWorldZ);
		float bottom = Mathf.Clamp(EditorCameraBoundsBottom, minWorldZ, maxWorldZ);

		var linePoints = new List<Vector3>();

		float GetTerrainHeightAtCoord(float worldX, float worldZ)
		{
			if (GroundTerrain == null || GroundTerrain.Heights == null) return 0f;
			float gridX = worldX / quadSize + halfW;
			float gridZ = worldZ / quadSize + halfD;
			int x0 = Mathf.Clamp((int)Mathf.Floor(gridX), 0, width - 1);
			int x1 = Mathf.Clamp(x0 + 1, 0, width - 1);
			int z0 = Mathf.Clamp((int)Mathf.Floor(gridZ), 0, depth - 1);
			int z1 = Mathf.Clamp(z0 + 1, 0, depth - 1);
			
			float tx = gridX - x0;
			float tz = gridZ - z0;
			
			float h00 = GroundTerrain.Heights[x0, z0];
			float h10 = GroundTerrain.Heights[x1, z0];
			float h01 = GroundTerrain.Heights[x0, z1];
			float h11 = GroundTerrain.Heights[x1, z1];
			
			float h0 = Mathf.Lerp(h00, h10, tx);
			float h1 = Mathf.Lerp(h01, h11, tx);
			return Mathf.Lerp(h0, h1, tz);
		}

		void AddSegmentedLine(float x1, float z1, float x2, float z2)
		{
			float dist = Mathf.Sqrt((x2 - x1) * (x2 - x1) + (z2 - z1) * (z2 - z1));
			int segments = Mathf.Max(1, (int)Mathf.Ceil(dist / quadSize));
			for (int i = 0; i < segments; i++)
			{
				float t1 = (float)i / segments;
				float t2 = (float)(i + 1) / segments;
				
				float lx1 = Mathf.Lerp(x1, x2, t1);
				float lz1 = Mathf.Lerp(z1, z2, t1);
				float lx2 = Mathf.Lerp(x1, x2, t2);
				float lz2 = Mathf.Lerp(z1, z2, t2);
				
				float y1 = GetTerrainHeightAtCoord(lx1, lz1) + 0.2f;
				float y2 = GetTerrainHeightAtCoord(lx2, lz2) + 0.2f;
				
				linePoints.Add(new Vector3(lx1, y1, lz1));
				linePoints.Add(new Vector3(lx2, y2, lz2));
			}
		}

		AddSegmentedLine(left, top, right, top);
		AddSegmentedLine(right, top, right, bottom);
		AddSegmentedLine(right, bottom, left, bottom);
		AddSegmentedLine(left, bottom, left, top);

		int totalVertices = linePoints.Count * 3;
		var vertices = new Vector3[totalVertices];
		var colors = new Color[totalVertices];
		int idx = 0;

		Color boundsColor = new Color(0.9f, 0.1f, 0.8f, 0.95f);

		for (int i = 0; i < linePoints.Count; i += 2)
		{
			Vector3 p1 = linePoints[i];
			Vector3 p2 = linePoints[i + 1];

			vertices[idx] = p1;
			colors[idx] = boundsColor;
			idx++;
			vertices[idx] = p2;
			colors[idx] = boundsColor;
			idx++;

			Vector3 dir = (p2 - p1).Normalized();
			Vector3 ortho = new Vector3(-dir.Z, 0, dir.X) * 0.08f;

			vertices[idx] = p1 + ortho;
			colors[idx] = boundsColor;
			idx++;
			vertices[idx] = p2 + ortho;
			colors[idx] = boundsColor;
			idx++;

			vertices[idx] = p1 - ortho;
			colors[idx] = boundsColor;
			idx++;
			vertices[idx] = p2 - ortho;
			colors[idx] = boundsColor;
			idx++;
		}

		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices;
		arrays[(int)Mesh.ArrayType.Color] = colors;

		var arrayMesh = new ArrayMesh();
		arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Lines, arrays);
		_cameraBoundsOverlayMesh.Mesh = arrayMesh;
	}

	private void PerformCopyArea()
	{
		if (GroundTerrain == null || _editorService.SelectionStart == null || _editorService.SelectionEnd == null) return;

		var (minX, minZ, maxX, maxZ) = _editorService.GetCurrentSelectionBounds();

		var node3Ds = new List<Node3D>();
		foreach (var child in GetChildren())
		{
			if (child is Node3D n3d) node3Ds.Add(n3d);
		}

		var entities = _editorService.BuildCopiedEntityList(minX, minZ, maxX, maxZ, node3Ds);
		_editorService.CopyArea(minX, minZ, maxX, maxZ, entities);

		int selWidth = maxX - minX + 1;
		int selDepth = maxZ - minZ + 1;
		MapEditorHUD.Instance?.ShowFeedbackExternal($"Copied Area: {selWidth}x{selDepth} tiles, {entities.Count} entities");
	}

	private void InitializeCameraBoundsOverlay()
	{
		if (_cameraBoundsOverlayMesh != null) return;

		_cameraBoundsOverlayMesh = new MeshInstance3D();
		_cameraBoundsOverlayMesh.Name = "CameraBoundsOverlay";

		var mat = new StandardMaterial3D();
		mat.AlbedoColor = new Color(1.0f, 1.0f, 1.0f, 1.0f);
		mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
		mat.NoDepthTest = false;
		mat.VertexColorUseAsAlbedo = true;
		_cameraBoundsOverlayMesh.MaterialOverride = mat;

		AddChild(_cameraBoundsOverlayMesh);
		_cameraBoundsOverlayMesh.Visible = false;
	}

	private void RebuildPathingOverlay()
	{
		if (GroundTerrain == null || GroundTerrain.PathingCodes == null || GroundTerrain.Heights == null) return;
		GroundTerrain.UpdatePathingTexture();
	}

	public void RebuildGridOverlayMeshExternal()
	{
		// No longer needed, grid is on shader
	}

	public void UpdateGridOverlayVisibility()
	{
		if (GroundTerrain != null)
		{
			bool meshVisible = IsMapEditorMode && (EditorGridMode == GridOverlayMode.Mesh);
			GroundTerrain.SetGridVisible(meshVisible);
		}
	}

	public void PerformFloodFill(Vector3 clickPos, int fillTextureIndex, bool isCliff = false)
	{
		if (GroundTerrain == null || GroundTerrain.Heights == null || GroundTerrain.SplatMap == null) return;

		if (GroundTerrain.CliffSplatMap == null)
		{
			GroundTerrain.CliffSplatMap = new TerrainSplatWeights[GroundTerrain.Width + 1, GroundTerrain.Depth + 1];
		}

		var heightsBefore = (float[,])GroundTerrain.Heights.Clone();
		var splatBefore = (TerrainSplatWeights[,])GroundTerrain.SplatMap.Clone();
		var cliffBefore = (TerrainSplatWeights[,])GroundTerrain.CliffSplatMap.Clone();
		int cliffTextureIndex = EditorCliffPaintTextureIndex;

		var result = _editorService.PerformFloodFill(clickPos, fillTextureIndex, cliffTextureIndex, EditorMirrorMode, isCliff);
		if (result.Heights == null || result.SplatMap == null) return;

		if (result.IsCliff)
		{
			Array.Copy(result.SplatMap, GroundTerrain.CliffSplatMap, result.SplatMap.Length);
		}
		else
		{
			Array.Copy(result.SplatMap, GroundTerrain.SplatMap, result.SplatMap.Length);
		}

		GroundTerrain.UpdateMeshAndPhysics(false, false);
		var heightsAfter = (float[,])GroundTerrain.Heights.Clone();
		var splatAfter = (TerrainSplatWeights[,])GroundTerrain.SplatMap.Clone();
		var cliffAfter = (TerrainSplatWeights[,])GroundTerrain.CliffSplatMap.Clone();
		var action = new TerrainModifyAction(heightsBefore, heightsAfter, splatBefore, splatAfter, null, null, cliffBefore, cliffAfter);
		EditorHistoryManager.RecordAction(action);
		EditorHasUnsavedChanges = true;
		MapEditorHUD.Instance?.ShowFeedbackExternal(result.IsCliff ? "Flood filled cliff face area" : "Flood filled terrain area");
	}

	public void PerformFloodFillPathing(Vector3 clickPos, int pathingMask, bool pathingAdd)
	{
		if (GroundTerrain == null || GroundTerrain.PathingCodes == null) return;

		var result = _editorService.PerformFloodFillPathing(clickPos, pathingMask, pathingAdd, EditorMirrorMode);

		if (result.Before != null && result.After != null)
		{
			GroundTerrain.UpdateMeshAndPhysics(false, false);
			var action = new TerrainModifyAction((Realm.Ecs.Components.Terrain.TerrainCell[,])null, (Realm.Ecs.Components.Terrain.TerrainCell[,])null, null, null, result.Before, result.After);
			EditorHistoryManager.RecordAction(action);
			EditorHasUnsavedChanges = true;
			MapEditorHUD.Instance?.ShowFeedbackExternal("Flood filled pathing area");
			UpdatePathingOverlay();
		}
	}

	private void CreateSelectionHighlight()
	{
		if (_selectionHighlightMesh != null) return;
		_selectionHighlightMesh = new MeshInstance3D();
		_selectionHighlightMesh.Name = "SelectionHighlight";
		var mat = new StandardMaterial3D();
		mat.AlbedoColor = new Color(0.0f, 0.6f, 1.0f, 0.35f);
		mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
		mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
		_selectionHighlightMesh.MaterialOverride = mat;
		AddChild(_selectionHighlightMesh);
		_selectionHighlightMesh.Visible = false;
	}

	private void RebuildSelectionHighlightMesh(int minX, int minZ, int maxX, int maxZ)
	{
		if (_selectionHighlightMesh == null || GroundTerrain == null || GroundTerrain.Heights == null) return;
		int selWidth = maxX - minX + 1;
		int selDepth = maxZ - minZ + 1;
		if (selWidth < 2 || selDepth < 2)
		{
			_selectionHighlightMesh.Visible = false;
			return;
		}
		int vertexCount = selWidth * selDepth;
		var vertices = new Vector3[vertexCount];
		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;
		float quadSize = GroundTerrain.QuadSize;
		for (int sz = 0; sz < selDepth; sz++)
		{
			for (int sx = 0; sx < selWidth; sx++)
			{
				int mapX = minX + sx;
				int mapZ = minZ + sz;
				int idx = sz * selWidth + sx;
				float lx = (mapX - (width - 1) / 2.0f) * quadSize;
				float lz = (mapZ - (depth - 1) / 2.0f) * quadSize;
				vertices[idx] = new Vector3(lx, GroundTerrain.Heights[mapX, mapZ] + 0.05f, lz);
			}
		}
		int cellWidth = selWidth - 1;
		int cellDepth = selDepth - 1;
		int indexCount = cellWidth * cellDepth * 6;
		var indices = new int[indexCount];
		int iIdx = 0;
		for (int sz = 0; sz < cellDepth; sz++)
		{
			for (int sx = 0; sx < cellWidth; sx++)
			{
				int v00 = sz * selWidth + sx;
				int v10 = sz * selWidth + (sx + 1);
				int v01 = (sz + 1) * selWidth + sx;
				int v11 = (sz + 1) * selWidth + (sx + 1);
				indices[iIdx++] = v00;
				indices[iIdx++] = v10;
				indices[iIdx++] = v01;
				indices[iIdx++] = v10;
				indices[iIdx++] = v11;
				indices[iIdx++] = v01;
			}
		}
		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices;
		arrays[(int)Mesh.ArrayType.Index] = indices;
		var arrayMesh = new ArrayMesh();
		arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		_selectionHighlightMesh.Mesh = arrayMesh;
		_selectionHighlightMesh.Visible = true;
	}

	private void CreateCoordinatePreviewMesh()
	{
		if (_coordinatePreviewMesh != null) return;
		_coordinatePreviewMesh = new MeshInstance3D();
		_coordinatePreviewMesh.Name = "CoordinatePreview";
		var mat = new StandardMaterial3D();
		mat.AlbedoColor = new Color(0.1f, 1.0f, 0.3f, 0.4f);
		mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
		mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
		_coordinatePreviewMesh.MaterialOverride = mat;
		AddChild(_coordinatePreviewMesh);
		_coordinatePreviewMesh.Visible = false;
	}

	public void UpdateCoordinatePreviewMesh(int minX, int minZ, int maxX, int maxZ)
	{
		CreateCoordinatePreviewMesh();
		RebuildCoordinateMeshInstance(_coordinatePreviewMesh, minX, minZ, maxX, maxZ, new Color(0.1f, 1.0f, 0.3f, 0.4f));
	}

	public void HideCoordinatePreviewMesh()
	{
		if (_coordinatePreviewMesh != null) _coordinatePreviewMesh.Visible = false;
	}

	private void RebuildCoordinateMeshInstance(MeshInstance3D meshInstance, int minX, int minZ, int maxX, int maxZ, Color color, float yOffset = 0.15f)
	{
		if (meshInstance == null || GroundTerrain == null || GroundTerrain.Heights == null) return;
		int selWidth = maxX - minX + 1;
		int selDepth = maxZ - minZ + 1;
		if (selWidth < 2 || selDepth < 2)
		{
			meshInstance.Visible = false;
			return;
		}
		int vertexCount = selWidth * selDepth;
		var vertices = new Vector3[vertexCount];
		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;
		float quadSize = GroundTerrain.QuadSize;
		for (int sz = 0; sz < selDepth; sz++)
		{
			for (int sx = 0; sx < selWidth; sx++)
			{
				int mapX = minX + sx;
				int mapZ = minZ + sz;
				int idx = sz * selWidth + sx;
				float lx = (mapX - (width - 1) / 2.0f) * quadSize;
				float lz = (mapZ - (depth - 1) / 2.0f) * quadSize;
				vertices[idx] = new Vector3(lx, GroundTerrain.Heights[mapX, mapZ] + yOffset, lz);
			}
		}
		int cellWidth = selWidth - 1;
		int cellDepth = selDepth - 1;
		int indexCount = cellWidth * cellDepth * 6;
		var indices = new int[indexCount];
		var colors = new Color[vertexCount];
		for (int i = 0; i < vertexCount; i++) colors[i] = color;
		int iIdx = 0;
		for (int sz = 0; sz < cellDepth; sz++)
		{
			for (int sx = 0; sx < cellWidth; sx++)
			{
				int v00 = sz * selWidth + sx;
				int v10 = sz * selWidth + (sx + 1);
				int v01 = (sz + 1) * selWidth + sx;
				int v11 = (sz + 1) * selWidth + (sx + 1);
				indices[iIdx++] = v00;
				indices[iIdx++] = v10;
				indices[iIdx++] = v01;
				indices[iIdx++] = v10;
				indices[iIdx++] = v11;
				indices[iIdx++] = v01;
			}
		}
		var arrays = new Godot.Collections.Array();
		arrays.Resize((int)Mesh.ArrayType.Max);
		arrays[(int)Mesh.ArrayType.Vertex] = vertices;
		arrays[(int)Mesh.ArrayType.Index] = indices;
		arrays[(int)Mesh.ArrayType.Color] = colors;
		var arrayMesh = new ArrayMesh();
		arrayMesh.AddSurfaceFromArrays(Mesh.PrimitiveType.Triangles, arrays);
		meshInstance.Mesh = arrayMesh;
		meshInstance.Visible = true;
	}

	public bool CommitCoordinateExternal(string coordinateName, int minX, int minZ, int maxX, int maxZ)
	{
		if (GroundTerrain == null) return false;
		string safeName = coordinateName.Trim();
		if (string.IsNullOrEmpty(safeName)) return false;

		var oldCoordinates = new List<EditorCoordinate>(EditorCoordinates);

		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;
		float quadSize = GroundTerrain.QuadSize;

		float worldMinX = (minX - (width - 1) / 2.0f) * quadSize;
		float worldMinZ = (minZ - (depth - 1) / 2.0f) * quadSize;
		float worldMaxX = (maxX - (width - 1) / 2.0f) * quadSize;
		float worldMaxZ = (maxZ - (depth - 1) / 2.0f) * quadSize;

		bool committed = false;
		for (int i = 0; i < EditorCoordinates.Count; i++)
		{
			if (EditorCoordinates[i].Name == safeName)
			{
				EditorCoordinates[i] = new EditorCoordinate { Name = safeName, MinX = worldMinX, MinZ = worldMinZ, MaxX = worldMaxX, MaxZ = worldMaxZ };
				committed = true;
				break;
			}
		}

		if (!committed)
		{
			EditorCoordinates.Add(new EditorCoordinate { Name = safeName, MinX = worldMinX, MinZ = worldMinZ, MaxX = worldMaxX, MaxZ = worldMaxZ });
		}

		RebuildAllCoordinatePersistentMeshes();

		var newCoordinates = new List<EditorCoordinate>(EditorCoordinates);
		var action = new CoordinateAction(oldCoordinates, newCoordinates);
		EditorHistoryManager.RecordAction(action);
		EditorHasUnsavedChanges = true;

		return true;
	}

	public void DeleteCoordinateExternal(string coordinateName)
	{
		var oldCoordinates = new List<EditorCoordinate>(EditorCoordinates);
		EditorCoordinates.RemoveAll(r => r.Name == coordinateName);
		RebuildAllCoordinatePersistentMeshes();

		var newCoordinates = new List<EditorCoordinate>(EditorCoordinates);
		var action = new CoordinateAction(oldCoordinates, newCoordinates);
		EditorHistoryManager.RecordAction(action);
		EditorHasUnsavedChanges = true;
	}

	public void RebuildAllCoordinatePersistentMeshes()
	{
		foreach (var mesh in _coordinatePersistentMeshes)
		{
			if (GodotObject.IsInstanceValid(mesh))
			{
				RemoveChild(mesh);
				mesh.QueueFree();
			}
		}
		_coordinatePersistentMeshes.Clear();

		if (ActiveEditorTool != EditorTool.DrawCoordinate)
		{
			HideCoordinatePreviewMesh();
			return;
		}

		if (GroundTerrain == null || GroundTerrain.Heights == null) return;

		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;
		float quadSize = GroundTerrain.QuadSize;

		foreach (var coord in EditorCoordinates)
		{
			int minX = Mathf.Clamp((int)Mathf.Round(coord.MinX / quadSize + (width - 1) / 2.0f), 0, width - 1);
			int minZ = Mathf.Clamp((int)Mathf.Round(coord.MinZ / quadSize + (depth - 1) / 2.0f), 0, depth - 1);
			int maxX = Mathf.Clamp((int)Mathf.Round(coord.MaxX / quadSize + (width - 1) / 2.0f), 0, width - 1);
			int maxZ = Mathf.Clamp((int)Mathf.Round(coord.MaxZ / quadSize + (depth - 1) / 2.0f), 0, depth - 1);

			var meshInst = new MeshInstance3D();
			meshInst.Name = $"Coordinate_{coord.Name}";
			var mat = new StandardMaterial3D();
			mat.AlbedoColor = new Color(0.1f, 0.9f, 0.3f, 0.25f);
			mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
			mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
			mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
			meshInst.MaterialOverride = mat;
			AddChild(meshInst);
			RebuildCoordinateMeshInstance(meshInst, minX, minZ, maxX, maxZ, new Color(0.1f, 0.9f, 0.3f, 0.25f));
			_coordinatePersistentMeshes.Add(meshInst);
		}
	}

	public void SelectCoordinateExternal(string coordinateName)
	{
		if (string.IsNullOrEmpty(coordinateName))
		{
			HideCoordinateSelectionOutline();
			return;
		}

		if (GroundTerrain == null || GroundTerrain.Heights == null) return;

		EditorCoordinate? found = null;
		foreach (var r in EditorCoordinates)
		{
			if (r.Name == coordinateName)
			{
				found = r;
				break;
			}
		}

		if (found == null)
		{
			HideCoordinateSelectionOutline();
			return;
		}

		var coord = found.Value;

		if (_coordinateSelectionOutlineMesh == null)
		{
			_coordinateSelectionOutlineMesh = new MeshInstance3D();
			_coordinateSelectionOutlineMesh.Name = "CoordinateSelectionOutline";
			var mat = new StandardMaterial3D();
			mat.AlbedoColor = new Color(1.0f, 0.6f, 0.0f, 0.45f);
			mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
			mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
			mat.CullMode = BaseMaterial3D.CullModeEnum.Disabled;
			_coordinateSelectionOutlineMesh.MaterialOverride = mat;
			AddChild(_coordinateSelectionOutlineMesh);
		}

		int width = GroundTerrain.Width;
		int depth = GroundTerrain.Depth;
		float quadSize = GroundTerrain.QuadSize;

		int minX = Mathf.Clamp((int)Mathf.Round(coord.MinX / quadSize + (width - 1) / 2.0f), 0, width - 1);
		int minZ = Mathf.Clamp((int)Mathf.Round(coord.MinZ / quadSize + (depth - 1) / 2.0f), 0, depth - 1);
		int maxX = Mathf.Clamp((int)Mathf.Round(coord.MaxX / quadSize + (width - 1) / 2.0f), 0, width - 1);
		int maxZ = Mathf.Clamp((int)Mathf.Round(coord.MaxZ / quadSize + (depth - 1) / 2.0f), 0, depth - 1);

		_coordinateSelectionOutlineMesh.Visible = true;
		RebuildCoordinateMeshInstance(_coordinateSelectionOutlineMesh, minX, minZ, maxX, maxZ, new Color(1.0f, 0.6f, 0.0f, 0.45f), 0.25f);

		float centerX = (coord.MinX + coord.MaxX) / 2.0f;
		float centerZ = (coord.MinZ + coord.MaxZ) / 2.0f;
		float centerY = GetTerrainHeightAt(new Vector3(centerX, 0f, centerZ));

		var camera = GetViewport().GetCamera3D();
		if (camera != null)
		{
			camera.GlobalPosition = new Vector3(centerX, centerY, centerZ + 25f);
		}
	}

	public void HideCoordinateSelectionOutline()
	{
		if (_coordinateSelectionOutlineMesh != null)
		{
			_coordinateSelectionOutlineMesh.Visible = false;
		}
	}

	public void PerformEraseAreaExternal()
	{
		PerformEraseArea();
		if (GroundTerrain != null && _editorService.SelectionStart != null && _editorService.SelectionEnd != null)
		{
			var (minX, minZ, maxX, maxZ) = _editorService.GetCurrentSelectionBounds();
			if (_selectionHighlightMesh != null && _selectionHighlightMesh.Visible)
			{
				RebuildSelectionHighlightMesh(minX, minZ, maxX, maxZ);
			}
		}
	}

	public void PerformMirrorSelectionVerticallyExternal()
	{
		if (GroundTerrain == null || _editorService.SelectionStart == null || _editorService.SelectionEnd == null)
		{
			MapEditorHUD.Instance?.ShowFeedbackExternal("Nothing to mirror (select an area first)");
			return;
		}

		PerformCopyArea();
		var eraseActions = PerformEraseArea(false);
		_editorService.MirrorCopiedAreaVertically();

		var (minX, minZ, maxX, maxZ) = _editorService.GetCurrentSelectionBounds();
		var pasteActions = PerformPasteArea(minX, minZ, 0.0f, false);

		var combined = new List<IEditorAction>();
		if (eraseActions != null) combined.AddRange(eraseActions);
		if (pasteActions != null) combined.AddRange(pasteActions);

		if (combined.Count > 0)
		{
			var composite = new CompositeAction(combined);
			EditorHistoryManager.RecordAction(composite);
			EditorHasUnsavedChanges = true;
		}

		if (_selectionHighlightMesh != null && _selectionHighlightMesh.Visible)
		{
			RebuildSelectionHighlightMesh(minX, minZ, maxX, maxZ);
		}

		MapEditorHUD.Instance?.ShowFeedbackExternal("Selection Mirrored Vertically");
	}

	public void PerformMirrorSelectionHorizontallyExternal()
	{
		if (GroundTerrain == null || _editorService.SelectionStart == null || _editorService.SelectionEnd == null)
		{
			MapEditorHUD.Instance?.ShowFeedbackExternal("Nothing to mirror (select an area first)");
			return;
		}

		PerformCopyArea();
		var eraseActions = PerformEraseArea(false);
		_editorService.MirrorCopiedAreaHorizontally();

		var (minX, minZ, maxX, maxZ) = _editorService.GetCurrentSelectionBounds();
		var pasteActions = PerformPasteArea(minX, minZ, 0.0f, false);

		var combined = new List<IEditorAction>();
		if (eraseActions != null) combined.AddRange(eraseActions);
		if (pasteActions != null) combined.AddRange(pasteActions);

		if (combined.Count > 0)
		{
			var composite = new CompositeAction(combined);
			EditorHistoryManager.RecordAction(composite);
			EditorHasUnsavedChanges = true;
		}

		if (_selectionHighlightMesh != null && _selectionHighlightMesh.Visible)
		{
			RebuildSelectionHighlightMesh(minX, minZ, maxX, maxZ);
		}

		MapEditorHUD.Instance?.ShowFeedbackExternal("Selection Mirrored Horizontally");
	}

	public void PerformCopyAreaExternal()
	{
		if (GroundTerrain == null || _editorService.SelectionStart == null || _editorService.SelectionEnd == null)
		{
			MapEditorHUD.Instance?.ShowFeedbackExternal("Nothing to Copy (select an area first)");
			return;
		}

		PerformCopyArea();
		MapEditorHUD.Instance?.ShowFeedbackExternal("Area Copied");
	}

	public void PerformCutAreaExternal()
	{
		if (GroundTerrain == null || _editorService.SelectionStart == null || _editorService.SelectionEnd == null)
		{
			MapEditorHUD.Instance?.ShowFeedbackExternal("Nothing to Cut (select an area first)");
			return;
		}

		PerformCopyArea();
		PerformEraseArea();

		var (minX, minZ, maxX, maxZ) = _editorService.GetCurrentSelectionBounds();
		if (_selectionHighlightMesh != null && _selectionHighlightMesh.Visible)
		{
			RebuildSelectionHighlightMesh(minX, minZ, maxX, maxZ);
		}

		MapEditorHUD.Instance?.ShowFeedbackExternal("Area Cut");
	}

	private List<IEditorAction> PerformEraseArea(bool recordToHistory = true)
	{
		if (GroundTerrain == null || GroundTerrain.Heights == null || GroundTerrain.SplatMap == null || _editorService.SelectionStart == null || _editorService.SelectionEnd == null)
		{
			MapEditorHUD.Instance?.ShowFeedbackExternal("Nothing to Erase (select an area first)");
			return new List<IEditorAction>();
		}

		var (minX, minZ, maxX, maxZ) = _editorService.GetCurrentSelectionBounds();

		var heightsBefore = (float[,])GroundTerrain.Heights.Clone();
		var splatBefore = (TerrainSplatWeights[,])GroundTerrain.SplatMap.Clone();
		var pathingBefore = (int[,])GroundTerrain.PathingCodes.Clone();

		var node3Ds = new List<Node3D>();
		foreach (var child in GetChildren())
		{
			if (child is Node3D n3d) node3Ds.Add(n3d);
		}

		var eraseResult = _editorService.BuildEraseAreaResult(
			minX, minZ, maxX, maxZ,
			PasteOptionHeights, PasteOptionTextures, PasteOptionEntities, PasteOptionPathing,
			node3Ds, _editorPreviewNode as Node3D);

		if (eraseResult.TerrainModified)
		{
			GroundTerrain.UpdateMeshAndPhysics(eraseResult.HeightsModified, false);
			if (eraseResult.HeightsModified)
			{
				Rect2I affected = new Rect2I(minX - 2, minZ - 2, maxX - minX + 4, maxZ - minZ + 4);
				AlignAllEntitiesToTerrain(affected);
			}
			if (eraseResult.PathingModified)
			{
				UpdatePathingOverlay();
			}
		}

		var deleteActions = new List<IEditorAction>();
		foreach (var node in eraseResult.NodesToDelete)
		{
			var act = DeleteObjectAtWithUndo(node, node.Position);
			if (act != null) deleteActions.Add(act);
		}

		var heightsAfter = (float[,])GroundTerrain.Heights.Clone();
		var splatAfter = (TerrainSplatWeights[,])GroundTerrain.SplatMap.Clone();
		var pathingAfter = (int[,])GroundTerrain.PathingCodes.Clone();
		var actions = new List<IEditorAction>();
		if (eraseResult.TerrainModified)
		{
			actions.Add(new TerrainModifyAction(heightsBefore, heightsAfter, splatBefore, splatAfter, pathingBefore, pathingAfter));
		}
		if (deleteActions.Count > 0)
		{
			actions.AddRange(deleteActions);
		}

		if (actions.Count > 0)
		{
			if (recordToHistory)
			{
				var composite = new CompositeAction(actions);
				EditorHistoryManager.RecordAction(composite);
				EditorHasUnsavedChanges = true;
				MapEditorHUD.Instance?.ShowFeedbackExternal("Area Erased");
			}
		}
		return actions;
	}

	private List<IEditorAction> PerformPasteArea(int startX, int startZ, float rotationDegrees, bool recordToHistory = true)
	{
		if (GroundTerrain == null || GroundTerrain.Heights == null || GroundTerrain.SplatMap == null || !_editorService.HasCopiedArea) return new List<IEditorAction>();

		var heightsBefore = (float[,])GroundTerrain.Heights.Clone();
		var splatBefore = (TerrainSplatWeights[,])GroundTerrain.SplatMap.Clone();
		var pathingBefore = (int[,])GroundTerrain.PathingCodes.Clone();

		var pasteResult = _editorService.BuildPasteAreaResult(
			startX, startZ,
			PasteOptionHeights, PasteOptionTextures, PasteOptionEntities, PasteOptionPathing,
			EditorMirrorMode,
			rotationDegrees);

		if (pasteResult.TerrainModified)
		{
			GroundTerrain.UpdateMeshAndPhysics(pasteResult.HeightsModified, false);
			if (pasteResult.HeightsModified)
			{
				AlignAllEntitiesToTerrain();
			}
			if (pasteResult.PathingModified)
			{
				UpdatePathingOverlay();
			}
		}

		var spawnActions = new List<IEditorAction>();
		foreach (var req in pasteResult.SpawnRequests)
		{
			Node pastedNode = null;
			if (req.Type == "unit")
				pastedNode = SpawnUnitExternal(req.Id, req.Position, req.IsEnemy, req.Rotation, req.Scale);
			else if (req.Type == "prop")
				pastedNode = SpawnPropExternalWithParams(req.Id, req.Position, req.Rotation, req.Scale);
			else if (req.Type == "decal")
				pastedNode = SpawnDecalExternalWithParams(req.Id, req.Position, req.Rotation, req.Scale);

			if (pastedNode != null)
			{
				spawnActions.Add(new ObjectSpawnAction(req.Type, req.Id, req.Position, req.Rotation, req.Scale, req.IsEnemy, pastedNode));
			}
		}

		var heightsAfter = (float[,])GroundTerrain.Heights.Clone();
		var splatAfter = (TerrainSplatWeights[,])GroundTerrain.SplatMap.Clone();
		var pathingAfter = (int[,])GroundTerrain.PathingCodes.Clone();
		var actions = new List<IEditorAction>();
		if (pasteResult.TerrainModified)
		{
			actions.Add(new TerrainModifyAction(heightsBefore, heightsAfter, splatBefore, splatAfter, pathingBefore, pathingAfter));
		}
		if (spawnActions.Count > 0)
		{
			actions.AddRange(spawnActions);
		}
		if (actions.Count > 0)
		{
			if (recordToHistory)
			{
				var composite = new CompositeAction(actions);
				EditorHistoryManager.RecordAction(composite);
				EditorHasUnsavedChanges = true;
				MapEditorHUD.Instance?.ShowFeedbackExternal("Pasted Area");
			}
		}
		return actions;
	}

	public void UpdateCameraBoundsOverlayVisibility()
	{
		if (_cameraBoundsOverlayMesh == null) return;
		_cameraBoundsOverlayMesh.Visible = IsMapEditorMode && EditorCameraBoundsVisible;
		if (_cameraBoundsOverlayMesh.Visible)
		{
			RebuildCameraBoundsOverlay();
		}
	}

	public void UpdatePathingOverlay()
	{
		bool isPathingTool = ActiveEditorTool == EditorTool.PaintPathing || ActiveEditorTool == EditorTool.FloodFillPathing;
		bool isClipboardTool = ActiveEditorTool == EditorTool.SelectArea || ActiveEditorTool == EditorTool.PasteArea;

		bool shouldBeVisible = IsMapEditorMode && PathingOverlayVisible && (isPathingTool || (isClipboardTool && PasteOptionPathing));
		
		if (GroundTerrain != null)
		{
			GroundTerrain.SetPathingVisible(shouldBeVisible);
			if (shouldBeVisible)
			{
				RebuildPathingOverlay();
			}
		}
	}

	public void RefreshSelectionHighlight()
	{
		if (GroundTerrain != null && _editorService.SelectionStart != null && _editorService.SelectionEnd != null)
		{
			var (minX, minZ, maxX, maxZ) = _editorService.GetCurrentSelectionBounds();
			if (_selectionHighlightMesh != null && _selectionHighlightMesh.Visible)
			{
				RebuildSelectionHighlightMesh(minX, minZ, maxX, maxZ);
			}
		}
	}
}
