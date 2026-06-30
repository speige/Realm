using Godot;
using System;

public partial class Prop3D : StaticBody3D
{
	[Export] public string PropId { get; set; } = "tree"; // "tree", "rock", "goldmine", "pillar", "flag"
	[Export] public float ResourceAmount { get; set; } = 500f;

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

	private void CreateSelectionRing()
	{
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
		else
		{
			torusMesh.InnerRadius = 1.2f;
			torusMesh.OuterRadius = 1.4f;
		}
		
		_selectionRing.Mesh = torusMesh;
		_selectionRing.Position = new Vector3(0, 0.05f, 0);

		var material = new StandardMaterial3D();
		material.AlbedoColor = new Color(0.95f, 0.82f, 0.15f); // Neon Gold/Yellow
		material.EmissionEnabled = true;
		material.Emission = new Color(0.95f, 0.82f, 0.15f);
		material.ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded;
		
		_selectionRing.MaterialOverride = material;
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

		if (PropId == "goldmine") ResourceAmount = 2000f;
		else if (PropId == "rock") ResourceAmount = 1000f;
		else if (PropId == "tree") ResourceAmount = 500f;

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
		else
		{
			boxShape.Size = new Vector3(1.5f, 4.5f, 1.5f);
			collisionShape.Position = new Vector3(0, 2.25f, 0);
		}
		collisionShape.Shape = boxShape;

		CreatePropVisual();
	}

