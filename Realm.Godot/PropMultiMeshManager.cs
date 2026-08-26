using Godot;
using System;
using System.Collections.Generic;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Meta;

public partial class PropMultiMeshManager : Node3D
{
	public static PropMultiMeshManager Instance { get; private set; }

	private struct PropData
	{
		public string PropId;
		public Vector3 Position;
		public float RotationY;
		public float Scale;
	}

	private class MeshSubInfo
	{
		public Mesh Mesh;
		public Transform3D RelativeTransform;
		public Material[] SurfaceMaterials;
		public Material MaterialOverride;
		public GeometryInstance3D.ShadowCastingSetting CastShadow = GeometryInstance3D.ShadowCastingSetting.On;
	}

	private class PropChunkGroup
	{
		public Vector2I ChunkIndex;
		public Aabb Bounds;
		public List<MultiMeshInstance3D> MultiMeshNodes = new();
	}

	private class PropModelGroup
	{
		public string AssetKey;
		public List<MeshSubInfo> SubMeshes = new();
		public Dictionary<Vector2I, PropChunkGroup> ChunkGroups = new();
	}

	private readonly Dictionary<string, PropModelGroup> _groups = new(StringComparer.OrdinalIgnoreCase);
	private readonly HashSet<string> _dirtyAssetKeys = new(StringComparer.OrdinalIgnoreCase);
	private readonly List<PropData> _reusableMatchingPropsData = new();
	private readonly Dictionary<Vector2I, List<PropData>> _reusableChunkBucketsData = new();
	private readonly HashSet<string> _reusableKeysToRebuild = new(StringComparer.OrdinalIgnoreCase);
	private bool _allDirty = false;

	public override void _Ready()
	{
		Instance = this;
		Name = "PropMultiMeshManager";
	}

	public override void _ExitTree()
	{
		if (Instance == this)
		{
			Instance = null;
		}
	}

	public void MarkDirty(string assetKeyOrPropId)
	{
		if (string.IsNullOrEmpty(assetKeyOrPropId)) return;
		string normKey = GameHost.Instance != null ? GameHost.Instance.NormalizeModelAssetKey(assetKeyOrPropId) : assetKeyOrPropId.ToLowerInvariant();
		_dirtyAssetKeys.Add(normKey);
	}

	public void MarkAllDirty()
	{
		_allDirty = true;
	}

	public void Clear()
	{
		_dirtyAssetKeys.Clear();
		_allDirty = false;
		foreach (var group in _groups.Values)
		{
			foreach (var chunkGroup in group.ChunkGroups.Values)
			{
				foreach (var node in chunkGroup.MultiMeshNodes)
				{
					if (GodotObject.IsInstanceValid(node))
					{
						node.QueueFree();
					}
				}
			}
		}
		_groups.Clear();
	}

	public override void _Process(double delta)
	{
		if (_allDirty)
		{
			RebuildAllInternal();
			_allDirty = false;
			_dirtyAssetKeys.Clear();
		}
		else if (_dirtyAssetKeys.Count > 0)
		{
			foreach (var key in _dirtyAssetKeys)
			{
				RebuildGroupInternal(key);
			}
			_dirtyAssetKeys.Clear();
		}

		UpdateFrustumCulling();
	}

	private void UpdateFrustumCulling()
	{
		if (EditableTerrain.IsMinimapRendering)
		{
			SetAllNodesVisible(true);
			return;
		}

		var viewport = GetViewport();
		if (viewport == null) return;
		var camera = viewport.GetCamera3D();
		if (camera == null || !GodotObject.IsInstanceValid(camera)) return;

		if (camera.Projection == Camera3D.ProjectionType.Orthogonal)
		{
			SetAllNodesVisible(true);
			return;
		}

		var frustum = camera.GetFrustum();
		if (frustum == null || frustum.Count < 6) return;

		foreach (var group in _groups.Values)
		{
			foreach (var chunkGroup in group.ChunkGroups.Values)
			{
				bool visible = EditableTerrain.IntersectsFrustum(frustum, chunkGroup.Bounds);
				foreach (var node in chunkGroup.MultiMeshNodes)
				{
					if (GodotObject.IsInstanceValid(node))
					{
						node.Visible = visible;
					}
				}
			}
		}
	}

