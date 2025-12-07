using Godot;
using System.Runtime.InteropServices;
using System;
using System.Collections.Generic;

public partial class oldGameHost : Node3D
{
	// private SimulationHost _simulationHost; 

	// [System.Runtime.InteropServices.DllImport("YourNativeLibrary")]
	// private static extern void ExchangeData(System.IntPtr data, int size);

	public override void _Ready()
	{
		//_simulationHost = new SimulationHost();
		//_simulationHost.InitializeMap("ChaosArena");
	}

	public override void _PhysicsProcess(double delta)
	{
		// 30 HZ

		// var snapshotBuffer = _simulationHost.PrepareSnapshot(delta);

		//_simulationHost.Update((float)delta); 

		// ExchangeData(snapshotBuffer.Pointer, snapshotBuffer.Size);

		// _simulationHost.ApplyGodotUpdates(this);
	}

}
