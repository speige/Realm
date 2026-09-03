using Arch.Core;
using Godot;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Resources;
using System;
using System.Collections.Generic;
using Realm.Godot.Utils;

public partial class Prop3D : StaticBody3D
{
	public Entity Entity { get; set; }
	public Vector3 Velocity { get; set; } = Vector3.Zero;

	private string _propId = string.Empty;

	[Export]
	public virtual string PropId
	{
		get
		{
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld.IsAlive(Entity)
				&& GameHost.Instance.EcsWorld.Has<PropIdentity>(Entity))
				return GameHost.Instance.EcsWorld.Get<PropIdentity>(Entity).PropId;
			return _propId;
		}
		set
		{
			_propId = value;
			_cachedResolvedModelPath = null;
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld.IsAlive(Entity))
			{
				var world = GameHost.Instance.EcsWorld;
				if (world.Has<PropIdentity>(Entity))
					world.Set(Entity, new PropIdentity(value));
				else
					world.Add(Entity, new PropIdentity(value));
			}
		}
	}

	private string _cachedResolvedModelPath;
	public string ModelAssetPath
	{
		get
		{
			if (_cachedResolvedModelPath == null)
			{
				_cachedResolvedModelPath = ResolvePropModelPath(PropId);
			}
			return _cachedResolvedModelPath;
		}
	}

	public float ResourceAmount
	{
		get
		{
			if (GameHost.Instance == null || !GameHost.Instance.EcsWorld.IsAlive(Entity)) return 0f;
			if (GameHost.Instance.EcsWorld.Has<ResourceNode>(Entity))
				return GameHost.Instance.EcsWorld.Get<ResourceNode>(Entity).Amount;
			return 0f;
		}
		set
		{
			if (GameHost.Instance == null || !GameHost.Instance.EcsWorld.IsAlive(Entity)) return;
			var world = GameHost.Instance.EcsWorld;
			if (world.Has<ResourceNode>(Entity))
			{
				var existing = world.Get<ResourceNode>(Entity);
				world.Set(Entity, new ResourceNode(existing.ResourceTypeId, value));
			}
			else
			{
				world.Add(Entity, new ResourceNode(Guid.Empty, value));
			}
		}
	}

	private MeshInstance3D _selectionRing;
	private bool _isSelected = false;

	private MeshInstance3D _hoverRing;
	private bool _isHovered = false;

	private static readonly Dictionary<string, TorusMesh> _sharedTorusMeshes = new(StringComparer.OrdinalIgnoreCase);
	private static StandardMaterial3D _sharedSelectionMaterial;
	private static StandardMaterial3D _sharedHoverMaterial;

	public virtual float GetBaseObstacleRadius()
	{
		if (GameHost.Instance != null)
		{
			return GameHost.Instance.GetOrCalculateObstacleRadius(PropId, this);
		}
		return 1.4f;
	}

	private static TorusMesh GetOrCreateTorusMesh(float radius)
	{
		float r = Mathf.Max(0.4f, (float)Math.Round(radius, 1));
		string key = r.ToString("0.0", System.Globalization.CultureInfo.InvariantCulture);
		if (!_sharedTorusMeshes.TryGetValue(key, out var mesh) || !GodotObject.IsInstanceValid(mesh))
		{
			mesh = new TorusMesh
			{
				InnerRadius = Mathf.Max(0.1f, r - 0.25f),
				OuterRadius = r
			};
			_sharedTorusMeshes[key] = mesh;
		}
		return mesh;
	}

	private static StandardMaterial3D GetOrCreateSelectionMaterial(Color color)
	{
		if (_sharedSelectionMaterial == null || !GodotObject.IsInstanceValid(_sharedSelectionMaterial))
		{
			_sharedSelectionMaterial = new StandardMaterial3D
			{
				AlbedoColor = color,
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				DisableReceiveShadows = true,
				EmissionEnabled = false,
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
			};
		}
		return _sharedSelectionMaterial;
	}

	private static StandardMaterial3D GetOrCreateHoverMaterial()
	{
		if (_sharedHoverMaterial == null || !GodotObject.IsInstanceValid(_sharedHoverMaterial))
		{
			_sharedHoverMaterial = new StandardMaterial3D
			{
				AlbedoColor = new Color(0.88f, 0.88f, 0.88f, 0.22f),
				Transparency = BaseMaterial3D.TransparencyEnum.Alpha,
				DisableReceiveShadows = true,
				EmissionEnabled = false,
				ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded
			};
		}
		return _sharedHoverMaterial;
	}

	public bool IsHovered
	{
		get => _isHovered;
		set
		{
			_isHovered = value;
			if (_hoverRing == null && _isHovered)
			{
				CreateHoverRing();
			}
			if (_hoverRing != null)
			{
				_hoverRing.Visible = _isHovered && !IsSelected;
			}
		}
	}

	private void CreateHoverRing()
	{
		if (_hoverRing != null) return;
		float baseRadius = GetBaseObstacleRadius();
		_hoverRing = new MeshInstance3D
		{
			Name = "_hover_ring",
			Mesh = GetOrCreateTorusMesh(baseRadius),
			Position = new Vector3(0, 0.05f, 0),
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			GIMode = GeometryInstance3D.GIModeEnum.Disabled,
			MaterialOverride = GetOrCreateHoverMaterial()
		};
		float ratio = GameHost.Instance != null ? GameHost.Instance.GetModelCollisionCircleRatio(GameHost.Instance.GetModelAssetKey(this)) : 1.0f;
		_hoverRing.Scale = new Vector3(ratio, 1.0f, ratio);
		_hoverRing.Visible = _isHovered && !IsSelected;
		AddChild(_hoverRing);
	}

	public virtual void UpdateCollisionCircleScale(float ratio)
	{
		Vector3 ringScale = new Vector3(ratio, 1.0f, ratio);
		if (_selectionRing != null)
		{
			_selectionRing.Scale = ringScale;
			_selectionRing.Visible = _isSelected;
		}
		if (_hoverRing != null)
		{
			_hoverRing.Scale = ringScale;
		}
	}

	public virtual bool IsSelected
	{
		get => _isSelected;
		set
		{
			_isSelected = value;
			if (_selectionRing == null && _isSelected)
			{
				CreateSelectionRing();
			}
			if (_selectionRing != null)
			{
				_selectionRing.Visible = _isSelected;
			}
			if (_hoverRing != null)
			{
				_hoverRing.Visible = _isHovered && !_isSelected;
			}
		}
	}

	public virtual void SetTemporarySelectionHighlight(bool highlight)
	{
		if (_selectionRing == null && highlight)
		{
			CreateSelectionRing();
		}
		if (_selectionRing != null)
		{
			_selectionRing.Visible = highlight || _isSelected;
		}
	}

	protected virtual Color GetSelectionRingColor()
	{
		return new Color(0.95f, 0.82f, 0.15f);
	}

	protected virtual void CreateSelectionRing()
	{
		if (_selectionRing != null) return;
		float baseRadius = GetBaseObstacleRadius();
		_selectionRing = new MeshInstance3D
		{
			Name = "_selection_ring",
			CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
			GIMode = GeometryInstance3D.GIModeEnum.Disabled,
			Mesh = GetOrCreateTorusMesh(baseRadius),
			Position = new Vector3(0, 0.05f, 0),
			MaterialOverride = GetOrCreateSelectionMaterial(GetSelectionRingColor())
		};
		float ratio = GameHost.Instance != null ? GameHost.Instance.GetModelCollisionCircleRatio(GameHost.Instance.GetModelAssetKey(this)) : 1.0f;
		_selectionRing.Scale = new Vector3(ratio, 1.0f, ratio);
		_selectionRing.Visible = _isSelected;
		AddChild(_selectionRing);
	}

	private static readonly Dictionary<string, (Shape3D Shape, Vector3 Offset)> _modelShapeCache = new(StringComparer.OrdinalIgnoreCase);

	protected static Aabb GetCombinedAabb(Node root)
	{
		Aabb totalAabb = new Aabb();
		bool first = true;
		CollectAabbRecursive(root, Transform3D.Identity, ref totalAabb, ref first);
		return totalAabb;
	}

	private static void CollectAabbRecursive(Node node, Transform3D currentTransform, ref Aabb totalAabb, ref bool first)
	{
		if (node == null) return;

		if (node is MeshInstance3D mi && mi.Mesh != null)
		{
			Aabb localAabb = mi.Mesh.GetAabb();
			if (localAabb.Size != Vector3.Zero)
			{
				for (int i = 0; i < 8; i++)
				{
					Vector3 corner = currentTransform * localAabb.GetEndpoint(i);
					if (first)
					{
						totalAabb = new Aabb(corner, Vector3.Zero);
						first = false;
					}
					else
					{
						totalAabb = totalAabb.Expand(corner);
					}
				}
			}
		}

		foreach (var child in node.GetChildren())
		{
			if (child is Node3D child3D)
			{
				CollectAabbRecursive(child, currentTransform * child3D.Transform, ref totalAabb, ref first);
			}
		}
	}

	private (Shape3D Shape, Vector3 Offset) GetOrCreateCollisionShape()
	{
		string modelPath = ResolvePropModelPath(PropId);
		if (!string.IsNullOrEmpty(modelPath) && _modelShapeCache.TryGetValue(modelPath, out var cached))
		{
			return cached;
		}

		if (!string.IsNullOrEmpty(modelPath))
		{
			try
			{
				Node modelNode = ModelCache.GetModel(modelPath);
				if (modelNode != null)
				{
					Aabb modelAabb = GetCombinedAabb(modelNode);
					modelNode.Free();

					if (modelAabb.Size.LengthSquared() > 0.01f)
					{
						var (analShape, analOffset) = Realm.Godot.Services.ModelOptimization.ModelOptimizerService.GenerateAnalyticalCollisionShape(modelAabb, isBuilding: true);
						if (analShape != null)
						{
							var result = (analShape, analOffset);
							_modelShapeCache[modelPath] = result;
							return result;
						}
					}
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"Failed to compute analytical collision shape for '{PropId}': {ex.Message}");
			}
		}

		float radius = GetBaseObstacleRadius();
		float height = Math.Max(1.0f, radius * 2.5f);
		var fallbackBox = new BoxShape3D
		{
			Size = new Vector3(radius * 2.0f, height, radius * 2.0f)
		};
		var fallbackResult = (fallbackBox, new Vector3(0, height * 0.5f, 0));
		if (!string.IsNullOrEmpty(modelPath))
		{
			_modelShapeCache[modelPath] = fallbackResult;
		}
		return fallbackResult;
	}

	public bool IsPreview { get; set; } = false;

	public override void _Ready()
	{
		Name = $"Prop_{PropId}_{Guid.NewGuid().ToString().Substring(0, 4)}";
		
		SetNotifyTransform(true);

		if (IsPreview)
		{
			CreatePropVisual();
			return;
		}

		var collisionShape = new CollisionShape3D();
		collisionShape.Name = "CollisionShape";
		AddChild(collisionShape);
		
		var (shape, offset) = GetOrCreateCollisionShape();
		collisionShape.Shape = shape;
		collisionShape.Position = offset;

		CreatePropVisual();
	}

	public void UpdateVisualYOffset(float yOffset)
	{
		var visual = GetNodeOrNull<Node3D>("VisualModel");
		if (visual != null)
		{
			visual.Position = new Vector3(visual.Position.X, yOffset, visual.Position.Z);
		}
	}

	public void UpdateVisualScale(float globalScale)
	{
		var visual = GetNodeOrNull<Node3D>("VisualModel");
		if (visual != null && GodotObject.IsInstanceValid(visual))
		{
			visual.Scale = new Vector3(globalScale, globalScale, globalScale);
			UpdateLodVisibility();
		}
	}

	public void RefreshPropVisual()
	{
		var visual = GetNodeOrNull<Node3D>("VisualModel");
		if (visual != null && GodotObject.IsInstanceValid(visual))
		{
			RemoveChild(visual);
			visual.QueueFree();
		}
		CreatePropVisual();
	}

	private void CreatePropVisual()
	{
		if (IsPreview)
		{
			var visual = GetNodeOrNull<Node3D>("VisualModel");
			if (visual == null)
			{
				visual = new Node3D();
				visual.Name = "VisualModel";
				string assetKey = GameHost.Instance != null ? GameHost.Instance.GetModelAssetKey(PropId) : "";
				float yOffset = GameHost.Instance != null ? GameHost.Instance.GetModelYOffset(assetKey) : 0f;
				float globalScale = GameHost.Instance != null ? GameHost.Instance.GetModelScale(this) : 1.0f;
				visual.Position = new Vector3(0, yOffset, 0);
				visual.Scale = new Vector3(globalScale, globalScale, globalScale);
				AddChild(visual);

				string modelPath = ResolvePropModelPath(PropId);
				try
				{
					if (!string.IsNullOrEmpty(modelPath))
					{
						Node node = ModelCache.GetModel(modelPath);
						if (node != null)
						{
							visual.AddChild(node);
							if (!IsPreview)
							{
								GameHost.Instance?.ApplyAllGlobalOverridesToObject(this);
							}
						}
					}
				}
				catch (Exception ex)
				{
					GD.PrintErr($"Failed to load prop visual for '{PropId}' ({modelPath}): {ex.Message}");
				}

				UpdateLodVisibility();
			}
		}

		if (!IsPreview)
		{
			PropMultiMeshManager.Instance?.MarkDirty(PropId);
		}
	}

	public override void _Notification(int what)
	{
		base._Notification(what);
		if (what == NotificationTransformChanged)
		{
			UpdateLodVisibility();
		}
	}

	public virtual void UpdateLodVisibility()
	{
		var visual = GetNodeOrNull<Node3D>("VisualModel");
		if (visual != null && GodotObject.IsInstanceValid(visual))
		{
			float effectiveScale = Math.Max(0.01f, Scale.Y * visual.Scale.Y);
			Realm.Godot.Services.ModelOptimization.GltfDocumentExtensionMsftLod.UpdateLodVisibilityRanges(visual, effectiveScale);
		}
	}

	private static readonly Dictionary<string, string> _resolvedModelPathCache = new(StringComparer.OrdinalIgnoreCase);

	private string ResolvePropModelPath(string propId)
	{
		if (string.IsNullOrEmpty(propId))
			return string.Empty;

		if (_resolvedModelPathCache.TryGetValue(propId, out string cachedPath))
			return cachedPath;

		string resolved = ResolvePropModelPathInternal(propId);
		_resolvedModelPathCache[propId] = resolved;
		return resolved;
	}

	public static void ClearModelPathCache()
	{
		_resolvedModelPathCache.Clear();
		_modelShapeCache.Clear();
		_sharedTorusMeshes.Clear();
	}

	private string ResolvePropModelPathInternal(string propId)
	{
		if (string.IsNullOrEmpty(propId))
			return string.Empty;

		string targetModel = propId;
		string cleanId = System.IO.Path.GetFileNameWithoutExtension(propId);

		if (GameHost.PropRegistry != null && ((GameHost.PropRegistry.TryGetValue(propId, out var propMeta) || GameHost.PropRegistry.TryGetValue(cleanId, out propMeta)) && !string.IsNullOrEmpty(propMeta.ModelPath)))
		{
			targetModel = propMeta.ModelPath;
		}
		else if (GameHost.ResourceRegistry != null && ((GameHost.ResourceRegistry.TryGetValue(propId, out var resMeta) || GameHost.ResourceRegistry.TryGetValue(cleanId, out resMeta)) && !string.IsNullOrEmpty(resMeta.ModelPath)))
		{
			targetModel = resMeta.ModelPath;
		}
		else if (GameHost.UnitRegistry != null && ((GameHost.UnitRegistry.TryGetValue(propId, out var unitMeta) || GameHost.UnitRegistry.TryGetValue(cleanId, out unitMeta)) && !string.IsNullOrEmpty(unitMeta.ModelPath)))
		{
			targetModel = unitMeta.ModelPath;
		}
		else if (GameHost.BuildingRegistry != null && ((GameHost.BuildingRegistry.TryGetValue(propId, out var bldMeta) || GameHost.BuildingRegistry.TryGetValue(cleanId, out bldMeta)) && !string.IsNullOrEmpty(bldMeta.ModelPath)))
		{
			targetModel = bldMeta.ModelPath;
		}

		if (string.IsNullOrEmpty(targetModel))
			return string.Empty;

		if (targetModel.StartsWith("res://") || System.IO.File.Exists(targetModel))
			return targetModel;

		string wsPath = MapWorkspaceService.GetActiveWorkspacePath();
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

public partial class Resource3D : Prop3D
{
}
