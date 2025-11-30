using System.Collections.Generic;
using System.Numerics;

namespace Realm.Ecs.Components.Movement;

/// <summary>
///     Stores a queue of pending waypoints for sequential pathing.
/// </summary>
public struct WaypointQueue
{
	public List<Vector3> Waypoints;

	public WaypointQueue(List<Vector3> waypoints)
	{
		Waypoints = waypoints ?? new List<Vector3>();
	}
}
