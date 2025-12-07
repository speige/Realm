using Godot;
using System;
using System.Collections.Generic;

public partial class BatchRenderBenchmark : Node3D
{
	private const int InstancesPerType = 5;

	[Export] public string[] UnitGlbPaths = {
		"res://Assets/barbarian_warlord.glb",
		"res://Assets/blue_haired_tigress_rider.glb",
		"res://Assets/blue_goblin_warrior.glb",
		"res://Assets/blue_dragon_knight.glb",
		"res://Assets/blue_dragon_king.glb",
		"res://Assets/blue_armor_panda.glb",
		"res://Assets/blue_armor_knight.glb",
		"res://Assets/bear_warrior.glb",
		"res://Assets/battlebot.glb",
        "res://Assets/barbarian_warrior.glb"
	};

	private Dictionary<string, MultiMeshInstance3D> _renderers = new();
	private Dictionary<string, List<UnitData>> _unitDataByType = new();
	private Transform3D[] _transformBuffer = new Transform3D[InstancesPerType];
	private float _time = 0f;

	public override void _Ready()
	{
		var rand = new RandomNumberGenerator();
		rand.Randomize();

		for (int typeIdx = 0; typeIdx < UnitGlbPaths.Length; typeIdx++)
		{
			string path = UnitGlbPaths[typeIdx];
			string typeName = $"UnitType_{typeIdx}";

			// --- Load and extract Mesh + Material at runtime ---
			var scene = GD.Load<PackedScene>(path);
			if (scene == null)
			{
				GD.PrintErr($"Failed to load GLB: {path}");
				continue;
			}

			Node3D root = scene.Instantiate<Node3D>();
			if (root.FindChild("*", true, false) is not MeshInstance3D mi)
			{
				GD.PrintErr($"No MeshInstance3D found in {path}");
				root.QueueFree();
				continue;
			}

			Mesh mesh = mi.Mesh;
			Material material = mi.MaterialOverride ?? mi.GetActiveMaterial(0);

			root.QueueFree();

			if (mesh == null || material == null) continue;

			// Duplicate to avoid shared resources
			Mesh uniqueMesh = (Mesh)mesh.Duplicate();
			
			Material uniqueMaterial = (Material)material.Duplicate();

			// Optional: tint per type for visual debug
			if (uniqueMaterial is StandardMaterial3D sm)
				sm.AlbedoColor = Color.FromHsv(typeIdx / (float)UnitGlbPaths.Length, 0.8f, 1.0f);

			// --- Create MultiMeshInstance3D ---
			var multiMeshInstance = new MultiMeshInstance3D();
			var multiMesh = new MultiMesh();
			multiMesh.Mesh = uniqueMesh;
			multiMesh.TransformFormat = MultiMesh.TransformFormatEnum.Transform3D;
			multiMesh.InstanceCount = InstancesPerType;
			multiMeshInstance.Multimesh = multiMesh;
			multiMeshInstance.MaterialOverride = uniqueMaterial;
			multiMeshInstance.Name = typeName;

			AddChild(multiMeshInstance);
			_renderers[typeName] = multiMeshInstance;

			// --- Setup per-instance data ---
			var unitList = new List<UnitData>();
			_unitDataByType[typeName] = unitList;

			for (int i = 0; i < InstancesPerType; i++)
			{
				unitList.Add(new UnitData
				{
					InitialPosition = new Vector3(
						rand.RandfRange(-10f, 10f),
						0.5f,
						rand.RandfRange(-10f, 10f)
					),
					MovementOffset = rand.RandfRange(0f, 100f),
					SpeedFactor = rand.RandfRange(1.0f, 3.0f)
				});
			}
		}

		GD.Print($"Initialized {UnitGlbPaths.Length * InstancesPerType} units using MultiMeshInstance3D");
	}

bool ran = false;
public override void _Process(double delta)
{
	if (ran) {
		return;
	}
	ran = true;
	
	_time += (float)delta;

	foreach (var kvp in _renderers)
	{
		MultiMeshInstance3D mmi = kvp.Value;
		MultiMesh mm = mmi.Multimesh;
		List<UnitData> units = _unitDataByType[kvp.Key];

		// Replace the array buffering logic with SetInstanceTransform
		for (int i = 0; i < units.Count; i++)
		{
			var u = units[i];
			float t = _time * u.SpeedFactor + u.MovementOffset;

			// Calculate new position
			float x = u.InitialPosition.X + Mathf.Sin(t * 1.5f) * 5f;
			float z = u.InitialPosition.Z + Mathf.Cos(t * 1.5f) * 5f;
			float y = u.InitialPosition.Y + Mathf.Sin(t * 3.0f) * 0.5f;

			// Create the Transform3D
			// Using Basis.Identity ensures no rotation and a uniform scale of (1, 1, 1).
			Transform3D newTransform = new Transform3D(Basis.Identity, new Vector3(x, y, z));
			
			// Set the transform for this instance index
			mm.SetInstanceTransform(i, newTransform);
		}
		
		// The MultiMesh.Buffer update is no longer needed
		// Span<Transform3D> transformSpan = _transformBuffer.AsSpan(0, units.Count);
		// Span<float> floatSpan = System.Runtime.InteropServices.MemoryMarshal.Cast<Transform3D, float>(transformSpan);
		// mm.Buffer = floatSpan.ToArray();
	}
}

	public struct UnitData
	{
		public Vector3 InitialPosition;
		public float MovementOffset;
		public float SpeedFactor;
	}
}
