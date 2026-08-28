using System;
using Godot;
using System.Collections.Generic;
using System.Text.Json;

public static class LocalizationManager
{
	private static readonly string[] Locales = { "en", "es", "fr", "de", "pt", "ru", "zh", "ja", "ar", "hi" };

	public static string CurrentMapName { get; set; } = "";

	public static void SetupTranslations()
	{
		foreach (var locale in Locales)
		{
			var mergedDict = new Dictionary<string, string>();

			string basePath = $"res://locale/{locale}.json";
			if (FileAccess.FileExists(basePath))
			{
				using var file = FileAccess.Open(basePath, FileAccess.ModeFlags.Read);
				if (file != null)
				{
					string content = file.GetAsText();
					try
					{
						var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(content);
						if (dict != null)
						{
							foreach (var kvp in dict)
								mergedDict[kvp.Key] = kvp.Value;
						}
					}
					catch (System.Exception e)
					{
						GD.PrintErr($"Failed to load base translation for {locale}: {e.Message}");
					}
				}
			}

			if (!string.IsNullOrEmpty(CurrentMapName))
			{
				string mapPath = $"res://Maps/{CurrentMapName}/locale/{locale}.json";
				if (FileAccess.FileExists(mapPath))
				{
					using var file = FileAccess.Open(mapPath, FileAccess.ModeFlags.Read);
					if (file != null)
					{
						string content = file.GetAsText();
						try
						{
							var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(content);
							if (dict != null)
							{
								foreach (var kvp in dict)
									mergedDict[kvp.Key] = kvp.Value;
							}
						}
						catch (System.Exception e)
						{
							GD.PrintErr($"Failed to load map translation for {locale} in {CurrentMapName}: {e.Message}");
						}
					}
				}
			}

			if (mergedDict.Count > 0)
			{
				var translation = new Translation();
				translation.Locale = locale;
				foreach (var kvp in mergedDict)
				{
					translation.AddMessage(kvp.Key, kvp.Value);
				}
				TranslationServer.AddTranslation(translation);
			}
		}

		UpdateLocale(GameSettings.Language);
	}

	public static event Action<GameLanguage> LanguageChanged;

	public static void UpdateLocale(GameLanguage language)
	{
		UpdateLocale(language.ToLocaleCode());
		LanguageChanged?.Invoke(language);
	}

	public static void UpdateLocale(string locale)
	{
		TranslationServer.SetLocale(locale);
		if (UIManager.Instance != null)
		{
			UIManager.Instance.LayoutDirection = IsLocaleRtl(locale)
				? Control.LayoutDirectionEnum.Rtl
				: Control.LayoutDirectionEnum.Ltr;
		}
	}

	public static Dictionary<string, string> GetDictionary(string locale)
	{
		var dict = new Dictionary<string, string>();
		string basePath = $"res://locale/{locale}.json";
		if (FileAccess.FileExists(basePath))
		{
			using var file = FileAccess.Open(basePath, FileAccess.ModeFlags.Read);
			if (file != null)
			{
				try
				{
					var parsed = JsonSerializer.Deserialize<Dictionary<string, string>>(file.GetAsText());
					if (parsed != null)
					{
						foreach (var kvp in parsed) dict[kvp.Key] = kvp.Value;
					}
				}
				catch { }
			}
		}
		if (dict.Count == 0 && locale != "en")
		{
			return GetDictionary("en");
		}
		return dict;
	}

	public static bool IsLocaleRtl(string locale)
	{
		return locale == "ar";
	}
}
