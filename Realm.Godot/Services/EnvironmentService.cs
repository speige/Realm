using Arch.Core;
using Realm.Ecs.Services;
using Godot;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Terrain;
using System;

public class EnvironmentService
{
	private readonly WorldAccessor _ecsWorldAccessor;
	private World _ecsWorld => _ecsWorldAccessor.Current;

	public EnvironmentService(WorldAccessor ecsWorldAccessor)
	{
		_ecsWorldAccessor = ecsWorldAccessor;
	}

	private Entity FindWorldEntity()
	{
		Entity worldEntity = Entity.Null;
		var query = Realm.Ecs.Common.QueryCache.AllFogAndWeatherStateQuery;
		_ecsWorld.Query(in query, (Entity entity) => worldEntity = entity);
		return worldEntity;
	}

	public string GetCurrentWeather()
	{
		var worldEntity = FindWorldEntity();
		if (worldEntity != Entity.Null && _ecsWorld.IsAlive(worldEntity) && _ecsWorld.Has<FogAndWeatherState>(worldEntity))
		{
			return _ecsWorld.Get<FogAndWeatherState>(worldEntity).CurrentWeather;
		}
		return "clear";
	}

	public void SetCurrentWeather(string weather)
	{
		var worldEntity = FindWorldEntity();
		if (worldEntity != Entity.Null && _ecsWorld.IsAlive(worldEntity) && _ecsWorld.Has<FogAndWeatherState>(worldEntity))
		{
			ref var state = ref _ecsWorld.Get<FogAndWeatherState>(worldEntity);
			state.CurrentWeather = weather;
		}
	}

	public float GetBaseFogDensity()
	{
		var worldEntity = FindWorldEntity();
		if (worldEntity != Entity.Null && _ecsWorld.IsAlive(worldEntity) && _ecsWorld.Has<FogAndWeatherState>(worldEntity))
		{
			return _ecsWorld.Get<FogAndWeatherState>(worldEntity).BaseFogDensity;
		}
		return 0f;
	}

	public void SetBaseFogDensity(float density)
	{
		var worldEntity = FindWorldEntity();
		if (worldEntity != Entity.Null && _ecsWorld.IsAlive(worldEntity) && _ecsWorld.Has<FogAndWeatherState>(worldEntity))
		{
			ref var state = ref _ecsWorld.Get<FogAndWeatherState>(worldEntity);
			state.BaseFogDensity = density;
		}
	}

	public string CycleWeather()
	{
		string current = GetCurrentWeather();
		string next = current switch
		{
			"clear" => "rain",
			"rain" => "fog",
			"fog" => "clear",
			_ => "clear"
		};
		SetCurrentWeather(next);

		float density = next switch
		{
			"clear" => 0f,
			"rain" => 0.008f,
			"fog" => 0.045f,
			_ => 0f
		};
		SetBaseFogDensity(density);

		return next;
	}

	public void UpdateDayNightVisuals(Node3D host, float progress)
	{
		var worldEnv = host.GetNodeOrNull<WorldEnvironment>("WorldEnvironment");
		var light = host.GetNodeOrNull<DirectionalLight3D>("DirectionalLight3D");
		if (worldEnv == null || worldEnv.Environment == null) return;
		
		var env = worldEnv.Environment;
		env.TonemapMode = Godot.Environment.ToneMapper.Aces; 
		env.AdjustmentEnabled = true;
		env.AdjustmentSaturation = 1.2f;
		env.AdjustmentContrast = 1.05f; 

		env.AmbientLightSource = Godot.Environment.AmbientSource.Color;

		Color[] colors = new Color[]
		{
			new Color(0.22f, 0.38f, 0.58f),   
			new Color(0.9804f, 0.9569f, 0.8784f),  
			new Color(0.58f, 0.35f, 0.42f),   
			new Color(0.19f, 0.29f, 0.48f),   
			new Color(0.22f, 0.38f, 0.58f)    
		};

		float[] sunEnergies = new float[] { 0.88f, 1.0f, 0.95f, 0.80f, 0.88f }; 
		float[] ambientEnergies = new float[] { 0.8f, 0.5f, 0.75f, 1.6f, 0.8f }; 

		float segment = progress * 4f;
		int idx = (int)Mathf.Floor(segment) % 4;
		float t = segment - idx;

		Color rawColor = colors[idx].Lerp(colors[idx + 1], t);
		float currentSunEnergy = Mathf.Lerp(sunEnergies[idx], sunEnergies[idx + 1], t);
		float currentAmbientEnergy = Mathf.Lerp(ambientEnergies[idx], ambientEnergies[idx + 1], t);

		Color nightVisibilityFloor = new Color(0.22f, 0.26f, 0.42f); 
		Color ambientColor = new Color(
			Mathf.Max(rawColor.R, nightVisibilityFloor.R),
			Mathf.Max(rawColor.G, nightVisibilityFloor.G),
			Mathf.Max(rawColor.B, nightVisibilityFloor.B)
		);

		env.AmbientLightColor = ambientColor;
		env.AmbientLightEnergy = currentAmbientEnergy;

		if (light != null)
		{
			light.LightColor = rawColor;
			light.LightEnergy = currentSunEnergy;
			
			float angle = progress * 360.0f;
			light.RotationDegrees = new Vector3(-90.0f, angle, 0.0f);
		}
	}

	public (int TimeOfDayIndex, float TimeOfDayTimer) CycleTimeOfDay(Node3D host, Entity worldEntity, float cycleDuration)
	{
		if (!_ecsWorld.IsAlive(worldEntity) || !_ecsWorld.Has<WorldState>(worldEntity))
		{
			return (0, 0f);
		}

		ref var state = ref _ecsWorld.Get<WorldState>(worldEntity);
		int nextIndex = (state.TimeOfDayIndex + 1) % 4;

		float targetHour = nextIndex switch
		{
			0 => 12.0f, 
			1 => 19.0f, 
			2 => 0.0f,  
			3 => 5.5f,  
			_ => 12.0f
		};

		float progress = targetHour / 24f;
		float nextTimer = progress * cycleDuration;

		UpdateDayNightVisuals(host, progress);

		_ecsWorld.Set(worldEntity, new WorldState(state.GameElapsedTime, nextIndex, nextTimer, state.DayNightCycleEnabled));

		return (nextIndex, nextTimer);
	}

	public string GetTimeOfDayName(int timeOfDayIndex)
	{
		return timeOfDayIndex switch
		{
			0 => "Day",
			1 => "Sunset",
			2 => "Night",
			3 => "Dawn",
			_ => "Unknown"
		};
	}
}
