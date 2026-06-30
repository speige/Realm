using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

public class PeerSeederManager
{
    private static PeerSeederManager? _instance;
    public static PeerSeederManager Instance => _instance ??= new PeerSeederManager();

    private UdpClient? _udpListener;
    private ClientWebSocket? _wsSignaling;
    private CancellationTokenSource? _cts;
    private bool _isSeeding;
    private string _seederId = "";

    private string _activeSeedingHash = "";
    private byte[]? _activeSeedingFileBytes;
    private string _activeSeedingMapId = "";
    private byte[]? _activeSeedingMapBytes;

    private IPEndPoint? _activeLeecherEndPoint;
    private DateTime _lastRequestTime = DateTime.MinValue;
    private readonly object _sessionLock = new();

    private TokenBucket? _tokenBucket;

    private PeerSeederManager()
    {
        _seederId = Guid.NewGuid().ToString("N");
        _tokenBucket = new TokenBucket(64 * 1024, 150 * 1024);
    }

    public void Start()
    {
        if (_isSeeding) return;
        _isSeeding = true;
        _seederId = Guid.NewGuid().ToString("N");
        _cts = new CancellationTokenSource();

        int localPort = LobbyManager.Instance.ENetPort + 15;
        try
        {
            _udpListener = new UdpClient();
            _udpListener.ExclusiveAddressUse = false;
            _udpListener.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udpListener.Client.Bind(new IPEndPoint(IPAddress.Any, localPort));
            GD.Print($"[PeerSeeder] Bound UDP listener to port {localPort}");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[PeerSeeder] Failed to bind UDP listener to port {localPort}: {ex.Message}");
            _isSeeding = false;
            return;
        }

        Task.Run(() => AcceptAndServePacketsAsync(_cts.Token));
        Task.Run(() => MaintainSignalingAndRegistrationAsync(_cts.Token));
    }

    public void Stop()
    {
        if (!_isSeeding) return;
        _isSeeding = false;
        _cts?.Cancel();

        try
        {
            _udpListener?.Close();
            _udpListener = null;
        }
        catch { }

        try
        {
            _wsSignaling?.CloseAsync(WebSocketCloseStatus.NormalClosure, "Stopped", CancellationToken.None).Wait(1000);
            _wsSignaling = null;
        }
        catch { }

        SendUnregisterRequest();
        GD.Print("[PeerSeeder] Stopped seeding.");
    }

