using Godot;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

public class PeerMapDownloader
{
    private readonly System.Net.Http.HttpClient _httpClient = new();
    private UdpClient? _udpClient;
    private IPEndPoint? _seederEndPoint;
    private string _currentMapId = "";
    private int _localPort;

    public event Action<float>? DownloadProgressChanged;

    public PeerMapDownloader()
    {
        _localPort = LobbyManager.Instance.ENetPort + 16;
    }

    public async Task<bool> DownloadMapAsync(string mapId)
    {
        _currentMapId = mapId;
        GD.Print($"[PeerDownloader] Initiating P2P download for map {mapId}");

        int maxPeerAttempts = 5;
        for (int attempt = 0; attempt < maxPeerAttempts; attempt++)
        {
            if (await TryDownloadWithRandomPeerAsync(mapId))
            {
                GD.Print($"[PeerDownloader] Map {mapId} downloaded successfully via P2P.");
                return true;
            }
            GD.PrintErr($"[PeerDownloader] Attempt {attempt + 1} to download {mapId} failed. Retrying with another peer...");
            await Task.Delay(1000);
        }

        GD.PrintErr($"[PeerDownloader] Failed to download {mapId} via P2P after {maxPeerAttempts} attempts.");
        return false;
    }

    private async Task<bool> TryDownloadWithRandomPeerAsync(string mapId)
    {
        try
        {
            string clientPublicIp = LobbyManager.Instance.PublicIP;
            int clientPublicPort = LobbyManager.Instance.PublicPort + 16;

            var payload = new
            {
                MapId = mapId,
                ClientPublicIP = clientPublicIp,
                ClientPublicPort = clientPublicPort
            };
            var jsonContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            var downloadUrl = $"{LobbyManager.Instance.RegistryServerUrl}/seeders/download";
            var response = await _httpClient.PostAsync(downloadUrl, jsonContent);

            if (!response.IsSuccessStatusCode)
            {
                GD.PrintErr($"[PeerDownloader] Orchestrator download request failed: {response.StatusCode}");
                return false;
            }

            var respText = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(respText);
            string seederIp = doc.RootElement.GetProperty("seederIP").GetString() ?? "";
            int seederPort = doc.RootElement.GetProperty("seederPort").GetInt32();

            GD.Print($"[PeerDownloader] Selected Seeder: {seederIp}:{seederPort}. Punching hole...");
            _seederEndPoint = new IPEndPoint(IPAddress.Parse(seederIp), seederPort);

            await UdpHolePuncher.PunchHoleAsync(seederIp, seederPort, _localPort);

            _udpClient = new UdpClient();
            _udpClient.ExclusiveAddressUse = false;
            _udpClient.Client.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _udpClient.Client.Bind(new IPEndPoint(IPAddress.Any, _localPort));
            _udpClient.Client.ReceiveTimeout = 400;

            byte[]? metadataBytes = await DownloadMetadataAsync(mapId);
            if (metadataBytes == null)
            {
                CleanupSockets();
                return false;
            }

            string metadataJson = Encoding.UTF8.GetString(metadataBytes);
            var manifest = JsonSerializer.Deserialize<MapManifest>(metadataJson);
            if (manifest == null || manifest.Files == null)
            {
                CleanupSockets();
                return false;
            }

            string localManifestDir = MapAssetManager.GlobalArchiveDirectory;
            if (!Directory.Exists(localManifestDir))
            {
                Directory.CreateDirectory(localManifestDir);
            }
            string localManifestPath = Path.Combine(localManifestDir, $"{mapId}_manifest.json");
            File.WriteAllText(localManifestPath, metadataJson);

            var missingHashes = MapAssetManager.GetMissingHashes(manifest.Files.Values);
            GD.Print($"[PeerDownloader] Manifest retrieved. Missing {missingHashes.Count} files.");

            if (missingHashes.Count > 0)
            {
                int completed = 0;
                foreach (var hash in missingHashes)
                {
                    byte[]? fileData = await DownloadFileByHashAsync(hash);
                    if (fileData == null)
                    {
                        CleanupSockets();
                        return false;
                    }

                    string computedHash = MapAssetManager.ComputeBlake3(fileData);
                    if (computedHash != hash)
                    {
                        GD.PrintErr($"[PeerDownloader] BLAKE3 verification failed for file {hash}. Computed: {computedHash}");
                        CleanupSockets();
                        return false;
                    }

                    MapAssetManager.AddOrUpdateGlobalArchive(new Dictionary<string, byte[]> { { hash, fileData } });
                    completed++;
                    float progress = (float)completed / missingHashes.Count;
                    DownloadProgressChanged?.Invoke(progress);
                }
            }
            else
            {
                DownloadProgressChanged?.Invoke(1.0f);
            }

            CleanupSockets();
            return true;
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[PeerDownloader] Download with peer exception: {ex.Message}");
            CleanupSockets();
            return false;
        }
    }

    private void CleanupSockets()
    {
        try
        {
            _udpClient?.Close();
            _udpClient = null;
        }
        catch { }
    }

