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
		var query = QueryCache.AllWeatherStateQuery;
		EcsWorld.Query(in query, entity => worldEntity = entity);
		return worldEntity;
	}

	public string GetCurrentWeather()
	{
		var worldEntity = FindWorldEntity();
		if (worldEntity != Entity.Null && EcsWorld.IsAlive(worldEntity) && EcsWorld.Has<WeatherState>(worldEntity))
		{
			return EcsWorld.Get<WeatherState>(worldEntity).CurrentWeather;
		}
		return "clear";
	}

	public void SetCurrentWeather(string weather)
	{
		var worldEntity = FindWorldEntity();
		if (worldEntity != Entity.Null && EcsWorld.IsAlive(worldEntity) && EcsWorld.Has<WeatherState>(worldEntity))
		{
			ref var state = ref EcsWorld.Get<WeatherState>(worldEntity);
			state.CurrentWeather = weather;
		}
	}

	public float GetBaseFogDensity()
	{
		var worldEntity = FindWorldEntity();
		if (worldEntity != Entity.Null && EcsWorld.IsAlive(worldEntity) && EcsWorld.Has<WeatherState>(worldEntity))
		{
			return EcsWorld.Get<WeatherState>(worldEntity).BaseFogDensity;
		}
		return 0f;
	}

	public void SetBaseFogDensity(float density)
	{
		var worldEntity = FindWorldEntity();
		if (worldEntity != Entity.Null && EcsWorld.IsAlive(worldEntity) && EcsWorld.Has<WeatherState>(worldEntity))
		{
			ref var state = ref EcsWorld.Get<WeatherState>(worldEntity);
			state.BaseFogDensity = density;
		}
	}

	public void UpdateEnvironmentalFog(Camera3D camera3D, WorldEnvironment worldEnv)
	{
		if (worldEnv == null || worldEnv.Environment == null) return;

		if (GameHost.Instance != null && GameHost.Instance.IsMapEditorMode)
		{
			worldEnv.Environment.FogEnabled = false;
			return;
		}

		float baseFogDensity = GetBaseFogDensity();
		if (baseFogDensity > 0f && camera3D != null && GodotObject.IsInstanceValid(camera3D))
		{
			worldEnv.Environment.FogEnabled = true;
			float height = camera3D.GlobalPosition.Y;
			float scale = 18.0f / Mathf.Max(8.0f, height);
			worldEnv.Environment.FogDensity = baseFogDensity * scale;
		}
		else
		{
			worldEnv.Environment.FogEnabled = false;
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
	public static readonly float[] SunPitches       = { -58.0f, -45.0f, -50.0f, -45.0f };
	public static readonly float[] SunYaws          = {  29.0f,-115.0f, 155.0f,  95.0f };
	public static readonly float[] SunEnergies      = {   2.50f,  1.50f,   0.50f,  2.70f };
	public static readonly Color[] SunColors        = {
		new Color(1.000f, 0.980f, 0.940f), // Day (Midday Warm Sunlight)
		new Color(1.000f, 0.700f, 0.380f), // Dusk (Golden Amber Sun)
		new Color(0.700f, 0.880f, 1.000f), // Night (Subtle Cool Moonlight)
		new Color(1.000f, 0.880f, 0.720f)  // Dawn (Soft Sunrise Gold)
	};

	// Universal Ambient Fill (Soft sky/indirect light fill)
	public static readonly float[] AmbientEnergies  = {   0.80f,  0.80f,   0.95f,  0.90f };
	public static readonly Color[] AmbientColors    = {
		new Color(0.480f, 0.580f, 0.740f), // Day (#7A93BC)
		new Color(0.420f, 0.460f, 0.720f), // Dusk (#6B75B7)
		new Color(0.280f, 0.420f, 0.850f), // Night (#476BD8)
		new Color(0.580f, 0.540f, 0.840f)  // Dawn (#9389D6)
	};

	public static readonly float[] FogDensities     = { 0.0080f, 0.0120f, 0.0150f, 0.0120f };
	public static readonly Color[] FogColors        = {
		new Color(0.550f, 0.650f, 0.750f),
		new Color(0.450f, 0.300f, 0.350f),
		new Color(0.080f, 0.120f, 0.200f),
		new Color(0.400f, 0.450f, 0.550f)
	};

	public static readonly float[] SsaoIntensities  = {   0.40f,  0.30f,   0.20f,  0.30f };
	public static readonly float[] SsaoRadii        = {   1.20f,  1.20f,   1.00f,  1.10f };

	public static readonly float[] Exposures        = {   1.18f,  1.14f,   1.14f,  1.18f };
	public static readonly float[] Contrasts        = {   1.02f,  1.02f,   1.00f,  1.02f };
	public static readonly float[] Saturations      = {   1.06f,  0.98f,   1.06f,  1.04f };

	public static readonly float[] GlowIntensities  = {   0.15f,  0.30f,   0.25f,  0.20f };
	public static readonly float[] GlowBlooms       = {   0.14f,  0.14f,   0.08f,  0.10f };

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

		GameSettings.ApplyEnvironmentQuality(env, GameSettings.QualityIdx);

		if (GameSettings.QualityIdx > GraphicsQuality.Low)
		{
			env.SsaoRadius = Mathf.Lerp(SsaoRadii[phaseIndex], SsaoRadii[nextIndex], t);
			env.SsaoIntensity = Mathf.Lerp(SsaoIntensities[phaseIndex], SsaoIntensities[nextIndex], t);
			env.SsaoDetail = 0.5f;

			env.GlowIntensity = Mathf.Lerp(GlowIntensities[phaseIndex], GlowIntensities[nextIndex], t);
			env.GlowStrength = 0.90f;
			env.GlowBloom = Mathf.Lerp(GlowBlooms[phaseIndex], GlowBlooms[nextIndex], t);
			env.GlowBlendMode = Godot.Environment.GlowBlendModeEnum.Additive;
		}

		env.TonemapExposure = Mathf.Lerp(Exposures[phaseIndex], Exposures[nextIndex], t);
		env.AdjustmentContrast = Mathf.Lerp(Contrasts[phaseIndex], Contrasts[nextIndex], t);
		env.AdjustmentSaturation = Mathf.Lerp(Saturations[phaseIndex], Saturations[nextIndex], t);

		env.FogLightColor = FogColors[phaseIndex].Lerp(FogColors[nextIndex], t);
		env.FogDensity = Mathf.Lerp(FogDensities[phaseIndex], FogDensities[nextIndex], t);

		// --- 3. Primary Directional Accent Light ---
		Color interpSunColor = SunColors[phaseIndex].Lerp(SunColors[nextIndex], t);
		float interpSunEnergy = Mathf.Lerp(SunEnergies[phaseIndex], SunEnergies[nextIndex], t);
		
		float radSunPitch = Mathf.LerpAngle(Mathf.DegToRad(SunPitches[phaseIndex]), Mathf.DegToRad(SunPitches[nextIndex]), t);
		float radSunYaw   = Mathf.LerpAngle(Mathf.DegToRad(SunYaws[phaseIndex]),   Mathf.DegToRad(SunYaws[nextIndex]),   t);

		float interpSunPitch = Mathf.RadToDeg(radSunPitch);
		float interpSunYaw   = Mathf.RadToDeg(radSunYaw);

		if (sun != null)
		{
			sun.DirectionalShadowBlendSplits = true;
			sun.DirectionalShadowFadeStart = 0.8f;
			sun.ShadowBias = 0.03f;
			sun.ShadowNormalBias = 1.2f;
			sun.LightColor = interpSunColor;
			sun.LightEnergy = interpSunEnergy;
			sun.LightSpecular = 0.5f;
			sun.RotationDegrees = new Vector3(interpSunPitch, interpSunYaw, 0f);
			GameSettings.ApplyDirectionalLightQuality(sun, GameSettings.QualityIdx);
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