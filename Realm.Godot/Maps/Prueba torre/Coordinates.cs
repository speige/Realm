using Realm.MapAPI;

namespace Realm.Maps;

public static class Coordinates
{
    public static readonly Coordinate towers = new Coordinate(
        new System.Numerics.Vector3(-31.00f, 0f, -55.00f),
        new System.Numerics.Vector3(47.00f, 0f, 21.00f)
    );

    public static readonly Coordinate basezone = new Coordinate(
        new System.Numerics.Vector3(-33.00f, 0f, 111.00f),
        new System.Numerics.Vector3(57.00f, 0f, 125.00f)
    );

    public static readonly Coordinate spawn = new Coordinate(
        new System.Numerics.Vector3(-27.00f, 0f, -113.00f),
        new System.Numerics.Vector3(51.00f, 0f, -93.00f)
    );

}