	public void SetAllNodesVisible(bool visible)
	{
		foreach (var group in _groups.Values)
		{
			foreach (var chunkGroup in group.ChunkGroups.Values)
			{
				foreach (var node in chunkGroup.MultiMeshNodes)
				{
					if (GodotObject.IsInstanceValid(node))
					{
						node.Visible = visible;
					}
				}
			}
		}
	}

	public void RebuildAll()
	{
		RebuildAllInternal();
	}

	private void RebuildAllInternal()
	{
		if (GameHost.Instance == null) return;
		_reusableKeysToRebuild.Clear();

		if (GameHost.Instance.EcsWorld != null)
		{
			var query = Realm.Ecs.Common.QueryCache.AllPropIdentityAndPositionQuery;
			GameHost.Instance.EcsWorld.Query(in query, (ref PropIdentity propIdComp) =>
			{
				string key = GameHost.Instance.NormalizeModelAssetKey(propIdComp.PropId);
				if (!string.IsNullOrEmpty(key))
				{
					_reusableKeysToRebuild.Add(key);
				}
			});
		}

		foreach (var prop in GameHost.Instance.AllProps)
		{
			if (GodotObject.IsInstanceValid(prop) && !prop.IsPreview && prop.GetNodeOrNull<Node3D>("VisualModel") == null)
			{
				string key = GameHost.Instance.GetModelAssetKey(prop);
				if (!string.IsNullOrEmpty(key))
				{
					_reusableKeysToRebuild.Add(key);
				}
			}
		}

		foreach (var existingKey in _groups.Keys)
		{
			_reusableKeysToRebuild.Add(existingKey);
		}

		foreach (var key in _reusableKeysToRebuild)
		{
			RebuildGroupInternal(key);
		}
	}

