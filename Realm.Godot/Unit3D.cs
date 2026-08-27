using System;
using System.Collections.Generic;
using Arch.Core;
using Godot;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Tags;
using Realm.Ecs.Components.Terrain;

public partial class Unit3D : Prop3D
{
	private string _unitId = "worker";

	public string UnitId
	{
		get
		{
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld.IsAlive(Entity)
				&& GameHost.Instance.EcsWorld.Has<DefinitionId>(Entity))
				return GameHost.Instance.EcsWorld.Get<DefinitionId>(Entity).Value;
			return _unitId;
		}
		set
		{
			_unitId = value;
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld.IsAlive(Entity))
			{
				var world = GameHost.Instance.EcsWorld;
				if (world.Has<DefinitionId>(Entity))
					world.Set(Entity, new DefinitionId(value));
				else
					world.Add(Entity, new DefinitionId(value));
			}
		}
	}

	public override string PropId
	{
		get => UnitId;
		set => UnitId = value;
	}

	private bool _isBuilding;
	public bool IsBuilding
	{
		get
		{
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld != null && Entity != default && GameHost.Instance.EcsWorld.IsAlive(Entity))
				return GameHost.Instance.EcsWorld.Has<Building>(Entity);
			if (GameHost.UnitRegistry != null && !string.IsNullOrEmpty(UnitId) && GameHost.UnitRegistry.TryGetValue(UnitId, out var meta))
				return meta.Speed == 0f;
			return _isBuilding;
		}
		set
		{
			_isBuilding = value;
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld != null && Entity != default && GameHost.Instance.EcsWorld.IsAlive(Entity))
			{
				var world = GameHost.Instance.EcsWorld;
				if (value)
				{
					if (!world.Has<Building>(Entity))
						world.Add(Entity, new Building());
				}
				else
				{
					if (world.Has<Building>(Entity))
						world.Remove<Building>(Entity);
				}
			}
		}
	}

	private bool _isResource;
	public bool IsResource
	{
		get
		{
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld != null && Entity != default && GameHost.Instance.EcsWorld.IsAlive(Entity))
			{
				if (GameHost.Instance.EcsWorld.Has<Realm.Ecs.Components.Resources.ResourceNode>(Entity))
					return true;
			}
			if (GameHost.ResourceRegistry != null && !string.IsNullOrEmpty(UnitId) && GameHost.ResourceRegistry.ContainsKey(UnitId))
				return true;
			return _isResource;
		}
		set
		{
			_isResource = value;
		}
	}

	private Node3D _modelNode;
	private AnimationPlayer _animationPlayer;
	private string _currentAnimation = string.Empty;
	private MeshInstance3D _selectionRing;
	private bool _isSelected = false;
	private Node3D _pathVisualsContainer;
	private readonly System.Collections.Generic.List<MeshInstance3D> _pathMarkersPool = new();
	private readonly System.Collections.Generic.List<MeshInstance3D> _pathLinesPool = new();

	private int _player = 0;

	public int Player
	{
		get
		{
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld.IsAlive(Entity)
				&& GameHost.Instance.EcsWorld.Has<UnitOwnerPlayer>(Entity))
				return GameHost.Instance.EcsWorld.Get<UnitOwnerPlayer>(Entity).PlayerIndex;
			return _player;
		}
		set
		{
			_player = value;
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld.IsAlive(Entity))
			{
				var world = GameHost.Instance.EcsWorld;
				if (world.Has<UnitOwnerPlayer>(Entity))
					world.Set(Entity, new UnitOwnerPlayer(value));
				else
					world.Add(Entity, new UnitOwnerPlayer(value));
			}
			UpdatePlayerColorVisual();
		}
	}

	public int PlayerIndex
	{
		get => Player;
		set => Player = value;
	}

	public Color PlayerColor { get; private set; } = PlayerColorConfig.GetColor(0);

	public void SetPlayer(int playerIndex)
	{
		Player = playerIndex;
	}

	public void SetPlayerColor(Color color)
	{
		PlayerColor = color;
		if (_modelNode != null && GodotObject.IsInstanceValid(_modelNode))
		{
			bool ignorePlayerColor = GameHost.Instance != null && (GameHost.Instance.GetModelIgnorePlayerColor(ModelPath) || GameHost.Instance.GetModelIgnorePlayerColor(UnitId));
			Realm.Godot.Utils.PlayerColorShaderManager.SetPlayerColor(_modelNode, color);
			Realm.Godot.Utils.PlayerColorShaderManager.SetIgnorePlayerColor(_modelNode, ignorePlayerColor);
		}
	}

	public void InterpolatePlayerColor(Color targetColor, float weight)
	{
		Color blended = PlayerColor.Lerp(targetColor, weight);
		SetPlayerColor(blended);
	}

	public void UpdatePlayerColorVisual()
	{
		bool ignorePlayerColor = GameHost.Instance != null && (GameHost.Instance.GetModelIgnorePlayerColor(ModelPath) || GameHost.Instance.GetModelIgnorePlayerColor(UnitId));
		if (ignorePlayerColor)
		{
			if (_modelNode != null && GodotObject.IsInstanceValid(_modelNode))
			{
				Realm.Godot.Utils.PlayerColorShaderManager.SetIgnorePlayerColor(_modelNode, true);
			}
			return;
		}

		Color resolvedColor = PlayerColorConfig.GetColor(Player);
		if (LobbyManager.Instance != null && LobbyManager.Instance.PlayerList.Count > 0)
		{
			var matchPlayer = LobbyManager.Instance.PlayerList.Find(x => x.Slot == Player);
			if (matchPlayer != null)
			{
				resolvedColor = matchPlayer.Color;
			}
		}
		SetPlayerColor(resolvedColor);
	}

	public bool IsEnemy
	{
		get
		{
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld.IsAlive(Entity)
				&& GameHost.Instance.EcsWorld.Has<UnitFaction>(Entity))
				return GameHost.Instance.EcsWorld.Get<UnitFaction>(Entity).IsEnemy;
			return false;
		}
		set
		{
			if (GameHost.Instance != null && GameHost.Instance.EcsWorld.IsAlive(Entity))
			{
				var world = GameHost.Instance.EcsWorld;
				if (world.Has<UnitFaction>(Entity))
					world.Set(Entity, new UnitFaction(value));
				else
					world.Add(Entity, new UnitFaction(value));
			}
		}
	}

	private Node3D _rallyVisualsContainer;
	private MeshInstance3D _rallyMarker;
	private readonly System.Collections.Generic.List<MeshInstance3D> _rallyMarkersPool = new();
	private readonly System.Collections.Generic.List<MeshInstance3D> _rallyLinesPool = new();

	public override bool IsSelected
	{
		get => base.IsSelected;
		set
		{
			base.IsSelected = value;
			if (IsBuilding && !IsEnemy)
			{
				UpdateRallyVisuals();
			}

			if (!value)
			{
				HidePathVisuals();
				if (_rallyVisualsContainer != null)
				{
					_rallyVisualsContainer.Visible = false;
				}
			}
			SetProcess(value);
		}
	}

	public override void _Ready()
	{
		if (IsPreview) return;

		SetNotifyTransform(true);

		var collisionShape = new CollisionShape3D();
		collisionShape.Name = "CollisionShape";
		if (IsBuilding)
		{
			var boxShape = new BoxShape3D();
			float size = (UnitId == "castle") ? 4f : 3f;
			boxShape.Size = new Vector3(size, size, size);
			collisionShape.Shape = boxShape;
			collisionShape.Position = new Vector3(0, size * 0.5f, 0);
		}
		else
		{
			var capsuleShape = new CapsuleShape3D();
			capsuleShape.Radius = 1.0f;
			capsuleShape.Height = 2.5f;
			collisionShape.Shape = capsuleShape;
			collisionShape.Position = new Vector3(0, 1.25f, 0);
		}
		AddChild(collisionShape);


		CreateSelectionRing();
		SetProcess(false);
	}

	public override void UpdateLodVisibility()
	{
		if (_modelNode != null && GodotObject.IsInstanceValid(_modelNode))
		{
			float effectiveScale = Math.Max(0.01f, Scale.Y * _modelNode.Scale.Y);
			Realm.Godot.Services.ModelOptimization.GltfDocumentExtensionMsftLod.UpdateLodVisibilityRanges(_modelNode, effectiveScale);
		}
	}

	private float _baseModelYOffset = 0f;
	public float BaseModelYOffset => _baseModelYOffset;
	public string ModelPath { get; private set; }
	public Node3D ModelNode => _modelNode;

	public void UpdateModelYOffset(float yOffset)
	{
		if (_modelNode != null)
		{
			_modelNode.Position = new Vector3(_modelNode.Position.X, _baseModelYOffset + yOffset, _modelNode.Position.Z);
		}
	}

	public void UpdateModelScale(float globalScale)
	{
		if (_modelNode != null && GodotObject.IsInstanceValid(_modelNode))
		{
			float baseScale = IsResource ? 2.75f : (IsBuilding ? 1.2f : 1.5f);
			float finalScale = baseScale * globalScale;
			_modelNode.Scale = new Vector3(finalScale, finalScale, finalScale);
			float minY = GetMinY(_modelNode, Transform3D.Identity);
			_baseModelYOffset = -minY * _modelNode.Scale.Y;
			string assetKey = GameHost.Instance != null ? GameHost.Instance.GetModelAssetKey(ModelPath ?? UnitId) : "";
			float yOffset = GameHost.Instance != null ? GameHost.Instance.GetModelYOffset(assetKey) : 0f;
			_modelNode.Position = new Vector3(0f, _baseModelYOffset + yOffset, 0f);
			UpdateLodVisibility();
		}
	}

	public void LoadModel(string modelPath)
	{
		if (string.IsNullOrEmpty(modelPath)) return;
		ModelPath = modelPath;

		try
		{
			if (_modelNode != null && GodotObject.IsInstanceValid(_modelNode))
			{
				RemoveChild(_modelNode);
				_modelNode.QueueFree();
				_modelNode = null;
			}

			_modelNode = Realm.Godot.Utils.ModelCache.GetModel(modelPath) as Node3D;

			if (_modelNode != null)
			{
				AddChild(_modelNode);
				_animationPlayer = Realm.Godot.Animation.AnimationRetargetingService.FindOrCreateAnimationPlayer(_modelNode);
				Realm.Godot.Animation.AnimationRetargetingService.LoadAndBindUnitAnimations(_modelNode, UnitId, modelPath);
				SeekToIdleFirstFrame();

				float baseScale = IsResource ? 2.75f : (IsBuilding ? 1.2f : 1.5f);
				string assetKey = GameHost.Instance != null ? GameHost.Instance.GetModelAssetKey(modelPath) : "";
				float globalScale = GameHost.Instance != null ? GameHost.Instance.GetModelScale(assetKey) : 1.0f;
				float finalScale = baseScale * globalScale;
				_modelNode.Scale = new Vector3(finalScale, finalScale, finalScale);

				UpdateLodVisibility();

				float minY = GetMinY(_modelNode, Transform3D.Identity);
				_baseModelYOffset = -minY * _modelNode.Scale.Y;
				float yOffset = GameHost.Instance != null ? GameHost.Instance.GetModelYOffset(assetKey) : 0f;
				_modelNode.Position = new Vector3(0f, _baseModelYOffset + yOffset, 0f);

				if (!IsPreview)
				{
					bool ignorePlayerColor = GameHost.Instance != null && (GameHost.Instance.GetModelIgnorePlayerColor(modelPath) || GameHost.Instance.GetModelIgnorePlayerColor(UnitId));
					bool normalizeLuminance = GameHost.Instance != null && (GameHost.Instance.GetModelNormalizeLuminance(modelPath) || GameHost.Instance.GetModelNormalizeLuminance(UnitId));
					Realm.Godot.Utils.PlayerColorShaderManager.ApplyPlayerColorShader(_modelNode, PlayerColor, ignorePlayerColor, normalizeLuminance);
					if (!ignorePlayerColor)
					{
						UpdatePlayerColorVisual();
					}
					else
					{
						Realm.Godot.Utils.PlayerColorShaderManager.SetIgnorePlayerColor(_modelNode, true);
					}

					GameHost.Instance?.ApplyAllGlobalOverridesToObject(this);
				}

				var colShapeNode = GetNodeOrNull<CollisionShape3D>("CollisionShape");
				if (colShapeNode != null && _modelNode != null)
				{
					Aabb modelAabb = GetCombinedAabb(_modelNode);
					if (modelAabb.Size.LengthSquared() > 0.01f)
					{
						var (analShape, analOffset) = Realm.Godot.Services.ModelOptimization.ModelOptimizerService.GenerateAnalyticalCollisionShape(modelAabb, IsBuilding);
						if (analShape != null)
						{
							colShapeNode.Shape = analShape;
							colShapeNode.Position = analOffset;
						}
					}
				}
			}
			else
			{
				GD.PrintErr($"Failed to load 3D model path: {modelPath}");
				CreateFallbackMesh();
			}
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"Error loading model {modelPath}: {ex.Message}");
			CreateFallbackMesh();
		}

		UpdateDropShadow();
	}

	private Aabb GetCombinedAabb(Node root)
	{
		Aabb totalAabb = new Aabb();
		bool first = true;
		CollectAabbRecursive(root, Transform3D.Identity, ref totalAabb, ref first);
		return totalAabb;
	}

	private void CollectAabbRecursive(Node node, Transform3D currentTransform, ref Aabb totalAabb, ref bool first)
	{
		if (node is MeshInstance3D mi && mi.Mesh != null)
		{
			Aabb localAabb = mi.Mesh.GetAabb();
			Vector3[] corners = new Vector3[8]
			{
				new Vector3(localAabb.Position.X, localAabb.Position.Y, localAabb.Position.Z),
				new Vector3(localAabb.Position.X + localAabb.Size.X, localAabb.Position.Y, localAabb.Position.Z),
				new Vector3(localAabb.Position.X, localAabb.Position.Y + localAabb.Size.Y, localAabb.Position.Z),
				new Vector3(localAabb.Position.X, localAabb.Position.Y, localAabb.Position.Z + localAabb.Size.Z),
				new Vector3(localAabb.Position.X + localAabb.Size.X, localAabb.Position.Y + localAabb.Size.Y, localAabb.Position.Z),
				new Vector3(localAabb.Position.X + localAabb.Size.X, localAabb.Position.Y, localAabb.Position.Z + localAabb.Size.Z),
				new Vector3(localAabb.Position.X, localAabb.Position.Y + localAabb.Size.Y, localAabb.Position.Z + localAabb.Size.Z),
				new Vector3(localAabb.Position.X + localAabb.Size.X, localAabb.Position.Y + localAabb.Size.Y, localAabb.Position.Z + localAabb.Size.Z)
			};

			foreach (var c in corners)
			{
				Vector3 transformed = currentTransform * c;
				if (first)
				{
					totalAabb = new Aabb(transformed, Vector3.Zero);
					first = false;
				}
				else
				{
					totalAabb = totalAabb.Expand(transformed);
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

	/// <summary>
	///     Adds (or refreshes) a projected drop shadow for flying units so they read clearly
	///     against the terrain below instead of appearing to float in empty space.
	/// </summary>
	private void UpdateDropShadow()
	{
		if (GameHost.Instance == null || !GameHost.Instance.IsPathingCapability(Entity, TerrainPathingFlags.Flying))
		{
			var oldDecal = GetNodeOrNull<Decal>("DropShadow");
			if (oldDecal != null)
			{
				oldDecal.QueueFree();
			}
			return;
		}

		var existing = GetNodeOrNull<Decal>("DropShadow");
		if (existing != null)
		{
			float updatedRadius = GameHost.Instance.GetOrCalculateObstacleRadius(UnitId, this, IsBuilding) * GameHost.Instance.GetModelCollisionCircleRatio(ModelPath);
			if (updatedRadius > 0f)
			{
				existing.Size = new Vector3(updatedRadius * 2.5f, 3f, updatedRadius * 2.5f);
			}
			return;
		}

		float radius = GameHost.Instance.GetOrCalculateObstacleRadius(UnitId, this, IsBuilding) * GameHost.Instance.GetModelCollisionCircleRatio(ModelPath);
		if (radius <= 0f) radius = 1f;

		Decal shadowDecal = new Decal();
		shadowDecal.Name = "DropShadow";
		shadowDecal.TextureAlbedo = GameHost.Instance.GetSharedShadowGradient();
		shadowDecal.Size = new Vector3(radius * 2.5f, 3f, radius * 2.5f);
		shadowDecal.Position = Vector3.Zero;
		// Decals project along local -Z; tilt the node down so the shadow lands on the terrain.
		shadowDecal.RotationDegrees = new Vector3(-90f, 0f, 0f);
		// Project only onto the terrain layer so the shadow does not bleed onto other units.
		shadowDecal.CullMask = 1;

		AddChild(shadowDecal);
	}

	public void PlayAnimation(string animName)
	{
		if (_animationPlayer == null || !GodotObject.IsInstanceValid(_animationPlayer)) return;
		if (string.IsNullOrEmpty(animName)) return;

		StringName resolved = ResolveAnimationName(animName);
		if (resolved == null) return;

		if (_currentAnimation == resolved.ToString() && _animationPlayer.IsPlaying()) return;

		_currentAnimation = resolved.ToString();

		var animResource = _animationPlayer.GetAnimation(resolved);
		if (animResource != null)
		{
			if (animName.Equals("Death", System.StringComparison.OrdinalIgnoreCase))
			{
				animResource.LoopMode = global::Godot.Animation.LoopModeEnum.None;
			}
			else
			{
				animResource.LoopMode = global::Godot.Animation.LoopModeEnum.Linear;
			}
		}

		_animationPlayer.Play(resolved);
	}

	private void SeekToIdleFirstFrame()
	{
		if (_animationPlayer == null || !GodotObject.IsInstanceValid(_animationPlayer)) return;
		StringName idleAnim = ResolveAnimationName("Idle");
		if (idleAnim == null) return;
		_animationPlayer.Play(idleAnim);
		_animationPlayer.Seek(0.0, true);
		_animationPlayer.Stop(true);
	}

	private StringName ResolveAnimationName(string animName)
	{
		if (_animationPlayer == null) return null;

		var animations = _animationPlayer.GetAnimationList();

		if (!animName.Contains('_'))
		{
			var variants = new List<StringName>();
			string prefix = $"{animName}_";
			foreach (var name in animations)
			{
				string nameStr = name.ToString();
				if (nameStr.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
				{
					variants.Add(name);
				}
			}

			if (variants.Count > 0)
			{
				int randIdx = Random.Shared.Next(variants.Count);
				return variants[randIdx];
			}
		}

		StringName direct = new StringName(animName);
		if (_animationPlayer.HasAnimation(direct))
		{
			return direct;
		}

		foreach (var name in animations)
		{
			if (name.ToString().Equals(animName, System.StringComparison.OrdinalIgnoreCase))
				return name;
		}

		string resolvedPath = Realm.Godot.Animation.AnimationRetargetingService.ResolveAnimationFilePath(animName, UnitId);
		if (!string.IsNullOrEmpty(resolvedPath))
		{
			var animData = Realm.Godot.Animation.AnimationRetargetingService.GetOrLoadRanimData(resolvedPath);
			if (animData != null && _modelNode != null)
			{
				if (Realm.Godot.Animation.AnimationRetargetingService.RetargetAndBind(animData, _modelNode, animName, out _))
				{
					return direct;
				}
			}
		}

		return null;
	}

	public void ApplyModelTint(Color color)
	{
		SetPlayerColor(color);
	}


	private void CreateFallbackMesh()
	{

		var meshInstance = new MeshInstance3D();
		var material = new StandardMaterial3D();
		
		if (IsBuilding)
		{
			var boxMesh = new BoxMesh();
			boxMesh.Size = new Vector3(4, 4, 4);
			meshInstance.Mesh = boxMesh;
			material.AlbedoColor = new Color(0.2f, 0.4f, 0.8f);
			meshInstance.Position = new Vector3(0, 2f, 0);
		}
		else
		{
			var capsuleMesh = new CapsuleMesh();
			capsuleMesh.Radius = 0.8f;
			capsuleMesh.Height = 2.0f;
			meshInstance.Mesh = capsuleMesh;
			material.AlbedoColor = new Color(0.8f, 0.2f, 0.2f);
			meshInstance.Position = new Vector3(0, 1f, 0);
		}
		
		meshInstance.MaterialOverride = material;
		AddChild(meshInstance);
		_modelNode = meshInstance;
	}

	protected override Color GetSelectionRingColor()
	{
		return new Color(0.22f, 0.54f, 0.26f);
	}

	private float GetMinY(Node node, Transform3D currentTransform)
	{
		float minY = float.MaxValue;
		bool foundMesh = false;
		GetMinYRecursive(node, currentTransform, ref minY, ref foundMesh);
		if (!foundMesh)
		{
			GD.PushWarning($"[Unit3D] No mesh instances found under model '{Name}' — vertical offset defaults to 0, the unit may sit below the terrain.");
		}
		return foundMesh ? minY : 0f;
	}

	private void GetMinYRecursive(Node node, Transform3D currentTransform, ref float minY, ref bool foundMesh)
	{
		if (node is MeshInstance3D meshInstance)
		{
			var mesh = meshInstance.Mesh;
			if (mesh != null)
			{
				var localAabb = mesh.GetAabb();

				Vector3[] corners = new Vector3[8] {
					new Vector3(localAabb.Position.X, localAabb.Position.Y, localAabb.Position.Z),
					new Vector3(localAabb.Position.X + localAabb.Size.X, localAabb.Position.Y, localAabb.Position.Z),
					new Vector3(localAabb.Position.X, localAabb.Position.Y + localAabb.Size.Y, localAabb.Position.Z),
					new Vector3(localAabb.Position.X, localAabb.Position.Y, localAabb.Position.Z + localAabb.Size.Z),
					new Vector3(localAabb.Position.X + localAabb.Size.X, localAabb.Position.Y + localAabb.Size.Y, localAabb.Position.Z),
					new Vector3(localAabb.Position.X + localAabb.Size.X, localAabb.Position.Y, localAabb.Position.Z + localAabb.Size.Z),
					new Vector3(localAabb.Position.X, localAabb.Position.Y + localAabb.Size.Y, localAabb.Position.Z + localAabb.Size.Z),
					new Vector3(localAabb.Position.X + localAabb.Size.X, localAabb.Position.Y + localAabb.Size.Y, localAabb.Position.Z + localAabb.Size.Z)
				};

				foreach (var corner in corners)
				{
					Vector3 modelSpaceCorner = currentTransform * corner;
					if (!foundMesh || modelSpaceCorner.Y < minY)
					{
						minY = modelSpaceCorner.Y;
						foundMesh = true;
					}
				}
			}
		}

		foreach (var child in node.GetChildren())
		{
			if (child is Node3D child3D)
			{
				GetMinYRecursive(child, currentTransform * child3D.Transform, ref minY, ref foundMesh);
			}
			else
			{
				GetMinYRecursive(child, currentTransform, ref minY, ref foundMesh);
			}
		}
	}

	private void GetRemainingRallyPoints(System.Collections.Generic.List<Vector3> points)
	{
		points.Clear();
		if (GameHost.Instance == null || !GameHost.Instance.EcsWorld.IsAlive(Entity)) return;

		var world = GameHost.Instance.EcsWorld;
		points.Add(GlobalPosition);

		if (world.Has<Realm.Ecs.Components.Core.RallyPoint>(Entity))
		{
			var rp = world.Get<Realm.Ecs.Components.Core.RallyPoint>(Entity);
			for (int i = 0; i < rp.Count; i++)
			{
				points.Add(new Vector3(rp.Waypoints[i].X, rp.Waypoints[i].Y, rp.Waypoints[i].Z));
			}
		}
		else
		{
			points.Add(GlobalPosition + new Vector3(0, 0, 8));
		}
	}

	private void EnsureRallyVisualsContainer()
	{
		if (_rallyVisualsContainer == null)
		{
			_rallyVisualsContainer = new Node3D();
			_rallyVisualsContainer.TopLevel = true;
			AddChild(_rallyVisualsContainer);
		}
	}

	private MeshInstance3D GetOrCreateRallyMarker(int index)
	{
		while (_rallyMarkersPool.Count <= index)
		{
			var marker = new MeshInstance3D();
			var cylinderMesh = new CylinderMesh();
			cylinderMesh.TopRadius = 0.25f;
			cylinderMesh.BottomRadius = 0.25f;
			cylinderMesh.Height = 0.05f;
			marker.Mesh = cylinderMesh;

			var mat = new StandardMaterial3D();
			mat.AlbedoColor = new Color(0.95f, 0.82f, 0.55f);
			mat.EmissionEnabled = true;
			mat.Emission = new Color(0.95f, 0.82f, 0.55f);
			mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
			marker.MaterialOverride = mat;

			_rallyVisualsContainer.AddChild(marker);
			_rallyMarkersPool.Add(marker);
		}
		return _rallyMarkersPool[index];
	}

	private MeshInstance3D GetOrCreateRallyLine(int index)
	{
		while (_rallyLinesPool.Count <= index)
		{
			var line = new MeshInstance3D();
			var boxMesh = new BoxMesh();
			boxMesh.Size = new Vector3(0.1f, 0.05f, 1.0f);
			line.Mesh = boxMesh;

			var mat = new StandardMaterial3D();
			mat.AlbedoColor = new Color(0.95f, 0.82f, 0.55f, 0.5f);
			mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
			mat.EmissionEnabled = true;
			mat.Emission = new Color(0.95f, 0.82f, 0.55f);
			mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
			line.MaterialOverride = mat;

			_rallyVisualsContainer.AddChild(line);
			_rallyLinesPool.Add(line);
		}
		return _rallyLinesPool[index];
	}

	public void UpdateRallyVisuals()
	{
		if (!IsBuilding || IsEnemy) return;

		if (GameHost.Instance == null || !GameHost.Instance.EcsWorld.IsAlive(Entity)) return;

		EnsureRallyVisualsContainer();
		if (!GameHost.Instance.CanProduceUnits(this))
		{
			_rallyVisualsContainer.Visible = false;
			return;
		}
		_rallyVisualsContainer.Visible = IsSelected;

		if (!IsSelected) return;

		var points = new System.Collections.Generic.List<Vector3>();
		GetRemainingRallyPoints(points);

		if (points.Count <= 1)
		{
			if (_rallyMarker != null) _rallyMarker.Visible = false;
			return;
		}

		if (_rallyMarker == null)
		{
			_rallyMarker = new MeshInstance3D();
			var cylinderMesh = new CylinderMesh();
			cylinderMesh.TopRadius = 0.1f;
			cylinderMesh.BottomRadius = 0.1f;
			cylinderMesh.Height = 3.0f;
			_rallyMarker.Mesh = cylinderMesh;
			
			var markerMat = new StandardMaterial3D();
			markerMat.AlbedoColor = new Color(0.95f, 0.82f, 0.55f);
			markerMat.EmissionEnabled = true;
			markerMat.Emission = new Color(0.95f, 0.82f, 0.55f);
			_rallyMarker.MaterialOverride = markerMat;
			
			_rallyVisualsContainer.AddChild(_rallyMarker);
		}

		_rallyMarker.Visible = true;
		Vector3 finalPos = points[points.Count - 1];
		if (GameHost.Instance.GroundTerrain != null)
		{
			GameHost.Instance.GroundTerrain.GetHeightAndNormal(finalPos.X, finalPos.Z, out float hFinal, out _);
			finalPos.Y = hFinal + 1.5f;
		}
		_rallyMarker.GlobalPosition = finalPos;

		int markerCountNeeded = points.Count - 2;
		for (int i = 0; i < markerCountNeeded; i++)
		{
			var marker = GetOrCreateRallyMarker(i);
			marker.Visible = true;
			Vector3 pos = points[i + 1];
			if (GameHost.Instance.GroundTerrain != null)
			{
				GameHost.Instance.GroundTerrain.GetHeightAndNormal(pos.X, pos.Z, out float h, out _);
				pos.Y = h + 0.1f;
			}
			marker.GlobalPosition = pos;
		}
		for (int i = Mathf.Max(0, markerCountNeeded); i < _rallyMarkersPool.Count; i++)
		{
			_rallyMarkersPool[i].Visible = false;
		}

		int lineCountNeeded = points.Count - 1;
		for (int i = 0; i < lineCountNeeded; i++)
		{
			var line = GetOrCreateRallyLine(i);
			line.Visible = true;

			Vector3 start = points[i];
			Vector3 end = points[i + 1];

			if (GameHost.Instance.GroundTerrain != null)
			{
				GameHost.Instance.GroundTerrain.GetHeightAndNormal(start.X, start.Z, out float hStart, out _);
				start.Y = hStart + 0.1f;

				GameHost.Instance.GroundTerrain.GetHeightAndNormal(end.X, end.Z, out float hEnd, out _);
				end.Y = hEnd + 0.1f;
			}

			Vector3 diff = end - start;
			float length = diff.Length();

			if (length > 0.05f)
			{
				Vector3 direction = diff.Normalized();
				Vector3 upVector = Mathf.Abs(direction.Dot(Vector3.Up)) > 0.99f ? Vector3.Forward : Vector3.Up;
				var basis = Basis.LookingAt(direction, upVector).Scaled(new Vector3(1f, 1f, length));
				line.GlobalTransform = new Transform3D(basis, start + diff * 0.5f);
			}
			else
			{
				line.Visible = false;
			}
		}
		for (int i = lineCountNeeded; i < _rallyLinesPool.Count; i++)
		{
			_rallyLinesPool[i].Visible = false;
		}
	}

	public override void _Process(double delta)
	{
		if (IsSelected && !IsEnemy)
		{
			if (IsBuilding)
			{
				UpdateRallyVisuals();
			}
			else
			{
				UpdatePathVisuals();
			}
		}
		else
		{
			HidePathVisuals();
			if (_rallyVisualsContainer != null)
			{
				_rallyVisualsContainer.Visible = false;
			}
		}
	}

	private void EnsurePathVisualsContainer()
	{
		if (_pathVisualsContainer == null)
		{
			_pathVisualsContainer = new Node3D();
			_pathVisualsContainer.TopLevel = true;
			AddChild(_pathVisualsContainer);
		}
	}

	public bool IsAttackPath { get; set; } = false;

	private void GetRemainingPathPoints(System.Collections.Generic.List<Vector3> points)
	{
		points.Clear();
		IsAttackPath = false;
		if (GameHost.Instance == null || !GameHost.Instance.EcsWorld.IsAlive(Entity)) return;

		var world = GameHost.Instance.EcsWorld;
		if (world.Has<Realm.Ecs.Components.Combat.AttackTarget>(Entity) || world.Has<Realm.Ecs.Components.Movement.AttackMove>(Entity))
		{
			IsAttackPath = true;
		}

		points.Add(GlobalPosition);

		if (world.Has<Realm.Ecs.Services.PathFollow>(Entity))
		{
			var pf = world.Get<Realm.Ecs.Services.PathFollow>(Entity);
			for (int i = pf.CurrentWaypointIndex; i < pf.WaypointCount; i++)
			{
				points.Add(new Vector3(pf.Waypoints[i].X, pf.Waypoints[i].Y, pf.Waypoints[i].Z));
			}
		}
		else if (world.Has<Realm.Ecs.Components.Movement.MoveTo>(Entity))
		{
			var mt = world.Get<Realm.Ecs.Components.Movement.MoveTo>(Entity);
			points.Add(new Vector3(mt.Target.X, mt.Target.Y, mt.Target.Z));
		}

		if (world.Has<Realm.Ecs.Components.Movement.WaypointQueue>(Entity))
		{
			var q = world.Get<Realm.Ecs.Components.Movement.WaypointQueue>(Entity);
			for (int i = 0; i < q.Count; i++)
			{
				points.Add(new Vector3(q.Waypoints[i].X, q.Waypoints[i].Y, q.Waypoints[i].Z));
			}
		}
	}

	private void UpdatePathVisuals()
	{
		EnsurePathVisualsContainer();
		_pathVisualsContainer.Visible = true;

		var points = new System.Collections.Generic.List<Vector3>();
		GetRemainingPathPoints(points);

		if (points.Count <= 1)
		{
			HidePathVisuals();
			return;
		}

		int markerCountNeeded = points.Count - 1;
		for (int i = 0; i < markerCountNeeded; i++)
		{
			var marker = GetOrCreateMarker(i);
			marker.Visible = true;
			Vector3 pos = points[i + 1];
			if (GameHost.Instance.GroundTerrain != null)
			{
				GameHost.Instance.GroundTerrain.GetHeightAndNormal(pos.X, pos.Z, out float h, out _);
				pos.Y = h + 0.1f;
			}
			marker.GlobalPosition = pos;
		}
		for (int i = markerCountNeeded; i < _pathMarkersPool.Count; i++)
		{
			_pathMarkersPool[i].Visible = false;
		}

		int lineCountNeeded = points.Count - 1;
		for (int i = 0; i < lineCountNeeded; i++)
		{
			var line = GetOrCreateLine(i);
			line.Visible = true;

			Vector3 start = points[i];
			Vector3 end = points[i + 1];

			if (GameHost.Instance.GroundTerrain != null)
			{
				GameHost.Instance.GroundTerrain.GetHeightAndNormal(start.X, start.Z, out float hStart, out _);
				start.Y = hStart + 0.1f;

				GameHost.Instance.GroundTerrain.GetHeightAndNormal(end.X, end.Z, out float hEnd, out _);
				end.Y = hEnd + 0.1f;
			}

			Vector3 diff = end - start;
			float length = diff.Length();

			if (length > 0.05f)
			{
				Vector3 direction = diff.Normalized();
				Vector3 upVector = Mathf.Abs(direction.Dot(Vector3.Up)) > 0.99f ? Vector3.Forward : Vector3.Up;
				var basis = Basis.LookingAt(direction, upVector).Scaled(new Vector3(1f, 1f, length));
				line.GlobalTransform = new Transform3D(basis, start + diff * 0.5f);
			}
			else
			{
				line.Visible = false;
			}
		}
		for (int i = lineCountNeeded; i < _pathLinesPool.Count; i++)
		{
			_pathLinesPool[i].Visible = false;
		}
	}

	private void HidePathVisuals()
	{
		if (_pathVisualsContainer != null)
		{
			_pathVisualsContainer.Visible = false;
		}
	}

	private MeshInstance3D GetOrCreateMarker(int index)
	{
		while (_pathMarkersPool.Count <= index)
		{
			var marker = new MeshInstance3D();
			var cylinderMesh = new CylinderMesh();
			cylinderMesh.TopRadius = 0.25f;
			cylinderMesh.BottomRadius = 0.25f;
			cylinderMesh.Height = 0.05f;
			marker.Mesh = cylinderMesh;

			var mat = new StandardMaterial3D();
			mat.AlbedoColor = new Color(0.2f, 0.6f, 1.0f);
			mat.EmissionEnabled = true;
			mat.Emission = new Color(0.2f, 0.6f, 1.0f);
			mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
			marker.MaterialOverride = mat;

			_pathVisualsContainer.AddChild(marker);
			_pathMarkersPool.Add(marker);
		}
		var m = _pathMarkersPool[index];
		if (m.MaterialOverride is StandardMaterial3D markerMat)
		{
			Color c = IsAttackPath ? new Color(0.9f, 0.1f, 0.1f) : new Color(0.2f, 0.6f, 1.0f);
			markerMat.AlbedoColor = c;
			markerMat.Emission = c;
		}
		return m;
	}

	private MeshInstance3D GetOrCreateLine(int index)
	{
		while (_pathLinesPool.Count <= index)
		{
			var line = new MeshInstance3D();
			var boxMesh = new BoxMesh();
			boxMesh.Size = new Vector3(0.1f, 0.05f, 1.0f);
			line.Mesh = boxMesh;

			var mat = new StandardMaterial3D();
			mat.AlbedoColor = new Color(0.2f, 0.6f, 1.0f, 0.5f);
			mat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
			mat.EmissionEnabled = true;
			mat.Emission = new Color(0.2f, 0.6f, 1.0f);
			mat.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
			line.MaterialOverride = mat;

			_pathVisualsContainer.AddChild(line);
			_pathLinesPool.Add(line);
		}
		var l = _pathLinesPool[index];
		if (l.MaterialOverride is StandardMaterial3D lineMat)
		{
			Color c = IsAttackPath ? new Color(0.9f, 0.1f, 0.1f, 0.5f) : new Color(0.2f, 0.6f, 1.0f, 0.5f);
			Color emissionColor = IsAttackPath ? new Color(0.9f, 0.1f, 0.1f) : new Color(0.2f, 0.6f, 1.0f);
			lineMat.AlbedoColor = c;
			lineMat.Emission = emissionColor;
		}
		return l;
	}

	public Color Modulate
	{
		get => new Color(1f, 1f, 1f, _modulateAlpha);
		set
		{
			_modulateAlpha = value.A;
			if (_modelNode != null)
				SetAlphaRecursive(_modelNode, value.A);
		}
	}

	private float _modulateAlpha = 1f;

	public void SetConstructionAlpha(float alpha)
	{
		if (_modelNode != null)
		{
			SetAlphaRecursive(_modelNode, alpha);
		}
	}

	private void SetAlphaRecursive(Node node, float alpha)
	{
		if (node is MeshInstance3D mesh)
		{
			Material mat = mesh.MaterialOverride;
			if (mat is StandardMaterial3D stdMat)
			{
				var dup = (StandardMaterial3D)stdMat.Duplicate();
				dup.Transparency = alpha < 1f ? BaseMaterial3D.TransparencyEnum.Alpha : BaseMaterial3D.TransparencyEnum.Disabled;
				var c = dup.AlbedoColor;
				c.A = alpha;
				dup.AlbedoColor = c;
				mesh.MaterialOverride = dup;
			}
		}
		foreach (var child in node.GetChildren())
		{
			SetAlphaRecursive(child, alpha);
		}
	}
}

public partial class Building3D : Unit3D
{
}