namespace Realm.Ecs.Components.Terrain
{
	/// <summary>
	/// Represents the domain data for the fog of war grid and weather conditions.
	/// </summary>
	internal struct FogAndWeatherState
	{
		public byte[,] FogGrid;
		public string FogOfWarType;
		public string CurrentWeather;
		public float BaseFogDensity;

		public FogAndWeatherState(byte[,] fogGrid, string fogOfWarType, string currentWeather, float baseFogDensity)
		{
			FogGrid = fogGrid;
			FogOfWarType = fogOfWarType;
			CurrentWeather = currentWeather;
			BaseFogDensity = baseFogDensity;
		}
	}
}
