using System.Numerics;

namespace Realm.Ecs.Services;

[System.Runtime.CompilerServices.InlineArray(256)]
internal struct WaypointBuffer
{
	private Vector3 _element0;
	public const int Length = 256;
}

/// <summary>
/// Caches the pathfinding waypoints for navigation to avoid GC allocations in the tick query loop.
/// </summary>
internal struct PathFollow
{
	public WaypointBuffer Waypoints;
	public int WaypointCount;
	public int CurrentWaypointIndex;
	public Vector3 Target;
}
