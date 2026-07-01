using Arch.Core;
using Godot;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Terrain;
using System;

public class MapPropertiesLoader
{
	private readonly World _ecsWorld;

	public MapPropertiesLoader(World ecsWorld)
	{
		_ecsWorld = ecsWorld;
	}

	public void LoadMapProperties(Entity worldEntity, string path)
	{
		if (!FileAccess.FileExists(path))
		{
			return;
		}

		using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
		if (file == null)
		{
			return;
		}

		try
		{
			string jsonText = file.GetAsText();
			using var jsonDoc = System.Text.Json.JsonDocument.Parse(jsonText);
			if (jsonDoc.RootElement.TryGetProperty("MapProperties", out var mapProps))
			{
				if (mapProps.TryGetProperty("CameraBoundsLeft", out var leftProp) && leftProp.ValueKind == System.Text.Json.JsonValueKind.Number)
				{
					_ecsWorld.Mutate<CameraState>(worldEntity, (ref CameraState s) => s.LimitLeft = (float)leftProp.GetDouble());
				}
				if (mapProps.TryGetProperty("CameraBoundsRight", out var rightProp) && rightProp.ValueKind == System.Text.Json.JsonValueKind.Number)
				{
					_ecsWorld.Mutate<CameraState>(worldEntity, (ref CameraState s) => s.LimitRight = (float)rightProp.GetDouble());
				}
				if (mapProps.TryGetProperty("CameraBoundsTop", out var topProp) && topProp.ValueKind == System.Text.Json.JsonValueKind.Number)
				{
					_ecsWorld.Mutate<CameraState>(worldEntity, (ref CameraState s) => s.LimitTop = (float)topProp.GetDouble());
				}
				if (mapProps.TryGetProperty("CameraBoundsBottom", out var bottomProp) && bottomProp.ValueKind == System.Text.Json.JsonValueKind.Number)
				{
					_ecsWorld.Mutate<CameraState>(worldEntity, (ref CameraState s) => s.LimitBottom = (float)bottomProp.GetDouble());
				}
				if (mapProps.TryGetProperty("FogOfWarType", out var fogTypeProp) && fogTypeProp.ValueKind == System.Text.Json.JsonValueKind.String)
				{
					string val = fogTypeProp.GetString() ?? "grey";
					_ecsWorld.Mutate<FogAndWeatherState>(worldEntity, (ref FogAndWeatherState s) => s.FogOfWarType = val);
				}
				if (mapProps.TryGetProperty("WeatherType", out var weatherProp) && weatherProp.ValueKind == System.Text.Json.JsonValueKind.String)
				{
					string val = weatherProp.GetString() ?? "clear";
					_ecsWorld.Mutate<FogAndWeatherState>(worldEntity, (ref FogAndWeatherState s) => s.CurrentWeather = val);
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapPropertiesLoader] Failed to load map properties: {ex.Message}");
		}
	}
}
