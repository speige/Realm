using System.Numerics;

namespace Realm.Ecs.Components.Movement;

[System.Runtime.CompilerServices.InlineArray(16)]
public struct WaypointBuffer
{
	public Vector3 Element0;
	public const int Length = 16;
}

/// <summary>
/// Stores a queue of pending waypoints for sequential pathing.
/// </summary>
public struct WaypointQueue
{
	public WaypointBuffer Waypoints;
	public int Count;

	public WaypointQueue(Vector3 target)
	{
		Count = 1;
		Waypoints[0] = target;
	}

	public void Add(Vector3 wp)
	{
		if (Count < 16)
		{
			Waypoints[Count] = wp;
			Count++;
		}
	}

	public Vector3 Dequeue()
	{
		if (Count == 0)
		{
			throw new System.InvalidOperationException("Queue is empty");
		}
		Vector3 first = Waypoints[0];
		for (int i = 1; i < Count; i++)
		{
			Waypoints[i - 1] = Waypoints[i];
		}
		Count--;
		return first;
	}
}
