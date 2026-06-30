using Godot;
using System;
using System.Collections.Generic;
using System.Text.Json;

public static class MapInfoHelper
{
	public static List<MapBriefingDetails> GetAvailableMaps()
	{
		var maps = new List<MapBriefingDetails>();
		using var dir = DirAccess.Open("res://Maps");
		if (dir != null)
		{
			dir.ListDirBegin();
			string dirName = dir.GetNext();
			while (dirName != "")
			{
				if (dir.CurrentIsDir() && !dirName.StartsWith("."))
				{
					maps.Add(LoadMapDetails(dirName));
				}
				dirName = dir.GetNext();
			}
			dir.ListDirEnd();
		}
		
		if (maps.Count == 0)
		{
			foreach (var name in new[] { "green_td", "melee", "legion_td" })
			{
				maps.Add(LoadMapDetails(name));
			}
		}
		
		maps.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
		return maps;
	}

	public static MapBriefingDetails LoadMapDetails(string mapFolder)
	{
		string displayName = FormatMapDisplayName(mapFolder);
		string description = "Map info - Situate in [color=#ff5555]" + displayName + "[/color], a treacherous valley once controlled by ancient lords. Guard the gates and gather resources to secure the valley. Supports melee conflict and co-op campaign modes. Defeat enemy bases to win.";
		
		string path = $"res://Maps/{mapFolder}/map.json";
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
							description = descProp.GetString();
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