	private void RebuildGroupInternal(string normAssetKey)
	{
		if (GameHost.Instance == null || string.IsNullOrEmpty(normAssetKey)) return;

		_reusableMatchingPropsData.Clear();

		if (GameHost.Instance.EcsWorld != null)
		{
			var query = Realm.Ecs.Common.QueryCache.AllPropIdentityAndPositionQuery;
			GameHost.Instance.EcsWorld.Query(in query, (Arch.Core.Entity entity, ref PropIdentity propIdComp, ref Position posComp) =>
			{
				if (GameHost.EntityToProp3D.ContainsKey(entity)) return;

				string key = GameHost.Instance.NormalizeModelAssetKey(propIdComp.PropId);
				if (key == normAssetKey)
				{
					float rotY = GameHost.Instance.EcsWorld.Has<RotationY>(entity)
						? GameHost.Instance.EcsWorld.Get<RotationY>(entity).Value : 0f;
					float scale = GameHost.Instance.EcsWorld.Has<ModelScale>(entity)
						? GameHost.Instance.EcsWorld.Get<ModelScale>(entity).Value : 1f;

					_reusableMatchingPropsData.Add(new PropData
					{
						PropId = propIdComp.PropId,
						Position = new Vector3(posComp.Value.X, posComp.Value.Y, posComp.Value.Z),
						RotationY = rotY,
						Scale = scale
					});
				}
			});
		}

		foreach (var prop in GameHost.Instance.AllProps)
		{
			if (GodotObject.IsInstanceValid(prop) && !prop.IsPreview && prop.Visible && prop.GetNodeOrNull<Node3D>("VisualModel") == null && GameHost.Instance.GetModelAssetKey(prop) == normAssetKey)
			{
				_reusableMatchingPropsData.Add(new PropData
				{
					PropId = prop.PropId,
					Position = prop.Position,
					RotationY = prop.RotationDegrees.Y,
					Scale = prop.Scale.X
				});
			}
		}

		if (!_groups.TryGetValue(normAssetKey, out var group))
		{
			if (_reusableMatchingPropsData.Count == 0) return;
			group = CreateGroupForAsset(normAssetKey);
			if (group == null) return;
			_groups[normAssetKey] = group;
		}

		float quadSize = 1.0f;
		float halfW = 64.0f;
		float halfD = 64.0f;
		var terrain = GameHost.Instance.GroundTerrain;
		if (terrain != null)
		{
			quadSize = terrain.QuadSize;
			halfW = (terrain.Width / 2.0f) * quadSize;
			halfD = (terrain.Depth / 2.0f) * quadSize;
		}
		float chunkSizeUnits = 16.0f * quadSize;

		foreach (var bucket in _reusableChunkBucketsData.Values)
		{
			bucket.Clear();
		}

		for (int i = 0; i < _reusableMatchingPropsData.Count; i++)
		{
			var prop = _reusableMatchingPropsData[i];
			Vector3 p = prop.Position;
			int cx = (int)Math.Floor((p.X + halfW) / chunkSizeUnits);
			int cz = (int)Math.Floor((p.Z + halfD) / chunkSizeUnits);
			var cKey = new Vector2I(cx, cz);

			if (!_reusableChunkBucketsData.TryGetValue(cKey, out var list))
			{
				list = new List<PropData>();
				_reusableChunkBucketsData[cKey] = list;
			}
			list.Add(prop);
		}

		float yOffset = GameHost.Instance.GetModelYOffset(normAssetKey);

		// Hide any chunk groups that no longer have props
		foreach (var kvp in group.ChunkGroups)
		{
			if (!_reusableChunkBucketsData.TryGetValue(kvp.Key, out var chunkList) || chunkList.Count == 0)
			{
				foreach (var mmNode in kvp.Value.MultiMeshNodes)
				{
					if (mmNode.Multimesh != null)
					{
						mmNode.Multimesh.VisibleInstanceCount = 0;
					}
					mmNode.CustomAabb = new Aabb();
				}
			}
		}

		// Rebuild active chunk groups
		foreach (var kvp in _reusableChunkBucketsData)
		{
			Vector2I chunkKey = kvp.Key;
			List<PropData> chunkProps = kvp.Value;
			if (chunkProps.Count == 0) continue;

			if (!group.ChunkGroups.TryGetValue(chunkKey, out var chunkGroup))
			{
				chunkGroup = new PropChunkGroup { ChunkIndex = chunkKey };
				group.ChunkGroups[chunkKey] = chunkGroup;
			}

			Vector3 minPos = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
			Vector3 maxPos = new Vector3(float.MinValue, float.MinValue, float.MinValue);
			int instanceCount = chunkProps.Count;

			for (int subIdx = 0; subIdx < group.SubMeshes.Count; subIdx++)
			{
				var subInfo = group.SubMeshes[subIdx];
				while (chunkGroup.MultiMeshNodes.Count <= subIdx)
				{
					var mmNode = new MultiMeshInstance3D();
					mmNode.Name = $"MultiMesh_{normAssetKey}_C{chunkKey.X}_{chunkKey.Y}_{chunkGroup.MultiMeshNodes.Count}";
					mmNode.CastShadow = subInfo.CastShadow;
					AddChild(mmNode);
					chunkGroup.MultiMeshNodes.Add(mmNode);
				}

				var node = chunkGroup.MultiMeshNodes[subIdx];
				node.CastShadow = subInfo.CastShadow;
				node.Visible = true;
				var mm = node.Multimesh;

				if (instanceCount == 0)
				{
					if (mm != null) mm.VisibleInstanceCount = 0;
					node.CustomAabb = new Aabb();
					node.Visible = false;
					continue;
				}

				if (mm == null || mm.InstanceCount < instanceCount)
				{
					int allocated = Math.Max(instanceCount, mm != null ? mm.InstanceCount * 2 : 16);
					mm = new MultiMesh
					{
						TransformFormat = MultiMesh.TransformFormatEnum.Transform3D,
						Mesh = subInfo.Mesh,
						InstanceCount = allocated
					};
					node.Multimesh = mm;
				}

				mm.VisibleInstanceCount = instanceCount;

				for (int i = 0; i < instanceCount; i++)
				{
					var prop = chunkProps[i];
					Vector3 pos = prop.Position;
					pos.Y += yOffset;
					minPos = minPos.Min(pos);
					maxPos = maxPos.Max(pos);

					float propScale = Mathf.Max(0.01f, prop.Scale);
					Basis basis = Basis.Identity.Rotated(Vector3.Up, Mathf.DegToRad(prop.RotationY)).Scaled(Vector3.One * propScale);
					Transform3D propTransform = new Transform3D(basis, pos);

					if (prop.PropId == "tree" || prop.PropId.Contains("tree"))
					{
						propTransform.Basis = propTransform.Basis.Scaled(new Vector3(3f, 3f, 3f));
					}

					Transform3D finalXform = propTransform * subInfo.RelativeTransform;
					mm.SetInstanceTransform(i, finalXform);
				}

				minPos -= new Vector3(8.0f, 2.0f, 8.0f);
				maxPos += new Vector3(8.0f, 30.0f, 8.0f);
				chunkGroup.Bounds = new Aabb(minPos, maxPos - minPos);
				node.CustomAabb = chunkGroup.Bounds;
			}
		}

		UpdateMaterialOverridesForAsset(normAssetKey);
	}

