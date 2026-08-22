using Godot;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;

public enum HealthBarMode
{
	Hidden = 0,
	Visible = 1,
	Damaged = 2
}

public enum GameLanguage
{
	English = 0,
	Spanish = 1,
	French = 2,
	German = 3,
	Portuguese = 4,
	Russian = 5,
	Chinese = 6,
	Japanese = 7,
	Arabic = 8,
	Hindi = 9
}

public enum GraphicsQuality
{
	Low = 0,
	Medium = 1,
	High = 2,
	Ultra = 3
}

public enum DownsamplingMode
{
	Off = 0,
	Quality = 1,
	Performance = 2
}

public enum WindowMode
{
	Fullscreen = 0,
	Windowed = 1,
	Borderless = 2
}

public static class GameLanguageExtensions
{
	public static string ToLocaleCode(this GameLanguage language) => language switch
	{
		GameLanguage.English => "en",
		GameLanguage.Spanish => "es",
		GameLanguage.French => "fr",
		GameLanguage.German => "de",
		GameLanguage.Portuguese => "pt",
		GameLanguage.Russian => "ru",
		GameLanguage.Chinese => "zh",
		GameLanguage.Japanese => "ja",
		GameLanguage.Arabic => "ar",
		GameLanguage.Hindi => "hi",
		_ => "en"
	};

	public static GameLanguage ParseGameLanguage(string code) => code?.ToLowerInvariant() switch
	{
		"en" or "english" => GameLanguage.English,
		"es" or "spanish" => GameLanguage.Spanish,
		"fr" or "french" => GameLanguage.French,
		"de" or "german" => GameLanguage.German,
		"pt" or "portuguese" => GameLanguage.Portuguese,
		"ru" or "russian" => GameLanguage.Russian,
		"zh" or "chinese" => GameLanguage.Chinese,
		"ja" or "japanese" => GameLanguage.Japanese,
		"ar" or "arabic" => GameLanguage.Arabic,
		"hi" or "hindi" => GameLanguage.Hindi,
		_ => GameLanguage.English
	};
}

public static class GameSettings
{
	private const string SettingsPath = "user://settings.json";

	private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
	{
		PropertyNameCaseInsensitive = true,
		WriteIndented = true,
		Converters = { new JsonStringEnumConverter() }
	};

	public static int ResolutionIdx { get; set; } = 0;
	public static List<Vector2I> Resolutions { get; private set; }
	public static GraphicsQuality QualityIdx { get; set; } = GraphicsQuality.High;
	public static DownsamplingMode DownsamplingIdx { get; set; } = DownsamplingMode.Off;
	public static WindowMode WindowModeIdx { get; set; } = WindowMode.Windowed;
	public static bool Vsync { get; set; } = true;
	public static bool VsyncIdx
	{
		get => Vsync;
		set => Vsync = value;
	}

	public static float MasterVolume { get; set; } = 80f;
	public static float MusicVolume { get; set; } = 70f;
	public static float SfxVolume { get; set; } = 90f;
	public static float VoiceVolume { get; set; } = 60f;

	public static float ScrollSpeed { get; set; } = 50f;
	public static float MouseSens { get; set; } = 40f;
	public static float HudScale { get; set; } = 100f; // 100% default
	public static HealthBarMode ShowHealthBars { get; set; } = HealthBarMode.Damaged;
	public static GameLanguage Language { get; set; } = GameLanguage.English;
	public static bool DisplayFps { get; set; } = false;
	public static bool RecordReplays { get; set; } = false;
	public static bool SeedMapFiles { get; set; } = true;
	public static bool DisableShadows { get; set; } = false;
	public static bool DisableDayNightLighting { get; set; } = false;

	public static void ResetToDefaults()
	{
		int defaultIdx = Resolutions != null ? Resolutions.FindIndex(r => r == new Vector2I(1280, 720)) : 0;
		ResolutionIdx = defaultIdx >= 0 ? defaultIdx : 0;
		QualityIdx = AutoDetectQuality();
		DownsamplingIdx = GetDownsamplingIdxForQuality(QualityIdx);
		WindowModeIdx = WindowMode.Windowed;
		Vsync = true;
		DisableShadows = false;
		DisableDayNightLighting = false;
		MasterVolume = 80f;
		MusicVolume = 70f;
		SfxVolume = 90f;
		VoiceVolume = 60f;
		ScrollSpeed = 50f;
		MouseSens = 40f;
		HudScale = 100f;
		ShowHealthBars = HealthBarMode.Damaged;
		Language = GameLanguage.English;
		DisplayFps = false;
		RecordReplays = false;
		SeedMapFiles = true;
	}

