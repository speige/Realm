using Arch.Core;
using Godot;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Combat;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;
using Realm.Ecs.Components.Movement;
using Realm.Ecs.Components.Resources;
using Realm.Ecs.Components.Tags;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Realm.Godot.Utils;

public partial class GameHost
{
	public readonly Dictionary<string, float> ModelYOffsets = new(StringComparer.OrdinalIgnoreCase);
	public readonly Dictionary<string, float> ModelCollisionCircleRatios = new(StringComparer.OrdinalIgnoreCase);
	public readonly Dictionary<string, float> ModelObstacleRadii = new(StringComparer.OrdinalIgnoreCase);
	public readonly Dictionary<string, float> ModelBrightness = new(StringComparer.OrdinalIgnoreCase);
	public readonly Dictionary<string, Color> ModelColorTint = new(StringComparer.OrdinalIgnoreCase);
	public readonly Dictionary<string, bool> ModelGenerateNormals = new(StringComparer.OrdinalIgnoreCase);
	public readonly Dictionary<string, bool> ModelIgnorePlayerColor = new(StringComparer.OrdinalIgnoreCase);
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
		if (objOrId is Unit3D unit)
		{
			if (!string.IsNullOrEmpty(unit.ModelPath))
				return NormalizeModelAssetKey(unit.ModelPath);
			if (UnitRegistry.TryGetValue(unit.UnitId, out var meta) && !string.IsNullOrEmpty(meta.ModelPath))
				return NormalizeModelAssetKey(meta.ModelPath);
			return NormalizeModelAssetKey(unit.UnitId);
		}
		if (objOrId is Prop3D prop)
		{
			if (PropRegistry.TryGetValue(prop.PropId, out var propMeta) && !string.IsNullOrEmpty(propMeta.ModelPath))
				return NormalizeModelAssetKey(propMeta.ModelPath);
			if (ResourceRegistry.TryGetValue(prop.PropId, out var resMeta) && !string.IsNullOrEmpty(resMeta.ModelPath))
				return NormalizeModelAssetKey(resMeta.ModelPath);
			if (UnitRegistry.TryGetValue(prop.PropId, out var unitMeta) && !string.IsNullOrEmpty(unitMeta.ModelPath))
				return NormalizeModelAssetKey(unitMeta.ModelPath);
			if (!string.IsNullOrEmpty(prop.ModelAssetPath))
				return NormalizeModelAssetKey(prop.ModelAssetPath);
			return NormalizeModelAssetKey(prop.PropId);
		}
		if (objOrId is string str)
		{
			if (PropRegistry.TryGetValue(str, out var propMeta) && !string.IsNullOrEmpty(propMeta.ModelPath))
				return NormalizeModelAssetKey(propMeta.ModelPath);
			if (ResourceRegistry.TryGetValue(str, out var resMeta) && !string.IsNullOrEmpty(resMeta.ModelPath))
				return NormalizeModelAssetKey(resMeta.ModelPath);
			if (UnitRegistry.TryGetValue(str, out var unitMeta) && !string.IsNullOrEmpty(unitMeta.ModelPath))
				return NormalizeModelAssetKey(unitMeta.ModelPath);
			return NormalizeModelAssetKey(str);
		}
		if (objOrId is Node node)
		{
			return NormalizeModelAssetKey(node.Name.ToString());
		}
		return "";
	}

	public string GetSelectedEntityOrAssetKey(object objOrId)
	{
		if (objOrId == null) return "";
		if (objOrId is Unit3D unit)
		{
			if (!string.IsNullOrEmpty(unit.UnitId)) return unit.UnitId;
			return GetModelAssetKey(unit);
		}
		if (objOrId is Prop3D prop)
		{
			if (!string.IsNullOrEmpty(prop.PropId)) return prop.PropId;
			return GetModelAssetKey(prop);
		}
		if (objOrId is string str) return str;
		return GetModelAssetKey(objOrId);
	}

	public bool MatchesEntityOrAssetKey(object objOrId, string targetKey)
	{
		if (objOrId == null || string.IsNullOrEmpty(targetKey)) return false;
		string normTarget = NormalizeModelAssetKey(targetKey);

		if (objOrId is Unit3D unit)
		{
			if (!string.IsNullOrEmpty(unit.UnitId) && NormalizeModelAssetKey(unit.UnitId) == normTarget)
				return true;
			if (!string.IsNullOrEmpty(unit.ModelPath) && NormalizeModelAssetKey(unit.ModelPath) == normTarget)
				return true;
			if (UnitRegistry.TryGetValue(unit.UnitId, out var meta) && !string.IsNullOrEmpty(meta.ModelPath) && NormalizeModelAssetKey(meta.ModelPath) == normTarget)
				return true;
		}
		else if (objOrId is Prop3D prop)
		{
			if (!string.IsNullOrEmpty(prop.PropId) && NormalizeModelAssetKey(prop.PropId) == normTarget)
				return true;
			if (PropRegistry.TryGetValue(prop.PropId, out var propMeta) && !string.IsNullOrEmpty(propMeta.ModelPath) && NormalizeModelAssetKey(propMeta.ModelPath) == normTarget)
				return true;
			if (ResourceRegistry.TryGetValue(prop.PropId, out var resMeta) && !string.IsNullOrEmpty(resMeta.ModelPath) && NormalizeModelAssetKey(resMeta.ModelPath) == normTarget)
				return true;
		}
		else if (objOrId is string str)
		{
			if (NormalizeModelAssetKey(str) == normTarget) return true;
			if (UnitRegistry.TryGetValue(str, out var meta) && !string.IsNullOrEmpty(meta.ModelPath) && NormalizeModelAssetKey(meta.ModelPath) == normTarget)
				return true;
			if (PropRegistry.TryGetValue(str, out var propMeta) && !string.IsNullOrEmpty(propMeta.ModelPath) && NormalizeModelAssetKey(propMeta.ModelPath) == normTarget)
				return true;
			if (ResourceRegistry.TryGetValue(str, out var resMeta) && !string.IsNullOrEmpty(resMeta.ModelPath) && NormalizeModelAssetKey(resMeta.ModelPath) == normTarget)
				return true;
		}
		return GetModelAssetKey(objOrId) == normTarget;
	}

	public float GetModelYOffset(object objOrId)
	{
		if (objOrId == null) return 0f;
		string primaryKey = GetSelectedEntityOrAssetKey(objOrId);
		string normPrimary = NormalizeModelAssetKey(primaryKey);
		if (!string.IsNullOrEmpty(normPrimary) && ModelYOffsets.TryGetValue(normPrimary, out float val1))
			return val1;

		string assetKey = GetModelAssetKey(objOrId);
		string normAsset = NormalizeModelAssetKey(assetKey);
		if (!string.IsNullOrEmpty(normAsset) && ModelYOffsets.TryGetValue(normAsset, out float val2))
			return val2;

		if (!string.IsNullOrEmpty(primaryKey))
		{
			if (UnitRegistry.TryGetValue(primaryKey, out var meta) && meta.YOffset != 0f) return meta.YOffset;
			if (ResourceRegistry.TryGetValue(primaryKey, out var resMeta) && resMeta.YOffset != 0f) return resMeta.YOffset;
			if (PropRegistry.TryGetValue(primaryKey, out var propMeta) && propMeta.YOffset != 0f) return propMeta.YOffset;
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
			if (GodotObject.IsInstanceValid(prop) && MatchesEntityOrAssetKey(prop, norm))
			{
				prop.Position = new Vector3(prop.Position.X, _editorService.GetTerrainHeightAt(prop.Position) + offset, prop.Position.Z);
			}
		}
		PropMultiMeshManager.Instance?.MarkDirty(norm);

		foreach (var unit in AllUnits)
		{
			if (GodotObject.IsInstanceValid(unit) && MatchesEntityOrAssetKey(unit, norm))
			{
				unit.UpdateModelYOffset(offset);
			}
		}

		if (_editorPreviewNode != null && GodotObject.IsInstanceValid(_editorPreviewNode) && MatchesEntityOrAssetKey(_editorPreviewNode, norm))
		{
			if (_editorPreviewNode is Prop3D previewProp)
			{
				previewProp.UpdateVisualYOffset(offset);
			}
			else if (_editorPreviewNode is Unit3D previewUnit)
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

	public float GetModelCollisionCircleRatio(object objOrId)
	{
		if (objOrId == null) return 1.0f;
		string primaryKey = GetSelectedEntityOrAssetKey(objOrId);
		string normPrimary = NormalizeModelAssetKey(primaryKey);
		if (!string.IsNullOrEmpty(normPrimary) && ModelCollisionCircleRatios.TryGetValue(normPrimary, out float val1))
			return val1;

		string assetKey = GetModelAssetKey(objOrId);
		string normAsset = NormalizeModelAssetKey(assetKey);
		if (!string.IsNullOrEmpty(normAsset) && ModelCollisionCircleRatios.TryGetValue(normAsset, out float val2))
			return val2;

		if (!string.IsNullOrEmpty(primaryKey))
		{
			if (UnitRegistry.TryGetValue(primaryKey, out var meta) && meta.CollisionCircle > 0f) return meta.CollisionCircle;
			if (ResourceRegistry.TryGetValue(primaryKey, out var resMeta) && resMeta.CollisionCircle > 0f) return resMeta.CollisionCircle;
			if (PropRegistry.TryGetValue(primaryKey, out var propMeta) && propMeta.CollisionCircle > 0f) return propMeta.CollisionCircle;
		}

		return 1.0f;
	}

	public void SetModelCollisionCircleRatio(string assetKey, float ratio)
	{
		string norm = NormalizeModelAssetKey(assetKey);
		if (string.IsNullOrEmpty(norm)) return;

		ModelCollisionCircleRatios[norm] = ratio;

		string modelAsset = GetModelAssetKey(assetKey);
		if (!string.IsNullOrEmpty(modelAsset))
		{
			ModelCollisionCircleRatios[NormalizeModelAssetKey(modelAsset)] = ratio;
		}

		UpdateCollisionRadiiForAsset(norm);

		_modelCollisionCircleSavePending = true;
		EditorHasUnsavedChanges = true;
	}

	public float GetModelBrightness(object objOrId)
	{
		if (objOrId == null) return 1.0f;
		string primaryKey = GetSelectedEntityOrAssetKey(objOrId);
		string normPrimary = NormalizeModelAssetKey(primaryKey);
		if (!string.IsNullOrEmpty(normPrimary) && ModelBrightness.TryGetValue(normPrimary, out float val1))
			return Mathf.Clamp(val1, 0.10f, 1.75f);

		string assetKey = GetModelAssetKey(objOrId);
		string normAsset = NormalizeModelAssetKey(assetKey);
		if (!string.IsNullOrEmpty(normAsset) && ModelBrightness.TryGetValue(normAsset, out float val2))
			return Mathf.Clamp(val2, 0.10f, 1.75f);

		if (!string.IsNullOrEmpty(primaryKey))
		{
			if (UnitRegistry.TryGetValue(primaryKey, out var meta) && meta.Brightness > 0f) return Mathf.Clamp(meta.Brightness, 0.10f, 1.75f);
			if (ResourceRegistry.TryGetValue(primaryKey, out var resMeta) && resMeta.Brightness > 0f) return Mathf.Clamp(resMeta.Brightness, 0.10f, 1.75f);
			if (PropRegistry.TryGetValue(primaryKey, out var propMeta) && propMeta.Brightness > 0f) return Mathf.Clamp(propMeta.Brightness, 0.10f, 1.75f);
		}

		return 1.0f;
	}

	public void SetModelBrightness(string assetKey, float brightness)
	{
		string norm = NormalizeModelAssetKey(assetKey);
		if (string.IsNullOrEmpty(norm)) return;

		float k = Mathf.Clamp(brightness, 0.10f, 1.75f);
		ModelBrightness[norm] = k;
		UpdateMaterialOverridesForAsset(norm);

		_modelYOffsetSavePending = true;
		EditorHasUnsavedChanges = true;
	}

	public Color GetModelColorTint(object objOrId)
	{
		if (objOrId == null) return new Color(1.0f, 1.0f, 1.0f);
		string primaryKey = GetSelectedEntityOrAssetKey(objOrId);
		string normPrimary = NormalizeModelAssetKey(primaryKey);
		if (!string.IsNullOrEmpty(normPrimary) && ModelColorTint.TryGetValue(normPrimary, out Color c1))
			return c1;

		string assetKey = GetModelAssetKey(objOrId);
		string normAsset = NormalizeModelAssetKey(assetKey);
		if (!string.IsNullOrEmpty(normAsset) && ModelColorTint.TryGetValue(normAsset, out Color c2))
			return c2;

		if (!string.IsNullOrEmpty(primaryKey))
		{
			if (UnitRegistry.TryGetValue(primaryKey, out var meta) && !string.IsNullOrEmpty(meta.Tint) && Color.HtmlIsValid(meta.Tint)) return Color.FromHtml(meta.Tint);
			if (ResourceRegistry.TryGetValue(primaryKey, out var resMeta) && !string.IsNullOrEmpty(resMeta.Tint) && Color.HtmlIsValid(resMeta.Tint)) return Color.FromHtml(resMeta.Tint);
			if (PropRegistry.TryGetValue(primaryKey, out var propMeta) && !string.IsNullOrEmpty(propMeta.Tint) && Color.HtmlIsValid(propMeta.Tint)) return Color.FromHtml(propMeta.Tint);
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

	public bool GetModelGenerateNormals(object objOrId)
	{
		if (objOrId == null) return true;
		string primaryKey = GetSelectedEntityOrAssetKey(objOrId);
		string normPrimary = NormalizeModelAssetKey(primaryKey);
		if (!string.IsNullOrEmpty(normPrimary) && ModelGenerateNormals.TryGetValue(normPrimary, out bool b1))
			return b1;

		string assetKey = GetModelAssetKey(objOrId);
		string normAsset = NormalizeModelAssetKey(assetKey);
		if (!string.IsNullOrEmpty(normAsset) && ModelGenerateNormals.TryGetValue(normAsset, out bool b2))
			return b2;

		if (!string.IsNullOrEmpty(primaryKey))
		{
			if (UnitRegistry.TryGetValue(primaryKey, out var meta)) return meta.RecalculateNormals;
			if (ResourceRegistry.TryGetValue(primaryKey, out var resMeta)) return resMeta.RecalculateNormals;
			if (PropRegistry.TryGetValue(primaryKey, out var propMeta)) return propMeta.RecalculateNormals;
		}

		return true;
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

	public bool GetModelIgnorePlayerColor(object objOrId)
	{
		if (objOrId == null) return false;
		string primaryKey = GetSelectedEntityOrAssetKey(objOrId);
		string normPrimary = NormalizeModelAssetKey(primaryKey);
		if (!string.IsNullOrEmpty(normPrimary) && ModelIgnorePlayerColor.TryGetValue(normPrimary, out bool b1))
			return b1;

		string assetKey = GetModelAssetKey(objOrId);
		string normAsset = NormalizeModelAssetKey(assetKey);
		if (!string.IsNullOrEmpty(normAsset) && ModelIgnorePlayerColor.TryGetValue(normAsset, out bool b2))
			return b2;

		if (!string.IsNullOrEmpty(primaryKey))
		{
			if (UnitRegistry.TryGetValue(primaryKey, out var meta) && meta.IgnorePlayerColor) return true;
			if (ResourceRegistry.TryGetValue(primaryKey, out var resMeta) && resMeta.IgnorePlayerColor) return true;
			if (PropRegistry.TryGetValue(primaryKey, out var propMeta) && propMeta.IgnorePlayerColor) return true;
		}

		string lookupKey = !string.IsNullOrEmpty(normPrimary) ? normPrimary : normAsset;
		if (!string.IsNullOrEmpty(lookupKey))
		{
			var modelNode = ModelCache.GetModel(lookupKey) as Node;
			if (modelNode != null && !PlayerColorShaderManager.ModelHasPlayerMask(modelNode))
			{
				ModelIgnorePlayerColor[lookupKey] = true;
				_modelYOffsetSavePending = true;
				EditorHasUnsavedChanges = true;
				return true;
			}
		}

		return false;
	}

	public void SetModelIgnorePlayerColor(string assetKey, bool ignorePlayerColor)
	{
		string norm = NormalizeModelAssetKey(assetKey);
		if (string.IsNullOrEmpty(norm)) return;

		ModelIgnorePlayerColor[norm] = ignorePlayerColor;
		_modelYOffsetSavePending = true;
		EditorHasUnsavedChanges = true;
		UpdateMaterialOverridesForAsset(norm);
	}

	public void UpdateMaterialOverridesForAsset(string normAssetKey)
	{
		if (string.IsNullOrEmpty(normAssetKey)) return;

		float brightness = GetModelBrightness(normAssetKey);
		Color tint = GetModelColorTint(normAssetKey);
		bool generateNormals = GetModelGenerateNormals(normAssetKey);
		bool ignorePlayerColor = GetModelIgnorePlayerColor(normAssetKey);

		foreach (var prop in AllProps)
		{
			if (GodotObject.IsInstanceValid(prop) && MatchesEntityOrAssetKey(prop, normAssetKey))
			{
				ApplyMaterialOverridesToNode(prop, brightness, tint, generateNormals);
			}
		}

		foreach (var unit in AllUnits)
		{
			if (GodotObject.IsInstanceValid(unit) && MatchesEntityOrAssetKey(unit, normAssetKey))
			{
				ApplyMaterialOverridesToNode(unit, brightness, tint, generateNormals);
				Realm.Godot.Utils.PlayerColorShaderManager.SetIgnorePlayerColor(unit, ignorePlayerColor);
				if (!ignorePlayerColor)
				{
					unit.UpdatePlayerColorVisual();
				}
			}
		}

		if (_editorPreviewNode != null && GodotObject.IsInstanceValid(_editorPreviewNode) && MatchesEntityOrAssetKey(_editorPreviewNode, normAssetKey))
		{
			ApplyMaterialOverridesToNode(_editorPreviewNode, brightness, tint, generateNormals);
			Realm.Godot.Utils.PlayerColorShaderManager.SetIgnorePlayerColor(_editorPreviewNode, ignorePlayerColor);
		}

		PropMultiMeshManager.Instance?.UpdateMaterialOverrides(normAssetKey);
		PropMultiMeshManager.Instance?.MarkDirty(normAssetKey);
	}

	public void ApplyAllGlobalOverridesToObject(object objOrNode)
	{
		if (objOrNode == null) return;

		if (objOrNode is Unit3D unit && GodotObject.IsInstanceValid(unit))
		{
			float yOffset = GetModelYOffset(unit);
			if (yOffset != 0f)
			{
				unit.Position = new Vector3(unit.Position.X, _editorService.GetTerrainHeightAt(unit.Position) + yOffset, unit.Position.Z);
				unit.UpdateModelYOffset(yOffset);
			}

			float circleRatio = GetModelCollisionCircleRatio(unit);
			unit.UpdateCollisionCircleScale(circleRatio);
			if (unit.Entity != default && EcsWorld.IsAlive(unit.Entity))
			{
				float autoDetected = GetOrCalculateObstacleRadius(unit.UnitId, unit, unit.IsBuilding);
				float baseRadius = autoDetected * circleRatio;
				if (EcsWorld.Has<Realm.Ecs.Components.Core.CollisionRadius>(unit.Entity))
				{
					EcsWorld.Set(unit.Entity, new Realm.Ecs.Components.Core.CollisionRadius(baseRadius));
				}
				else
				{
					EcsWorld.Add(unit.Entity, new Realm.Ecs.Components.Core.CollisionRadius(baseRadius));
				}
			}

			float brightness = GetModelBrightness(unit);
			Color tint = GetModelColorTint(unit);
			bool generateNormals = GetModelGenerateNormals(unit);
			bool ignorePlayerColor = GetModelIgnorePlayerColor(unit);
			Realm.Godot.Utils.PlayerColorShaderManager.SetIgnorePlayerColor(unit, ignorePlayerColor);
			if (MathF.Abs(brightness - 1.0f) > 0.001f || generateNormals || tint != new Color(1.0f, 1.0f, 1.0f))
			{
				ApplyMaterialOverridesToNode(unit, brightness, tint, generateNormals);
			}
		}
		else if (objOrNode is Prop3D prop && GodotObject.IsInstanceValid(prop))
		{
			float yOffset = GetModelYOffset(prop);
			if (yOffset != 0f)
			{
				prop.Position = new Vector3(prop.Position.X, _editorService.GetTerrainHeightAt(prop.Position) + yOffset, prop.Position.Z);
				prop.UpdateVisualYOffset(yOffset);
			}

			float circleRatio = GetModelCollisionCircleRatio(prop);
			prop.UpdateCollisionCircleScale(circleRatio);
			if (prop.Entity != default && EcsWorld.IsAlive(prop.Entity))
			{
				float autoDetected = GetOrCalculateObstacleRadius(prop.PropId, prop);
				float baseRadius = autoDetected * circleRatio;
				if (EcsWorld.Has<Realm.Ecs.Components.Core.CollisionRadius>(prop.Entity))
				{
					EcsWorld.Set(prop.Entity, new Realm.Ecs.Components.Core.CollisionRadius(baseRadius));
				}
				else
				{
					EcsWorld.Add(prop.Entity, new Realm.Ecs.Components.Core.CollisionRadius(baseRadius));
				}
			}

			float brightness = GetModelBrightness(prop);
			Color tint = GetModelColorTint(prop);
			bool generateNormals = GetModelGenerateNormals(prop);
			if (MathF.Abs(brightness - 1.0f) > 0.001f || generateNormals || tint != new Color(1.0f, 1.0f, 1.0f))
			{
				ApplyMaterialOverridesToNode(prop, brightness, tint, generateNormals);
			}
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

		Realm.Godot.Utils.PlayerColorShaderManager.SetBrightnessAndTint(node, brightness, tint);

		var meshNodes = FindMeshInstancesRecursive(node);
		foreach (var meshInst in meshNodes)
		{
			string nameStr = meshInst.Name.ToString();
			if (nameStr.StartsWith("_selection", StringComparison.OrdinalIgnoreCase)
				|| nameStr.StartsWith("Selection", StringComparison.OrdinalIgnoreCase)
				|| nameStr.StartsWith("_hover", StringComparison.OrdinalIgnoreCase)
				|| nameStr.StartsWith("Hover", StringComparison.OrdinalIgnoreCase)
				|| nameStr.StartsWith("BrushIndicator", StringComparison.OrdinalIgnoreCase)
				|| nameStr.StartsWith("DropShadow", StringComparison.OrdinalIgnoreCase)
				|| nameStr.Contains("SelectionRing", StringComparison.OrdinalIgnoreCase)
				|| nameStr.Contains("HoverRing", StringComparison.OrdinalIgnoreCase)) continue;

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
			if (GodotObject.IsInstanceValid(prop) && MatchesEntityOrAssetKey(prop, normAssetKey))
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
			if (GodotObject.IsInstanceValid(unit) && MatchesEntityOrAssetKey(unit, normAssetKey))
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

		if (_editorPreviewNode != null && GodotObject.IsInstanceValid(_editorPreviewNode) && MatchesEntityOrAssetKey(_editorPreviewNode, normAssetKey))
		{
			if (_editorPreviewNode is Prop3D previewProp)
			{
				previewProp.UpdateCollisionCircleScale(ratio);
			}
			else if (_editorPreviewNode is Unit3D previewUnit)
			{
				previewUnit.UpdateCollisionCircleScale(ratio);
			}
		}
	}

	private const float MaxSafeModelYOffset = 50f;
	private const float MinSafeModelCollisionRatio = 0.1f;
	private const float MaxSafeModelCollisionRatio = 10f;

	private bool IsValidModelYOffset(string assetKey, float val)
	{
		if (!float.IsFinite(val) || Math.Abs(val) > MaxSafeModelYOffset)
		{
			GD.PushWarning($"Ignoring invalid y_offset {val} for model '{assetKey}' (|offset| > {MaxSafeModelYOffset}).");
			return false;
		}
		return true;
	}

	private bool IsValidModelCollisionRatio(string assetKey, float val)
	{
		if (!float.IsFinite(val) || val < MinSafeModelCollisionRatio || val > MaxSafeModelCollisionRatio)
		{
			GD.PushWarning($"Ignoring invalid collision_circle_ratio {val} for model '{assetKey}' (expected {MinSafeModelCollisionRatio}..{MaxSafeModelCollisionRatio}).");
			return false;
		}
		return true;
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
			LoadUnitMetadata(mapDir);
			string metadataPath = System.IO.Path.Combine(mapDir, "metadata.json");
			if (!System.IO.File.Exists(metadataPath)) return;

			string jsonText = System.IO.File.ReadAllText(metadataPath);
			if (string.IsNullOrWhiteSpace(jsonText)) return;

			var root = System.Text.Json.Nodes.JsonNode.Parse(jsonText) as System.Text.Json.Nodes.JsonObject;
			if (root == null) return;

			ModelYOffsets.Clear();
			ModelCollisionCircleRatios.Clear();
			ModelObstacleRadii.Clear();
			ModelBrightness.Clear();
			ModelGenerateNormals.Clear();
			ModelIgnorePlayerColor.Clear();

			if (root.ContainsKey("ModelOffsets") && root["ModelOffsets"] is System.Text.Json.Nodes.JsonObject offsetsObj)
			{
				foreach (var kvp in offsetsObj)
				{
					if (kvp.Value != null && float.TryParse(kvp.Value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float val) && IsValidModelYOffset(kvp.Key, val))
					{
						ModelYOffsets[NormalizeModelAssetKey(kvp.Key)] = val;
					}
				}
			}

			if (root.ContainsKey("ModelCollisionCircleRatios") && root["ModelCollisionCircleRatios"] is System.Text.Json.Nodes.JsonObject circlesObj)
			{
				foreach (var kvp in circlesObj)
				{
					if (kvp.Value != null && float.TryParse(kvp.Value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float val) && IsValidModelCollisionRatio(kvp.Key, val))
					{
						ModelCollisionCircleRatios[NormalizeModelAssetKey(kvp.Key)] = val;
					}
				}
			}

			if (root.ContainsKey("ModelObstacleRadii") && root["ModelObstacleRadii"] is System.Text.Json.Nodes.JsonObject radiiObj)
			{
				foreach (var kvp in radiiObj)
				{
					if (kvp.Value != null && float.TryParse(kvp.Value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float val) && val > 0f)
					{
						ModelObstacleRadii[NormalizeModelAssetKey(kvp.Key)] = val;
					}
				}
			}

			if (root.ContainsKey("ModelBrightness") && root["ModelBrightness"] is System.Text.Json.Nodes.JsonObject mbObj)
			{
				foreach (var kvp in mbObj)
				{
					if (kvp.Value != null && float.TryParse(kvp.Value.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
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

			if (root.ContainsKey("ModelIgnorePlayerColor") && root["ModelIgnorePlayerColor"] is System.Text.Json.Nodes.JsonObject ipcObj)
			{
				foreach (var kvp in ipcObj)
				{
					if (kvp.Value != null && bool.TryParse(kvp.Value.ToString(), out bool val))
					{
						ModelIgnorePlayerColor[NormalizeModelAssetKey(kvp.Key)] = val;
					}
				}
			}

			string[] entityArrays = new[] { "CustomUnits", "CustomBuildings", "CustomResources", "CustomProps" };
			foreach (var arrKey in entityArrays)
			{
				if (root.ContainsKey(arrKey) && root[arrKey] is System.Text.Json.Nodes.JsonArray arr)
				{
					foreach (var item in arr)
					{
						if (item is System.Text.Json.Nodes.JsonObject uObj && uObj.ContainsKey("UnitId"))
						{
							string uId = uObj["UnitId"]?.ToString() ?? "";
							string normKey = NormalizeModelAssetKey(uId);
							if (uObj.ContainsKey("YOffset") && float.TryParse(uObj["YOffset"]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float yVal))
							{
								ModelYOffsets[normKey] = yVal;
							}
							if (uObj.ContainsKey("CollisionCircle") && float.TryParse(uObj["CollisionCircle"]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float rVal))
							{
								ModelCollisionCircleRatios[normKey] = rVal;
							}
							if (uObj.ContainsKey("Brightness") && float.TryParse(uObj["Brightness"]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float brightVal))
							{
								ModelBrightness[normKey] = brightVal;
							}
							if (uObj.ContainsKey("Tint") && uObj["Tint"]?.ToString() is string tintStr && !string.IsNullOrEmpty(tintStr))
							{
								ModelColorTint[normKey] = Color.FromString(tintStr, new Color(1, 1, 1));
							}
							if (uObj.ContainsKey("RecalculateNormals") && bool.TryParse(uObj["RecalculateNormals"]?.ToString(), out bool gnVal))
							{
								ModelGenerateNormals[normKey] = gnVal;
							}
							if (uObj.ContainsKey("IgnorePlayerColor") && bool.TryParse(uObj["IgnorePlayerColor"]?.ToString(), out bool ipcVal))
							{
								ModelIgnorePlayerColor[normKey] = ipcVal;
							}
							else if (uObj.ContainsKey("ignore_player_color") && bool.TryParse(uObj["ignore_player_color"]?.ToString(), out bool ipcVal2))
							{
								ModelIgnorePlayerColor[normKey] = ipcVal2;
							}
						}
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
								if (itemObj.ContainsKey("y_offset") && float.TryParse(itemObj["y_offset"]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float yVal))
								{
									if (IsValidModelYOffset(itemKvp.Key, yVal))
									{
										ModelYOffsets[NormalizeModelAssetKey(itemKvp.Key)] = yVal;
									}
								}
								if (itemObj.ContainsKey("collision_circle_ratio") && float.TryParse(itemObj["collision_circle_ratio"]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float rVal))
								{
									if (IsValidModelCollisionRatio(itemKvp.Key, rVal))
									{
										ModelCollisionCircleRatios[NormalizeModelAssetKey(itemKvp.Key)] = rVal;
									}
								}
								if (itemObj.ContainsKey("collision_radius") && float.TryParse(itemObj["collision_radius"]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float radVal) && radVal > 0f)
								{
									ModelObstacleRadii[NormalizeModelAssetKey(itemKvp.Key)] = radVal;
								}
								if (itemObj.ContainsKey("brightness") && float.TryParse(itemObj["brightness"]?.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float brightVal))
								{
									ModelBrightness[NormalizeModelAssetKey(itemKvp.Key)] = brightVal;
								}
								if (itemObj.ContainsKey("generate_normals") && bool.TryParse(itemObj["generate_normals"]?.ToString(), out bool gnVal))
								{
									ModelGenerateNormals[NormalizeModelAssetKey(itemKvp.Key)] = gnVal;
								}
								string normKey = NormalizeModelAssetKey(itemKvp.Key);
								if (itemObj.ContainsKey("ignore_player_color") && bool.TryParse(itemObj["ignore_player_color"]?.ToString(), out bool ipcVal))
								{
									ModelIgnorePlayerColor[normKey] = ipcVal;
								}
								else if (itemObj.ContainsKey("IgnorePlayerColor") && bool.TryParse(itemObj["IgnorePlayerColor"]?.ToString(), out bool ipcVal2))
								{
									ModelIgnorePlayerColor[normKey] = ipcVal2;
								}
								else
								{
									var modelNode = ModelCache.GetModel(normKey) as Node;
									if (modelNode != null && !PlayerColorShaderManager.ModelHasPlayerMask(modelNode))
									{
										ModelIgnorePlayerColor[normKey] = true;
										_modelYOffsetSavePending = true;
										EditorHasUnsavedChanges = true;
									}
								}
							}
						}
					}
				}
			}

			foreach (var key in ModelBrightness.Keys
				.Concat(ModelGenerateNormals.Keys)
				.Concat(ModelIgnorePlayerColor.Keys)
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

	public void RefreshAllPlacedObjectModels(string targetId = null)
	{
		Prop3D.ClearModelPathCache();

		foreach (var unit in AllUnits)
		{
			if (!GodotObject.IsInstanceValid(unit)) continue;
			if (string.IsNullOrEmpty(targetId) || string.Equals(unit.UnitId, targetId, StringComparison.OrdinalIgnoreCase))
			{
				string targetModel = unit.UnitId;
				bool isBuilding = unit.IsBuilding;
				if (UnitRegistry.TryGetValue(unit.UnitId, out var meta) && !string.IsNullOrEmpty(meta.ModelPath))
				{
					targetModel = meta.ModelPath;
					if (meta.Speed == 0f) isBuilding = true;
				}
				else if (ResourceRegistry.TryGetValue(unit.UnitId, out var resMeta) && !string.IsNullOrEmpty(resMeta.ModelPath))
				{
					targetModel = resMeta.ModelPath;
				}
				else if (PropRegistry.TryGetValue(unit.UnitId, out var propMeta) && !string.IsNullOrEmpty(propMeta.ModelPath))
				{
					targetModel = propMeta.ModelPath;
				}
				else if (!string.IsNullOrEmpty(unit.ModelPath))
				{
					targetModel = unit.ModelPath;
				}
				string modelPath = GetFallbackModelPath(targetModel, isBuilding);
				unit.LoadModel(modelPath);
			}
		}

		foreach (var prop in AllProps)
		{
			if (!GodotObject.IsInstanceValid(prop)) continue;
			if (string.IsNullOrEmpty(targetId) || string.Equals(prop.PropId, targetId, StringComparison.OrdinalIgnoreCase))
			{
				prop.RefreshPropVisual();
			}
		}

		if (!string.IsNullOrEmpty(targetId))
		{
			PropMultiMeshManager.Instance?.MarkDirty(targetId);
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

			System.Text.Json.Nodes.JsonObject radiiObj = new System.Text.Json.Nodes.JsonObject();
			foreach (var kvp in ModelObstacleRadii) radiiObj[kvp.Key] = kvp.Value;
			root["ModelObstacleRadii"] = radiiObj;



			System.Text.Json.Nodes.JsonObject mbObj = new System.Text.Json.Nodes.JsonObject();
			foreach (var kvp in ModelBrightness) mbObj[kvp.Key] = kvp.Value;
			root["ModelBrightness"] = mbObj;

			System.Text.Json.Nodes.JsonObject gnObj = new System.Text.Json.Nodes.JsonObject();
			foreach (var kvp in ModelGenerateNormals) gnObj[kvp.Key] = kvp.Value;
			root["ModelGenerateNormals"] = gnObj;

			System.Text.Json.Nodes.JsonObject ipcObj = new System.Text.Json.Nodes.JsonObject();
			foreach (var kvp in ModelIgnorePlayerColor) ipcObj[kvp.Key] = kvp.Value;
			root["ModelIgnorePlayerColor"] = ipcObj;

			string[] entitySaveArrays = new[] { "CustomUnits", "CustomBuildings", "CustomResources", "CustomProps" };
			foreach (var arrKey in entitySaveArrays)
			{
				if (root.ContainsKey(arrKey) && root[arrKey] is System.Text.Json.Nodes.JsonArray arr)
				{
					foreach (var item in arr)
					{
						if (item is System.Text.Json.Nodes.JsonObject uObj && uObj.ContainsKey("UnitId"))
						{
							string uId = uObj["UnitId"]?.ToString() ?? "";
							string normKey = NormalizeModelAssetKey(uId);
							if (ModelYOffsets.TryGetValue(normKey, out float yVal)) uObj["YOffset"] = yVal;
							if (ModelCollisionCircleRatios.TryGetValue(normKey, out float rVal)) uObj["CollisionCircle"] = rVal;
							if (ModelBrightness.TryGetValue(normKey, out float bVal)) uObj["Brightness"] = bVal;
							if (ModelColorTint.TryGetValue(normKey, out Color tColor)) uObj["Tint"] = "#" + tColor.ToHtml(false);
							if (ModelGenerateNormals.TryGetValue(normKey, out bool gnVal)) uObj["RecalculateNormals"] = gnVal;
							if (ModelIgnorePlayerColor.TryGetValue(normKey, out bool ipcVal)) uObj["IgnorePlayerColor"] = ipcVal;
						}
					}
				}
			}

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
							bool hasRadius = ModelObstacleRadii.TryGetValue(normKey, out float radVal);
							bool hasBright = ModelBrightness.TryGetValue(normKey, out float brightVal);
							bool hasGn = ModelGenerateNormals.TryGetValue(normKey, out bool gnVal);
							bool hasIpc = ModelIgnorePlayerColor.TryGetValue(normKey, out bool ipcVal);

							if (hasY || hasRatio || hasRadius || hasBright || hasGn || hasIpc)
							{
								var nodeVal = catDict[key];
								if (nodeVal is System.Text.Json.Nodes.JsonObject itemObj)
								{
									if (hasY) itemObj["y_offset"] = yVal;
									if (hasRatio) itemObj["collision_circle_ratio"] = rVal;
									if (hasRadius) itemObj["collision_radius"] = radVal;
									if (hasBright) itemObj["brightness"] = brightVal;
									if (hasGn) itemObj["generate_normals"] = gnVal;
									if (hasIpc) itemObj["ignore_player_color"] = ipcVal;
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
									if (hasRadius) newItemObj["collision_radius"] = radVal;
									if (hasBright) newItemObj["brightness"] = brightVal;
									if (hasGn) newItemObj["generate_normals"] = gnVal;
									if (hasIpc) newItemObj["ignore_player_color"] = ipcVal;
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
		PropMultiMeshManager.Instance?.Clear();
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
		HideSelectionHighlight();

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
		HideCoordinateSelectionOutline();
		MapEditorHUD.Instance?.RefreshCoordinateListExternal();

		MapEditorHUD.Instance?.ClearTempWorkspaceExternal();
		MapEditorHUD.Instance?.GenerateVSCodeFilesExternal();
		MapEditorHUD.Instance?.ShowFeedbackExternal("Map reset: cleared all entities & terrain");
		MapEditorHUD.Instance?.RegenerateMinimap();
	}

	private bool IsMouseOverUI()
	{
		if (SettingsMenu.IsOpen)
		{
			return true;
		}

		if (GodotObject.IsInstanceValid(MapEditorHUD.Instance))
		{
			return MapEditorHUD.Instance.IsMouseOverUI(GetViewport().GetMousePosition());
		}

		var hoveredControl = GetViewport().GuiGetHoveredControl();
		if (hoveredControl != null && (InGameHUD.Instance == null || hoveredControl != InGameHUD.Instance))
		{
			return true;
		}

		var mousePos = GetViewport().GetMousePosition();
		var viewportSize = GetViewport().GetVisibleRect().Size;
		
		if (mousePos.Y < 75) return true;
		if (mousePos.Y > viewportSize.Y - 245) return true;
		if (mousePos.X < 225 || mousePos.X > viewportSize.X - 225) return true;
		
		// In-game HUD relies on Godot's built-in Control input consumption.
		// If an event reaches _UnhandledInput, it means the UI did not consume it,
		// so it is a valid world click. Hardcoded bounds here falsely block clicks.
		return false;
	}

	private long _lastTerrainMeshRebuildMs = long.MinValue;
	private Rect2I? _terrainFlushRegion;
	private bool _terrainGeometryDirty;
	private const float TerrainMeshRebuildPeriodMs = 33.3f;

	private void ApplyContinuousTerrainEditing(Vector3 worldPos, float delta, bool isFirstClick = false)
	{
		if (GroundTerrain == null) return;

		var positions = new List<Vector3> { worldPos };
		if (EditorMirrorMode != MirrorMode.None)
		{
			foreach (var t in GetMirroredTransforms(worldPos, 0.0f))
			{
				positions.Add(t.Position);
			}
		}

		int pathingMask = 0;
		bool pathingAdd = true;
		if (ActiveEditorTool == EditorTool.PaintPathing && MapEditorHUD.Instance != null)
		{
			pathingMask = MapEditorHUD.Instance.GetSelectedPathingMask();
			pathingAdd = MapEditorHUD.Instance.IsPathingAddMode();
		}

		bool applyGround = MapEditorHUD.Instance?.IsApplyGroundTextureEnabled() ?? true;
		bool applyCliff = MapEditorHUD.Instance?.IsApplyCliffTextureEnabled() ?? true;

		var frameAffectedRegions = new List<Rect2I>();
		bool anyHeightsModified = false;
		bool anyPathingModified = false;
		bool anyModified = false;

		foreach (var pos in positions)
		{
			var result = _editorService.ApplyContinuousTerrainEditing(
				pos, delta,
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

				frameAffectedRegions.Add(affected);
				if (result.HeightsModified) anyHeightsModified = true;
				if (result.PathingModified) anyPathingModified = true;
				anyModified = true;
			}
		}

		if (anyModified)
		{
			// Limit the number of full mesh/water rebuilds while dragging so painting stays smooth
			// on large maps, while the terrain data itself is updated every frame.
			long nowMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
			if (isFirstClick || nowMs - _lastTerrainMeshRebuildMs >= TerrainMeshRebuildPeriodMs)
			{
				_lastTerrainMeshRebuildMs = nowMs;
				GroundTerrain.UpdateMeshAndPhysics(false, false, frameAffectedRegions, anyHeightsModified); // false for physics rebuild during drag
				if (anyHeightsModified)
				{
					foreach (var affected in frameAffectedRegions)
					{
						AlignAllEntitiesToTerrain(affected);
					}
				}
				if (anyPathingModified && PathingOverlayVisible)
				{
					RebuildPathingOverlay();
				}
			}
			EditorHasUnsavedChanges = true;
		}
	}

	public bool IsStaticPropAsset(string propIdOrEntityId)
	{
		if (string.IsNullOrEmpty(propIdOrEntityId)) return false;

		if (PropRegistry.ContainsKey(propIdOrEntityId) || ResourceRegistry.ContainsKey(propIdOrEntityId))
			return true;

		if (UnitRegistry.TryGetValue(propIdOrEntityId, out var unitMeta))
		{
			if (unitMeta.ArmorType == "building") return false;
			return false;
		}

		string wsPath = Godot.ProjectSettings.GlobalizePath("user://temp_map_workspace");
		string metadataPath = System.IO.Path.Combine(wsPath, "metadata.json");
		if (System.IO.File.Exists(metadataPath))
		{
			try
			{
				string json = System.IO.File.ReadAllText(metadataPath);
				using var doc = System.Text.Json.JsonDocument.Parse(json);
				if (doc.RootElement.TryGetProperty("Assets", out var assets) && assets.TryGetProperty("glb", out var glb))
				{
					foreach (var catProp in glb.EnumerateObject())
					{
						string catName = catProp.Name.ToLowerInvariant();
						if (catProp.Value.ValueKind == System.Text.Json.JsonValueKind.Object)
						{
							foreach (var modelProp in catProp.Value.EnumerateObject())
							{
								string modelName = modelProp.Name;
								string modelWithoutExt = System.IO.Path.GetFileNameWithoutExtension(modelName);
								if (modelName.Equals(propIdOrEntityId, StringComparison.OrdinalIgnoreCase) ||
									modelWithoutExt.Equals(propIdOrEntityId, StringComparison.OrdinalIgnoreCase))
								{
									if (catName == "units" || catName == "buildings") return false;
									if (catName == "resources" || catName == "props" || catName == "prop") return true;
								}
							}
						}
					}
				}
			}
			catch { }
		}

		string filename = System.IO.Path.GetFileName(propIdOrEntityId);
		if (!filename.EndsWith(".glb") && !filename.EndsWith(".gltf")) filename += ".glb";
		string[] subDirs = new[] { "resources", "props", "units", "building" };
		foreach (var sub in subDirs)
		{
			string candidate = System.IO.Path.Combine(wsPath, "Assets", "models", sub, filename);
			if (System.IO.File.Exists(candidate))
			{
				return sub == "resources" || sub == "props";
			}
		}

		return true;
	}

	public void DeleteStaticPropAtPosition(string propId, Vector3 hitPos)
	{
		if (EcsWorld == null) return;
		Entity targetEntity = Entity.Null;
		float minDistance = 2.0f;

		var propQuery = Realm.Ecs.Common.QueryCache.AllPropIdentityAndPositionQuery;
		EcsWorld.Query(in propQuery, (Entity entity, ref PropIdentity pid, ref Position pos) =>
		{
			if (EntityToProp3D.ContainsKey(entity)) return;
			if (!string.IsNullOrEmpty(propId) && !pid.PropId.Equals(propId, StringComparison.OrdinalIgnoreCase)) return;

			Vector3 wPos = new Vector3(pos.Value.X, pos.Value.Y, pos.Value.Z);
			float d = wPos.DistanceTo(hitPos);
			if (d < minDistance)
			{
				minDistance = d;
				targetEntity = entity;
			}
		});

		if (targetEntity != Entity.Null && EcsWorld.IsAlive(targetEntity))
		{
			string pid = EcsWorld.Get<PropIdentity>(targetEntity).PropId;
			EcsWorld.Destroy(targetEntity);
			PropMultiMeshManager.Instance?.MarkDirty(pid);
		}
	}

	public Prop3D SpawnPropExternal(string propId, Vector3 position)
	{
		float rotY = IsMapEditorMode ? EditorPlacementRotation : 0f;
		float scale = IsMapEditorMode ? EditorPlacementScale : 1f;
		return SpawnPropExternalWithParams(propId, position, rotY, scale);
	}

	public Texture2D LoadDecalTexture(string decalId)
	{
		if (string.IsNullOrEmpty(decalId)) decalId = "logo";

		if (decalId.StartsWith("res://"))
		{
			try
			{
				if (ResourceLoader.Exists(decalId))
					return GD.Load<Texture2D>(decalId);
			}
			catch { }
		}

		string filename = System.IO.Path.GetFileName(decalId);
		string wsPath = ProjectSettings.GlobalizePath("user://temp_map_workspace");

		List<string> candidatePaths = new List<string>();

		if (System.IO.Path.IsPathRooted(decalId))
		{
			candidatePaths.Add(decalId);
		}
		else
		{
			candidatePaths.Add(System.IO.Path.Combine(wsPath, "Assets", "decals", filename));
			candidatePaths.Add(System.IO.Path.Combine(wsPath, decalId));
			if (!filename.Contains('.'))
			{
				candidatePaths.Add(System.IO.Path.Combine(wsPath, "Assets", "decals", filename + ".png"));
			}
			candidatePaths.Add(decalId);
		}

		foreach (var path in candidatePaths)
		{
			if (!string.IsNullOrEmpty(path) && System.IO.File.Exists(path))
			{
				try
				{
					var img = Image.LoadFromFile(path);
					if (img != null)
					{
						return ImageTexture.CreateFromImage(img);
					}
				}
				catch (Exception ex)
				{
					GD.PrintErr($"Failed to load decal image from '{path}': {ex.Message}");
				}
			}
		}

		return GD.Load<Texture2D>("res://icon.svg");
	}

	public string GetDecalTexturePath(string decalId)
	{
		if (string.IsNullOrEmpty(decalId))
		{
			decalId = "logo";
		}
		if (decalId.StartsWith("res://")) return decalId;

		string filename = System.IO.Path.GetFileName(decalId);
		string wsPath = ProjectSettings.GlobalizePath("user://temp_map_workspace");

		string candidate1 = System.IO.Path.Combine(wsPath, "Assets", "decals", filename);
		if (System.IO.File.Exists(candidate1)) return candidate1;

		if (!filename.Contains('.'))
		{
			string candidate2 = System.IO.Path.Combine(wsPath, "Assets", "decals", filename + ".png");
			if (System.IO.File.Exists(candidate2)) return candidate2;
		}

		if (System.IO.Path.IsPathRooted(decalId) && System.IO.File.Exists(decalId))
		{
			return decalId;
		}

		string candidate3 = System.IO.Path.Combine(wsPath, decalId);
		if (System.IO.File.Exists(candidate3)) return candidate3;

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
		PropMultiMeshManager.Instance?.MarkAllDirty();
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
				PropMultiMeshManager.Instance?.MarkDirty(prop.PropId);
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
	
	public Unit3D SpawnUnitExternal(string unitId, Vector3 position, bool isEnemy, float rotationY, float scale, int player = -1)
	{
		// Preserve the authored Y (saved maps, pasted/cloned/undone objects). Placement paths
		// that want feet-on-terrain snap the Y to the terrain before calling this method.
		int playerIndex = player >= 0 ? player : 0;
		bool actualIsEnemy = player >= 0 ? NetworkService.ArePlayerIndicesEnemies(LocalPlayerIndex, playerIndex) : isEnemy;
		if (!UnitRegistry.ContainsKey(unitId))
		{
			LoadUnitMetadata(!string.IsNullOrEmpty(CurrentMapDirectory) ? CurrentMapDirectory : Godot.ProjectSettings.GlobalizePath("user://temp_map_workspace"));
		}
		if (!UnitRegistry.ContainsKey(unitId))
		{
			string resolvedModelPath = GetFallbackModelPath(unitId, unitId.Contains("Buildings") || unitId.Contains("castle") || unitId.Contains("tower"));
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

		var playerOwner = GetPlayerEntityForPlayerIndex(playerIndex).AsPlayerEntity(EcsWorld);
		
		string targetModel = !string.IsNullOrEmpty(meta.ModelPath) ? meta.ModelPath : unitId;
		string modelPath = GetFallbackModelPath(targetModel, meta.Speed == 0f);

		string name = meta.Name;
		var entity = CreateEcsUnit(unitId, name, meta.MaxHp, meta.Damage, meta.Range, meta.Armor, meta.Speed, position, playerOwner);

		var unit3D = SpawnUnit3D(entity, unitId, modelPath, position, meta.Speed == 0f, actualIsEnemy, false, playerIndex);
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

		ApplyAllGlobalOverridesToObject(unit3D);

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
		if (ResourceRegistry.TryGetValue(propId, out var meta) && (meta.MaxCapacity > 0f || defaultAmount > 0f))
		{
			float amount = meta.MaxCapacity > 0f ? meta.MaxCapacity : defaultAmount;
			float harvestRate = meta.HarvestRate > 0f ? meta.HarvestRate : 10f;
			float growthRate = meta.GrowthRate;
			int maxWorkers = meta.MaxWorkers > 0 ? meta.MaxWorkers : 5;
			EcsWorld.Add(entity, new ResourceNode(Guid.Empty, amount, amount, harvestRate, growthRate, maxWorkers));
		}
		else if (defaultAmount > 0f)
		{
			EcsWorld.Add(entity, new ResourceNode(Guid.Empty, defaultAmount, defaultAmount, 10f, 0f, 5));
		}

		position.Y = _editorService.GetTerrainHeightAt(position);

		EcsWorld.Add(entity, new Realm.Ecs.Components.Tags.Prop());
		EcsWorld.Add(entity, new Realm.Ecs.Components.Core.Position(new System.Numerics.Vector3(position.X, position.Y, position.Z)));
		EcsWorld.Add(entity, new RotationY(rotationY));
		EcsWorld.Add(entity, new ModelScale(scale));
		EcsWorld.Add(entity, new CollisionScale(scale));

		string propAssetKey = NormalizeModelAssetKey(propId);
		float autoDetectedRadius = GetOrCalculateObstacleRadius(propId, null);
		float baseRadius = autoDetectedRadius * GetModelCollisionCircleRatio(propAssetKey);
		EcsWorld.Add(entity, new Realm.Ecs.Components.Core.CollisionRadius(baseRadius));

		if (!IsMapEditorMode && IsStaticPropAsset(propId))
		{
			PropMultiMeshManager.Instance?.MarkDirty(propId);
			return null;
		}

		var prop = new Prop3D();
		prop.Entity = entity;
		prop.PropId = propId;
		AddChild(prop);
		AllProps.Add(prop);
		PropMultiMeshManager.Instance?.MarkDirty(propId);

		EntityToProp3D[entity] = prop;

		prop.Position = position;
		prop.RotationDegrees = new Vector3(0.0f, rotationY, 0.0f);
		prop.Scale = Vector3.One * scale;
		
		ApplyAllGlobalOverridesToObject(prop);

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
			string propId = prop.PropId;
			AllProps.Remove(prop);
			EntityToProp3D.Remove(prop.Entity);
			if (EcsWorld.IsAlive(prop.Entity))
			{
				EcsWorld.Destroy(prop.Entity);
			}
			prop.QueueFree();
			PropMultiMeshManager.Instance?.MarkDirty(propId);
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
			var action = new ObjectDeleteAction("unit", unit.UnitId, unit.Position, unit.RotationDegrees.Y, unit.Scale.X, unit.IsEnemy, unit, unit.Player);
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
			string propId = prop.PropId;
			var action = new ObjectDeleteAction("prop", propId, prop.Position, prop.RotationDegrees.Y, prop.Scale.X, false, prop);
			AllProps.Remove(prop);
			EntityToProp3D.Remove(prop.Entity);
			if (EcsWorld.IsAlive(prop.Entity)) EcsWorld.Destroy(prop.Entity);
			prop.QueueFree();
			PropMultiMeshManager.Instance?.MarkDirty(propId);
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
		foreach (var p in AllProps)
		{
			if (GodotObject.IsInstanceValid(p))
			{
				float d = p.Position.DistanceTo(hitPos);
				if (d < closestPropDist)
				{
					closestPropDist = d;
					closestProp = p;
				}
			}
		}

		Entity closestStaticPropEntity = Entity.Null;
		string closestStaticPropId = null;
		Vector3 closestStaticPropPos = Vector3.Zero;
		float closestStaticPropRotY = 0f;
		float closestStaticPropScale = 1f;
		float closestStaticPropDist = 2.0f;

		if (EcsWorld != null)
		{
			var propQuery = Realm.Ecs.Common.QueryCache.AllPropIdentityAndPositionQuery;
			EcsWorld.Query(in propQuery, (Entity entity, ref PropIdentity pId, ref Position pPos) =>
			{
				if (EntityToProp3D.ContainsKey(entity)) return;
				Vector3 wPos = new Vector3(pPos.Value.X, pPos.Value.Y, pPos.Value.Z);
				float d = wPos.DistanceTo(hitPos);
				if (d < closestStaticPropDist)
				{
					closestStaticPropDist = d;
					closestStaticPropEntity = entity;
					closestStaticPropId = pId.PropId;
					closestStaticPropPos = wPos;
					closestStaticPropRotY = EcsWorld.Has<RotationY>(entity) ? EcsWorld.Get<RotationY>(entity).Value : 0f;
					closestStaticPropScale = EcsWorld.Has<ModelScale>(entity) ? EcsWorld.Get<ModelScale>(entity).Value : 1f;
				}
			});
		}

		Decal closestDecal = null;
		float closestDecalDist = 2.0f;
		foreach (var decalObj in AllDecals)
		{
			if (GodotObject.IsInstanceValid(decalObj))
			{
				float d = decalObj.GlobalPosition.DistanceTo(hitPos);
				if (d < closestDecalDist)
				{
					closestDecalDist = d;
					closestDecal = decalObj;
				}
			}
		}

		float minDistance = Mathf.Min(closestUnitDist, Mathf.Min(closestPropDist, Mathf.Min(closestStaticPropDist, closestDecalDist)));
		if (minDistance < 2.0f)
		{
			if (closestUnit != null && minDistance == closestUnitDist)
			{
				if (closestUnit == _selectedEditorObject) SelectedEditorObject = null;
				var action = new ObjectDeleteAction("unit", closestUnit.UnitId, closestUnit.Position, closestUnit.RotationDegrees.Y, closestUnit.Scale.X, closestUnit.IsEnemy, closestUnit, closestUnit.Player);
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
				string propId = closestProp.PropId;
				var action = new ObjectDeleteAction("prop", propId, closestProp.Position, closestProp.RotationDegrees.Y, closestProp.Scale.X, false, closestProp);
				AllProps.Remove(closestProp);
				EntityToProp3D.Remove(closestProp.Entity);
				if (EcsWorld.IsAlive(closestProp.Entity)) EcsWorld.Destroy(closestProp.Entity);
				closestProp.QueueFree();
				PropMultiMeshManager.Instance?.MarkDirty(propId);
				return action;
			}
			else if (closestStaticPropEntity != Entity.Null && minDistance == closestStaticPropDist)
			{
				var action = new ObjectDeleteAction("prop", closestStaticPropId, closestStaticPropPos, closestStaticPropRotY, closestStaticPropScale, false, null);
				EcsWorld.Destroy(closestStaticPropEntity);
				PropMultiMeshManager.Instance?.MarkDirty(closestStaticPropId);
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
					LoadUnitMetadata(!string.IsNullOrEmpty(CurrentMapDirectory) ? CurrentMapDirectory : Godot.ProjectSettings.GlobalizePath("user://temp_map_workspace"));
				}
				if (!UnitRegistry.ContainsKey(reqId))
				{
					string resolvedModelPath = GetFallbackModelPath(reqId, reqId.Contains("Buildings") || reqId.Contains("castle") || reqId.Contains("tower"));
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
					string targetModel = !string.IsNullOrEmpty(meta.ModelPath) ? meta.ModelPath : reqId;
					string modelPath = GetFallbackModelPath(targetModel, meta.Speed == 0f);

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
			ApplyAllGlobalOverridesToObject(_editorPreviewNode);

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

	public void SetUnitPlayerExternal(Unit3D unit, int playerIndex)
	{
		if (GodotObject.IsInstanceValid(unit) && EcsWorld.IsAlive(unit.Entity))
		{
			bool isEnemy = NetworkService.ArePlayerIndicesEnemies(LocalPlayerIndex, playerIndex);
			var playerOwner = GetPlayerEntityForPlayerIndex(playerIndex).AsPlayerEntity(EcsWorld);
			EcsWorld.Set(unit.Entity, new Owner(playerOwner));
			
			if (EcsWorld.Has<UnitOwnerPlayer>(unit.Entity))
				EcsWorld.Set(unit.Entity, new UnitOwnerPlayer(playerIndex));
			else
				EcsWorld.Add(unit.Entity, new UnitOwnerPlayer(playerIndex));

			if (EcsWorld.Has<UnitFaction>(unit.Entity))
				EcsWorld.Set(unit.Entity, new UnitFaction(isEnemy));
			else
				EcsWorld.Add(unit.Entity, new UnitFaction(isEnemy));

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
			unit.Player = playerIndex;
			unit.IsEnemy = isEnemy;
			unit.UpdatePlayerColorVisual();
			unit.IsSelected = unit.IsSelected;
		}
	}

	public void SetUnitTeamExternal(Unit3D unit, bool isEnemy)
	{
		int targetPlayer = isEnemy ? 1 : 0;
		if (LobbyManager.Instance != null && LobbyManager.Instance.PlayerList.Count > 0)
		{
			var enemyPlayer = LobbyManager.Instance.PlayerList.Find(p => NetworkService.ArePlayerIndicesEnemies(LocalPlayerIndex, p.Slot));
			if (isEnemy && enemyPlayer != null)
			{
				targetPlayer = enemyPlayer.Slot;
			}
		}
		SetUnitPlayerExternal(unit, targetPlayer);
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
			ring.Name = "_selection_ring_decal";
			ring.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
			ring.GIMode = GeometryInstance3D.GIModeEnum.Disabled;
			var torusMesh = new TorusMesh();
			torusMesh.InnerRadius = 2.5f;
			torusMesh.OuterRadius = 2.8f;
			ring.Mesh = torusMesh;
			ring.Position = new Vector3(0, 0.05f, 0);
			var material = new StandardMaterial3D();
			material.AlbedoColor = new Color(0.22f, 0.54f, 0.26f);
			material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
			material.DisableReceiveShadows = true;
			material.EmissionEnabled = false;
			material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
			ring.MaterialOverride = material;
			decal.AddChild(ring);
		}
	}

	private void UpdateDecalHoverRing(Decal decal, bool hovered)
	{
		if (!GodotObject.IsInstanceValid(decal)) return;
		var existing = decal.GetNodeOrNull<MeshInstance3D>("_hover_ring_decal");
		if (existing != null)
		{
			existing.QueueFree();
		}
		if (hovered && SelectedEditorObject != decal)
		{
			var ring = new MeshInstance3D();
			ring.Name = "_hover_ring_decal";
			ring.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
			ring.GIMode = GeometryInstance3D.GIModeEnum.Disabled;
			var torusMesh = new TorusMesh();
			torusMesh.InnerRadius = 2.5f;
			torusMesh.OuterRadius = 2.8f;
			ring.Mesh = torusMesh;
			ring.Position = new Vector3(0, 0.05f, 0);
			var material = new StandardMaterial3D();
			material.AlbedoColor = new Color(0.88f, 0.88f, 0.88f, 0.22f);
			material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
			material.DisableReceiveShadows = true;
			material.EmissionEnabled = false;
			material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
			ring.MaterialOverride = material;
			decal.AddChild(ring);
		}
	}

	private Node3D? _editorCoverageOverlayRoot;

	/// <summary>
	///     When enabled, the editor draws vision/attack range rings around the selected unit.
	///     Off by default so the overlay only appears on demand.
	/// </summary>
	public bool EditorCoverageOverlayEnabled { get; set; } = false;

	internal void UpdateEditorCoverageOverlay()
	{
		if (_editorCoverageOverlayRoot == null)
		{
			_editorCoverageOverlayRoot = new Node3D();
			_editorCoverageOverlayRoot.Name = "EditorCoverageOverlay";
			AddChild(_editorCoverageOverlayRoot);
		}
		foreach (Node child in _editorCoverageOverlayRoot.GetChildren())
		{
			child.QueueFree();
		}

		if (!EditorCoverageOverlayEnabled)
		{
			_editorCoverageOverlayRoot.Visible = false;
			return;
		}

		if (!(_selectedEditorObject is Unit3D unit) || !EcsWorld.IsAlive(unit.Entity))
		{
			_editorCoverageOverlayRoot.Visible = false;
			return;
		}

		float scanRadius = EcsWorld.Has<ScanRadius>(unit.Entity) ? EcsWorld.Get<ScanRadius>(unit.Entity).Value : 0f;
		float range = EcsWorld.Has<Attack>(unit.Entity) ? EcsWorld.Get<Attack>(unit.Entity).Range : 0f;

		_editorCoverageOverlayRoot.Visible = scanRadius > 0f || range > 0f;
		if (!_editorCoverageOverlayRoot.Visible) return;

		_editorCoverageOverlayRoot.Position = unit.Position;
		if (scanRadius > 0f) CreateCoverageRing(scanRadius, new Color(0.3f, 0.7f, 1.0f, 0.6f));
		if (range > 0f) CreateCoverageRing(range, new Color(1.0f, 0.5f, 0.1f, 0.7f));
	}

	private void CreateCoverageRing(float radius, Color color)
	{
		var meshInstance = new MeshInstance3D();
		meshInstance.CastShadow = GeometryInstance3D.ShadowCastingSetting.Off;
		meshInstance.GIMode = GeometryInstance3D.GIModeEnum.Disabled;
		var torusMesh = new TorusMesh();
		torusMesh.InnerRadius = Mathf.Max(radius - 0.25f, 0.05f);
		torusMesh.OuterRadius = radius + 0.25f;
		torusMesh.Rings = 32;
		meshInstance.Mesh = torusMesh;
		meshInstance.Position = new Vector3(0, 0.3f, 0);
		meshInstance.Scale = new Vector3(1f, 0.04f, 1f);

		var material = new StandardMaterial3D();
		material.AlbedoColor = color;
		material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		material.DisableReceiveShadows = true;
		material.EmissionEnabled = true;
		material.Emission = new Color(color.R, color.G, color.B);
		material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
		meshInstance.MaterialOverride = material;

		_editorCoverageOverlayRoot.AddChild(meshInstance);
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
					float mouseDistPx = mousePos.DistanceTo(_dragStartMousePos);
					if (!_dragObjectHasMoved && mouseDistPx > 4.0f)
					{
						_dragObjectHasMoved = true;
					}

					if (_dragObjectHasMoved)
					{
						var node3D = SelectedEditorObject as Node3D;
						Vector3 delta = hitPos - _dragStartGroundPos;
						Vector3 dragPos = _dragObjectStartPos + delta;
						if (EditorSnapToGrid && GroundTerrain != null)
						{
							dragPos = _editorService.SnapToGrid(dragPos);
						}
						float authoredYOffset = _dragObjectStartPos.Y - _editorService.GetTerrainHeightAt(_dragObjectStartPos);
						dragPos.Y = _editorService.GetTerrainHeightAt(dragPos) + (Mathf.Abs(authoredYOffset) < 0.05f ? 0f : authoredYOffset);
						node3D.Position = dragPos;
						if (SelectedEditorObject is Unit3D unit && EcsWorld.IsAlive(unit.Entity))
						{
							EcsWorld.Set(unit.Entity, new Position(new System.Numerics.Vector3(dragPos.X, dragPos.Y, dragPos.Z)));
							UpdateEditorCoverageOverlay();
						}
						else if (SelectedEditorObject is Prop3D prop)
						{
							PropMultiMeshManager.Instance?.MarkDirty(prop.PropId);
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
						if (SelectedEditorObject is Prop3D prop && SelectedEditorObject is not Unit3D)
						{
							PropMultiMeshManager.Instance?.MarkDirty(prop.PropId);
						}
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
		Realm.Godot.ReplaySystem.ReplayPlaybackManager.Instance.StopReplay();
		IsMapEditorMode = true;

		string wsPath = Godot.ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
		try
		{
			System.IO.Directory.CreateDirectory(wsPath);
			MapWorkspaceService.SetupWorkspace(wsPath, "MapScript");
		}
		catch (Exception ex)
		{
			GD.PrintErr($"Failed setting up map workspace: {ex.Message}");
		}

		ActiveEditorTool = EditorTool.None;
		EditorHistoryManager.Clear();

		if (MapEditorHUD.ReturningFromTest)
		{
			try
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
			catch (Exception ex)
			{
				GD.PrintErr($"Failed loading map state: {ex.Message}. Resetting to blank map.");
				ResetWorldAndState();
				ClearMapEntirely();
			}
		}
		else
		{
			ResetWorldAndState();
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

		HideCoordinateSelectionOutline();
		HideCoordinatePreviewMesh();
		RebuildAllCoordinatePersistentMeshes();
		
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

		int splatW = GroundTerrain.SplatMap.GetLength(0);
		int splatD = GroundTerrain.SplatMap.GetLength(1);

		float[,] heightsBefore = GroundTerrain.Heights != null ? (float[,])GroundTerrain.Heights.Clone() : null;
		TerrainSplatWeights[,] splatBefore = (TerrainSplatWeights[,])GroundTerrain.SplatMap.Clone();
		TerrainSplatWeights[,] cliffBefore = GroundTerrain.CliffSplatMap != null ? (TerrainSplatWeights[,])GroundTerrain.CliffSplatMap.Clone() : null;

		bool anyChanged = false;

		for (int z = 0; z < splatD; z++)
		{
			for (int x = 0; x < splatW; x++)
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

				if (GroundTerrain.CliffSplatMap != null && x < GroundTerrain.CliffSplatMap.GetLength(0) && z < GroundTerrain.CliffSplatMap.GetLength(1))
				{
					var c = GroundTerrain.CliffSplatMap[x, z];
					if (c.Index0 == indexA || c.Index0 == indexB ||
						c.Index1 == indexA || c.Index1 == indexB ||
						c.Index2 == indexA || c.Index2 == indexB ||
						c.Index3 == indexA || c.Index3 == indexB)
					{
						GroundTerrain.CliffSplatMap[x, z] = new TerrainSplatWeights
						{
							Index0 = c.Index0 == indexA ? indexB : (c.Index0 == indexB ? indexA : c.Index0),
							Index1 = c.Index1 == indexA ? indexB : (c.Index1 == indexB ? indexA : c.Index1),
							Index2 = c.Index2 == indexA ? indexB : (c.Index2 == indexB ? indexA : c.Index2),
							Index3 = c.Index3 == indexA ? indexB : (c.Index3 == indexB ? indexA : c.Index3),
							Weight0 = c.Weight0,
							Weight1 = c.Weight1,
							Weight2 = c.Weight2,
							Weight3 = c.Weight3
						};
						anyChanged = true;
					}
				}
			}
		}

		if (anyChanged)
		{
			float[,] heightsAfter = GroundTerrain.Heights != null ? (float[,])GroundTerrain.Heights.Clone() : null;
			TerrainSplatWeights[,] splatAfter = (TerrainSplatWeights[,])GroundTerrain.SplatMap.Clone();
			TerrainSplatWeights[,] cliffAfter = GroundTerrain.CliffSplatMap != null ? (TerrainSplatWeights[,])GroundTerrain.CliffSplatMap.Clone() : null;
			
			var action = new TerrainModifyAction(heightsBefore, heightsAfter, splatBefore, splatAfter, null, null, cliffBefore, cliffAfter);
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

	public void HideSelectionHighlight()
	{
		if (_selectionHighlightMesh != null)
		{
			_selectionHighlightMesh.Visible = false;
		}
		_editorService?.SetIsSelectingArea(false);
		_editorService?.SetSelectionStart(null);
		_editorService?.SetSelectionEnd(null);
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
		EditorCoordinates.RemoveAll(r => string.Equals(r.Name, coordinateName, StringComparison.OrdinalIgnoreCase));
		HideCoordinateSelectionOutline();
		HideSelectionHighlight();
		HideCoordinatePreviewMesh();
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
		}

		if (!IsMapEditorMode || ActiveEditorTool != EditorTool.DrawCoordinate)
		{
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
			if (string.Equals(r.Name, coordinateName, StringComparison.OrdinalIgnoreCase))
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

		var camera = (MainCamera ?? GetViewport()?.GetCamera3D()) as CameraControl;
		if (camera != null)
		{
			camera.FocusOnPosition(new Vector3(centerX, centerY, centerZ));
		}
	}

	public void HideCoordinateSelectionOutline()
	{
		if (_coordinateSelectionOutlineMesh != null && GodotObject.IsInstanceValid(_coordinateSelectionOutlineMesh))
		{
			_coordinateSelectionOutlineMesh.Visible = false;
			_coordinateSelectionOutlineMesh.Mesh = null;
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
