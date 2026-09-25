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

    private struct HostAssetTransferItem
    {
        public string AssetKey;
        public string SourceFilePath;
        public string? Metadata;
        public long Size;
    }

    private class HostTransferSession
    {
        public string TransferId { get; set; } = string.Empty;
        public int PeerId { get; set; }
        public string MapName { get; set; } = string.Empty;
        public string MapVersion { get; set; } = string.Empty;
        public List<List<HostAssetTransferItem>> ChunkItems { get; set; } = new();
        public int CurrentChunkIndex { get; set; } = 0;
        public int TotalChunks => ChunkItems.Count;
        public long TotalRawBytes { get; set; }
        public CancellationTokenSource Cts { get; set; } = new();
    }

    private class ClientTransferSession
    {
        public string TransferId { get; set; } = string.Empty;
        public string MapName { get; set; } = string.Empty;
        public string MapVersion { get; set; } = string.Empty;
        public MapManifest Manifest { get; set; } = new();
        public long ExpectedTotalBytes { get; set; }
        public int ExpectedTotalChunks { get; set; }
        public int CurrentChunkIndex { get; set; }
        public MemoryStream CurrentChunkStream { get; set; } = new();
        public int ReceivedPacketsInCurrentChunk { get; set; }
        public long TotalReceivedBytes { get; set; }
        public int TotalAssetsInManifest { get; set; }
        public int AlreadyPresentAssets { get; set; }
        public int SessionMissingAssets { get; set; }
        public DateTime LastActivityTimeUtc { get; set; } = DateTime.UtcNow;
        public float LastEmittedProgress { get; set; } = -1.0f;
        public long LastProgressEmitTicks { get; set; } = 0;
        public Action<float>? ProgressCallback { get; set; }
    }

    private readonly ConcurrentDictionary<string, HostTransferSession> _hostTransfers = new();
    private TaskCompletionSource<string>? _manifestTcs;
    private TaskCompletionSource<bool>? _transferCompleteTcs;
    private ClientTransferSession? _currentClientTransfer;
    private int _activeEphemeralTransferPeerId = 0;

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

        var allHashes = manifest.Files.Values.ToList();
        int totalManifestFiles = allHashes.Count;
        string version = !string.IsNullOrWhiteSpace(manifest.Version) ? manifest.Version.Trim() : "1.0.0";
        string targetMapDir = MapAssetManager.GetMapDirectory(targetMap, version);

        int maxRetries = 10;
        int retryCount = 0;

        while (retryCount < maxRetries && !cancellationToken.IsCancellationRequested)
        {
            var missingHashes = await Task.Run(() => MapAssetManager.GetMissingHashes(allHashes));

            if (missingHashes.Count == 0)
            {
                await Task.Run(() =>
                {
                    MapAssetManager.ExtractManifestFiles(manifest, targetMapDir);
                    string localManifestPath = Path.Combine(targetMapDir, "manifest.json");
                    AssetIndexService.Instance.RegisterManifest(manifest, localManifestPath);
                });

                progressCallback?.Invoke(1.0f);
                DownloadProgressChanged?.Invoke(1.0f);
                DownloadCompleted?.Invoke();
                return true;
            }

            float initialProgress = totalManifestFiles > 0
                ? Math.Clamp((float)(totalManifestFiles - missingHashes.Count) / totalManifestFiles, 0.0f, 1.0f)
                : 0.0f;
            progressCallback?.Invoke(initialProgress);
            DownloadProgressChanged?.Invoke(initialProgress);

            string transferId = Guid.NewGuid().ToString("N");
            var transferSession = new ClientTransferSession
            {
                TransferId = transferId,
                MapName = targetMap,
                MapVersion = version,
                Manifest = manifest,
                CurrentChunkStream = new MemoryStream(),
                ProgressCallback = progressCallback,
                TotalAssetsInManifest = totalManifestFiles,
                AlreadyPresentAssets = totalManifestFiles - missingHashes.Count,
                SessionMissingAssets = missingHashes.Count,
                LastActivityTimeUtc = DateTime.UtcNow
            };

            _currentClientTransfer = transferSession;
            _transferCompleteTcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            Callable.From(() => RpcId(targetPeerId, nameof(RequestMapAssetTransferRpc), transferId, targetMap, version, missingHashes.ToArray())).CallDeferred();

            bool transferSuccess = false;
            while (!_transferCompleteTcs.Task.IsCompleted && !cancellationToken.IsCancellationRequested)
            {
                var completedTask = await Task.WhenAny(_transferCompleteTcs.Task, Task.Delay(2000, cancellationToken));
                if (completedTask == _transferCompleteTcs.Task)
                {
                    transferSuccess = _transferCompleteTcs.Task.Result;
                    break;
                }

                if (DateTime.UtcNow - transferSession.LastActivityTimeUtc > TimeSpan.FromSeconds(45))
                {
                    GD.PrintErr("[EnetMapTransferService] Transfer inactivity timeout. Retrying remaining assets...");
                    break;
                }
            }

            if (transferSuccess)
            {
                _currentClientTransfer = null;
                progressCallback?.Invoke(1.0f);
                DownloadProgressChanged?.Invoke(1.0f);
                DownloadCompleted?.Invoke();
                return true;
            }

            if (_currentClientTransfer != null)
            {
                try
                {
                    _currentClientTransfer.CurrentChunkStream.Dispose();
                }
                catch { }
                _currentClientTransfer = null;
            }

            retryCount++;
            await Task.Delay(1000, cancellationToken);
        }

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
        if (PeerSeederManager.Instance != null && PeerSeederManager.Instance.IsSeeding)
        {
            if (_activeEphemeralTransferPeerId != 0 && _activeEphemeralTransferPeerId != senderId)
            {
                GD.Print($"[EnetMapTransferService] Rejecting map transfer {transferId} from peer {senderId}: Seeder busy with peer {_activeEphemeralTransferPeerId}.");
                return;
            }
            _activeEphemeralTransferPeerId = senderId;
        }

        foreach (var kvp in _hostTransfers)
        {
            if (kvp.Value.PeerId == senderId)
            {
                kvp.Value.Cts.Cancel();
                _hostTransfers.TryRemove(kvp.Key, out _);
            }
        }

        _ = HandleHostAssetTransferAsync(senderId, transferId, mapName, mapVersion, missingHashes);
    }

    private async Task HandleHostAssetTransferAsync(int peerId, string transferId, string mapName, string mapVersion, string[] missingHashes)
    {
        var cts = new CancellationTokenSource();
        var session = new HostTransferSession
        {
            TransferId = transferId,
            PeerId = peerId,
            MapName = mapName,
            MapVersion = mapVersion,
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

                var localFileMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (manifest != null && manifest.Files != null && !string.IsNullOrEmpty(mapDir))
                {
                    foreach (var kvp in manifest.Files)
                    {
                        string normKvp = ContentAddressableStorage.NormalizeBlake3Hash(kvp.Value);
                        string relPath = kvp.Key.Replace("res://", "").TrimStart('/', '\\');
                        string localFilePath = Path.Combine(mapDir, relPath);
                        if (File.Exists(localFilePath))
                        {
                            localFileMap[normKvp] = localFilePath;
                        }
                    }
                }

                var itemsToPack = new List<HostAssetTransferItem>(missingHashes.Length);
                foreach (var hash in missingHashes)
                {
                    string norm = ContentAddressableStorage.NormalizeBlake3Hash(hash);
                    string? filePath = MapAssetManager.Storage.FindAssetFilePath(norm) ?? MapAssetManager.P2PStorage.FindAssetFilePath(norm);
                    string? meta = null;

                    if (filePath != null && File.Exists(filePath))
                    {
                        meta = MapAssetManager.Storage.GetAssetMetadata(norm);
                    }
                    else if (localFileMap.TryGetValue(norm, out var localPath) && File.Exists(localPath))
                    {
                        filePath = localPath;
                    }

                    if (filePath != null && File.Exists(filePath))
                    {
                        long size = new FileInfo(filePath).Length;
                        itemsToPack.Add(new HostAssetTransferItem
                        {
                            AssetKey = hash,
                            SourceFilePath = filePath,
                            Metadata = meta,
                            Size = size
                        });
                    }
                }

                var chunkList = new List<List<HostAssetTransferItem>>();
                var currentChunk = new List<HostAssetTransferItem>();
                long currentChunkBytes = 0;

                foreach (var item in itemsToPack)
                {
                    long estimated = item.Size + (item.AssetKey?.Length ?? 0) * 2 + (item.Metadata?.Length ?? 0) * 2 + 16;
                    if (currentChunk.Count > 0 && currentChunkBytes + estimated > ZstdAssetBundleHelper.MaxBundleChunkSize)
                    {
                        chunkList.Add(currentChunk);
                        currentChunk = new List<HostAssetTransferItem>();
                        currentChunkBytes = 0;
                    }
                    currentChunk.Add(item);
                    currentChunkBytes += estimated;
                }

                if (currentChunk.Count > 0)
                {
                    chunkList.Add(currentChunk);
                }

                session.ChunkItems = chunkList;
                session.TotalRawBytes = itemsToPack.Sum(a => a.Size);
            }, cts.Token);

            int totalChunks = session.TotalChunks;
            long totalBytes = session.TotalRawBytes;

            Callable.From(() => RpcId(peerId, nameof(BeginMapTransferRpc), transferId, mapName, mapVersion, totalBytes, totalChunks)).CallDeferred();

            if (totalChunks > 0)
            {
                await SendHostChunkAsync(session, 0);
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[EnetMapTransferService] Transfer error: {ex.Message}");
        }
    }

    private async Task SendHostChunkAsync(HostTransferSession session, int chunkIndex)
    {
        if (chunkIndex < 0 || chunkIndex >= session.ChunkItems.Count || session.Cts.IsCancellationRequested)
        {
            return;
        }

        try
        {
            byte[] compressedChunkBytes = await Task.Run(() =>
            {
                var chunkItems = session.ChunkItems[chunkIndex];
                var chunkAssets = new List<(string AssetKey, byte[] Data, string? Metadata)>(chunkItems.Count);
                foreach (var item in chunkItems)
                {
                    if (session.Cts.IsCancellationRequested) break;
                    if (File.Exists(item.SourceFilePath))
                    {
                        byte[] data = File.ReadAllBytes(item.SourceFilePath);
                        chunkAssets.Add((item.AssetKey, data, item.Metadata));
                    }
                }
                return ZstdAssetBundleHelper.CreateBundleBytes(chunkAssets, 1);
            }, session.Cts.Token);

            int totalPacketsInChunk = (int)Math.Ceiling((double)compressedChunkBytes.Length / ZstdAssetBundleHelper.PacketChunkSize);
            if (totalPacketsInChunk <= 0) totalPacketsInChunk = 1;

            int packetIndex = 0;
            int offset = 0;

            while (offset < compressedChunkBytes.Length)
            {
                if (session.Cts.IsCancellationRequested) break;

                int packetSize = Math.Min(ZstdAssetBundleHelper.PacketChunkSize, compressedChunkBytes.Length - offset);
                byte[] packetData = new byte[packetSize];
                Buffer.BlockCopy(compressedChunkBytes, offset, packetData, 0, packetSize);

                int currentPacketIndex = packetIndex;
                int currentChunkIndex = chunkIndex;
                int totalChunks = session.TotalChunks;
                int totalPackets = totalPacketsInChunk;
                long totalBytes = session.TotalRawBytes;

                Callable.From(() => RpcId(
                    session.PeerId,
                    nameof(SendMapTransferChunkRpc),
                    session.TransferId,
                    currentChunkIndex,
                    totalChunks,
                    currentPacketIndex,
                    totalPackets,
                    totalBytes,
                    packetData)).CallDeferred();

                offset += packetSize;
                packetIndex++;

                if (packetIndex % 2 == 0)
                {
                    await Task.Delay(1, session.Cts.Token);
                }
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[EnetMapTransferService] Error sending chunk {chunkIndex} for transfer {session.TransferId}: {ex.Message}");
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestNextMapTransferChunkRpc(string transferId, int nextChunkIndex)
    {
        if (_hostTransfers.TryGetValue(transferId, out var session) && !session.Cts.IsCancellationRequested)
        {
            session.CurrentChunkIndex = nextChunkIndex;
            _ = SendHostChunkAsync(session, nextChunkIndex);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void BeginMapTransferRpc(string transferId, string mapName, string mapVersion, long totalBytes, int totalChunks)
    {
        if (_currentClientTransfer != null && _currentClientTransfer.TransferId == transferId)
        {
            _currentClientTransfer.LastActivityTimeUtc = DateTime.UtcNow;
            _currentClientTransfer.ExpectedTotalBytes = totalBytes;
            _currentClientTransfer.ExpectedTotalChunks = totalChunks;
            _currentClientTransfer.CurrentChunkIndex = 0;
            _currentClientTransfer.ReceivedPacketsInCurrentChunk = 0;
            _currentClientTransfer.CurrentChunkStream.SetLength(0);
            _currentClientTransfer.CurrentChunkStream.Position = 0;

            if (totalChunks == 0)
            {
                string targetMapDir = MapAssetManager.GetMapDirectory(mapName, mapVersion);
                MapAssetManager.ExtractManifestFiles(_currentClientTransfer.Manifest, targetMapDir);
                string localManifestPath = Path.Combine(targetMapDir, "manifest.json");
                AssetIndexService.Instance.RegisterManifest(_currentClientTransfer.Manifest, localManifestPath);

                Callable.From(() => RpcId(1, nameof(AcknowledgeMapTransferCompleteRpc), transferId)).CallDeferred();
                _transferCompleteTcs?.TrySetResult(true);
            }
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SendMapTransferChunkRpc(string transferId, int chunkIndex, int totalChunks, int packetIndex, int totalPacketsInChunk, long totalBytes, byte[] packetData)
    {
        if (_currentClientTransfer == null || _currentClientTransfer.TransferId != transferId) return;

        try
        {
            _currentClientTransfer.LastActivityTimeUtc = DateTime.UtcNow;
            _currentClientTransfer.ExpectedTotalChunks = totalChunks;
            _currentClientTransfer.ExpectedTotalBytes = totalBytes;
            _currentClientTransfer.CurrentChunkIndex = chunkIndex;

            _currentClientTransfer.CurrentChunkStream.Write(packetData, 0, packetData.Length);
            _currentClientTransfer.ReceivedPacketsInCurrentChunk++;
            _currentClientTransfer.TotalReceivedBytes += packetData.Length;

            float chunkFraction = totalPacketsInChunk > 0 ? (float)(packetIndex + 1) / totalPacketsInChunk : 1.0f;
            float sessionFraction = totalChunks > 0
                ? Math.Clamp(((float)chunkIndex + chunkFraction) / totalChunks, 0.0f, 1.0f)
                : 1.0f;

            float overallProgress;
            if (_currentClientTransfer.TotalAssetsInManifest > 0)
            {
                float downloadedAssetsInSession = sessionFraction * _currentClientTransfer.SessionMissingAssets;
                overallProgress = Math.Clamp((_currentClientTransfer.AlreadyPresentAssets + downloadedAssetsInSession) / _currentClientTransfer.TotalAssetsInManifest, 0.0f, 1.0f);
            }
            else
            {
                overallProgress = sessionFraction;
            }

            bool isChunkEnd = packetIndex + 1 >= totalPacketsInChunk;
            long nowTicks = System.Environment.TickCount64;
            if (isChunkEnd || overallProgress >= 1.0f || Math.Abs(overallProgress - _currentClientTransfer.LastEmittedProgress) >= 0.005f || (nowTicks - _currentClientTransfer.LastProgressEmitTicks) >= 100)
            {
                _currentClientTransfer.LastEmittedProgress = overallProgress;
                _currentClientTransfer.LastProgressEmitTicks = nowTicks;
                _currentClientTransfer.ProgressCallback?.Invoke(overallProgress);
                DownloadProgressChanged?.Invoke(overallProgress);
            }

            if (isChunkEnd)
            {
                byte[] chunkBytes = _currentClientTransfer.CurrentChunkStream.ToArray();
                _currentClientTransfer.CurrentChunkStream.SetLength(0);
                _currentClientTransfer.CurrentChunkStream.Position = 0;
                _currentClientTransfer.ReceivedPacketsInCurrentChunk = 0;

                _ = ProcessReceivedChunkAsync(_currentClientTransfer, chunkIndex, totalChunks, chunkBytes);
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[EnetMapTransferService] Chunk write error: {ex.Message}");
            _transferCompleteTcs?.TrySetException(ex);
        }
    }

    private async Task ProcessReceivedChunkAsync(ClientTransferSession transferSession, int chunkIndex, int totalChunks, byte[] chunkBytes)
    {
        try
        {
            var extractedAssets = await Task.Run(() => ZstdAssetBundleHelper.ExtractBundleBytes(chunkBytes));
            transferSession.LastActivityTimeUtc = DateTime.UtcNow;

            await Task.Run(() =>
            {
                foreach (var (assetKey, data, metadata) in extractedAssets)
                {
                    string ext = Path.GetExtension(assetKey);
                    MapAssetManager.Storage.StoreAsset(data, ext, metadata, precomputedBlake3: assetKey);
                }
            });

            transferSession.LastActivityTimeUtc = DateTime.UtcNow;

            if (chunkIndex + 1 < totalChunks)
            {
                Callable.From(() => RpcId(1, nameof(RequestNextMapTransferChunkRpc), transferSession.TransferId, chunkIndex + 1)).CallDeferred();
            }
            else
            {
                string targetMapDir = MapAssetManager.GetMapDirectory(transferSession.MapName, transferSession.MapVersion);
                await Task.Run(() =>
                {
                    MapAssetManager.ExtractManifestFiles(transferSession.Manifest, targetMapDir);
                    string localManifestPath = Path.Combine(targetMapDir, "manifest.json");
                    AssetIndexService.Instance.RegisterManifest(transferSession.Manifest, localManifestPath);
                });

                Callable.From(() => RpcId(1, nameof(AcknowledgeMapTransferCompleteRpc), transferSession.TransferId)).CallDeferred();
                _transferCompleteTcs?.TrySetResult(true);
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[EnetMapTransferService] Decompression failed: {ex.Message}");
            _transferCompleteTcs?.TrySetException(ex);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void AcknowledgeMapTransferCompleteRpc(string transferId)
    {
        if (_hostTransfers.TryRemove(transferId, out var session))
        {
            session.Cts.Cancel();
            if (_activeEphemeralTransferPeerId == session.PeerId)
            {
                _activeEphemeralTransferPeerId = 0;
            }
        }
    }
}