	public static DownsamplingMode GetDownsamplingIdxForQuality(GraphicsQuality quality)
	{
		return quality switch
		{
			GraphicsQuality.Low => DownsamplingMode.Performance,
			GraphicsQuality.Medium => DownsamplingMode.Quality,
			GraphicsQuality.High => DownsamplingMode.Off,
			GraphicsQuality.Ultra => DownsamplingMode.Off,
			_ => DownsamplingMode.Off
		};
	}

	public static void InitializeResolutions()
	{
		Vector2I screenSize = DisplayServer.ScreenGetSize(DisplayServer.WindowGetCurrentScreen());
		var standardRes = new List<Vector2I>
		{
			new Vector2I(3840, 2160),
			new Vector2I(3440, 1440),
			new Vector2I(2560, 1600),
			new Vector2I(2560, 1440),
			new Vector2I(2560, 1080),
			new Vector2I(1920, 1200),
			new Vector2I(1920, 1080),
			new Vector2I(1680, 1050),
			new Vector2I(1600, 900),
			new Vector2I(1440, 900),
			new Vector2I(1366, 768),
			new Vector2I(1280, 800),
			new Vector2I(1280, 720)
		};

		var available = new List<Vector2I>();
		foreach (var res in standardRes)
		{
			if (res.X <= screenSize.X && res.Y <= screenSize.Y)
			{
				available.Add(res);
			}
		}

		if (!available.Contains(screenSize))
		{
			available.Add(screenSize);
		}

		available.Sort((a, b) => b.X == a.X ? b.Y.CompareTo(a.Y) : b.X.CompareTo(a.X));
		Resolutions = available;

		int defaultIdx = Resolutions.FindIndex(r => r == new Vector2I(1280, 720));
		ResolutionIdx = defaultIdx >= 0 ? defaultIdx : 0;
	}

	static GameSettings()
	{
		Load();
	}

	public static void Load()
	{
		if (Resolutions == null)
		{
			InitializeResolutions();
		}
		if (!FileAccess.FileExists(SettingsPath))
		{
			QualityIdx = AutoDetectQuality();
			DownsamplingIdx = GetDownsamplingIdxForQuality(QualityIdx);
			Save();
			return;
		}

		using var file = FileAccess.Open(SettingsPath, FileAccess.ModeFlags.Read);
		if (file == null) return;

		string json = file.GetAsText();
		try
		{
			var data = JsonSerializer.Deserialize<SettingsData>(json, JsonOptions);
			if (data != null)
			{
				ResolutionIdx = data.ResolutionIdx;
				QualityIdx = data.QualityIdx;
				DownsamplingIdx = data.DownsamplingIdx;
				WindowModeIdx = data.WindowModeIdx;
				Vsync = data.Vsync;
				MasterVolume = data.MasterVolume;
				MusicVolume = data.MusicVolume;
				SfxVolume = data.SfxVolume;
				VoiceVolume = data.VoiceVolume;
				ScrollSpeed = data.ScrollSpeed;
				MouseSens = data.MouseSens;
				HudScale = data.HudScale;
				Language = data.Language;
				DisplayFps = data.DisplayFps;
				RecordReplays = data.RecordReplays;
				SeedMapFiles = data.SeedMapFiles;
				DisableShadows = data.DisableShadows;
				DisableDayNightLighting = data.DisableDayNightLighting;
				ShowHealthBars = data.ShowHealthBars;

				int defaultIdx = Resolutions != null ? Resolutions.FindIndex(r => r == new Vector2I(1280, 720)) : 0;
				if (Resolutions != null)
				{
					if (data.ResolutionIdx <= 0 && WindowModeIdx == WindowMode.Windowed && defaultIdx >= 0)
					{
						ResolutionIdx = defaultIdx;
					}
					else if (ResolutionIdx < 0 || ResolutionIdx >= Resolutions.Count)
					{
						ResolutionIdx = defaultIdx >= 0 ? defaultIdx : 0;
					}
				}
			}
		}
		catch (System.Exception e)
		{
			GD.PrintErr($"Failed to deserialize settings: {e.Message}");
			ResetToDefaults();
		}
	}

