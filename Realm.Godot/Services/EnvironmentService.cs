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

	public bool OverrideDayNightVisuals { get; set; } = false;

	// --- Core Day-Night Keyframe Preset Arrays (0=Noon, 1=Dusk, 2=Midnight, 3=Dawn) ---

	// Directional Sun Angles & Accent Energies (Low energy = Accent rim/shadows only)
	public static readonly float[] SunPitches       = {  55.0f,  15.0f,  65.0f,  15.0f };
	public static readonly float[] SunYaws          = {  20.0f,-101.0f, 180.0f,  74.0f };
	public static readonly float[] SunEnergies      = {   0.80f,  0.45f,   0.05f,  0.50f }; // Subtle accents
	public static readonly Color[] SunColors        = {
		new Color(1.000f, 0.957f, 0.878f), // Noon (Midday Warm White)
		new Color(1.000f, 0.700f, 0.400f), // Dusk (Golden Amber Rim)
		new Color(0.450f, 0.650f, 0.950f), // Midnight (Subtle Moonlight)
		new Color(1.000f, 0.800f, 0.600f)  // Dawn (Soft Sunrise Gold)
	};

	// Locked Universal Ambient Baseline
	public static readonly float[] AmbientEnergies  = {   3.88f,  2.10f,   1.93f,  2.70f };
	public static readonly Color[] AmbientColors    = {
		new Color(0.680f, 0.760f, 0.860f), // Noon
		new Color(0.420f, 0.450f, 0.600f), // Dusk
		new Color(0.350f, 0.500f, 0.780f), // Midnight
		new Color(0.520f, 0.620f, 0.750f)  // Dawn
	};

	public static readonly float[] FogDensities     = { 0.0150f, 0.0190f, 0.0190f, 0.0250f };
	public static readonly Color[] FogColors        = {
		new Color(0.080f, 0.100f, 0.150f),
		new Color(0.190f, 0.080f, 0.120f),
		new Color(0.030f, 0.060f, 0.120f),
		new Color(0.120f, 0.150f, 0.220f)
	};

	public static readonly float[] SsaoIntensities  = {   0.80f,  0.90f,   1.15f,  0.85f };
	public static readonly float[] SsaoRadii        = {   1.80f,  1.80f,   1.80f,  1.80f };

	public static readonly float[] Exposures        = {   1.00f,  1.02f,   1.04f,  1.02f };
	public static readonly float[] Contrasts        = {   1.10f,  1.08f,   1.04f,  1.08f };
	public static readonly float[] Saturations      = {   0.98f,  0.92f,   0.88f,  0.96f };

	public static readonly float[] GlowIntensities  = {   0.60f,  0.70f,   0.80f,  0.65f };
	public static readonly float[] GlowBlooms       = {   0.12f,  0.10f,   0.10f,  0.11f };

	public void UpdateDayNightVisuals(Node3D host, float progress)
	{
		if (OverrideDayNightVisuals) return;

		var worldEnv = host.GetNodeOrNull<WorldEnvironment>("WorldEnvironment");
		var sun = host.GetNodeOrNull<DirectionalLight3D>("DirectionalLight3D");
		if (worldEnv == null || worldEnv.Environment == null) return;

		var env = worldEnv.Environment;

		// --- 1. Compute Phase Interpolation (Noon -> Dusk -> Midnight -> Dawn -> Noon) ---
		float normalizedProgress = Mathf.PosMod(progress, 1.0f);
		float segment = normalizedProgress * 4.0f;
		int phaseIndex = (int)Mathf.Floor(segment);
		float t = segment - phaseIndex;
		int nextIndex = (phaseIndex + 1) % 4;

		// --- 2. Update Environment Settings ---
		env.AmbientLightSource = Godot.Environment.AmbientSource.Color;
		env.AmbientLightColor = AmbientColors[phaseIndex].Lerp(AmbientColors[nextIndex], t);
		env.AmbientLightEnergy = Mathf.Lerp(AmbientEnergies[phaseIndex], AmbientEnergies[nextIndex], t);

		GameSettings.ApplyEnvironmentQuality(env);

		if (GameSettings.QualityIdx > 0)
		{
			env.TonemapExposure = Mathf.Lerp(Exposures[phaseIndex], Exposures[nextIndex], t);
			env.AdjustmentContrast = Mathf.Lerp(Contrasts[phaseIndex], Contrasts[nextIndex], t);
			env.AdjustmentSaturation = Mathf.Lerp(Saturations[phaseIndex], Saturations[nextIndex], t);

			env.SsaoRadius = Mathf.Lerp(SsaoRadii[phaseIndex], SsaoRadii[nextIndex], t);
			env.SsaoIntensity = Mathf.Lerp(SsaoIntensities[phaseIndex], SsaoIntensities[nextIndex], t);
			env.SsaoDetail = 0.5f;

			env.FogLightColor = FogColors[phaseIndex].Lerp(FogColors[nextIndex], t);
			env.FogDensity = Mathf.Lerp(FogDensities[phaseIndex], FogDensities[nextIndex], t);

			env.GlowIntensity = Mathf.Lerp(GlowIntensities[phaseIndex], GlowIntensities[nextIndex], t);
			env.GlowStrength = 0.90f;
			env.GlowBloom = Mathf.Lerp(GlowBlooms[phaseIndex], GlowBlooms[nextIndex], t);
			env.GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Additive;
		}

		// --- 3. Primary Directional Accent Light ---
		Color interpSunColor = SunColors[phaseIndex].Lerp(SunColors[nextIndex], t);
		float interpSunEnergy = Mathf.Lerp(SunEnergies[phaseIndex], SunEnergies[nextIndex], t);
		
		float radSunPitch = Mathf.LerpAngle(Mathf.DegToRad(SunPitches[phaseIndex]), Mathf.DegToRad(SunPitches[nextIndex]), t);
		float radSunYaw   = Mathf.LerpAngle(Mathf.DegToRad(SunYaws[phaseIndex]),   Mathf.DegToRad(SunYaws[nextIndex]),   t);

		float interpSunPitch = Mathf.Clamp(Mathf.RadToDeg(radSunPitch), 12.0f, 85.0f);
		float interpSunYaw   = Mathf.RadToDeg(radSunYaw);

		if (sun != null)
		{
			GameSettings.ApplyDirectionalLightQuality(sun);
			if (sun.ShadowEnabled)
			{
				sun.ShadowEnabled = interpSunEnergy > 0.1f;
			}
			sun.DirectionalShadowBlendSplits = true;
			sun.ShadowBias = 0.04f;
			sun.ShadowNormalBias = 1.5f;
			sun.LightColor = new Color(1.0f, 1.0f, 1.0f);
			sun.LightEnergy = interpSunEnergy;
			sun.LightSpecular = 0.0f;
			sun.RotationDegrees = new Vector3(interpSunPitch, interpSunYaw, 0f);
		}

		// --- 5. Character Fill Light ---
		var fillLight = host.GetNodeOrNull<Camera3D>("Camera3D")
			?.GetNodeOrNull<DirectionalLight3D>("CharacterFillLight");
		if (fillLight != null)
		{
			float[] fillEnergies = { 0.20f, 0.28f, 0.65f, 0.42f };
			fillLight.LightEnergy = Mathf.Lerp(fillEnergies[phaseIndex], fillEnergies[nextIndex], t);
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

		float progress = nextIndex * 0.25f;
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
			1 => "Dusk",
			2 => "Night",
			3 => "Dawn",
			_ => "Unknown"
		};
	}
}