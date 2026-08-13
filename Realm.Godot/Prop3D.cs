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

	private string _propId = "tree";

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
		_hoverRing = new MeshInstance3D();
		var torusMesh = new TorusMesh();
		if (PropId == "goldmine")
		{
			torusMesh.InnerRadius = 3.2f;
			torusMesh.OuterRadius = 3.5f;
		}
		else if (PropId == "rock")
		{
			torusMesh.InnerRadius = 1.8f;
			torusMesh.OuterRadius = 2.0f;
		}
		else if (PropId == "pillar")
		{
			torusMesh.InnerRadius = 1.0f;
			torusMesh.OuterRadius = 1.2f;
		}
		else if (PropId == "tree" || PropId.Contains("tree"))
		{
			torusMesh.InnerRadius = 3.6f;
			torusMesh.OuterRadius = 4.2f;
		}
		else
		{
			torusMesh.InnerRadius = 1.2f;
			torusMesh.OuterRadius = 1.4f;
		}
		_hoverRing.Mesh = torusMesh;
		_hoverRing.Position = new Vector3(0, 0.05f, 0);
		var material = new StandardMaterial3D();
		material.AlbedoColor = new Color(1f, 1f, 1f, 0.4f);
		material.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		material.EmissionEnabled = true;
		material.Emission = new Color(1f, 1f, 1f) * 0.3f;
		material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
		_hoverRing.MaterialOverride = material;
		float ratio = GameHost.Instance != null ? GameHost.Instance.GetModelCollisionCircleRatio(GameHost.Instance.GetModelAssetKey(this)) : 1.0f;
		_hoverRing.Scale = new Vector3(ratio, 1.0f, ratio);
		AddChild(_hoverRing);
	}

	public virtual void UpdateCollisionCircleScale(float ratio)
	{
		Vector3 ringScale = new Vector3(ratio, 1.0f, ratio);
		if (_selectionRing == null)
		{
			CreateSelectionRing();
		}
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
		_selectionRing = new MeshInstance3D();
		var torusMesh = new TorusMesh();
		
		if (PropId == "goldmine")
		{
			torusMesh.InnerRadius = 3.2f;
			torusMesh.OuterRadius = 3.5f;
		}
		else if (PropId == "rock")
		{
			torusMesh.InnerRadius = 1.8f;
			torusMesh.OuterRadius = 2.0f;
		}
		else if (PropId == "pillar")
		{
			torusMesh.InnerRadius = 1.0f;
			torusMesh.OuterRadius = 1.2f;
		}
		else if (PropId == "tree" || PropId.Contains("tree"))
		{
			torusMesh.InnerRadius = 3.6f;
			torusMesh.OuterRadius = 4.2f;
		}
		else
		{
			torusMesh.InnerRadius = 1.2f;
			torusMesh.OuterRadius = 1.4f;
		}
		
		_selectionRing.Mesh = torusMesh;
		_selectionRing.Position = new Vector3(0, 0.05f, 0);

		var material = new StandardMaterial3D();
		Color color = GetSelectionRingColor();
		material.AlbedoColor = color;
		material.EmissionEnabled = true;
		material.Emission = color;
		material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
		
		_selectionRing.MaterialOverride = material;
		float ratio = GameHost.Instance != null ? GameHost.Instance.GetModelCollisionCircleRatio(GameHost.Instance.GetModelAssetKey(this)) : 1.0f;
		_selectionRing.Scale = new Vector3(ratio, 1.0f, ratio);
		_selectionRing.Visible = _isSelected;
		AddChild(_selectionRing);
	}

	public bool IsPreview { get; set; } = false;

	public override void _Ready()
	{
		Name = $"Prop_{PropId}_{Guid.NewGuid().ToString().Substring(0, 4)}";
		
		if (IsPreview)
		{
			CreatePropVisual();
			return;
		}

		var collisionShape = new CollisionShape3D();
		collisionShape.Name = "CollisionShape";
		AddChild(collisionShape);
		
		var boxShape = new BoxShape3D();
		if (PropId == "goldmine")
		{
			boxShape.Size = new Vector3(4.0f, 2.5f, 4.0f);
			collisionShape.Position = new Vector3(0, 1.25f, 0);
		}
		else if (PropId == "rock")
		{
			boxShape.Size = new Vector3(2.4f, 1.8f, 2.4f);
			collisionShape.Position = new Vector3(0, 0.9f, 0);
		}
		else if (PropId == "pillar")
		{
			boxShape.Size = new Vector3(1.2f, 5.0f, 1.2f);
			collisionShape.Position = new Vector3(0, 2.5f, 0);
		}
		else if (PropId == "flag")
		{
			boxShape.Size = new Vector3(0.5f, 6.0f, 2.0f);
			collisionShape.Position = new Vector3(0.8f, 3.0f, 0);
		}
		else if (PropId == "tree" || PropId.Contains("tree"))
		{
			boxShape.Size = new Vector3(4.5f, 13.5f, 4.5f);
			collisionShape.Position = new Vector3(0, 6.75f, 0);
		}
		else
		{
			boxShape.Size = new Vector3(1.5f, 4.5f, 1.5f);
			collisionShape.Position = new Vector3(0, 2.25f, 0);
		}
		collisionShape.Shape = boxShape;

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
		bool isEditor = GameHost.Instance != null && GameHost.Instance.IsMapEditorMode;
		if (IsPreview || isEditor)
		{
			var visual = GetNodeOrNull<Node3D>("VisualModel");
			if (visual == null)
			{
				visual = new Node3D();
				visual.Name = "VisualModel";
				if (PropId == "tree" || PropId.Contains("tree"))
				{
					visual.Scale = new Vector3(3f, 3f, 3f);
				}
				string assetKey = GameHost.Instance != null ? GameHost.Instance.GetModelAssetKey(PropId) : "";
				float yOffset = GameHost.Instance != null ? GameHost.Instance.GetModelYOffset(assetKey) : 0f;
				visual.Position = new Vector3(0, yOffset, 0);
				AddChild(visual);

				string modelPath = ResolvePropModelPath(PropId);
				try
				{
					Node node = ModelCache.GetModel(modelPath);
					if (node != null)
					{
						visual.AddChild(node);
					}
				}
				catch (Exception ex)
				{
					GD.PrintErr($"Failed to load prop visual for '{PropId}' ({modelPath}): {ex.Message}");
				}
			}
		}

		if (!IsPreview)
		{
			PropMultiMeshManager.Instance?.MarkDirty(PropId);
		}
	}

	private static readonly Dictionary<string, string> _resolvedModelPathCache = new(StringComparer.OrdinalIgnoreCase);

	private string ResolvePropModelPath(string propId)
	{
		if (string.IsNullOrEmpty(propId))
			propId = "wooden_box.glb";

		if (_resolvedModelPathCache.TryGetValue(propId, out string cachedPath))
			return cachedPath;

		string resolved = ResolvePropModelPathInternal(propId);
		_resolvedModelPathCache[propId] = resolved;
		return resolved;
	}

	public static void ClearModelPathCache()
	{
		_resolvedModelPathCache.Clear();
	}

	private string ResolvePropModelPathInternal(string propId)
	{
		if (string.IsNullOrEmpty(propId))
			propId = "wooden_box.glb";

		string targetModel = propId;
		if (GameHost.PropRegistry != null && GameHost.PropRegistry.TryGetValue(propId, out var propMeta) && !string.IsNullOrEmpty(propMeta.ModelPath))
		{
			targetModel = propMeta.ModelPath;
		}
		else if (GameHost.ResourceRegistry != null && GameHost.ResourceRegistry.TryGetValue(propId, out var resMeta) && !string.IsNullOrEmpty(resMeta.ModelPath))
		{
			targetModel = resMeta.ModelPath;
		}
		else if (GameHost.UnitRegistry != null && GameHost.UnitRegistry.TryGetValue(propId, out var unitMeta) && !string.IsNullOrEmpty(unitMeta.ModelPath))
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

		// Fallback check root workspace
		string rootCandidate = System.IO.Path.Combine(wsPath, filename);
		if (System.IO.File.Exists(rootCandidate))
			return rootCandidate;

		return targetModel;
	}
}

public partial class Resource3D : Prop3D
{
}
