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

	// --- Core Day-Night Keyframe Preset Arrays (0=Noon/Day, 1=Dusk, 2=Midnight/Night, 3=Dawn) ---

	// Directional Sun Angles & Energies (Key light: creates directional contrast, normal maps & shadows)
	public static readonly float[] SunPitches       = { -55.0f, -35.0f, -48.0f, -34.0f };
	public static readonly float[] SunYaws          = {  27.0f,-106.0f, 180.0f,  74.0f };
	public static readonly float[] SunEnergies      = {   2.10f,  2.20f,   0.75f,  2.10f };
	public static readonly Color[] SunColors        = {
		new Color(1.000f, 0.970f, 0.920f), // Day (Midday Warm Sunlight)
		new Color(1.000f, 0.600f, 0.240f), // Dusk (Golden Amber Sun)
		new Color(0.500f, 0.720f, 1.000f), // Night (Subtle Cool Moonlight)
		new Color(1.000f, 0.840f, 0.650f)  // Dawn (Soft Sunrise Gold)
	};

	// Universal Ambient Fill (Soft sky/indirect light fill)
	public static readonly float[] AmbientEnergies  = {   0.95f,  1.10f,   1.25f,  1.05f };
	public static readonly Color[] AmbientColors    = {
		new Color(0.520f, 0.620f, 0.780f), // Day (#849EC6)
		new Color(0.490f, 0.520f, 0.750f), // Dusk (#7C84BF)
		new Color(0.340f, 0.460f, 0.700f), // Night (#5675B2)
		new Color(0.500f, 0.580f, 0.780f)  // Dawn (#7F93C6)
	};

	public static readonly float[] FogDensities     = { 0.0080f, 0.0120f, 0.0150f, 0.0120f };
	public static readonly Color[] FogColors        = {
		new Color(0.550f, 0.650f, 0.750f),
		new Color(0.450f, 0.300f, 0.350f),
		new Color(0.080f, 0.120f, 0.200f),
		new Color(0.400f, 0.450f, 0.550f)
	};

	public static readonly float[] SsaoIntensities  = {   0.90f,  0.70f,   0.50f,  0.60f };
	public static readonly float[] SsaoRadii        = {   1.50f,  1.50f,   1.50f,  1.50f };

	public static readonly float[] Exposures        = {   1.10f,  1.12f,   1.06f,  1.10f };
	public static readonly float[] Contrasts        = {   1.08f,  1.06f,   1.00f,  1.04f };
	public static readonly float[] Saturations      = {   1.04f,  1.04f,   0.92f,  1.00f };

	public static readonly float[] GlowIntensities  = {   0.20f,  0.25f,   0.30f,  0.20f };
	public static readonly float[] GlowBlooms       = {   0.08f,  0.06f,   0.06f,  0.08f };

	public void UpdateDayNightVisuals(Node3D host, float progress)
	{
		if (OverrideDayNightVisuals) return;
		if (GameSettings.DisableDayNightLighting)
		{
			progress = 0f;
		}

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

		float interpSunPitch = Mathf.RadToDeg(radSunPitch);
		float interpSunYaw   = Mathf.RadToDeg(radSunYaw);

		if (sun != null)
		{
			GameSettings.ApplyDirectionalLightQuality(sun);
			sun.DirectionalShadowMaxDistance = 250.0f;
			sun.DirectionalShadowBlendSplits = true;
			sun.DirectionalShadowFadeStart = 0.8f;
			sun.ShadowBias = 0.03f;
			sun.ShadowNormalBias = 1.2f;
			sun.ShadowEnabled = !GameSettings.DisableShadows && interpSunEnergy > 0.05f && GameSettings.QualityIdx > 0;
			sun.LightColor = interpSunColor;
			sun.LightEnergy = interpSunEnergy;
			sun.LightSpecular = 0.5f;
			sun.RotationDegrees = new Vector3(interpSunPitch, interpSunYaw, 0f);
		}

		// --- 5. Character Fill Light ---
		var fillLight = host.GetNodeOrNull<Camera3D>("Camera3D")
			?.GetNodeOrNull<DirectionalLight3D>("CharacterFillLight");
		if (fillLight != null)
		{
			float[] fillEnergies = { 0.15f, 0.20f, 0.35f, 0.22f };
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