	private PropModelGroup CreateGroupForAsset(string normAssetKey)
	{
		string modelPath = ResolveModelPathForAssetKey(normAssetKey);
		Node prototype = Realm.Godot.Utils.ModelCache.GetModel(modelPath);
		if (prototype == null) return null;

		var meshNodes = new List<MeshInstance3D>();
		FindMeshInstancesRecursive(prototype, meshNodes);

		if (meshNodes.Count == 0)
		{
			prototype.QueueFree();
			return null;
		}

		var group = new PropModelGroup { AssetKey = normAssetKey };

		bool hasLod0 = false;
		foreach (var mi in meshNodes)
		{
			string n = mi.Name.ToString();
			if (n.EndsWith("_LOD0", StringComparison.OrdinalIgnoreCase) || n.EndsWith("LOD0", StringComparison.OrdinalIgnoreCase))
			{
				hasLod0 = true;
				break;
			}
		}

		foreach (var mi in meshNodes)
		{
			if (mi.Mesh == null) continue;
			string nameStr = mi.Name.ToString();
			if (nameStr.StartsWith("_selection") || nameStr.StartsWith("_hover")) continue;
			if (hasLod0 && !nameStr.EndsWith("_LOD0", StringComparison.OrdinalIgnoreCase) && !nameStr.EndsWith("LOD0", StringComparison.OrdinalIgnoreCase)) continue;

			var subInfo = new MeshSubInfo
			{
				Mesh = mi.Mesh,
				RelativeTransform = GetRelativeTransform(mi, prototype),
				MaterialOverride = mi.MaterialOverride,
				CastShadow = mi.CastShadow
			};

			int surfaceCount = mi.Mesh.GetSurfaceCount();
			subInfo.SurfaceMaterials = new Material[surfaceCount];
			for (int s = 0; s < surfaceCount; s++)
			{
				subInfo.SurfaceMaterials[s] = mi.GetSurfaceOverrideMaterial(s) ?? mi.Mesh.SurfaceGetMaterial(s);
			}

			group.SubMeshes.Add(subInfo);
		}

		prototype.QueueFree();
		return group.SubMeshes.Count > 0 ? group : null;
	}

	public void UpdateMaterialOverrides(string normAssetKey)
	{
		UpdateMaterialOverridesForAsset(normAssetKey);
	}

