using Realm.MapAPI;

namespace Realm.Maps;

public static class Coordinates
{
    public static readonly Coordinate SpawnRegion = new Coordinate(
        new System.Numerics.Vector3(29.00f, 0f, -11.00f),
        new System.Numerics.Vector3(51.00f, 0f, 21.00f)
    );

    public static readonly Coordinate EndRegion = new Coordinate(
        new System.Numerics.Vector3(25.00f, 0f, 41.00f),
        new System.Numerics.Vector3(49.00f, 0f, 69.00f)
    );

    public static readonly Coordinate Tower1 = new Coordinate(
        new System.Numerics.Vector3(-3.00f, 0f, 27.00f),
        new System.Numerics.Vector3(3.00f, 0f, 33.00f)
    );

    public static readonly Coordinate Tower2 = new Coordinate(
        new System.Numerics.Vector3(9.00f, 0f, 27.00f),
        new System.Numerics.Vector3(15.00f, 0f, 33.00f)
    );

}
