using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

public static class MapInfoHelper
{
	public static List<MapBriefingDetails> GetAvailableMaps()
	{
		var maps = new List<MapBriefingDetails>();
		var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		void ScanDir(string basePath)
		{
			using var dir = DirAccess.Open(basePath);
			if (dir != null)
			{
				dir.ListDirBegin();
				string dirName = dir.GetNext();
				while (dirName != "")
				{
					if (dir.CurrentIsDir() && !dirName.StartsWith(".") && seen.Add(dirName))
					{
						maps.Add(LoadMapDetails(dirName, basePath));
					}
					dirName = dir.GetNext();
				}
				dir.ListDirEnd();
			}
		}

		ScanDir("res://Maps");
		ScanDir("user://maps");
		
		maps.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
		return maps;
	}

	public static MapBriefingDetails LoadMapDetails(string mapFolder, string basePath = "res://Maps")
	{
		string displayName = FormatMapDisplayName(mapFolder);
		string description = "";
		
		string path = $"{basePath}/{mapFolder}/map.json";
		if (!FileAccess.FileExists(path))
		{
			path = $"user://maps/{mapFolder}/map.json";
		}
		if (!FileAccess.FileExists(path))
		{
			path = $"res://Maps/{mapFolder}/map.json";
		}
		if (FileAccess.FileExists(path))
		{
			using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
			if (file != null)
			{
				try
				{
					string jsonText = file.GetAsText();
					using var jsonDoc = JsonDocument.Parse(jsonText);
					if (jsonDoc.RootElement.TryGetProperty("MapProperties", out var mapProps))
					{
						if (mapProps.TryGetProperty("MapName", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
						{
							displayName = nameProp.GetString();
						}
						if (mapProps.TryGetProperty("MapDescription", out var descProp) && descProp.ValueKind == JsonValueKind.String)
						{
							description = descProp.GetString() ?? "";
						}
					}
				}
				catch
				{
				}
			}
		}

		return new MapBriefingDetails
		{
			PathName = mapFolder,
			DisplayName = displayName,
			Description = description
		};
	}

	private static string FormatMapDisplayName(string rawName)
	{
		if (string.IsNullOrEmpty(rawName))
		{
			return "";
		}
		string formatted = rawName.Replace('_', ' ');
		string[] words = formatted.Split(' ', StringSplitOptions.RemoveEmptyEntries);
		for (int i = 0; i < words.Length; i++)
		{
			if (words[i].Equals("td", StringComparison.OrdinalIgnoreCase))
			{
				words[i] = "TD";
			}
			else if (words[i].Length > 0)
			{
				words[i] = char.ToUpper(words[i][0]) + words[i].Substring(1).ToLower();
			}
		}
		return string.Join(" ", words);
	}
}
