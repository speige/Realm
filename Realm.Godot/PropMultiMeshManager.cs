using Godot;
using System;
using System.Collections.Generic;

public partial class PropMultiMeshManager : Node3D
{
	public static PropMultiMeshManager Instance { get; private set; }

	private class MeshSubInfo
	{
		public Mesh Mesh;
		public Transform3D RelativeTransform;
		public Material[] SurfaceMaterials;
		public Material MaterialOverride;
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
		var keysToRebuild = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		foreach (var prop in GameHost.Instance.AllProps)
		{
			if (GodotObject.IsInstanceValid(prop) && !prop.IsPreview)
			{
				string key = GameHost.Instance.GetModelAssetKey(prop);
				if (!string.IsNullOrEmpty(key))
				{
					keysToRebuild.Add(key);
				}
			}
		}
		foreach (var existingKey in _groups.Keys)
		{
			keysToRebuild.Add(existingKey);
		}
		foreach (var key in keysToRebuild)
		{
			RebuildGroupInternal(key);
		}
	}

	private void RebuildGroupInternal(string normAssetKey)
	{
		if (GameHost.Instance == null || string.IsNullOrEmpty(normAssetKey)) return;

		var matchingProps = new List<Prop3D>();
		foreach (var prop in GameHost.Instance.AllProps)
		{
			if (GodotObject.IsInstanceValid(prop) && !prop.IsPreview && GameHost.Instance.GetModelAssetKey(prop) == normAssetKey)
			{
				matchingProps.Add(prop);
			}
		}

		if (!_groups.TryGetValue(normAssetKey, out var group))
		{
			if (matchingProps.Count == 0) return;
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

		var propsByChunk = new Dictionary<Vector2I, List<Prop3D>>();
		foreach (var prop in matchingProps)
		{
			Vector3 p = prop.Position;
			int cx = (int)Math.Floor((p.X + halfW) / chunkSizeUnits);
			int cz = (int)Math.Floor((p.Z + halfD) / chunkSizeUnits);
			var cKey = new Vector2I(cx, cz);
			if (!propsByChunk.TryGetValue(cKey, out var list))
			{
				list = new List<Prop3D>();
				propsByChunk[cKey] = list;
			}
			list.Add(prop);
		}

		float yOffset = GameHost.Instance.GetModelYOffset(normAssetKey);

		// Hide any chunk groups that no longer have props
		foreach (var kvp in group.ChunkGroups)
		{
			if (!propsByChunk.ContainsKey(kvp.Key))
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
		foreach (var kvp in propsByChunk)
		{
			Vector2I chunkKey = kvp.Key;
			List<Prop3D> chunkProps = kvp.Value;

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
					AddChild(mmNode);
					chunkGroup.MultiMeshNodes.Add(mmNode);
				}

				var node = chunkGroup.MultiMeshNodes[subIdx];
				var mm = node.Multimesh;

				if (instanceCount == 0)
				{
					if (mm != null) mm.VisibleInstanceCount = 0;
					node.CustomAabb = new Aabb();
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

					Transform3D propTransform = new Transform3D(prop.Transform.Basis, pos);
					if (prop.PropId == "tree" || prop.PropId.Contains("tree"))
					{
						propTransform.Basis = propTransform.Basis.Scaled(new Vector3(3f, 3f, 3f));
					}

					Transform3D finalXform = propTransform * subInfo.RelativeTransform;
					mm.SetInstanceTransform(i, finalXform);
				}

				minPos -= new Vector3(0.5f, 0.5f, 0.5f);
				maxPos += new Vector3(0.5f, 4.0f, 0.5f);
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

		foreach (var mi in meshNodes)
		{
			if (mi.Mesh == null) continue;
			string nameStr = mi.Name.ToString();
			if (nameStr.StartsWith("_selection") || nameStr.StartsWith("_hover")) continue;

			var subInfo = new MeshSubInfo
			{
				Mesh = mi.Mesh,
				RelativeTransform = GetRelativeTransform(mi, prototype),
				MaterialOverride = mi.MaterialOverride
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
		if (GameHost.Instance == null || !_groups.TryGetValue(normAssetKey, out var group)) return;

		float brightness = GameHost.Instance.GetModelBrightness(normAssetKey);
		Color tint = GameHost.Instance.GetModelColorTint(normAssetKey);
		bool generateNormals = GameHost.Instance.GetModelGenerateNormals(normAssetKey);

		float multR = brightness * tint.R;
		float multG = brightness * tint.G;
		float multB = brightness * tint.B;

		foreach (var chunkGroup in group.ChunkGroups.Values)
		{
			for (int i = 0; i < group.SubMeshes.Count && i < chunkGroup.MultiMeshNodes.Count; i++)
			{
				var subInfo = group.SubMeshes[i];
				var mmNode = chunkGroup.MultiMeshNodes[i];

				if (generateNormals && subInfo.Mesh is ArrayMesh arrayMesh)
				{
					var toolMesh = new ArrayMesh();
					var surfaceTool = new SurfaceTool();
					for (int s = 0; s < arrayMesh.GetSurfaceCount(); s++)
					{
						surfaceTool.CreateFrom(arrayMesh, s);
						surfaceTool.GenerateNormals();
						toolMesh = surfaceTool.Commit(toolMesh);
					}
					if (mmNode.Multimesh != null)
					{
						mmNode.Multimesh.Mesh = toolMesh;
					}
				}

				Material baseMatToUse = subInfo.MaterialOverride;
				if (baseMatToUse == null && subInfo.SurfaceMaterials != null && subInfo.SurfaceMaterials.Length > 0)
				{
					baseMatToUse = subInfo.SurfaceMaterials[0];
				}

				if (baseMatToUse is BaseMaterial3D baseMat)
				{
					var dupMat = (BaseMaterial3D)baseMat.Duplicate();
					dupMat.AlbedoColor = new Color(multR, multG, multB, dupMat.AlbedoColor.A);
					mmNode.MaterialOverride = dupMat;
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
		string wsPath = Godot.ProjectSettings.GlobalizePath("user://temp_map_workspace");
		if (normAssetKey.StartsWith("res://") || System.IO.File.Exists(normAssetKey))
			return normAssetKey;

		string filename = normAssetKey;
		if (!filename.EndsWith(".glb") && !filename.EndsWith(".gltf"))
		{
			filename += ".glb";
		}

		string[] subDirs = new[] { "props", "environment", "building", "character" };
		foreach (var sub in subDirs)
		{
			string candidate = System.IO.Path.Combine(wsPath, "Assets", "models", sub, filename);
			if (System.IO.File.Exists(candidate))
				return candidate;
		}

		string rootCandidate = System.IO.Path.Combine(wsPath, filename);
		if (System.IO.File.Exists(rootCandidate))
			return rootCandidate;

		return normAssetKey;
	}
}
