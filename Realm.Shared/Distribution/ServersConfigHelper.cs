using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Realm.Shared.Distribution;

public class ServerEntry
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string Region { get; set; } = string.Empty;
}

public class ServersConfig
{
    public List<string> AdminPublicKeys { get; set; } = new();
    public List<string> RegistryServers { get; set; } = new();
    public List<ServerEntry> Servers { get; set; } = new();
}

public static class ServersConfigHelper
{
    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };

    public static ServersConfig Load(string? explicitPath = null)
    {
        var candidates = new List<string>();

        if (!string.IsNullOrWhiteSpace(explicitPath))
        {
            candidates.Add(explicitPath);
        }

        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string currentDir = Directory.GetCurrentDirectory();

        candidates.Add(Path.Combine(currentDir, "servers.json"));
        candidates.Add(Path.Combine(baseDir, "servers.json"));
        candidates.Add(Path.Combine(currentDir, "Realm.Godot", "servers.json"));
        candidates.Add(Path.Combine(currentDir, "..", "Realm.Godot", "servers.json"));
        candidates.Add(Path.Combine(baseDir, "..", "..", "..", "Realm.Godot", "servers.json"));
        candidates.Add(Path.Combine(baseDir, "..", "..", "..", "..", "Realm.Godot", "servers.json"));
        candidates.Add(Path.Combine(currentDir, "servers.template.json"));
        candidates.Add(Path.Combine(baseDir, "servers.template.json"));
        candidates.Add(Path.Combine(currentDir, "Realm.Godot", "servers.template.json"));
        candidates.Add(Path.Combine(currentDir, "..", "Realm.Godot", "servers.template.json"));
        candidates.Add(Path.Combine(baseDir, "..", "..", "..", "Realm.Godot", "servers.template.json"));
        candidates.Add(Path.Combine(baseDir, "..", "..", "..", "..", "Realm.Godot", "servers.template.json"));

        foreach (var path in candidates)
        {
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
        fallback.RegistryServers.Add("http://127.0.0.1:5000");
        fallback.Servers.Add(new ServerEntry
        {
            Id = "official_primary",
            Name = "Official Primary Cluster",
            Url = "http://127.0.0.1:5000",
            Region = "global"
        });
        return fallback;
    }

    public static void NormalizeConfig(ServersConfig config)
    {
        if (config.AdminPublicKeys == null) config.AdminPublicKeys = new();
        if (config.RegistryServers == null) config.RegistryServers = new();
        if (config.Servers == null) config.Servers = new();

        if (config.RegistryServers.Count == 0 && config.Servers.Count > 0)
        {
            config.RegistryServers = config.Servers
                .Select(s => s.Url)
                .Where(u => !string.IsNullOrWhiteSpace(u))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        if (config.Servers.Count == 0 && config.RegistryServers.Count > 0)
        {
            int index = 1;
            foreach (var url in config.RegistryServers)
            {
                config.Servers.Add(new ServerEntry
                {
                    Id = $"server_{index}",
                    Name = $"Server {index}",
                    Url = url,
                    Region = "global"
                });
                index++;
            }
        }

        if (config.RegistryServers.Count == 0)
        {
            config.RegistryServers.Add("http://127.0.0.1:5000");
        }
    }
}
