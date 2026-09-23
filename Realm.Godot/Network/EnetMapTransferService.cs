using Godot;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Realm.Godot.Services;
using Realm.Shared.Distribution;

public partial class EnetMapTransferService : Node
{
    public static EnetMapTransferService Instance { get; private set; } = null!;

    public static event Action<float>? DownloadProgressChanged;
    public static event Action? DownloadCompleted;
    public static event Action? DownloadFailed;

    private class HostTransferSession
    {
        public string TransferId { get; set; } = string.Empty;
        public int PeerId { get; set; }
        public string TempFilePath { get; set; } = string.Empty;
        public CancellationTokenSource Cts { get; set; } = new();
    }

    private class ClientTransferSession
    {
        public string TransferId { get; set; } = string.Empty;
        public string MapName { get; set; } = string.Empty;
        public string MapVersion { get; set; } = string.Empty;
        public MapManifest Manifest { get; set; } = new();
        public string TempFilePath { get; set; } = string.Empty;
        public FileStream? TempFileStream { get; set; }
        public long ExpectedTotalBytes { get; set; }
        public int ExpectedTotalChunks { get; set; }
        public int ReceivedChunks { get; set; }
        public long ReceivedBytes { get; set; }
    }

    private readonly ConcurrentDictionary<string, HostTransferSession> _hostTransfers = new();
    private TaskCompletionSource<string>? _manifestTcs;
    private TaskCompletionSource<bool>? _transferCompleteTcs;
    private ClientTransferSession? _currentClientTransfer;

    public override void _Ready()
    {
        Instance = this;
        if (string.IsNullOrEmpty(Name))
        {
            Name = "EnetMapTransferService";
        }
    }

    public static EnetMapTransferService EnsureNode(Node treeNode)
    {
        var root = treeNode.GetTree().Root;
        var existing = root.GetNodeOrNull<EnetMapTransferService>("EnetMapTransferService");
        if (existing != null) return existing;

        var service = new EnetMapTransferService();
        service.Name = "EnetMapTransferService";
        root.AddChild(service);
        return service;
    }

    public async Task<bool> RequestAndDownloadMapAsync(
        SceneMultiplayer multiplayer,
        int targetPeerId,
        string targetMap,
        Action<float>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        _manifestTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);

        Callable.From(() => RpcId(targetPeerId, nameof(RequestMapManifestRpc), targetMap)).CallDeferred();

        var manifestTask = await Task.WhenAny(_manifestTcs.Task, Task.Delay(10000, cancellationToken));
        string? manifestJson = manifestTask == _manifestTcs.Task ? _manifestTcs.Task.Result : null;

        if (string.IsNullOrWhiteSpace(manifestJson))
        {
            DownloadFailed?.Invoke();
            return false;
        }

        MapManifest? manifest = null;
        try
        {
            manifest = MapManifest.LoadFromJson(manifestJson) ?? JsonSerializer.Deserialize<MapManifest>(manifestJson);
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[EnetMapTransferService] Failed to deserialize manifest: {ex.Message}");
        }

        if (manifest == null || manifest.Files == null)
        {
            DownloadFailed?.Invoke();
            return false;
        }

        var missingHashes = MapAssetManager.GetMissingHashes(manifest.Files.Values);
        string version = !string.IsNullOrWhiteSpace(manifest.Version) ? manifest.Version.Trim() : "1.0.0";
        string targetMapDir = MapAssetManager.GetMapDirectory(targetMap, version);

        if (missingHashes.Count == 0)
        {
            MapAssetManager.ExtractManifestFiles(manifest, targetMapDir);
            string localManifestPath = Path.Combine(targetMapDir, "manifest.json");
            AssetIndexService.Instance.RegisterManifest(manifest, localManifestPath);

            progressCallback?.Invoke(1.0f);
            DownloadProgressChanged?.Invoke(1.0f);
            DownloadCompleted?.Invoke();
            return true;
        }

        progressCallback?.Invoke(0.0f);
        DownloadProgressChanged?.Invoke(0.0f);

        string transferId = Guid.NewGuid().ToString("N");
        string tempDir = Path.Combine(Path.GetTempPath(), "Realm_Transfers");
        Directory.CreateDirectory(tempDir);
        string tempFilePath = Path.Combine(tempDir, $"{transferId}.zst");

        var transferSession = new ClientTransferSession
        {
            TransferId = transferId,
            MapName = targetMap,
            MapVersion = version,
            Manifest = manifest,
            TempFilePath = tempFilePath,
            TempFileStream = new FileStream(tempFilePath, FileMode.Create, System.IO.FileAccess.Write, FileShare.None, ZstdAssetBundleHelper.ChunkSize, useAsync: true)
        };

