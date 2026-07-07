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
		var sun = host.GetNodeOrNull<DirectionalLight3D>("DirectionalLight3D");
		if (worldEnv == null || worldEnv.Environment == null) return;

		var env = worldEnv.Environment;

		env.TonemapMode = Godot.Environment.ToneMapper.Filmic;
		env.TonemapExposure = 1.0f;
		env.AdjustmentEnabled = true;
		env.AdjustmentContrast = 1.05f;
		env.AmbientLightSource = Godot.Environment.AmbientSource.Color;

		const float AmbientEnergyFloor = 0.50f;
		const float DirectionalEnergyFloor = 0.50f;

		// Fix A: Night phases push toward fully-saturated deep indigo (#22254f)
		// rather than the previous muddy slate. The B channel dominates strongly
		// and R/G are kept very low so the scene reads as "midnight blue fantasy"
		// rather than a desaturated grey.
		Color[] ambientColors = new Color[]
		{
			new Color(0.38f, 0.52f, 0.78f),   // Day    – clear sky blue
			new Color(0.48f, 0.22f, 0.48f),   // Sunset – deep saturated mauve
			new Color(0.12f, 0.12f, 0.46f),   // Night  – vivid midnight indigo
			new Color(0.18f, 0.14f, 0.45f),   // Dawn   – cool violet pre-sunrise
			new Color(0.38f, 0.52f, 0.78f),   // Day    – wrap
		};

		float[] ambientEnergies = new float[]
		{
			0.70f,   // Day
			0.68f,   // Sunset
			0.55f,
			0.65f,   // Dawn
			0.70f,   // Day (wrap)
		};

		Color[] directionalColors = new Color[]
		{
			new Color(1.00f, 0.95f, 0.82f),   // Day    – warm golden white
			new Color(1.00f, 0.62f, 0.28f),   // Sunset – amber orange
			new Color(0.62f, 0.82f, 1.00f),   // Night  – silver-cyan moonlight
			new Color(0.92f, 0.70f, 0.88f),   // Dawn   – rosy pink
			new Color(1.00f, 0.95f, 0.82f),   // Day    – wrap
		};

		// Fix B: Raise sun energy (2.2 peak) for sharp armor glints on poly-edges.
		// Energy > 1.0 is valid with Filmic tonemap – highlights clip gracefully
		// rather than blowing out flat white.
		float[] directionalEnergies = new float[]
		{
			2.20f,   // Day    – strong enough for hard specular glints on armor
			1.80f,   // Sunset – dramatic rim lighting
			0.55f,   // Night  ← floor; moon carves upper-body highlights
			0.75f,   // Dawn
			2.20f,   // Day    – wrap
		};

		float[] pitchDegrees = { -65f,      -62f,      -72f,      -60f,      -65f };
		float[] yawDegrees   = {  30f,       52f,      -18f,      -45f,       30f };

		float segment = Mathf.Clamp(progress, 0f, 1f) * 4f;
		int phaseIndex = (int)Mathf.Floor(segment);
		float t = Mathf.Clamp(segment - phaseIndex, 0f, 1f);

		Color interpolatedAmbient = ambientColors[phaseIndex].Lerp(ambientColors[phaseIndex + 1], t);
		float interpolatedAmbientEnergy = Mathf.Lerp(ambientEnergies[phaseIndex], ambientEnergies[phaseIndex + 1], t);

		Color interpolatedDirectional = directionalColors[phaseIndex].Lerp(directionalColors[phaseIndex + 1], t);
		float interpolatedDirectionalEnergy = Mathf.Lerp(directionalEnergies[phaseIndex], directionalEnergies[phaseIndex + 1], t);

		float interpolatedPitch = Mathf.Lerp(pitchDegrees[phaseIndex], pitchDegrees[phaseIndex + 1], t);
		float interpolatedYaw = Mathf.Lerp(yawDegrees[phaseIndex], yawDegrees[phaseIndex + 1], t);

		env.AmbientLightColor = interpolatedAmbient;
		env.AmbientLightEnergy = Mathf.Max(interpolatedAmbientEnergy, AmbientEnergyFloor);

		if (sun != null)
		{
			sun.LightColor = interpolatedDirectional;
			sun.LightEnergy = Mathf.Max(interpolatedDirectionalEnergy, DirectionalEnergyFloor);
			sun.RotationDegrees = new Vector3(interpolatedPitch, interpolatedYaw, 0f);
		}

		var fillLight = host.GetNodeOrNull<Camera3D>("Camera3D")
			?.GetNodeOrNull<DirectionalLight3D>("CharacterFillLight");
		if (fillLight != null)
		{
			float[] fillEnergies = { 0.20f, 0.28f, 0.62f, 0.42f, 0.20f };
			float interpolatedFillEnergy = Mathf.Lerp(fillEnergies[phaseIndex], fillEnergies[phaseIndex + 1], t);
			fillLight.LightEnergy = interpolatedFillEnergy;
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
