namespace Realm.Ecs.Definitions;

/// <summary>
///     Represents the camera boundary properties of a map.
/// </summary>
internal class MapProperties
{
	public float? CameraBoundsLeft { get; set; }
	public float? CameraBoundsRight { get; set; }
	public float? CameraBoundsTop { get; set; }
	public float? CameraBoundsBottom { get; set; }
}