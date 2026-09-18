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
		string gameBuildNumber = "v0.0.0";
		
		string[] candidatePaths = new[]
		{
			$"{basePath}/{mapFolder}/metadata.json",
			$"user://maps/{mapFolder}/metadata.json",
			$"res://Maps/{mapFolder}/metadata.json",
			$"{basePath}/{mapFolder}/map.json",
			$"user://maps/{mapFolder}/map.json",
			$"res://Maps/{mapFolder}/map.json"
		};

		foreach (var path in candidatePaths)
		{
			if (FileAccess.FileExists(path))
			{
				using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
				if (file != null)
				{
					try
					{
						string jsonText = file.GetAsText();
						using var jsonDoc = JsonDocument.Parse(jsonText);
						var root = jsonDoc.RootElement;

						if (root.TryGetProperty("GameBuildNumber", out var gbnProp) && gbnProp.ValueKind == JsonValueKind.String)
						{
							string? gbn = gbnProp.GetString();
							if (!string.IsNullOrWhiteSpace(gbn))
							{
								gameBuildNumber = gbn.Trim();
							}
						}

						if (root.TryGetProperty("MapProperties", out var mapProps))
						{
							if (mapProps.TryGetProperty("MapName", out var nameProp) && nameProp.ValueKind == JsonValueKind.String)
							{
								string? nameVal = nameProp.GetString();
								if (!string.IsNullOrWhiteSpace(nameVal))
								{
									displayName = nameVal;
								}
							}
							if (mapProps.TryGetProperty("MapDescription", out var descProp) && descProp.ValueKind == JsonValueKind.String)
							{
								description = descProp.GetString() ?? "";
							}
						}
						break;
					}
					catch
					{
					}
				}
			}
		}

		return new MapBriefingDetails
		{
			PathName = mapFolder,
			DisplayName = displayName,
			Description = description,
			GameBuildNumber = gameBuildNumber
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
