namespace Realm.Ecs.Components.Terrain
{
	/// <summary>
	///     Holds the terrain color data in HTML format.
	/// </summary>
	internal struct TerrainColorsState
	{
		public string[] Colors;

		public TerrainColorsState(string[] colors)
		{
			Colors = colors;
		}
	}
}