	public static void Save()
	{
		var data = new SettingsData
		{
			ResolutionIdx = ResolutionIdx,
			QualityIdx = QualityIdx,
			DownsamplingIdx = DownsamplingIdx,
			WindowModeIdx = WindowModeIdx,
			Vsync = Vsync,
			MasterVolume = MasterVolume,
			MusicVolume = MusicVolume,
			SfxVolume = SfxVolume,
			VoiceVolume = VoiceVolume,
			ScrollSpeed = ScrollSpeed,
			MouseSens = MouseSens,
			HudScale = HudScale,
			ShowHealthBars = ShowHealthBars,
			Language = Language,
			DisplayFps = DisplayFps,
			RecordReplays = RecordReplays,
			SeedMapFiles = SeedMapFiles,
			DisableShadows = DisableShadows,
			DisableDayNightLighting = DisableDayNightLighting
		};

		string json = JsonSerializer.Serialize(data, JsonOptions);
		using var file = FileAccess.Open(SettingsPath, FileAccess.ModeFlags.Write);
		if (file != null)
		{
			file.StoreString(json);
		}
	}

	public static void ApplyGraphicsSettings(Node contextNode)
	{
		if (contextNode == null || !GodotObject.IsInstanceValid(contextNode)) return;

		var viewport = contextNode.GetViewport();
		if (viewport != null && GodotObject.IsInstanceValid(viewport))
		{
			switch (QualityIdx)
			{
				case GraphicsQuality.Low:
					viewport.Msaa3D = Viewport.Msaa.Disabled;
					viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled;
					viewport.UseTaa = false;
					viewport.PositionalShadowAtlasSize = 1024;
					break;
				case GraphicsQuality.Medium:
					viewport.Msaa3D = Viewport.Msaa.Disabled;
					viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Fxaa;
					viewport.UseTaa = false;
					viewport.PositionalShadowAtlasSize = 2048;
					break;
				case GraphicsQuality.High:
					viewport.Msaa3D = Viewport.Msaa.Msaa2X;
					viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Fxaa;
					viewport.UseTaa = false;
					viewport.PositionalShadowAtlasSize = 4096;
					break;
				case GraphicsQuality.Ultra:
					viewport.Msaa3D = Viewport.Msaa.Msaa4X;
					viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Fxaa;
					viewport.UseTaa = true;
					viewport.PositionalShadowAtlasSize = 4096;
					break;
			}

			switch (DownsamplingIdx)
			{
				case DownsamplingMode.Off:
					viewport.Scaling3DMode = Viewport.Scaling3DModeEnum.Bilinear;
					viewport.Scaling3DScale = 1.0f;
					break;
				case DownsamplingMode.Quality:
					viewport.Scaling3DMode = Viewport.Scaling3DModeEnum.Fsr;
					viewport.Scaling3DScale = 0.75f;
					break;
				case DownsamplingMode.Performance:
					viewport.Scaling3DMode = Viewport.Scaling3DModeEnum.Fsr;
					viewport.Scaling3DScale = 0.50f;
					break;
				default:
					viewport.Scaling3DMode = Viewport.Scaling3DModeEnum.Bilinear;
					viewport.Scaling3DScale = 1.0f;
					break;
			}
		}

		WorldEnvironment worldEnv = null;
		DirectionalLight3D light = null;
		Window root = null;

		var tree = contextNode.GetTree();
		if (tree != null && GodotObject.IsInstanceValid(tree))
		{
			root = tree.Root;
			if (root != null && GodotObject.IsInstanceValid(root))
			{
				worldEnv = FindNodeInTree<WorldEnvironment>(root);
				light = FindNodeInTree<DirectionalLight3D>(root);
			}
		}

		if (worldEnv != null && GodotObject.IsInstanceValid(worldEnv) && worldEnv.Environment != null)
		{
			var env = worldEnv.Environment;
			if (!env.IsLocalToScene())
			{
				env = (Godot.Environment)env.Duplicate();
				worldEnv.Environment = env;
			}

			ApplyEnvironmentQuality(env, QualityIdx);
		}

		if (light != null && GodotObject.IsInstanceValid(light))
		{
			ApplyDirectionalLightQuality(light, QualityIdx);
		}

		var terrain = (root != null ? FindNodeInTree<EditableTerrain>(root) : null) ?? EditableTerrain.Instance;
		if (terrain != null && GodotObject.IsInstanceValid(terrain))
		{
			terrain.ApplyQualitySettings((int)QualityIdx);
		}

		var gameHost = (root != null ? FindNodeInTree<GameHost>(root) : null) ?? GameHost.Instance;
		if (gameHost != null && GodotObject.IsInstanceValid(gameHost))
		{
			if (DisableDayNightLighting)
			{
				gameHost.EnvironmentService?.UpdateDayNightVisuals(gameHost, 0f);
			}
			else
			{
				if (gameHost.EcsWorld != null && gameHost.EcsWorld.IsAlive(gameHost.WorldEntity) && gameHost.EcsWorld.Has<Realm.Ecs.Components.Core.WorldState>(gameHost.WorldEntity))
				{
					var state = gameHost.EcsWorld.Get<Realm.Ecs.Components.Core.WorldState>(gameHost.WorldEntity);
					float progress = state.TimeOfDayTimer / GameHost.TimeOfDayCycleDuration;
					gameHost.EnvironmentService?.UpdateDayNightVisuals(gameHost, progress);
				}
				else
				{
					gameHost.EnvironmentService?.UpdateDayNightVisuals(gameHost, 0f);
				}
			}
		}
	}

