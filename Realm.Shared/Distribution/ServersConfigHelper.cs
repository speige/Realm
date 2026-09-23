using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Realm.Shared.Distribution;

public class ServersConfig
{
    public List<string> AdminPublicKeys { get; set; } = new();
    public List<string> AdminPublicKey { get => AdminPublicKeys; set => AdminPublicKeys = value; }
    public List<string> Servers { get; set; } = new();
}

public static class ServersConfigHelper
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
    public const string DefaultServerUrl = "http://127.0.0.1:5000";
    public const string DefaultAdminPublicKey = "";

    public static ServersConfig Load(string? explicitPath = null)
    {
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            candidates.Add(explicitPath);
        }

        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string currentDir = Directory.GetCurrentDirectory();
        string? processDir = !string.IsNullOrEmpty(Environment.ProcessPath) ? Path.GetDirectoryName(Environment.ProcessPath) : null;
        string appContextDir = AppContext.BaseDirectory;

        var baseDirectories = new List<string> { currentDir, baseDir, appContextDir };
        if (!string.IsNullOrEmpty(processDir) && !baseDirectories.Contains(processDir))
        {
            baseDirectories.Add(processDir);
        }

        foreach (var dir in baseDirectories)
        {
            candidates.Add(Path.Combine(dir, "servers.json"));
            candidates.Add(Path.Combine(dir, "Realm.Godot", "servers.json"));
            candidates.Add(Path.Combine(dir, "..", "Realm.Godot", "servers.json"));
            candidates.Add(Path.Combine(dir, "..", "servers.json"));
            candidates.Add(Path.Combine(dir, "..", "..", "servers.json"));
            candidates.Add(Path.Combine(dir, "..", "..", "..", "servers.json"));
            candidates.Add(Path.Combine(dir, "..", "..", "..", "..", "servers.json"));
            candidates.Add(Path.Combine(dir, "..", "..", "..", "Realm.Godot", "servers.json"));
            candidates.Add(Path.Combine(dir, "..", "..", "..", "..", "Realm.Godot", "servers.json"));
        }

        foreach (var dir in baseDirectories)
        {
            candidates.Add(Path.Combine(dir, "servers.template.json"));
            candidates.Add(Path.Combine(dir, "Realm.Godot", "servers.template.json"));
            candidates.Add(Path.Combine(dir, "..", "Realm.Godot", "servers.template.json"));
            candidates.Add(Path.Combine(dir, "..", "servers.template.json"));
            candidates.Add(Path.Combine(dir, "..", "..", "servers.template.json"));
            candidates.Add(Path.Combine(dir, "..", "..", "..", "servers.template.json"));
            candidates.Add(Path.Combine(dir, "..", "..", "..", "..", "servers.template.json"));
            candidates.Add(Path.Combine(dir, "..", "..", "..", "Realm.Godot", "servers.template.json"));
            candidates.Add(Path.Combine(dir, "..", "..", "..", "..", "Realm.Godot", "servers.template.json"));
        }

        var triedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var path in candidates)
        {
            if (string.IsNullOrWhiteSpace(path) || !triedPaths.Add(path))
            {
                continue;
            }

            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    var config = JsonSerializer.Deserialize<ServersConfig>(json, Options);
                    if (config != null)
                    {
                        NormalizeConfig(config);
                        return config;
                    }
                }
            }
            catch
            {
            }
        }

        var fallback = new ServersConfig();
        fallback.Servers.Add(DefaultServerUrl);
        if (!string.IsNullOrWhiteSpace(DefaultAdminPublicKey))
        {
            fallback.AdminPublicKeys.Add(DefaultAdminPublicKey);
        }
        return fallback;
    }

    public static string GetDefaultServerUrl(string? explicitPath = null)
    {
        var config = Load(explicitPath);
        if (config.Servers.Count > 0 && !string.IsNullOrWhiteSpace(config.Servers[0]))
        {
            return config.Servers[0];
        }
        return DefaultServerUrl;
    }

    public static List<string> GetRegistryServers(string? explicitPath = null)
    {
        return Load(explicitPath).Servers
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .ToList();
    }

    public static List<string> GetAdminPublicKeys(string? explicitPath = null)
    {
        return Load(explicitPath).AdminPublicKeys
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .ToList();
    }

    public static void NormalizeConfig(ServersConfig config)
    {
        if (config.AdminPublicKeys == null) config.AdminPublicKeys = new();
        if (config.Servers == null) config.Servers = new();

        config.AdminPublicKeys = config.AdminPublicKeys
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (config.AdminPublicKeys.Count == 0 && !string.IsNullOrWhiteSpace(DefaultAdminPublicKey))
        {
            config.AdminPublicKeys.Add(DefaultAdminPublicKey);
        }

        config.Servers = config.Servers
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (config.Servers.Count == 0)
        {
            config.Servers.Add(DefaultServerUrl);
        }
    }
}
