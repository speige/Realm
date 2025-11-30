using MemoryPack;

namespace Realm.Godot.ReplaySystem;

[MemoryPackable]
public partial struct ReplayPlayerResourceSnapshot
{
	public float Gold { get; set; }
	public float Wood { get; set; }
	public float Stone { get; set; }
}