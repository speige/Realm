using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Realm.Shared.Metadata;

namespace Realm.Shared.Distribution;

public class MapManifest
{
    private Dictionary<string, string> _files = new(StringComparer.OrdinalIgnoreCase);

    public string MapName { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public string Description { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();

    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public JsonObject? Assets { get; set; }

    [JsonIgnore]
    public Dictionary<string, string> Files
    {
        get
        {
            EnsureFilesFromAssets();
            return _files;
        }
        set
        {
            _files = value ?? new(StringComparer.OrdinalIgnoreCase);
        }
    }

    [JsonIgnore]
    public Dictionary<string, long>? FileSizes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    private void EnsureFilesFromAssets()
    {
        if (Assets != null && _files.Count == 0)
        {
            FlattenAssetsInto(_files, Assets);
        }
    }

    public static void FlattenAssetsInto(Dictionary<string, string> destinationFiles, JsonObject assets)
    {
        foreach (var categoryKeyValuePair in assets)
        {
            string category = categoryKeyValuePair.Key.ToLowerInvariant();
            if (category == "glb" && categoryKeyValuePair.Value is JsonObject glbObject)
            {
                foreach (var subCategoryKeyValuePair in glbObject)
                {
                    string subCategory = subCategoryKeyValuePair.Key.ToLowerInvariant();
                    if (subCategoryKeyValuePair.Value is JsonObject subCategoryObject)
                    {
                        foreach (var itemKeyValuePair in subCategoryObject)
                        {
                            string hash = ExtractHashFromNode(itemKeyValuePair.Value);
                            if (!string.IsNullOrEmpty(hash))
                            {
                                string assetKey = hash.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) ? hash : $"{hash}.glb";
                                string relativePath = $"Assets/models/{subCategory}/{itemKeyValuePair.Key}".Replace('\\', '/');
                                destinationFiles[relativePath] = assetKey;
                            }
                        }
                    }
                }
            }
            else if (categoryKeyValuePair.Value is JsonObject categoryObject)
            {
                string subFolder = category switch
                {
                    "vfx" or "vfx_spritesheets" => "vfx",
                    "animations" => "animations",
                    "sfx" => "audio/sfx",
                    "music" => "audio/music",
                    "icons" => "icons",
                    "decals" => "decals",
                    "ribbons" or "ribbon_textures" => "ribbons",
                    "noise" or "noise_textures" => "noise",
                    "skyboxes" => "skyboxes",
                    "textures" => "textures",
                    _ => category
                };

                foreach (var itemKeyValuePair in categoryObject)
                {
                    string fileName = itemKeyValuePair.Key;
                    string extension = Path.GetExtension(fileName).ToLowerInvariant();
                    string hash = ExtractHashFromNode(itemKeyValuePair.Value);
                    if (!string.IsNullOrEmpty(hash))
                    {
                        string assetKey = (!string.IsNullOrEmpty(extension) && hash.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
                            ? hash
                            : $"{hash}{extension}";
                        string relativePath = $"Assets/{subFolder}/{fileName}".Replace('\\', '/');
                        destinationFiles[relativePath] = assetKey;
                    }
                }
            }
        }
    }

    private static string ExtractHashFromNode(JsonNode? node)
    {
        if (node is JsonValue value)
        {
            return value.ToString();
        }
        if (node is JsonObject jsonObject && jsonObject.TryGetPropertyValue("hash", out var hashNode) && hashNode != null)
        {
            return hashNode.ToString();
        }
        return string.Empty;
    }

    public static Dictionary<string, string> FlattenAssetsToFiles(JsonObject? assets)
    {
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (assets != null)
        {
            FlattenAssetsInto(files, assets);
        }
        return files;
    }

    public static JsonObject UnflattenFilesToAssets(IDictionary<string, string> files)
    {
        var assets = new JsonObject();
        foreach (var keyValuePair in files)
        {
            string relativePath = keyValuePair.Key.Replace('\\', '/');
            if (relativePath.StartsWith("res://", StringComparison.OrdinalIgnoreCase))
            {
                relativePath = relativePath.Substring(6);
            }

            string hash = keyValuePair.Value;
            string fileName = Path.GetFileName(relativePath);
            string extension = Path.GetExtension(fileName).ToLowerInvariant();
            if (!string.IsNullOrEmpty(extension) && hash.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
            {
                hash = hash.Substring(0, hash.Length - extension.Length);
            }

            if (relativePath.StartsWith("Assets/models/", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = relativePath.Split('/');
                string subCategory = parts.Length >= 4 ? parts[2].ToLowerInvariant() : "props";
                if (!assets.ContainsKey("glb") || assets["glb"] is not JsonObject)
                {
                    assets["glb"] = new JsonObject();
                }
                var glbObject = assets["glb"]!.AsObject();
                if (!glbObject.ContainsKey(subCategory) || glbObject[subCategory] is not JsonObject)
                {
                    glbObject[subCategory] = new JsonObject();
                }
                glbObject[subCategory]!.AsObject()[fileName] = hash;
            }
            else if (relativePath.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                string[] parts = relativePath.Split('/');
                string folder = parts.Length >= 3 ? parts[1].ToLowerInvariant() : "textures";
                string category = folder switch
                {
                    "vfx" => "vfx_spritesheets",
                    "animations" => "animations",
                    "decals" => "decals",
                    "icons" => "icons",
                    "ribbons" => "ribbons",
                    "noise" => "noise_textures",
                    "skyboxes" => "skyboxes",
                    "audio" when parts.Length >= 4 && parts[2].Equals("music", StringComparison.OrdinalIgnoreCase) => "music",
                    "audio" when parts.Length >= 4 && parts[2].Equals("sfx", StringComparison.OrdinalIgnoreCase) => "sfx",
                    _ => "textures"
                };

                if (!assets.ContainsKey(category) || assets[category] is not JsonObject)
                {
                    assets[category] = new JsonObject();
                }
                assets[category]!.AsObject()[fileName] = hash;
            }
        }
        return assets;
    }

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

        string manifestJsonPath = Path.Combine(fullDirectoryPath, "manifest.json");
        if (File.Exists(manifestJsonPath))
        {
            try
            {
                var existing = LoadFromFile(manifestJsonPath);
                if (existing != null)
                {
                    if (string.IsNullOrEmpty(manifest.MapName) && !string.IsNullOrEmpty(existing.MapName))
                    {
                        manifest.MapName = existing.MapName;
                    }
                    if (string.IsNullOrEmpty(manifest.Author) && !string.IsNullOrEmpty(existing.Author))
                    {
                        manifest.Author = existing.Author;
                    }
                    if ((string.IsNullOrEmpty(manifest.Version) || manifest.Version == "1.0.0") && !string.IsNullOrEmpty(existing.Version))
                    {
                        manifest.Version = existing.Version;
                    }
                    if (string.IsNullOrEmpty(manifest.Description) && !string.IsNullOrEmpty(existing.Description))
                    {
                        manifest.Description = existing.Description;
                    }
                    if ((manifest.Tags == null || manifest.Tags.Count == 0) && existing.Tags != null && existing.Tags.Count > 0)
                    {
                        manifest.Tags = new List<string>(existing.Tags);
                    }
                    if (existing.Assets != null)
                    {
                        manifest.Assets = existing.Assets.DeepClone() as JsonObject;
                    }
                }
            }
            catch
            {
            }
        }

        if (string.IsNullOrEmpty(manifest.MapName))
        {
            manifest.MapName = Path.GetFileName(fullDirectoryPath);
        }
        {
            manifest.Version = "1.0.0";
        }
        if (manifest.Tags == null)
        {
            manifest.Tags = new List<string>();
        }

        if (manifest.Assets == null && manifest.Files.Count > 0)
        {
            manifest.Assets = UnflattenFilesToAssets(manifest.Files);
        }

        return manifest;
    }

    public string ToJson(bool writeIndented = true)
    {
        if (Assets == null && _files.Count > 0)
        {
            Assets = UnflattenFilesToAssets(_files);
        }

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
        var manifest = JsonSerializer.Deserialize<MapManifest>(json);
        if (manifest != null && manifest.Assets != null && manifest._files.Count == 0)
        {
            manifest.EnsureFilesFromAssets();
        }
        return manifest;
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
