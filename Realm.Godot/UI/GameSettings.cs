using Godot;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.Json;

public static class GameSettings
{
	private const string SettingsPath = "user://settings.json";

	public static int ResolutionIdx { get; set; } = 0;
	public static List<Vector2I> Resolutions { get; private set; }
	public static int QualityIdx { get; set; } = 2; // High
	public static int DownsamplingIdx { get; set; } = 0; // Off
	public static int WindowModeIdx { get; set; } = 2; // Borderless
	public static int VsyncIdx { get; set; } = 0; // On

	public static float MasterVolume { get; set; } = 80f;
	public static float MusicVolume { get; set; } = 70f;
	public static float SfxVolume { get; set; } = 90f;
	public static float VoiceVolume { get; set; } = 60f;

	public static float ScrollSpeed { get; set; } = 50f;
	public static float MouseSens { get; set; } = 40f;
	public static float HudScale { get; set; } = 100f; // 100% default
	public static string ShowHealthBars { get; set; } = "damaged";
	public static string Language { get; set; } = "en";
	public static bool DisplayFps { get; set; } = false;
	public static bool RecordReplays { get; set; } = false;
	public static bool SeedMapFiles { get; set; } = true;
	public static bool DisableShadows { get; set; } = false;
	public static bool DisableDayNightLighting { get; set; } = false;

	public static void ResetToDefaults()
	{
		ResolutionIdx = 0;
		QualityIdx = AutoDetectQuality();
		DownsamplingIdx = GetDownsamplingIdxForQuality(QualityIdx);
		WindowModeIdx = 2;
		VsyncIdx = 0;
		DisableShadows = false;
		DisableDayNightLighting = false;
		MasterVolume = 80f;
		MusicVolume = 70f;
		SfxVolume = 90f;
		VoiceVolume = 60f;
		ScrollSpeed = 50f;
		MouseSens = 40f;
		HudScale = 100f;
		ShowHealthBars = "damaged";
		Language = "en";
		DisplayFps = false;
		RecordReplays = false;
		SeedMapFiles = true;
	}

	public static int GetDownsamplingIdxForQuality(int qualityIdx)
	{
		return qualityIdx switch
		{
			0 => 3,
			1 => 2,
			2 => 0,
			3 => 0,
			_ => 0
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
			var data = JsonSerializer.Deserialize<SettingsData>(json);
			if (data != null)
			{
				ResolutionIdx = data.ResolutionIdx;
				QualityIdx = data.QualityIdx;
				DownsamplingIdx = data.DownsamplingIdx;
				WindowModeIdx = data.WindowModeIdx;
				VsyncIdx = data.VsyncIdx;
				MasterVolume = data.MasterVolume;
				MusicVolume = data.MusicVolume;
				SfxVolume = data.SfxVolume;
				VoiceVolume = data.VoiceVolume;
				ScrollSpeed = data.ScrollSpeed;
				MouseSens = data.MouseSens;
				HudScale = data.HudScale;
				Language = data.Language ?? "en";
				DisplayFps = data.DisplayFps ?? true;
				RecordReplays = data.RecordReplays ?? false;
				SeedMapFiles = data.SeedMapFiles ?? true;
				DisableShadows = data.DisableShadows ?? false;
				DisableDayNightLighting = data.DisableDayNightLighting ?? false;
				if (data.ShowHealthBars is JsonElement elem)
				{
					if (elem.ValueKind == JsonValueKind.True || elem.ValueKind == JsonValueKind.False)
					{
						ShowHealthBars = elem.GetBoolean() ? "damaged" : "hidden";
					}
					else if (elem.ValueKind == JsonValueKind.String)
					{
						ShowHealthBars = elem.GetString() ?? "damaged";
					}
					else
					{
						ShowHealthBars = "damaged";
					}
				}
				else
				{
					ShowHealthBars = "damaged";
				}
			}
		}
		catch (System.Exception e)
		{
			GD.PrintErr($"Failed to deserialize settings: {e.Message}");
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
			VsyncIdx = VsyncIdx,
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

		string json = JsonSerializer.Serialize(data);
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
				case 0:
					viewport.Msaa3D = Viewport.Msaa.Disabled;
					viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled;
					viewport.UseTaa = false;
					viewport.PositionalShadowAtlasSize = 1024;
					break;
				case 1:
					viewport.Msaa3D = Viewport.Msaa.Disabled;
					viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Fxaa;
					viewport.UseTaa = false;
					viewport.PositionalShadowAtlasSize = 2048;
					break;
				case 2:
					viewport.Msaa3D = Viewport.Msaa.Msaa2X;
					viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Fxaa;
					viewport.UseTaa = false;
					viewport.PositionalShadowAtlasSize = 4096;
					break;
				case 3:
					viewport.Msaa3D = Viewport.Msaa.Msaa4X;
					viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Fxaa;
					viewport.UseTaa = true;
					viewport.PositionalShadowAtlasSize = 4096;
					break;
			}

			switch (DownsamplingIdx)
			{
				case 0:
					viewport.Scaling3DMode = Viewport.Scaling3DModeEnum.Bilinear;
					viewport.Scaling3DScale = 1.0f;
					break;
				case 1:
					viewport.Scaling3DMode = Viewport.Scaling3DModeEnum.Fsr;
					viewport.Scaling3DScale = 0.85f;
					break;
				case 2:
					viewport.Scaling3DMode = Viewport.Scaling3DModeEnum.Fsr;
					viewport.Scaling3DScale = 0.75f;
					break;
				case 3:
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
			terrain.ApplyQualitySettings(QualityIdx);
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

	public static void ApplyEnvironmentQuality(Godot.Environment env, int qualityIdx = -1)
	{
		if (env == null || !GodotObject.IsInstanceValid(env)) return;

		int quality = qualityIdx >= 0 ? qualityIdx : QualityIdx;

		if (quality == 0)
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
			env.SsilEnabled = quality >= 2;
			env.SsrEnabled = quality == 3;
			env.SdfgiEnabled = quality == 3;
			env.FogEnabled = true;
			env.GlowEnabled = true;
		}
	}

	public static void ApplyDirectionalLightQuality(DirectionalLight3D light, int qualityIdx = -1)
	{
		if (light == null || !GodotObject.IsInstanceValid(light)) return;

		int quality = qualityIdx >= 0 ? qualityIdx : QualityIdx;

		if (DisableShadows || quality == 0)
		{
			light.ShadowEnabled = false;
		}
		else if (quality == 1)
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
		public int ResolutionIdx { get; set; }
		public int QualityIdx { get; set; }
		public int DownsamplingIdx { get; set; }
		public int WindowModeIdx { get; set; }
		public int VsyncIdx { get; set; }
		public float MasterVolume { get; set; }
		public float MusicVolume { get; set; }
		public float SfxVolume { get; set; }
		public float VoiceVolume { get; set; }
		public float ScrollSpeed { get; set; }
		public float MouseSens { get; set; }
		public float HudScale { get; set; }
		public object ShowHealthBars { get; set; }
		public string Language { get; set; }
		public bool? DisplayFps { get; set; }
		public bool? RecordReplays { get; set; }
		public bool? SeedMapFiles { get; set; }
		public bool? DisableShadows { get; set; }
		public bool? DisableDayNightLighting { get; set; }
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

	public static int AutoDetectQuality()
	{
		float vramGb = GetGpuVramGb();
		if (vramGb <= 3.0f)
		{
			return 0;
		}
		if (vramGb <= 6.0f)
		{
			return 1;
		}
		return 2;
	}
}
