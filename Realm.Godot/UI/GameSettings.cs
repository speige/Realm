using Godot;
using System.Collections.Generic;
using System.Text.Json;

public static class GameSettings
{
	private const string SettingsPath = "user://settings.json";

	public static int ResolutionIdx { get; set; } = 0;
	public static List<Vector2I> Resolutions { get; private set; }
	public static int QualityIdx { get; set; } = 2; // High
	public static int WindowModeIdx { get; set; } = 1; // Windowed by default
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
	public static bool DisplayFps { get; set; } = true;
	public static bool RecordReplays { get; set; } = false;
	public static bool SeedMapFiles { get; set; } = true;

	public static void ResetToDefaults()
	{
		ResolutionIdx = 0;
		QualityIdx = 2;
		WindowModeIdx = 1;
		VsyncIdx = 0;
		MasterVolume = 80f;
		MusicVolume = 70f;
		SfxVolume = 90f;
		VoiceVolume = 60f;
		ScrollSpeed = 50f;
		MouseSens = 40f;
		HudScale = 100f;
		ShowHealthBars = "damaged";
		Language = "en";
		DisplayFps = true;
		RecordReplays = false;
		SeedMapFiles = true;
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
			SeedMapFiles = SeedMapFiles
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
					break;
				case 1:
					viewport.Msaa3D = Viewport.Msaa.Disabled;
					viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Fxaa;
					viewport.UseTaa = false;
					break;
				case 2:
					viewport.Msaa3D = Viewport.Msaa.Msaa2X;
					viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Fxaa;
					viewport.UseTaa = false;
					break;
				case 3:
					viewport.Msaa3D = Viewport.Msaa.Msaa4X;
					viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Fxaa;
					viewport.UseTaa = true;
					break;
			}
		}

		WorldEnvironment worldEnv = null;
		DirectionalLight3D light = null;

		var tree = contextNode.GetTree();
		if (tree != null && GodotObject.IsInstanceValid(tree))
		{
			var root = tree.Root;
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

			switch (QualityIdx)
			{
				case 0:
					env.SsaoEnabled = false;
					env.SsilEnabled = false;
					env.SsrEnabled = false;
					env.SdfgiEnabled = false;
					env.GlowEnabled = false;
					break;
				case 1:
					env.SsaoEnabled = true;
					env.SsilEnabled = false;
					env.SsrEnabled = false;
					env.SdfgiEnabled = false;
					env.GlowEnabled = true;
					break;
				case 2:
					env.SsaoEnabled = true;
					env.SsilEnabled = true;
					env.SsrEnabled = false;
					env.SdfgiEnabled = false;
					env.GlowEnabled = true;
					break;
				case 3:
					env.SsaoEnabled = true;
					env.SsilEnabled = true;
					env.SsrEnabled = true;
					env.SdfgiEnabled = true;
					env.GlowEnabled = true;
					break;
			}
		}

		if (light != null && GodotObject.IsInstanceValid(light))
		{
			switch (QualityIdx)
			{
				case 0:
					light.ShadowEnabled = false;
					break;
				case 1:
					light.ShadowEnabled = true;
					light.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Orthogonal;
					break;
				case 2:
					light.ShadowEnabled = true;
					light.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits;
					break;
				case 3:
					light.ShadowEnabled = true;
					light.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel4Splits;
					break;
			}
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
	}
}
