using Realm.Lobby.Models;
using System.Collections.Concurrent;
using System.Net.WebSockets;

namespace Realm.Lobby.Services;

public class SeederRegistry
{
    private readonly ConcurrentDictionary<string, SeederInfo> _seeders = new();
    private readonly ConcurrentDictionary<string, WebSocket> _seederConnections = new();

    public void Register(SeederInfo info)
    {
        _seeders[info.SeederId] = info;
    }

    public void Unregister(string seederId)
    {
        _seeders.TryRemove(seederId, out _);
        _seederConnections.TryRemove(seederId, out _);
    }

    public void AddConnection(string seederId, WebSocket ws)
    {
        _seederConnections[seederId] = ws;
    }

    public void RemoveConnection(string seederId)
    {
        _seederConnections.TryRemove(seederId, out _);
    }

    public List<SeederInfo> GetAll()
    {
        return _seeders.Values.ToList();
    }

    public List<SeederInfo> GetSeedersForMap(string mapId)
    {
        return _seeders.Values.Where(s => s.MapIds.Contains(mapId, StringComparer.OrdinalIgnoreCase)).ToList();
    }

    public WebSocket? GetConnection(string seederId)
    {
        _seederConnections.TryGetValue(seederId, out var ws);
        return ws;
    }
}
