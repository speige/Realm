using DotRecast.Core.Numerics;
using DotRecast.Detour;
using Realm.Ecs.Services;
using System;
using System.Numerics;

internal class NavMeshPathfinder
{
	private readonly long[] _pathCorridorBuffer = new long[512];
	private readonly DtStraightPath[] _straightPathBuffer = new DtStraightPath[512];
	public static readonly RcVec3f PathfindingExtents = new RcVec3f(2f, 4f, 2f);
	private static readonly RcVec3f TargetPathfindingExtents = new RcVec3f(2f, 4f, 2f);
	private static readonly RcVec3f WideTargetExtents = new RcVec3f(50f, 50f, 50f);
	private readonly DtQueryDefaultFilter _filter = new DtQueryDefaultFilter();

	public DtQueryDefaultFilter Filter => _filter;

	public void ComputePath(DtNavMeshQuery query, Vector3 start, Vector3 end, ushort includeFlags, ref PathFollow pf)
	{
		pf.Target = end;

		pf.WaypointCount = 0;
		pf.CurrentWaypointIndex = 0;

		_filter.SetIncludeFlags(includeFlags);
		_filter.SetExcludeFlags(0);

		var startPos = new RcVec3f(start.X, start.Y, start.Z);
		var endPos = new RcVec3f(end.X, end.Y, end.Z);
		query.FindNearestPoly(startPos, PathfindingExtents, _filter, out long startRef, out var startPt, out _);
		query.FindNearestPoly(endPos, TargetPathfindingExtents, _filter, out long endRef, out var endPt, out _);

		if (endRef == 0)
		{
			query.FindNearestPoly(endPos, WideTargetExtents, _filter, out endRef, out endPt, out _);
		}

		if (startRef != 0 && endRef != 0)
		{
			query.FindPath(startRef, endRef, startPt, endPt, _filter, _pathCorridorBuffer, out int corridorCount, _pathCorridorBuffer.Length);
			if (corridorCount > 0)
			{
				float straightDist = Vector3.Distance(start, end);
				if (straightDist < 6.0f && corridorCount > 5)
				{
					pf.WaypointCount = 0;
				}
				else
				{
					query.FindStraightPath(startPt, endPt, _pathCorridorBuffer, corridorCount, _straightPathBuffer, out int straightPathCount, _straightPathBuffer.Length, 0);
					pf.WaypointCount = Math.Min(straightPathCount, WaypointBuffer.Length);
					pf.CurrentWaypointIndex = 0;
					for (int i = 0; i < pf.WaypointCount; i++)
					{
						pf.Waypoints[i] = new Vector3(_straightPathBuffer[i].pos.X, _straightPathBuffer[i].pos.Y, _straightPathBuffer[i].pos.Z);
					}
				}
			}
		}

		if (pf.WaypointCount <= 0)
		{
			if (startRef == 0)
			{
				pf.Waypoints[0] = end;
				pf.WaypointCount = 1;
				pf.CurrentWaypointIndex = 0;
			}
		}
	}
}
