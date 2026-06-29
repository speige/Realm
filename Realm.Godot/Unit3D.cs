using Godot;
using Arch.Core;

public partial class Unit3D : CharacterBody3D
{
	public Entity Entity { get; set; }
	public string UnitId { get; set; } // e.g. "soldier", "archer", "castle", "tower"
	public bool IsBuilding { get; set; }
	
	private Node3D _modelNode;
	private MeshInstance3D _selectionRing;
	private bool _isSelected = false;
	
	public bool IsEnemy { get; set; } = false;

	private MeshInstance3D _rallyMarker;
	private MeshInstance3D _rallyLine;

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
		if (IsBuilding)
		{
			torusMesh.InnerRadius = 4.2f;
			torusMesh.OuterRadius = 4.5f;
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
		AddChild(_hoverRing);
	}

	public bool IsSelected
	{
		get => _isSelected;
		set
		{
			_isSelected = value;
			if (_selectionRing != null)
			{
				_selectionRing.Visible = _isSelected;
				if (_selectionRing.MaterialOverride is StandardMaterial3D material)
				{
					Color color = IsEnemy ? new Color(0.9f, 0.1f, 0.2f) : new Color(0.1f, 0.9f, 0.2f);
					material.AlbedoColor = color;
					material.Emission = color;
				}
			}

			if (_hoverRing != null)
			{
				_hoverRing.Visible = _isHovered && !_isSelected;
			}

			if (IsBuilding && !IsEnemy)
			{
				UpdateRallyVisuals();
			}
		}
	}

	public bool IsPreview { get; set; } = false;