	private void CreatePropVisual()
	{
		var visual = new Node3D();
		visual.Name = "VisualModel";
		AddChild(visual);
		
		if (PropId.EndsWith(".glb") || PropId.Contains("res://") || PropId.Contains("/"))
		{
			string path = PropId;
			if (!path.StartsWith("res://"))
			{
				if (path.Contains("Environment"))
				{
					path = $"res://Assets/3d/Environment/{path}";
				}
				else
				{
					path = $"res://Assets/3d/Props/{path}";
				}
			}
			try
			{
				var scene = GD.Load<PackedScene>(path);
				if (scene != null)
				{
					var node = scene.Instantiate();
					visual.AddChild(node);
				}
				else
				{
					var meshInstance = new MeshInstance3D();
					var boxMesh = new BoxMesh();
					boxMesh.Size = new Vector3(1.5f, 2.0f, 1.5f);
					meshInstance.Mesh = boxMesh;
					var mat = new StandardMaterial3D();
					mat.AlbedoColor = new Color(0.8f, 0.4f, 0.1f);
					meshInstance.MaterialOverride = mat;
					meshInstance.Position = new Vector3(0, 1.0f, 0);
					visual.AddChild(meshInstance);
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"Failed to load dynamic prop visual: {ex.Message}");
			}
			return;
		}

		switch (PropId)
		{
			case "tree":

				var trunk = new MeshInstance3D();
				var trunkMesh = new CylinderMesh();
				trunkMesh.TopRadius = 0.25f;
				trunkMesh.BottomRadius = 0.35f;
				trunkMesh.Height = 1.8f;
				trunk.Mesh = trunkMesh;
				var trunkMat = new StandardMaterial3D();
				trunkMat.AlbedoColor = new Color(0.35f, 0.22f, 0.12f); // Brown
				trunkMat.Roughness = 0.9f;
				trunk.MaterialOverride = trunkMat;
				trunk.Position = new Vector3(0, 0.9f, 0);
				visual.AddChild(trunk);
				

				var leaves = new MeshInstance3D();
				var leavesMesh = new CylinderMesh();
				leavesMesh.TopRadius = 0.0f;
				leavesMesh.BottomRadius = 1.3f;
				leavesMesh.Height = 3.2f;
				leaves.Mesh = leavesMesh;
				var leavesMat = new StandardMaterial3D();
				leavesMat.AlbedoColor = new Color(0.12f, 0.42f, 0.16f); // Rich green
				leavesMat.Roughness = 0.85f;
				leaves.MaterialOverride = leavesMat;
				leaves.Position = new Vector3(0, 3.2f, 0);
				visual.AddChild(leaves);
				break;
				
			case "rock":
				var rock = new MeshInstance3D();
				var rockMesh = new SphereMesh();
				rockMesh.Radius = 1.2f;
				rockMesh.Height = 1.8f;
				rock.Mesh = rockMesh;
				var rockMat = new StandardMaterial3D();
				rockMat.AlbedoColor = new Color(0.42f, 0.42f, 0.45f); // Granite gray
				rockMat.Roughness = 0.85f;
				rock.MaterialOverride = rockMat;
				rock.Position = new Vector3(0, 0.9f, 0);
				
				var rand = new Random((int)(Position.X * 1000 + Position.Z));
				rock.RotationDegrees = new Vector3(
					(float)rand.NextDouble() * 360f,
					(float)rand.NextDouble() * 360f,
					(float)rand.NextDouble() * 360f
				);
				rock.Scale = new Vector3(
					0.85f + (float)rand.NextDouble() * 0.3f,
					0.85f + (float)rand.NextDouble() * 0.3f,
					0.85f + (float)rand.NextDouble() * 0.3f
				);
				visual.AddChild(rock);
				break;
				
			case "goldmine":

				var goldBase = new MeshInstance3D();
				var goldBaseMesh = new BoxMesh();
				goldBaseMesh.Size = new Vector3(3.8f, 1.8f, 3.8f);
				goldBase.Mesh = goldBaseMesh;
				var goldBaseMat = new StandardMaterial3D();
				goldBaseMat.AlbedoColor = new Color(0.28f, 0.28f, 0.3f); // Dark stone base
				goldBase.MaterialOverride = goldBaseMat;
				goldBase.Position = new Vector3(0, 0.9f, 0);
				visual.AddChild(goldBase);
				

				for (int i = 0; i < 6; i++)
				{
					var nugget = new MeshInstance3D();
					var nuggetMesh = new SphereMesh();
					nuggetMesh.Radius = 0.55f;
					nuggetMesh.Height = 0.75f;
					nugget.Mesh = nuggetMesh;
					var nuggetMat = new StandardMaterial3D();
					nuggetMat.AlbedoColor = new Color(0.92f, 0.76f, 0.15f); // Gold
					nuggetMat.Metallic = 0.9f;
					nuggetMat.Roughness = 0.25f;
					nugget.MaterialOverride = nuggetMat;
					
					var r = new Random(i + (int)(Position.X * 100));
					nugget.Position = new Vector3(
						(float)(r.NextDouble() - 0.5) * 3.0f,
						1.3f + (float)r.NextDouble() * 0.6f,
						(float)(r.NextDouble() - 0.5) * 3.0f
					);
					nugget.Scale = new Vector3(0.8f + (float)r.NextDouble() * 0.4f, 0.8f + (float)r.NextDouble() * 0.4f, 0.8f + (float)r.NextDouble() * 0.4f);
					visual.AddChild(nugget);
				}
				break;
				
			case "pillar":
				var pillar = new MeshInstance3D();
				var pillarMesh = new CylinderMesh();
				pillarMesh.TopRadius = 0.5f;
				pillarMesh.BottomRadius = 0.55f;
				pillarMesh.Height = 4.8f;
				pillar.Mesh = pillarMesh;
				var pillarMat = new StandardMaterial3D();
				pillarMat.AlbedoColor = new Color(0.72f, 0.68f, 0.62f); // Old marble
				pillarMat.Roughness = 0.7f;
				pillar.MaterialOverride = pillarMat;
				pillar.Position = new Vector3(0, 2.4f, 0);
				visual.AddChild(pillar);
				break;
				
			case "flag":
				var pole = new MeshInstance3D();
				var poleMesh = new CylinderMesh();
				poleMesh.TopRadius = 0.06f;
				poleMesh.BottomRadius = 0.1f;
				poleMesh.Height = 5.8f;
				pole.Mesh = poleMesh;
				var poleMat = new StandardMaterial3D();
				poleMat.AlbedoColor = new Color(0.25f, 0.25f, 0.25f); // Steel
				poleMat.Metallic = 0.8f;
				poleMat.Roughness = 0.3f;
				pole.MaterialOverride = poleMat;
				pole.Position = new Vector3(0, 2.9f, 0);
				visual.AddChild(pole);
				
				var flagCloth = new MeshInstance3D();
				var clothMesh = new BoxMesh();
				clothMesh.Size = new Vector3(1.8f, 1.1f, 0.08f);
				flagCloth.Mesh = clothMesh;
				var clothMat = new StandardMaterial3D();
				clothMat.AlbedoColor = new Color(0.82f, 0.12f, 0.15f); // Red
				clothMat.Roughness = 0.9f;
				flagCloth.MaterialOverride = clothMat;
				flagCloth.Position = new Vector3(0.9f, 4.8f, 0);
				visual.AddChild(flagCloth);
				break;
		}
	}
}
