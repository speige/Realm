using Godot;
using System.Text.Json;

public static class GameSettings
{
	private const string SettingsPath = "user://settings.json";

	public static int ResolutionIdx { get; set; } = 0;
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
	}

	static GameSettings()
	{
		Load();
	}

	public static void Load()
	{
		if (!FileAccess.FileExists(SettingsPath))
		{
			// Default values already set
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
			DisplayFps = DisplayFps
		};

		string json = JsonSerializer.Serialize(data);
		using var file = FileAccess.Open(SettingsPath, FileAccess.ModeFlags.Write);
		if (file != null)
		{
			file.StoreString(json);
		}
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
	}
}
