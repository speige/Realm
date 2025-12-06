using Godot;
using System.Collections.Generic;

public partial class InstanceRenderBenchmark : Node3D
{
	// NOTE: Removed [Export] PackedScene UnitTemplate

	// Array of 10 GLB paths copied from BatchRenderBenchmark.cs
	public string[] UnitGlbPaths = {
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

	private List<UnitData> _unitDataList = new List<UnitData>();
	
	private const int UnitCount = 30;
	private int InstancesPerType;
	private float _time = 0.0f; // Global timer

	public override void _Ready()
	{
		if (UnitGlbPaths.Length == 0) return;
		
		// Ensure the count matches the BatchRender setup
		InstancesPerType = UnitCount / UnitGlbPaths.Length;

		GD.Print($"Instantiating {UnitCount} units of {UnitGlbPaths.Length} types ({InstancesPerType} each)...");
		var rand = new RandomNumberGenerator();
		rand.Randomize();

		// 1. New logic to load 10 GLBs and instantiate InstancesPerType of each
		for (int i = 0; i < UnitGlbPaths.Length; i++)
		{
			string path = UnitGlbPaths[i];
			
			// Load the GLB once
			PackedScene unitScene = GD.Load<PackedScene>(path);
			if (unitScene == null)
			{
				GD.PrintErr($"Failed to load GLB: {path}");
				continue;
			}

			for (int j = 0; j < InstancesPerType; j++)
			{
				// Instantiate the scene
				Node3D newUnit = (Node3D)unitScene.Instantiate();
				
				// Add the new node to the scene tree
				AddChild(newUnit); 
				
				// Initialize the unit's position
				Vector3 initialPos = new Vector3(
					rand.RandfRange(-10f, 10f), 
					0.5f, // Flat plane height
					rand.RandfRange(-10f, 10f)
				);
				
				// Set the initial position and store it in UnitData
				newUnit.Position = initialPos;

				_unitDataList.Add(new UnitData
				{
					Node = newUnit,
					InitialPosition = initialPos, // Store for movement calculation
					MovementOffset = rand.RandfRange(0.0f, 100.0f),
					SpeedFactor = rand.RandfRange(1.0f, 3.0f)
				});
			}
		}
		
		GD.Print("Unit instantiation complete. Starting benchmark.");
	}

	public override void _Process(double delta)
	{
		_time += (float)delta;
		
		// 2. Move all units independently using flat-plane logic
		foreach (var unitData in _unitDataList)
		{
			// Use the global timer, offset, and speed factor for unique, independent movement
			float t = _time * unitData.SpeedFactor + unitData.MovementOffset;

			// Flat-plane gliding movement logic (relative to InitialPosition)
			float x = unitData.InitialPosition.X + Mathf.Sin(t * 1.5f) * 5.0f;
			float z = unitData.InitialPosition.Z + Mathf.Cos(t * 1.5f) * 5.0f;
			float y = unitData.InitialPosition.Y + Mathf.Sin(t * 3.0f) * 0.5f;

			// This is the core Godot API call being benchmarked per unit, per frame.
			unitData.Node.Position = new Vector3(x, y, z);
		}
	}
	
	// Updated struct to store the necessary InitialPosition
	public struct UnitData
	{
		public Godot.Node3D Node;
		public Godot.Vector3 InitialPosition; 
		public float MovementOffset; 
		public float SpeedFactor;
	}	
}
