using System.Collections.Generic;

public class MapSaveData
{
	public float[] Heights { get; set; }
	public string[] Colors { get; set; }
	public int[] Pathing { get; set; }
	public List<UnitSaveData> Units { get; set; }
	public List<PropSaveData> Props { get; set; }
	public List<DecalSaveData> Decals { get; set; }
	public bool? WaterEnabled { get; set; }
	public float? WaterHeight { get; set; }
	public float? CameraBoundsLeft { get; set; }
	public float? CameraBoundsRight { get; set; }
	public float? CameraBoundsTop { get; set; }
	public float? CameraBoundsBottom { get; set; }
	public string SkyboxPath { get; set; }
}