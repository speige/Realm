using Arch.Core;
using Realm.Ecs.Services;
using Godot;
using Realm.Ecs.Common;
using Realm.Ecs.Components.Core;
using Realm.Ecs.Components.Terrain;
using System;

public class EnvironmentService
{
	private readonly WorldAccessor _ecsWorldAccessor;
	private World EcsWorld => _ecsWorldAccessor.Current;

	public EnvironmentService(WorldAccessor ecsWorldAccessor)
	{
		_ecsWorldAccessor = ecsWorldAccessor;
	}

	private Entity FindWorldEntity()
	{
		Entity worldEntity = Entity.Null;
		var query = QueryCache.AllFogAndWeatherStateQuery;
		EcsWorld.Query(in query, entity => worldEntity = entity);
		return worldEntity;
	}

	public string GetCurrentWeather()
	{
		var worldEntity = FindWorldEntity();
		if (worldEntity != Entity.Null && EcsWorld.IsAlive(worldEntity) && EcsWorld.Has<FogAndWeatherState>(worldEntity))
		{
			return EcsWorld.Get<FogAndWeatherState>(worldEntity).CurrentWeather;
		}
		return "clear";
	}

	public void SetCurrentWeather(string weather)
	{
		var worldEntity = FindWorldEntity();
		if (worldEntity != Entity.Null && EcsWorld.IsAlive(worldEntity) && EcsWorld.Has<FogAndWeatherState>(worldEntity))
		{
			ref var state = ref EcsWorld.Get<FogAndWeatherState>(worldEntity);
			state.CurrentWeather = weather;
		}
	}

	public float GetBaseFogDensity()
	{
		var worldEntity = FindWorldEntity();
		if (worldEntity != Entity.Null && EcsWorld.IsAlive(worldEntity) && EcsWorld.Has<FogAndWeatherState>(worldEntity))
		{
			return EcsWorld.Get<FogAndWeatherState>(worldEntity).BaseFogDensity;
		}
		return 0f;
	}

	public void SetBaseFogDensity(float density)
	{
		var worldEntity = FindWorldEntity();
		if (worldEntity != Entity.Null && EcsWorld.IsAlive(worldEntity) && EcsWorld.Has<FogAndWeatherState>(worldEntity))
		{
			ref var state = ref EcsWorld.Get<FogAndWeatherState>(worldEntity);
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
	var sun = host.GetNodeOrNull<DirectionalLight3D>("DirectionalLight3D");
	if (worldEnv == null || worldEnv.Environment == null) return;

	var env = worldEnv.Environment;

	env.TonemapMode = Godot.Environment.ToneMapper.Filmic;
	env.AdjustmentEnabled = true;
	env.AmbientLightSource = Godot.Environment.AmbientSource.Color;

	float segment = Mathf.Clamp(progress, 0f, 1f) * 4f;
	int phaseIndex = (int)Mathf.Floor(segment);
	float t = Mathf.Clamp(segment - phaseIndex, 0f, 1f);

	if (phaseIndex >= 4)
	{
		phaseIndex = 3;
		t = 1.0f;
	}
	int nextIndex = (phaseIndex + 1) % 5;

	float[] exposures =  { 1.10f, 1.05f, 1.45f, 1.20f, 1.10f }; // Huge boost at Night (index 2) to lift dark textures
	float[] contrasts =  { 1.10f, 1.15f, 0.95f, 1.05f, 1.10f }; // Lower contrast at night prevents shadows from becoming pitch black
	float[] saturations = { 1.10f, 1.25f, 1.40f, 1.15f, 1.10f }; // High saturation keeps the night colorful and readable

	env.TonemapExposure = Mathf.Lerp(exposures[phaseIndex], exposures[nextIndex], t);
	env.AdjustmentContrast = Mathf.Lerp(contrasts[phaseIndex], contrasts[nextIndex], t);
	env.AdjustmentSaturation = Mathf.Lerp(saturations[phaseIndex], saturations[nextIndex], t);


	Color[] ambientColors = new Color[]
	{
		new Color(0.45f, 0.55f, 0.75f),   // Day
		new Color(0.52f, 0.32f, 0.50f),   // Sunset
		new Color(0.42f, 0.48f, 0.75f),   // Night - Brighter, luminous blue baseline
		new Color(0.38f, 0.35f, 0.65f),   // Dawn
		new Color(0.45f, 0.55f, 0.75f),   // Day (wrap)
	};

	float[] ambientEnergies = new float[] { 1.0f, 0.95f, 1.15f, 1.0f, 1.0f };
	
	Color interpolatedAmbient = ambientColors[phaseIndex].Lerp(ambientColors[nextIndex], t);
	float interpolatedAmbientEnergy = Mathf.Lerp(ambientEnergies[phaseIndex], ambientEnergies[nextIndex], t);
	
	env.AmbientLightColor = interpolatedAmbient;
	env.AmbientLightEnergy = interpolatedAmbientEnergy;


	Color[] directionalColors = new Color[]
	{
		new Color(1.00f, 0.96f, 0.88f),   // Day
		new Color(1.00f, 0.55f, 0.20f),   // Sunset
		new Color(0.55f, 0.80f, 1.00f),   // Night - Strong, vibrant cyan "moonlight"
		new Color(0.95f, 0.65f, 0.80f),   // Dawn
		new Color(1.00f, 0.96f, 0.88f),   // Day (wrap)
	};

	float[] directionalEnergies = new float[] { 2.40f, 1.90f, 1.30f, 1.50f, 2.40f };
	float[] pitchDegrees = { -48f, -42f, -45f, -40f, -48f };
	float[] yawDegrees   = {  35f,  60f, -25f, -50f,  35f };

	Color interpolatedDirectional = directionalColors[phaseIndex].Lerp(directionalColors[nextIndex], t);
	float interpolatedDirectionalEnergy = Mathf.Lerp(directionalEnergies[phaseIndex], directionalEnergies[nextIndex], t);
	float interpolatedPitch = Mathf.Lerp(pitchDegrees[phaseIndex], pitchDegrees[nextIndex], t);
	float interpolatedYaw = Mathf.Lerp(yawDegrees[phaseIndex], yawDegrees[nextIndex], t);

	if (sun != null)
	{
		sun.LightColor = interpolatedDirectional;
		sun.LightEnergy = interpolatedDirectionalEnergy;
		sun.RotationDegrees = new Vector3(interpolatedPitch, interpolatedYaw, 0f);
		
		sun.ShadowOpacity = Mathf.Lerp(0.75f, 0.50f, phaseIndex == 2 ? t : 0f); 
	}


	var fillLight = host.GetNodeOrNull<Camera3D>("Camera3D")
		?.GetNodeOrNull<DirectionalLight3D>("CharacterFillLight");
	if (fillLight != null)
	{
		float[] fillEnergies = { 0.20f, 0.28f, 0.65f, 0.42f, 0.20f };
		float interpolatedFillEnergy = Mathf.Lerp(fillEnergies[phaseIndex], fillEnergies[nextIndex], t);
		fillLight.LightEnergy = interpolatedFillEnergy;
	}
}

	public (int TimeOfDayIndex, float TimeOfDayTimer) CycleTimeOfDay(Node3D host, Entity worldEntity, float cycleDuration)
	{
		if (!EcsWorld.IsAlive(worldEntity) || !EcsWorld.Has<WorldState>(worldEntity))
		{
			return (0, 0f);
		}

		ref var state = ref EcsWorld.Get<WorldState>(worldEntity);
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

		EcsWorld.Set(worldEntity, new WorldState(state.GameElapsedTime, nextIndex, nextTimer, state.DayNightCycleEnabled));

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