	public static void ApplyEnvironmentQuality(Godot.Environment env, GraphicsQuality quality = GraphicsQuality.High)
	{
		if (env == null || !GodotObject.IsInstanceValid(env)) return;

		if (quality == GraphicsQuality.Low)
		{
			env.TonemapMode = Godot.Environment.ToneMapper.Agx;
			env.AdjustmentEnabled = false;
			env.SsaoEnabled = false;
			env.SsilEnabled = false;
			env.SsrEnabled = false;
			env.SdfgiEnabled = false;
			env.FogEnabled = false;
			env.GlowEnabled = false;
		}
		else
		{
			env.TonemapMode = Godot.Environment.ToneMapper.Agx;
			env.AdjustmentEnabled = true;
			env.SsaoEnabled = true;
			env.SsilEnabled = quality >= GraphicsQuality.High;
			env.SsrEnabled = quality == GraphicsQuality.Ultra;
			env.SdfgiEnabled = quality == GraphicsQuality.Ultra;
			env.FogEnabled = true;
			env.GlowEnabled = true;
		}
	}

	public static void ApplyDirectionalLightQuality(DirectionalLight3D light, GraphicsQuality quality = GraphicsQuality.High)
	{
		if (light == null || !GodotObject.IsInstanceValid(light)) return;

		if (DisableShadows || quality == GraphicsQuality.Low)
		{
			light.ShadowEnabled = false;
		}
		else if (quality == GraphicsQuality.Medium)
		{
			light.ShadowEnabled = true;
			light.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Orthogonal;
		}
		else
		{
			light.ShadowEnabled = true;
			light.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits;
		}
	}

	private static T FindNodeInTree<T>(Node parent) where T : Node
	{
		if (parent is T t) return t;
		int childCount = parent.GetChildCount();
		for (int i = 0; i < childCount; i++)
		{
			var found = FindNodeInTree<T>(parent.GetChild(i));
			if (found != null) return found;
		}
		return null;
	}

