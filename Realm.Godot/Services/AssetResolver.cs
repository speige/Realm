using Godot;
using System.IO;

public static class AssetResolver
{
    private static readonly (string oldPattern, string newPattern)[] _modelMappings =
    {
        ("3d/Buildings/{0}", "models/building/{0}"),
        ("3d/Characters/{0}", "models/character/{0}"),
        ("3d/Props/{0}", "models/props/{0}"),
        ("3d/Environment/{0}", "models/environment/{0}"),
    };

    public static string ResolveModel(string category, string fileName)
    {
        foreach (var (oldPat, newPat) in _modelMappings)
        {
            var resolvedOld = $"res://Assets/{string.Format(oldPat, fileName)}";
            if (ResourceLoader.Exists(resolvedOld))
                return resolvedOld;

            var resolvedNew = $"res://MapTemplate/Assets/{string.Format(newPat, fileName)}";
            if (ResourceLoader.Exists(resolvedNew))
                return resolvedNew;
        }
        return $"res://Assets/3d/{category}/{fileName}";
    }

    public static bool ModelFileExists(string category, string fileName)
    {
        var path = ResolveModel(category, fileName);
        return ResourceLoader.Exists(path);
    }

    public static string ResolveSkybox(string fileName)
    {
        var path = $"res://Assets/skyboxes/{fileName}";
        if (ResourceLoader.Exists(path)) return path;

        path = $"res://MapTemplate/Assets/skyboxes/{fileName}";
        if (ResourceLoader.Exists(path)) return path;

        return $"res://Assets/skyboxes/{fileName}";
    }

    public static string ResolveSkyboxRelative(string relativePath)
    {
        if (relativePath.StartsWith("res://"))
            return ResolveSkybox(System.IO.Path.GetFileName(relativePath));

        if (relativePath.Contains("/") || relativePath.Contains("\\"))
            relativePath = System.IO.Path.GetFileName(relativePath);

        return ResolveSkybox(relativePath);
    }

    public static string GlobalizeTexturePath(string mapDir, string name)
    {
        var paths = new[]
        {
            System.IO.Path.Combine(mapDir, "Assets", "textures", name + ".ktx2"),
            System.IO.Path.Combine(mapDir, name + ".ktx2"),
            ProjectSettings.GlobalizePath($"res://MapTemplate/Assets/textures/{name}.ktx2"),
            ProjectSettings.GlobalizePath($"res://Assets/2d/TileSheets/{name}.ktx2"),
            ProjectSettings.GlobalizePath($"res://MapTemplate/Assets/textures/{name}.png"),
            ProjectSettings.GlobalizePath($"res://Assets/2d/TileSheets/{name}.png"),
        };
        foreach (var p in paths)
            if (System.IO.File.Exists(p)) return p;
        return paths[0];
    }
}