        _currentClientTransfer = transferSession;
        _transferCompleteTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

        Callable.From(() => RpcId(targetPeerId, nameof(RequestMapAssetTransferRpc), transferId, targetMap, version, missingHashes.ToArray())).CallDeferred();

        var transferTask = await Task.WhenAny(_transferCompleteTcs.Task, Task.Delay(300000, cancellationToken));
        bool transferSuccess = transferTask == _transferCompleteTcs.Task && _transferCompleteTcs.Task.Result;

        if (transferSuccess)
        {
            Callable.From(() => RpcId(targetPeerId, nameof(AcknowledgeMapTransferCompleteRpc), transferId)).CallDeferred();

            try { transferSession.TempFileStream?.Dispose(); transferSession.TempFileStream = null; } catch { }

            bool extractSuccess = await Task.Run(() =>
            {
                try
                {
                    var extractedAssets = ZstdAssetBundleHelper.ExtractBundleFromFile(transferSession.TempFilePath);
                    foreach (var (assetKey, data, metadata) in extractedAssets)
                    {
                        string ext = Path.GetExtension(assetKey);
                        MapAssetManager.Storage.StoreAsset(data, ext, metadata);
                    }

                    MapAssetManager.ExtractManifestFiles(manifest, targetMapDir);
                    string localManifestPath = Path.Combine(targetMapDir, "manifest.json");
                    AssetIndexService.Instance.RegisterManifest(manifest, localManifestPath);

                    try { if (File.Exists(transferSession.TempFilePath)) File.Delete(transferSession.TempFilePath); } catch { }
                    return true;
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[EnetMapTransferService] Decompression failed: {ex.Message}");
                    return false;
                }
            }, cancellationToken);

            _currentClientTransfer = null;

            if (extractSuccess)
            {
                progressCallback?.Invoke(1.0f);
                DownloadProgressChanged?.Invoke(1.0f);
                DownloadCompleted?.Invoke();
                return true;
            }
        }