    private List<string> GetLocalMapIds()
    {
        var mapIds = new List<string>();
        try
        {
            string dir = MapAssetManager.GlobalArchiveDirectory;
            if (Directory.Exists(dir))
            {
                var files = Directory.GetFiles(dir, "*_manifest.json");
                foreach (var file in files)
                {
                    string name = Path.GetFileName(file);
                    if (name == "downloaded_map_manifest.json") continue;
                    string mapId = name.Substring(0, name.Length - "_manifest.json".Length);
                    mapIds.Add(mapId);
                }
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[PeerSeeder] Error scanning local maps: {ex.Message}");
        }
        return mapIds;
    }

    private async Task MaintainSignalingAndRegistrationAsync(CancellationToken token)
    {
        var httpClient = new System.Net.Http.HttpClient();
        while (!token.IsCancellationRequested && _isSeeding)
        {
            try
            {
                var mapIds = GetLocalMapIds();
                int publicPort = LobbyManager.Instance.PublicPort + 15;
                string publicIp = LobbyManager.Instance.PublicIP;

                bool isBusy = false;
                lock (_sessionLock)
                {
                    if (_activeLeecherEndPoint != null && (DateTime.UtcNow - _lastRequestTime) < TimeSpan.FromSeconds(5))
                    {
                        isBusy = true;
                    }
                }

                if (!isBusy && mapIds.Count > 0)
                {
                    var payload = new
                    {
                        SeederId = _seederId,
                        ReportedIP = publicIp,
                        Port = publicPort,
                        MapIds = mapIds
                    };
                    var jsonContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                    var regUrl = $"{LobbyManager.Instance.RegistryServerUrl}/seeders/register";
                    var resp = await httpClient.PostAsync(regUrl, jsonContent, token);
                    if (resp.IsSuccessStatusCode)
                    {
                        GD.Print($"[PeerSeeder] Registered {mapIds.Count} maps with orchestrator.");
                    }
                }

                if (_wsSignaling == null || _wsSignaling.State != WebSocketState.Open)
                {
                    _wsSignaling = new ClientWebSocket();
                    var wsUri = new Uri($"{LobbyManager.Instance.RegistryServerUrl.Replace("http://", "ws://").Replace("https://", "wss://")}/seeders/ws?seederId={_seederId}");
                    GD.Print($"[PeerSeeder] Connecting signaling WebSocket to {wsUri}...");
                    await _wsSignaling.ConnectAsync(wsUri, token);
                    GD.Print("[PeerSeeder] Signaling WebSocket connected.");
                    _ = Task.Run(() => ReceiveSignalingMessagesAsync(_wsSignaling, token), token);
                }

                await Task.Delay(4000, token);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                GD.PrintErr($"[PeerSeeder] Signaling loop exception: {ex.Message}");
                await Task.Delay(5000, token);
            }
        }
    }

    private async Task ReceiveSignalingMessagesAsync(ClientWebSocket ws, CancellationToken token)
    {
        var buffer = new byte[1024 * 4];
        while (!token.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            try
            {
                var result = await ws.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
                else if (result.MessageType == WebSocketMessageType.Text)
                {
                    string msg = Encoding.UTF8.GetString(buffer, 0, result.Count);
                    using var doc = JsonDocument.Parse(msg);
                    if (doc.RootElement.TryGetProperty("Action", out var actionProp) && actionProp.GetString() == "Punch")
                    {
                        string clientIp = doc.RootElement.GetProperty("ClientIP").GetString() ?? "";
                        int clientPort = doc.RootElement.GetProperty("ClientPort").GetInt32();
                        int localPort = LobbyManager.Instance.ENetPort + 15;
                        GD.Print($"[PeerSeeder] Received punch request to client {clientIp}:{clientPort}. Launching hole punch...");
                        
                        _ = UdpHolePuncher.PunchHoleAsync(clientIp, clientPort, localPort);
                    }
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[PeerSeeder] WS Receive error: {ex.Message}");
                break;
            }
        }
    }

    private void SendUnregisterRequest()
    {
        Task.Run(async () =>
        {
            try
            {
                using var client = new System.Net.Http.HttpClient();
                var payload = new { SeederId = _seederId };
                var jsonContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                await client.PostAsync($"{LobbyManager.Instance.RegistryServerUrl}/seeders/unregister", jsonContent);
                GD.Print("[PeerSeeder] Sent unregister request to orchestrator.");
            }
            catch { }
        });
    }

    private async Task AcceptAndServePacketsAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested && _udpListener != null)
        {
            try
            {
                var result = await _udpListener.ReceiveAsync(token);
                byte[] data = result.Buffer;
                if (data.Length == 0) continue;

                var ep = result.RemoteEndPoint;

                if (data.Length >= 5 && Encoding.UTF8.GetString(data, 0, 5) == "PUNCH")
                {
                    continue;
                }

                lock (_sessionLock)
                {
                    if (_activeLeecherEndPoint == null)
                    {
                        _activeLeecherEndPoint = ep;
                        _lastRequestTime = DateTime.UtcNow;
                        GD.Print($"[PeerSeeder] Session started with leecher {ep}");
                        SendUnregisterRequest();
                    }
                    else if (!Equals(_activeLeecherEndPoint, ep))
                    {
                        if ((DateTime.UtcNow - _lastRequestTime) > TimeSpan.FromSeconds(5))
                        {
                            _activeLeecherEndPoint = ep;
                            _lastRequestTime = DateTime.UtcNow;
                            GD.Print($"[PeerSeeder] Session timed out, starting new session with leecher {ep}");
                            SendUnregisterRequest();
                        }
                        else
                        {
                            SendError(ep, "Seeder busy");
                            continue;
                        }
                    }
                    else
                    {
                        _lastRequestTime = DateTime.UtcNow;
                    }
                }

                byte msgType = data[0];
                int offset = 1;

                if (msgType == 1)
                {
                    string mapId = ReadString(data, ref offset);
                    ServeMetadataInfo(ep, mapId);
                }
                else if (msgType == 2)
                {
                    string mapId = ReadString(data, ref offset);
                    int blockOffset = ReadInt32(data, ref offset);
                    int blockSize = ReadInt32(data, ref offset);
                    ServeMetadataBlock(ep, mapId, blockOffset, blockSize);
                }
                else if (msgType == 3)
                {
                    string hash = ReadString(data, ref offset);
                    ServeFileInfo(ep, hash);
                }
                else if (msgType == 4)
                {
                    string hash = ReadString(data, ref offset);
                    int blockOffset = ReadInt32(data, ref offset);
                    int blockSize = ReadInt32(data, ref offset);
                    ServeFileBlock(ep, hash, blockOffset, blockSize);
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                if (!token.IsCancellationRequested)
                {
                    GD.PrintErr($"[PeerSeeder] Error serving packet: {ex.Message}");
                }
            }
        }
    }

    private void ServeMetadataInfo(IPEndPoint ep, string mapId)
    {
        try
        {
            if (_activeSeedingMapId != mapId || _activeSeedingMapBytes == null)
            {
                string manifestPath = Path.Combine(MapAssetManager.GlobalArchiveDirectory, $"{mapId}_manifest.json");
                if (File.Exists(manifestPath))
                {
                    _activeSeedingMapBytes = File.ReadAllBytes(manifestPath);
                    _activeSeedingMapId = mapId;
                }
                else
                {
                    SendError(ep, "Map manifest not found");
                    return;
                }
            }

            int size = _activeSeedingMapBytes.Length;
            byte[] resp = new byte[1 + 4 + Encoding.UTF8.GetByteCount(mapId) + 4];
            int offset = 0;
            resp[offset++] = 10;
            WriteString(resp, ref offset, mapId);
            WriteInt32(resp, ref offset, size);

            SendPacketThrottled(ep, resp);
        }
        catch (Exception ex)
        {
            SendError(ep, ex.Message);
        }
    }

    private void ServeMetadataBlock(IPEndPoint ep, string mapId, int blockOffset, int blockSize)
    {
        try
        {
            if (_activeSeedingMapId != mapId || _activeSeedingMapBytes == null)
            {
                string manifestPath = Path.Combine(MapAssetManager.GlobalArchiveDirectory, $"{mapId}_manifest.json");
                if (File.Exists(manifestPath))
                {
                    _activeSeedingMapBytes = File.ReadAllBytes(manifestPath);
                    _activeSeedingMapId = mapId;
                }
                else
                {
                    SendError(ep, "Map manifest not found");
                    return;
                }
            }

            if (blockOffset < 0 || blockOffset >= _activeSeedingMapBytes.Length)
            {
                SendError(ep, "Invalid block offset");
                return;
            }

            int actualSize = Math.Min(blockSize, _activeSeedingMapBytes.Length - blockOffset);
            byte[] resp = new byte[1 + 4 + Encoding.UTF8.GetByteCount(mapId) + 4 + actualSize];
            int offset = 0;
            resp[offset++] = 11;
            WriteString(resp, ref offset, mapId);
            WriteInt32(resp, ref offset, blockOffset);
            Buffer.BlockCopy(_activeSeedingMapBytes, blockOffset, resp, offset, actualSize);

            SendPacketThrottled(ep, resp);
        }
        catch (Exception ex)
        {
            SendError(ep, ex.Message);
        }
    }

    private void ServeFileInfo(IPEndPoint ep, string hash)
    {
        try
        {
            LoadFileIntoCache(hash);
            if (_activeSeedingFileBytes == null)
            {
                SendError(ep, "File hash not found in global archive");
                return;
            }

            int size = _activeSeedingFileBytes.Length;
            byte[] resp = new byte[1 + 4 + Encoding.UTF8.GetByteCount(hash) + 4];
            int offset = 0;
            resp[offset++] = 12;
            WriteString(resp, ref offset, hash);
            WriteInt32(resp, ref offset, size);

            SendPacketThrottled(ep, resp);
        }
        catch (Exception ex)
        {
            SendError(ep, ex.Message);
        }
    }

    private void ServeFileBlock(IPEndPoint ep, string hash, int blockOffset, int blockSize)
    {
        try
        {
            LoadFileIntoCache(hash);
            if (_activeSeedingFileBytes == null)
            {
                SendError(ep, "File hash not found in global archive");
                return;
            }

            if (blockOffset < 0 || blockOffset >= _activeSeedingFileBytes.Length)
            {
                SendError(ep, "Invalid block offset");
                return;
            }

            int actualSize = Math.Min(blockSize, _activeSeedingFileBytes.Length - blockOffset);
            byte[] resp = new byte[1 + 4 + Encoding.UTF8.GetByteCount(hash) + 4 + actualSize];
            int offset = 0;
            resp[offset++] = 13;
            WriteString(resp, ref offset, hash);
            WriteInt32(resp, ref offset, blockOffset);
            Buffer.BlockCopy(_activeSeedingFileBytes, blockOffset, resp, offset, actualSize);

            SendPacketThrottled(ep, resp);
        }
        catch (Exception ex)
        {
            SendError(ep, ex.Message);
        }
    }

    private void LoadFileIntoCache(string hash)
    {
        if (_activeSeedingHash == hash && _activeSeedingFileBytes != null)
        {
            return;
        }

        _activeSeedingHash = "";
        _activeSeedingFileBytes = null;

        string globalArchive = MapAssetManager.GlobalArchiveFile;
        if (!File.Exists(globalArchive)) return;

        try
        {
            using (var archive = SharpCompress.Archives.ArchiveFactory.OpenArchive(globalArchive, null))
            {
                foreach (var entry in archive.Entries)
                {
                    if (!entry.IsDirectory && entry.Key == hash)
                    {
                        using (var ms = new MemoryStream())
                        {
                            using (var entryStream = entry.OpenEntryStream())
                            {
                                entryStream.CopyTo(ms);
                            }
                            _activeSeedingFileBytes = ms.ToArray();
                            _activeSeedingHash = hash;
                        }
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[PeerSeeder] Failed to load hash {hash} from archive: {ex.Message}");
        }
    }

    private void SendError(IPEndPoint ep, string errMsg)
    {
        try
        {
            byte[] resp = new byte[1 + 4 + Encoding.UTF8.GetByteCount(errMsg)];
            int offset = 0;
            resp[offset++] = 14;
            WriteString(resp, ref offset, errMsg);
            _udpListener?.Send(resp, resp.Length, ep);
        }
        catch { }
    }

    private void SendPacketThrottled(IPEndPoint ep, byte[] resp)
    {
        _tokenBucket?.Consume(resp.Length);
        _udpListener?.Send(resp, resp.Length, ep);
    }

    public void CheckIdleAndSeedStatus()
    {
        bool loggedIn = !string.IsNullOrEmpty(LobbyManager.Instance.AuthenticatedUsername);
        bool idle = string.IsNullOrEmpty(LobbyManager.Instance.ActiveLobbyId) &&
                    (LobbyManager.Instance.Multiplayer.MultiplayerPeer == null ||
                     LobbyManager.Instance.Multiplayer.MultiplayerPeer is OfflineMultiplayerPeer);

        bool shouldSeed = GameSettings.SeedMapFiles && loggedIn && idle;

        if (shouldSeed)
        {
            if (!_isSeeding)
            {
                GD.Print("[PeerSeeder] Seeder condition met. Starting background seeding service...");
                Start();
            }
        }
        else
        {
            if (_isSeeding)
            {
                GD.Print("[PeerSeeder] Seeder condition no longer met. Stopping seeding service...");
                Stop();
            }
        }
    }

    private string ReadString(byte[] buffer, ref int offset)
    {
        int len = ReadInt32(buffer, ref offset);
        string val = Encoding.UTF8.GetString(buffer, offset, len);
        offset += len;
        return val;
    }

    private void WriteString(byte[] buffer, ref int offset, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        WriteInt32(buffer, ref offset, bytes.Length);
        Buffer.BlockCopy(bytes, 0, buffer, offset, bytes.Length);
        offset += bytes.Length;
    }

    private int ReadInt32(byte[] buffer, ref int offset)
    {
        int val = BitConverter.ToInt32(buffer, offset);
        offset += 4;
        return val;
    }

    private void WriteInt32(byte[] buffer, ref int offset, int value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        Buffer.BlockCopy(bytes, 0, buffer, offset, 4);
        offset += 4;
    }
}
