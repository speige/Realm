using Godot;
using System.Collections.Generic;
using System.Text.Json;

public static class LocalizationManager
{
	private static readonly string[] Locales = { "en", "es", "fr", "de", "pt", "ru", "zh", "ja", "ar", "hi" };

	public static void SetupTranslations()
	{
		foreach (var locale in Locales)
		{
			string path = $"res://locale/{locale}.json";
			if (!FileAccess.FileExists(path))
			{
				continue;
			}

			using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
			if (file == null)
			{
				continue;
			}

			string content = file.GetAsText();
			try
			{
				var dict = JsonSerializer.Deserialize<Dictionary<string, string>>(content);
				if (dict != null)
				{
					var translation = new Translation();
					translation.Locale = locale;
					foreach (var kvp in dict)
					{
						translation.AddMessage(kvp.Key, kvp.Value);
					}
					TranslationServer.AddTranslation(translation);
				}
			}
			catch (System.Exception e)
			{
				GD.PrintErr($"Failed to load translation for {locale}: {e.Message}");
			}
		}

		UpdateLocale(GameSettings.Language);
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

	private static bool IsLocaleRtl(string locale)
	{
		return locale == "ar";
	}
}