        _currentClientTransfer = null;
        DownloadFailed?.Invoke();
        return false;
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestMapManifestRpc(string targetMap)
    {
        int senderId = Multiplayer.GetRemoteSenderId();
        var manifest = MapAssetManager.FindHostManifest(targetMap);
        if (manifest != null)
        {
            string json = JsonSerializer.Serialize(manifest);
            Callable.From(() => RpcId(senderId, nameof(ReceiveMapManifestRpc), targetMap, manifest.Version ?? "1.0.0", json)).CallDeferred();
        }
        else
        {
            Callable.From(() => RpcId(senderId, nameof(ReceiveMapManifestRpc), targetMap, "", "")).CallDeferred();
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveMapManifestRpc(string mapName, string mapVersion, string manifestJson)
    {
        _manifestTcs?.TrySetResult(manifestJson);
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestMapAssetTransferRpc(string transferId, string mapName, string mapVersion, string[] missingHashes)
    {
        int senderId = Multiplayer.GetRemoteSenderId();
        _ = HandleHostAssetTransferAsync(senderId, transferId, mapName, mapVersion, missingHashes);
    }

    private async Task HandleHostAssetTransferAsync(int peerId, string transferId, string mapName, string mapVersion, string[] missingHashes)
    {
        var cts = new CancellationTokenSource();
        string tempDir = Path.Combine(Path.GetTempPath(), "Realm_Transfers");
        Directory.CreateDirectory(tempDir);
        string tempFilePath = Path.Combine(tempDir, $"{transferId}.zst");

        var session = new HostTransferSession
        {
            TransferId = transferId,
            PeerId = peerId,
            TempFilePath = tempFilePath,
            Cts = cts
        };
        _hostTransfers[transferId] = session;

        try
        {
            await Task.Run(() =>
            {
                var manifest = MapAssetManager.FindHostManifest(mapName, mapVersion);
                string? mapDir = null;
                string? manifestPath = MapAssetManager.FindManifestPath(mapName, mapVersion);
                if (!string.IsNullOrEmpty(manifestPath) && File.Exists(manifestPath))
                {
                    mapDir = Path.GetDirectoryName(manifestPath);
                }

                var assetsToPack = new List<(string AssetKey, byte[] Data, string? Metadata)>();
                foreach (var hash in missingHashes)
                {
                    string norm = ContentAddressableStorage.NormalizeBlake3Hash(hash);
                    byte[]? bytes = MapAssetManager.Storage.GetAssetBytes(norm) ?? MapAssetManager.P2PStorage.GetAssetBytes(norm);

                    if (bytes == null && manifest != null && manifest.Files != null && !string.IsNullOrEmpty(mapDir))
                    {
                        foreach (var kvp in manifest.Files)
                        {
                            if (ContentAddressableStorage.NormalizeBlake3Hash(kvp.Value) == norm)
                            {
                                string relPath = kvp.Key.Replace("res://", "").TrimStart('/', '\\');
                                string localFilePath = Path.Combine(mapDir, relPath);
                                if (File.Exists(localFilePath))
                                {
                                    bytes = File.ReadAllBytes(localFilePath);
                                    string ext = Path.GetExtension(localFilePath);
                                    MapAssetManager.Storage.StoreAsset(bytes, ext);
                                    break;
                                }
                            }
                        }
                    }

                    if (bytes != null)
                    {
                        string? meta = MapAssetManager.Storage.GetAssetMetadata(norm);
                        assetsToPack.Add((hash, bytes, meta));
                    }
                }

                ZstdAssetBundleHelper.CreateBundleToFile(tempFilePath, assetsToPack);
            }, cts.Token);

            var fileInfo = new FileInfo(tempFilePath);
            long totalBytes = fileInfo.Length;
            int totalChunks = (int)Math.Ceiling((double)totalBytes / ZstdAssetBundleHelper.ChunkSize);
            if (totalChunks <= 0) totalChunks = 1;

            Callable.From(() => RpcId(peerId, nameof(BeginMapTransferRpc), transferId, mapName, mapVersion, totalBytes, totalChunks)).CallDeferred();

            using var fs = new FileStream(tempFilePath, FileMode.Open, System.IO.FileAccess.Read, FileShare.Read, ZstdAssetBundleHelper.ChunkSize);
            byte[] buffer = new byte[ZstdAssetBundleHelper.ChunkSize];
            int chunkIndex = 0;
            int bytesRead;

            while ((bytesRead = await fs.ReadAsync(buffer, 0, buffer.Length, cts.Token)) > 0)
            {
                if (cts.IsCancellationRequested) break;

                byte[] chunkData = new byte[bytesRead];
                Array.Copy(buffer, chunkData, bytesRead);

                int currentChunkIndex = chunkIndex;
                Callable.From(() => RpcId(peerId, nameof(SendMapTransferChunkRpc), transferId, currentChunkIndex, totalChunks, totalBytes, chunkData)).CallDeferred();
                chunkIndex++;

                if (chunkIndex % 4 == 0)
                {
                    await Task.Delay(1, cts.Token);
                }
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[EnetMapTransferService] Transfer error: {ex.Message}");
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void BeginMapTransferRpc(string transferId, string mapName, string mapVersion, long totalBytes, int totalChunks)
    {
        if (_currentClientTransfer != null && _currentClientTransfer.TransferId == transferId)
        {
            _currentClientTransfer.ExpectedTotalBytes = totalBytes;
            _currentClientTransfer.ExpectedTotalChunks = totalChunks;
            _currentClientTransfer.ReceivedChunks = 0;
            _currentClientTransfer.ReceivedBytes = 0;
            DownloadProgressChanged?.Invoke(0.0f);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SendMapTransferChunkRpc(string transferId, int chunkIndex, int totalChunks, long totalBytes, byte[] chunkData)
    {
        if (_currentClientTransfer == null || _currentClientTransfer.TransferId != transferId) return;

        try
        {
            if (_currentClientTransfer.TempFileStream != null)
            {
                _currentClientTransfer.TempFileStream.Write(chunkData, 0, chunkData.Length);
                _currentClientTransfer.ReceivedChunks++;
                _currentClientTransfer.ReceivedBytes += chunkData.Length;

                float progress = totalChunks > 0 ? Math.Clamp((float)_currentClientTransfer.ReceivedChunks / totalChunks, 0.0f, 1.0f) : 1.0f;
                DownloadProgressChanged?.Invoke(progress);

                if (_currentClientTransfer.ReceivedChunks >= totalChunks)
                {
                    _currentClientTransfer.TempFileStream.Flush();
                    _currentClientTransfer.TempFileStream.Dispose();
                    _currentClientTransfer.TempFileStream = null;
                    _transferCompleteTcs?.TrySetResult(true);
                }
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[EnetMapTransferService] Chunk write error: {ex.Message}");
            _transferCompleteTcs?.TrySetException(ex);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void AcknowledgeMapTransferCompleteRpc(string transferId)
    {
        if (_hostTransfers.TryRemove(transferId, out var session))
        {
            session.Cts.Cancel();
            try { if (File.Exists(session.TempFilePath)) File.Delete(session.TempFilePath); } catch { }
        }
    }
}
