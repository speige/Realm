using System;
using System.Collections.Generic;
using MemoryPack;

namespace Realm.Godot.ReplaySystem
{
	[MemoryPackable]
	public partial class ReplayUnitSnapshot
	{
		public int EntityId { get; set; }
		public string UnitId { get; set; }
		public int OwnerPlayerEntityId { get; set; }
		public NetworkVector3 Position { get; set; }
		public float RotationY { get; set; }
		public float CurrentHp { get; set; }
		public float MaxHp { get; set; }
		public bool IsDead { get; set; }
		public bool IsBuilding { get; set; }
		public NetworkVector3 Velocity { get; set; }
	}

	[MemoryPackable]
	public partial struct ReplayPlayerResourceSnapshot
	{
		public float Gold { get; set; }
		public float Wood { get; set; }
		public float Stone { get; set; }
	}

	[MemoryPackable]
	public partial class ReplayFrame
	{
		public int Tick { get; set; }
		public bool IsKeyframe { get; set; }
		public List<ReplayUnitSnapshot> Units { get; set; } = new();
		public ReplayPlayerResourceSnapshot Resources { get; set; }
	}

	[MemoryPackable]
	public partial class ReplayPlayerInfo
	{
		public int PeerId { get; set; }
		public string Name { get; set; }
		public string Faction { get; set; }
		public float ColorR { get; set; }
		public float ColorG { get; set; }
		public float ColorB { get; set; }
	}

	[MemoryPackable]
	public partial class ReplayHeader
	{
		public string Magic { get; set; }
		public int Version { get; set; }
		public string MapName { get; set; }
		public int TotalTicks { get; set; }
		public long Timestamp { get; set; }
		public List<ReplayPlayerInfo> Players { get; set; } = new();
	}
}