    private async Task<byte[]?> DownloadMetadataAsync(string mapId)
    {
        int size = await RequestMetadataSizeAsync(mapId);
        if (size <= 0) return null;

        byte[] metadataBuffer = new byte[size];
        int blockOffset = 0;
        int blockSize = 800;

        while (blockOffset < size)
        {
            int requestSize = Math.Min(blockSize, size - blockOffset);
            byte[]? block = await RequestMetadataBlockAsync(mapId, blockOffset, requestSize);
            if (block == null) return null;

            Buffer.BlockCopy(block, 0, metadataBuffer, blockOffset, block.Length);
            blockOffset += block.Length;
        }

        return metadataBuffer;
    }

    private async Task<byte[]?> DownloadFileByHashAsync(string hash)
    {
        int size = await RequestFileSizeAsync(hash);
        if (size <= 0) return null;

        byte[] fileBuffer = new byte[size];
        int blockOffset = 0;
        int blockSize = 800;

        while (blockOffset < size)
        {
            int requestSize = Math.Min(blockSize, size - blockOffset);
            byte[]? block = await RequestFileBlockAsync(hash, blockOffset, requestSize);
            if (block == null) return null;

            Buffer.BlockCopy(block, 0, fileBuffer, blockOffset, block.Length);
            blockOffset += block.Length;
        }

        return fileBuffer;
    }

    private async Task<int> RequestMetadataSizeAsync(string mapId)
    {
        byte[] req = new byte[1 + 4 + Encoding.UTF8.GetByteCount(mapId)];
        int offset = 0;
        req[offset++] = 1;
        WriteString(req, ref offset, mapId);

        byte[]? resp = await SendAndReceiveWithRetryAsync(req);
        if (resp == null || resp.Length < 1 || resp[0] != 10) return -1;

        int readOffset = 1;
        string returnedMapId = ReadString(resp, ref readOffset);
        return ReadInt32(resp, ref readOffset);
    }

    private async Task<byte[]?> RequestMetadataBlockAsync(string mapId, int blockOffset, int blockSize)
    {
        byte[] req = new byte[1 + 4 + Encoding.UTF8.GetByteCount(mapId) + 4 + 4];
        int offset = 0;
        req[offset++] = 2;
        WriteString(req, ref offset, mapId);
        WriteInt32(req, ref offset, blockOffset);
        WriteInt32(req, ref offset, blockSize);

        byte[]? resp = await SendAndReceiveWithRetryAsync(req);
        if (resp == null || resp.Length < 1 || resp[0] != 11) return null;

        int readOffset = 1;
        string returnedMapId = ReadString(resp, ref readOffset);
        int returnedOffset = ReadInt32(resp, ref readOffset);
        if (returnedOffset != blockOffset) return null;

        byte[] block = new byte[resp.Length - readOffset];
        Buffer.BlockCopy(resp, readOffset, block, 0, block.Length);
        return block;
    }

    private async Task<int> RequestFileSizeAsync(string hash)
    {
        byte[] req = new byte[1 + 4 + Encoding.UTF8.GetByteCount(hash)];
        int offset = 0;
        req[offset++] = 3;
        WriteString(req, ref offset, hash);

        byte[]? resp = await SendAndReceiveWithRetryAsync(req);
        if (resp == null || resp.Length < 1 || resp[0] != 12) return -1;

        int readOffset = 1;
        string returnedHash = ReadString(resp, ref readOffset);
        return ReadInt32(resp, ref readOffset);
    }

    private async Task<byte[]?> RequestFileBlockAsync(string hash, int blockOffset, int blockSize)
    {
        byte[] req = new byte[1 + 4 + Encoding.UTF8.GetByteCount(hash) + 4 + 4];
        int offset = 0;
        req[offset++] = 4;
        WriteString(req, ref offset, hash);
        WriteInt32(req, ref offset, blockOffset);
        WriteInt32(req, ref offset, blockSize);

        byte[]? resp = await SendAndReceiveWithRetryAsync(req);
        if (resp == null || resp.Length < 1 || resp[0] != 13) return null;

        int readOffset = 1;
        string returnedHash = ReadString(resp, ref readOffset);
        int returnedOffset = ReadInt32(resp, ref readOffset);
        if (returnedOffset != blockOffset) return null;

        byte[] block = new byte[resp.Length - readOffset];
        Buffer.BlockCopy(resp, readOffset, block, 0, block.Length);
        return block;
    }

    private async Task<byte[]?> SendAndReceiveWithRetryAsync(byte[] requestData)
    {
        if (_udpClient == null || _seederEndPoint == null) return null;

        int maxRetries = 5;
        for (int i = 0; i < maxRetries; i++)
        {
            try
            {
                await _udpClient.SendAsync(requestData, requestData.Length, _seederEndPoint);

                var receiveTask = _udpClient.ReceiveAsync();
                if (await Task.WhenAny(receiveTask, Task.Delay(200)) == receiveTask)
                {
                    var result = receiveTask.Result;
                    if (result.Buffer.Length > 0 && result.Buffer[0] == 14)
                    {
                        return null;
                    }
                    return result.Buffer;
                }
            }
            catch { }
        }
        return null;
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

    private void WriteString(byte[] buffer, int offset, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        Buffer.BlockCopy(bytes, 0, buffer, offset, bytes.Length);
    }

    private void WriteInt32(byte[] buffer, ref int offset, int value)
    {
        byte[] bytes = BitConverter.GetBytes(value);
        Buffer.BlockCopy(bytes, 0, buffer, offset, 4);
        offset += 4;
    }
}