	public void UpdateMaterialOverridesForAsset(string normAssetKey)
	{
		if (GameHost.Instance == null) return;

		foreach (var group in _groups.Values)
		{
			if (group == null) continue;
			if (!string.Equals(group.AssetKey, normAssetKey, StringComparison.OrdinalIgnoreCase)
				&& !string.Equals(GameHost.Instance.NormalizeModelAssetKey(group.AssetKey), GameHost.Instance.NormalizeModelAssetKey(normAssetKey), StringComparison.OrdinalIgnoreCase)
				&& !GameHost.Instance.MatchesEntityOrAssetKey(group.AssetKey, normAssetKey))
			{
				continue;
			}

			float brightness = GameHost.Instance.GetModelBrightness(group.AssetKey);
			Color tint = GameHost.Instance.GetModelColorTint(group.AssetKey);
			GameHost.ModelNormalMode normalMode = GameHost.Instance.GetModelNormalMode(group.AssetKey);
			bool ignorePlayerColor = GameHost.Instance.GetModelIgnorePlayerColor(group.AssetKey);
			bool normalizeLuminance = GameHost.Instance.GetModelNormalizeLuminance(group.AssetKey);

			foreach (var chunkGroup in group.ChunkGroups.Values)
			{
				for (int i = 0; i < group.SubMeshes.Count && i < chunkGroup.MultiMeshNodes.Count; i++)
				{
					var subInfo = group.SubMeshes[i];
					var mmNode = chunkGroup.MultiMeshNodes[i];

					if (subInfo.Mesh is ArrayMesh arrayMesh && mmNode.Multimesh != null)
					{
						mmNode.Multimesh.Mesh = GameHost.GetOrCreateNormalMesh(arrayMesh, normalMode);
					}

					Material baseMatToUse = subInfo.MaterialOverride;
					if (baseMatToUse == null && subInfo.SurfaceMaterials != null && subInfo.SurfaceMaterials.Length > 0)
					{
						baseMatToUse = subInfo.SurfaceMaterials[0];
					}

					if (baseMatToUse != null)
					{
						var shaderMat = Realm.Godot.Utils.PlayerColorShaderManager.GetOrCreateShaderMaterial(baseMatToUse, normalizeLuminance);
						mmNode.MaterialOverride = shaderMat;
						mmNode.SetInstanceShaderParameter(new StringName("model_brightness"), brightness);
						mmNode.SetInstanceShaderParameter(new StringName("model_color_tint"), tint);
						mmNode.SetInstanceShaderParameter(new StringName("ignore_player_color"), ignorePlayerColor ? 1.0f : 0.0f);
						mmNode.SetInstanceShaderParameter(new StringName("normal_mode"), (float)normalMode);
						mmNode.SetInstanceShaderParameter(new StringName("unit_ambient_boost"), 0.0f);
						mmNode.SetInstanceShaderParameter(new StringName("unit_rim_intensity"), 0.0f);
					}
				}
			}
		}
	}

	private static Transform3D GetRelativeTransform(Node node, Node root)
	{
		Transform3D xform = Transform3D.Identity;
		Node curr = node;
		while (curr != null && curr != root)
		{
			if (curr is Node3D n3d)
			{
				xform = n3d.Transform * xform;
			}
			curr = curr.GetParent();
		}
		return xform;
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

	private static string ResolveModelPathForAssetKey(string normAssetKey)
	{
		if (string.IsNullOrEmpty(normAssetKey))
			return "wooden_box.glb";

		string targetModel = normAssetKey;
		if (GameHost.PropRegistry != null && GameHost.PropRegistry.TryGetValue(normAssetKey, out var propMeta) && !string.IsNullOrEmpty(propMeta.ModelPath))
		{
			targetModel = propMeta.ModelPath;
		}
		else if (GameHost.ResourceRegistry != null && GameHost.ResourceRegistry.TryGetValue(normAssetKey, out var resMeta) && !string.IsNullOrEmpty(resMeta.ModelPath))
		{
			targetModel = resMeta.ModelPath;
		}
		else if (GameHost.UnitRegistry != null && GameHost.UnitRegistry.TryGetValue(normAssetKey, out var unitMeta) && !string.IsNullOrEmpty(unitMeta.ModelPath))
		{
			targetModel = unitMeta.ModelPath;
		}

		if (targetModel.StartsWith("res://") || System.IO.File.Exists(targetModel))
			return targetModel;

		string wsPath = Godot.ProjectSettings.GlobalizePath("user://temp_map_workspace");
		string filename = System.IO.Path.GetFileName(targetModel);
		if (!filename.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) && !filename.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase))
		{
			filename += ".glb";
		}

		string[] subDirs = new[] { "props", "resources", "buildings", "units" };
		foreach (var sub in subDirs)
		{
			string candidate = System.IO.Path.Combine(wsPath, "Assets", "models", sub, filename);
			if (System.IO.File.Exists(candidate))
				return candidate;
		}

		string rootCandidate = System.IO.Path.Combine(wsPath, filename);
		if (System.IO.File.Exists(rootCandidate))
			return rootCandidate;

		return targetModel;
	}
}
