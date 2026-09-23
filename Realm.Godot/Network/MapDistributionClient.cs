using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Realm.Shared.Distribution;

public partial class EnetEphemeralLobbyClientNode : Node
{
    private SceneMultiplayer _multiplayer = new();
    private ENetMultiplayerPeer _peer = new();
    private bool _connected;
    private EnetMapTransferService? _transferService;

    public async Task<bool> DownloadFromTargetAsync(string targetIp, int targetPort, string mapIdOrHash, Action<float>? progressCallback, CancellationToken token)
    {
        if (LobbyManager.Instance == null) return false;

        bool isLocal = targetIp == "127.0.0.1" || targetIp == "localhost" || targetIp == LobbyManager.Instance.PublicIP;
        if (!isLocal)
        {
            int clientLocalPort = LobbyManager.Instance.ENetPort + 16;
            await UdpHolePuncher.PunchHoleAsync(targetIp, targetPort, clientLocalPort);
        }

        if (GetParent() == null)
        {
            LobbyManager.Instance.AddChild(this);
        }

        _transferService = new EnetMapTransferService();
        _transferService.Name = $"ClientTransfer_{Guid.NewGuid():N}";
        AddChild(_transferService);

        GetTree().SetMultiplayer(_multiplayer, _transferService.GetPath());

        await Task.Delay(50, token);

        int clientPort = LobbyManager.Instance.ENetPort + 16;
        var err = _peer.CreateClient(targetIp, targetPort, localPort: isLocal ? 0 : clientPort);
        if (err != Error.Ok)
        {
            Cleanup();
            return false;
        }

        _multiplayer.ServerRelay = false;
        _multiplayer.MultiplayerPeer = _peer;

        _connected = false;
        _multiplayer.ConnectedToServer += () => _connected = true;

        int waitMs = 0;
        while (!_connected && waitMs < 8000 && !token.IsCancellationRequested)
        {
            await Task.Delay(100, token);
            waitMs += 100;
        }

        if (!_connected || token.IsCancellationRequested)
        {
            Cleanup();
            return false;
        }

        bool success = await _transferService.RequestAndDownloadMapAsync(_multiplayer, 1, mapIdOrHash, progressCallback, token);

        Cleanup();
        return success;
    }

    private void Cleanup()
    {
        try
        {
            if (_transferService != null && IsInstanceValid(_transferService) && GetTree() != null)
            {
                GetTree().SetMultiplayer(null, _transferService.GetPath());
                _transferService.QueueFree();
                _transferService = null;
            }
        }
        catch { }

        try { _peer?.Close(); } catch { }
        Callable.From(() => { try { QueueFree(); } catch { } }).CallDeferred();
    }
}

public class MapDistributionClient
{
    private readonly System.Net.Http.HttpClient _httpClient = new System.Net.Http.HttpClient();

    public event Action<float>? DownloadProgressChanged;

    public async Task<bool> DownloadMapAsync(string hostIp, int port, string mapName, Action<float>? progressCallback = null)
    {
        var node = new EnetEphemeralLobbyClientNode();
        return await node.DownloadFromTargetAsync(hostIp, port, mapName, p =>
        {
            progressCallback?.Invoke(p);
            DownloadProgressChanged?.Invoke(p);
        }, CancellationToken.None);
    }

    public async Task<bool> DownloadMapPackageFromRegistryAsync(
        string mapId,
        string registryServerUrl,
        Action<float>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        var candidateSeeders = new List<(string IP, int Port)>();
        try
        {
            string baseUrl = registryServerUrl.TrimEnd('/');
            using var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/seeders/download");
            var payload = new { MapId = mapId };
            requestMessage.Content = new StringContent(JsonSerializer.Serialize(payload), System.Text.Encoding.UTF8, "application/json");

            var response = await _httpClient.SendAsync(requestMessage, cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                string json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("seeders", out var seedersElem) && seedersElem.ValueKind == JsonValueKind.Array)
                {
                    foreach (var seeder in seedersElem.EnumerateArray())
                    {
                        string ip = seeder.GetProperty("ip").GetString() ?? seeder.GetProperty("IP").GetString() ?? "";
                        int port = seeder.GetProperty("port").GetInt32();
                        if (!string.IsNullOrEmpty(ip) && port > 0)
                        {
                            candidateSeeders.Add((ip, port));
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[MapDistributionClient] Seeder query error: {ex.Message}");
        }

        var rng = new Random();
        candidateSeeders = candidateSeeders.OrderBy(_ => rng.Next()).ToList();

        foreach (var (seederIp, seederPort) in candidateSeeders)
        {
            if (cancellationToken.IsCancellationRequested) break;

            var node = new EnetEphemeralLobbyClientNode();
            bool ok = await node.DownloadFromTargetAsync(seederIp, seederPort, mapId, p =>
            {
                progressCallback?.Invoke(p);
                DownloadProgressChanged?.Invoke(p);
            }, cancellationToken);

            if (ok) return true;
        }

        List<string> officialServers = LobbyManager.Instance != null && LobbyManager.Instance.OfficialServers.Count > 0
            ? LobbyManager.Instance.OfficialServers
            : new List<string> { registryServerUrl };

        var randomizedOfficial = officialServers.OrderBy(_ => rng.Next()).ToList();
        foreach (var serverUrl in randomizedOfficial)
        {
            if (cancellationToken.IsCancellationRequested) break;

            try
            {
                Uri uri = new Uri(serverUrl);
                string host = uri.Host;
                int officialPort = LobbyManager.Instance != null ? LobbyManager.Instance.ENetPort : 8999;

                var fallbackNode = new EnetEphemeralLobbyClientNode();
                bool ok = await fallbackNode.DownloadFromTargetAsync(host, officialPort, mapId, p =>
                {
                    progressCallback?.Invoke(p);
                    DownloadProgressChanged?.Invoke(p);
                }, cancellationToken);

                if (ok) return true;
            }
            catch { }
        }

        return false;
    }
}