	public override void _Ready()
	{
		if (IsPreview) return;
		// Set up collision shape if not already done
		var collisionShape = new CollisionShape3D();
		if (IsBuilding)
		{
			var boxShape = new BoxShape3D();
			boxShape.Size = new Vector3(6f, 6f, 6f);
			collisionShape.Shape = boxShape;
			collisionShape.Position = new Vector3(0, 3f, 0);
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

		// Create selection ring (visible only when selected)
		CreateSelectionRing();
	}

	public void LoadModel(string modelPath)
	{
		if (string.IsNullOrEmpty(modelPath)) return;

		try
		{
			var packedScene = GD.Load<PackedScene>(modelPath);
			if (packedScene != null)
			{
				_modelNode = packedScene.Instantiate<Node3D>();
				AddChild(_modelNode);
				
				// Apply scaling first
				if (IsBuilding)
				{
					_modelNode.Scale = new Vector3(1.2f, 1.2f, 1.2f);
				}
				else
				{
					float wScale = (UnitId == "worker") ? 0.9f : 1.5f;
					_modelNode.Scale = new Vector3(wScale, wScale, wScale);
				}

				// Dynamically compute the bottom of the mesh to align it perfectly with the floor Y=0
				float minY = GetMinY(_modelNode, Transform3D.Identity);
				
				// Set model position so its feet/base sit exactly at Y = 0
				_modelNode.Position = new Vector3(0f, -minY * _modelNode.Scale.Y, 0f);

				if (UnitId == "priest")
				{
					Color priestColor = IsEnemy ? new Color(0.8f, 0.2f, 0.8f) : new Color(1.0f, 0.85f, 0.2f);
					ApplyModelTint(priestColor);
				}
				else if (UnitId == "worker")
				{
					Color workerColor = IsEnemy ? new Color(0.6f, 0.4f, 0.2f) : new Color(0.8f, 0.6f, 0.4f);
					ApplyModelTint(workerColor);
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
	}

	public void ApplyModelTint(Color color)
	{
		if (_modelNode == null) return;
		ApplyModelTintRecursive(_modelNode, color);
	}

	private void ApplyModelTintRecursive(Node node, Color color)
	{
		if (node is MeshInstance3D meshInstance)
		{
			Material mat = meshInstance.MaterialOverride;
			if (mat == null && meshInstance.GetActiveMaterial(0) != null)
			{
				mat = meshInstance.GetActiveMaterial(0);
			}

			if (mat is StandardMaterial3D stdMat)
			{
				var dupMat = (StandardMaterial3D)stdMat.Duplicate();
				dupMat.AlbedoColor = color;
				meshInstance.MaterialOverride = dupMat;
			}
		}
		foreach (var child in node.GetChildren())
		{
			if (child is Node childNode)
			{
				ApplyModelTintRecursive(childNode, color);
			}
		}
	}


	private void CreateFallbackMesh()
	{
		// Fallback primitive mesh if model loading fails
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

	private void CreateSelectionRing()
	{
		_selectionRing = new MeshInstance3D();
		var torusMesh = new TorusMesh();
		
		if (IsBuilding)
		{
			torusMesh.InnerRadius = 4.2f;
			torusMesh.OuterRadius = 4.5f;
		}
		else
		{
			torusMesh.InnerRadius = 1.2f;
			torusMesh.OuterRadius = 1.4f;
		}
		
		_selectionRing.Mesh = torusMesh;
		_selectionRing.Position = new Vector3(0, 0.05f, 0);

		var material = new StandardMaterial3D();
		material.AlbedoColor = new Color(0.1f, 0.9f, 0.2f); // Neon green
		material.EmissionEnabled = true;
		material.Emission = new Color(0.1f, 0.9f, 0.2f);
		material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
		
		_selectionRing.MaterialOverride = material;
		_selectionRing.Visible = false;
		AddChild(_selectionRing);
	}

	private float GetMinY(Node node, Transform3D currentTransform)
	{
		float minY = 0f;
		bool foundMesh = false;
		GetMinYRecursive(node, currentTransform, ref minY, ref foundMesh);
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
				// Test the 8 corners of the local bounding box to find the absolute lowest Y coordinate in model space
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

	private void CreateRallyVisuals()
	{
		if (_rallyMarker != null) return;

		// 1. Rally Marker (a small gold flagpole/cylinder)
		_rallyMarker = new MeshInstance3D();
		var cylinderMesh = new CylinderMesh();
		cylinderMesh.TopRadius = 0.1f;
		cylinderMesh.BottomRadius = 0.1f;
		cylinderMesh.Height = 3.0f;
		_rallyMarker.Mesh = cylinderMesh;
		
		var markerMat = new StandardMaterial3D();
		markerMat.AlbedoColor = new Color(0.95f, 0.82f, 0.55f); // Gold
		markerMat.EmissionEnabled = true;
		markerMat.Emission = new Color(0.95f, 0.82f, 0.55f);
		_rallyMarker.MaterialOverride = markerMat;
		
		AddChild(_rallyMarker);
		_rallyMarker.Visible = false;

		// 2. Rally Line (a thin horizontal box mesh stretching between building and marker)
		_rallyLine = new MeshInstance3D();
		var boxMesh = new BoxMesh();
		boxMesh.Size = new Vector3(0.15f, 0.15f, 1.0f); // length will be scaled dynamically
		_rallyLine.Mesh = boxMesh;

		var lineMat = new StandardMaterial3D();
		lineMat.AlbedoColor = new Color(0.95f, 0.82f, 0.55f, 0.5f); // Transparent gold
		lineMat.Transparency = BaseMaterial3D.TransparencyEnum.Alpha;
		lineMat.EmissionEnabled = true;
		lineMat.Emission = new Color(0.95f, 0.82f, 0.55f);
		_rallyLine.MaterialOverride = lineMat;
		
		AddChild(_rallyLine);
		_rallyLine.Visible = false;
	}

	public void UpdateRallyVisuals()
	{
		if (!IsBuilding || IsEnemy) return;

		if (GameHost.Instance == null || !GameHost.Instance.EcsWorld.IsAlive(Entity)) return;

		var world = GameHost.Instance.EcsWorld;
		Vector3 localRally;
		if (world.Has<Realm.Ecs.Components.Core.RallyPoint>(Entity))
		{
			var rp = world.Get<Realm.Ecs.Components.Core.RallyPoint>(Entity);
			localRally = ToLocal(new Vector3(rp.Value.X, rp.Value.Y, rp.Value.Z));
		}
		else
		{
			// Default rally point is 8 meters in front of the building
			localRally = new Vector3(0, 0, 8);
		}

		if (_rallyMarker == null)
		{
			CreateRallyVisuals();
		}

		if (_rallyMarker != null && _rallyLine != null)
		{
			_rallyMarker.Visible = IsSelected;
			_rallyLine.Visible = IsSelected;

			if (IsSelected)
			{
				_rallyMarker.Position = new Vector3(localRally.X, 1.5f, localRally.Z);

				Vector3 start = new Vector3(0, 0.1f, 0);
				Vector3 end = new Vector3(localRally.X, 0.1f, localRally.Z);
				Vector3 diff = end - start;
				float length = diff.Length();

				if (length > 0.1f)
				{
					_rallyLine.Visible = true;
					_rallyLine.Position = start + diff * 0.5f; // Midpoint
					_rallyLine.Scale = new Vector3(1, 1, length);
					
					var basis = Basis.LookingAt(diff.Normalized(), Vector3.Up);
					_rallyLine.Transform = new Transform3D(basis, _rallyLine.Position);
				}
				else
				{
					_rallyLine.Visible = false;
				}
			}
		}
	}
}
