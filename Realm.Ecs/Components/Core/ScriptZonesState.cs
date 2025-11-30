
namespace Realm.Ecs.Components.Core;

/// <summary>
/// Represents the boundary coordinates of a zone defined by a script.
/// </summary>
internal struct ZoneBounds
{
	public float MinX;
	public float MinZ;
	public float MaxX;
	public float MaxZ;
	public System.Numerics.Vector3 Center;
}

/// <summary>
/// Stores all script-defined zones in the world entity.
/// </summary>
internal struct ScriptZonesState
{
	public ScriptZonesState(List<ZoneBounds> zones)
	{
		Zones = zones;
	}

	public List<ZoneBounds> Zones { get; }
}

/// <summary>
/// Tracks which script zones are currently occupied by a unit entity.
/// </summary>
internal struct OccupiedZones
{
	public OccupiedZones(HashSet<int> zoneIds)
	{
		ZoneIds = zoneIds;
	}

	public HashSet<int> ZoneIds { get; }
}
