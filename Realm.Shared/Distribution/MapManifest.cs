using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Realm.Shared.Metadata;

namespace Realm.Shared.Distribution;

public class MapManifest
{
    public string MapName { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public string Description { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();

    public Dictionary<string, string> Files { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, long>? FileSizes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static MapManifest CreateFromDirectory(
        string directoryPath,
        string mapName,
        string author,
        string version = "1.0.0",
        string description = "",
        List<string>? tags = null)
    {
        var manifest = new MapManifest
        {
            MapName = mapName,
            Author = author,
            Version = version,
            Description = description,
            Tags = tags ?? new List<string>()
        };

        if (!Directory.Exists(directoryPath))
        {
            return manifest;
        }

        string fullDirectoryPath = Path.GetFullPath(directoryPath);
        string[] allFiles = Directory.GetFiles(fullDirectoryPath, "*.*", SearchOption.AllDirectories);

        foreach (string filePath in allFiles)
        {
            string relativePath = Path.GetRelativePath(fullDirectoryPath, filePath).Replace('\\', '/');

            if (relativePath.StartsWith("bin/", StringComparison.OrdinalIgnoreCase) ||
                relativePath.StartsWith("obj/", StringComparison.OrdinalIgnoreCase) ||
                relativePath.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) ||
                relativePath.StartsWith(".vscode/", StringComparison.OrdinalIgnoreCase) ||
                relativePath.StartsWith(".godot/", StringComparison.OrdinalIgnoreCase) ||
                relativePath.StartsWith(".sidecarcache/", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(relativePath, "manifest.json", StringComparison.OrdinalIgnoreCase) ||
                relativePath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            byte[] fileBytes = File.ReadAllBytes(filePath);
            string extension = Path.GetExtension(filePath).ToLowerInvariant();
            string canonicalBlake3 = RealmMetadataHelper.ComputeBlake3(fileBytes, extension);
            string assetKey = string.IsNullOrEmpty(extension) ? canonicalBlake3 : $"{canonicalBlake3}{extension}";

            manifest.Files[relativePath] = assetKey;
            manifest.FileSizes![relativePath] = fileBytes.Length;
        }

        return manifest;
    }

    public string ToJson(bool writeIndented = true)
    {
        var options = new JsonSerializerOptions
        {
            WriteIndented = writeIndented
        };
        return JsonSerializer.Serialize(this, options);
    }

    public void SaveToFile(string filePath)
    {
        string? directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(filePath, ToJson());
    }

    public static MapManifest? LoadFromJson(string json)
    {
        return JsonSerializer.Deserialize<MapManifest>(json);
    }

    public static MapManifest? LoadFromFile(string filePath)
    {
        if (!File.Exists(filePath))
        {
            return null;
        }

        string json = File.ReadAllText(filePath);
        return LoadFromJson(json);
    }
}