	private class SettingsData
	{
		public int ResolutionIdx { get; set; } = 0;
		public GraphicsQuality QualityIdx { get; set; } = GraphicsQuality.High;
		public DownsamplingMode DownsamplingIdx { get; set; } = DownsamplingMode.Off;
		public WindowMode WindowModeIdx { get; set; } = WindowMode.Windowed;
		public bool Vsync { get; set; } = true;
		public float MasterVolume { get; set; } = 80f;
		public float MusicVolume { get; set; } = 70f;
		public float SfxVolume { get; set; } = 90f;
		public float VoiceVolume { get; set; } = 60f;
		public float ScrollSpeed { get; set; } = 50f;
		public float MouseSens { get; set; } = 40f;
		public float HudScale { get; set; } = 100f;
		public HealthBarMode ShowHealthBars { get; set; } = HealthBarMode.Damaged;
		public GameLanguage Language { get; set; } = GameLanguage.English;
		public bool DisplayFps { get; set; } = false;
		public bool RecordReplays { get; set; } = false;
		public bool SeedMapFiles { get; set; } = true;
		public bool DisableShadows { get; set; } = false;
		public bool DisableDayNightLighting { get; set; } = false;
	}

	[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
	private struct DXGI_ADAPTER_DESC1
	{
		[MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
		public string Description;
		public uint VendorId;
		public uint DeviceId;
		public uint SubSysId;
		public uint Revision;
		public nuint DedicatedVideoMemory;
		public nuint DedicatedSystemMemory;
		public nuint SharedSystemMemory;
		public long AdapterLuid;
		public uint Flags;
	}

	private static readonly System.Guid IID_IDXGIFactory1 = new System.Guid("770aae78-f26f-4dba-a829-253c83d1b387");

	[DllImport("dxgi.dll")]
	private static extern int CreateDXGIFactory1([In] ref System.Guid riid, out System.IntPtr ppFactory);

	public static float GetGpuVramGb()
	{
		if (!System.OperatingSystem.IsWindows())
		{
			return 8.0f;
		}

		try
		{
			var guid = IID_IDXGIFactory1;
			if (CreateDXGIFactory1(ref guid, out System.IntPtr pFactory) != 0 || pFactory == System.IntPtr.Zero)
			{
				return 8.0f;
			}

			ulong maxDedicatedVramBytes = 0;
			uint adapterIndex = 0;

			unsafe
			{
				void** factoryVtbl = *(void***)pFactory;
				var enumAdapters1 = (delegate* unmanaged[Stdcall]<System.IntPtr, uint, out System.IntPtr, int>)factoryVtbl[12];
				var releaseFactory = (delegate* unmanaged[Stdcall]<System.IntPtr, uint>)factoryVtbl[2];

				while (enumAdapters1(pFactory, adapterIndex, out System.IntPtr pAdapter) == 0 && pAdapter != System.IntPtr.Zero)
				{
					void** adapterVtbl = *(void***)pAdapter;
					var getDesc1 = (delegate* unmanaged[Stdcall]<System.IntPtr, out DXGI_ADAPTER_DESC1, int>)adapterVtbl[10];
					var releaseAdapter = (delegate* unmanaged[Stdcall]<System.IntPtr, uint>)adapterVtbl[2];

					if (getDesc1(pAdapter, out DXGI_ADAPTER_DESC1 desc) == 0)
					{
						const uint DXGI_ADAPTER_FLAG_SOFTWARE = 2;
						if ((desc.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) == 0)
						{
							ulong vram = (ulong)desc.DedicatedVideoMemory;
							if (vram > maxDedicatedVramBytes)
							{
								maxDedicatedVramBytes = vram;
							}
						}
					}

					releaseAdapter(pAdapter);
					adapterIndex++;
				}

				releaseFactory(pFactory);
			}

			if (maxDedicatedVramBytes > 0)
			{
				return (float)(maxDedicatedVramBytes / (1024.0 * 1024.0 * 1024.0));
			}
		}
		catch (System.Exception ex)
		{
			GD.PrintErr($"Failed to query GPU VRAM via DXGI: {ex.Message}");
		}

		return 8.0f;
	}

	public static GraphicsQuality AutoDetectQuality()
	{
		float vramGb = GetGpuVramGb();
		if (vramGb <= 3.0f)
		{
			return GraphicsQuality.Low;
		}
		if (vramGb <= 6.0f)
		{
			return GraphicsQuality.Medium;
		}
		return GraphicsQuality.High;
	}
}
