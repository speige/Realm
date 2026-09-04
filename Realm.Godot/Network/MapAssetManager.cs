using Blake3;
using Godot;
using Realm.Shared.Metadata;
using SharpCompress.Archives;
using SharpCompress.Common;
using SharpCompress.Writers.SevenZip;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

public static class MapAssetManager
{
    private static readonly object ArchiveLock = new object();

    public static bool IsGodotEngineRunning { get; set; } = true;

    public static void Log(string message)
    {
        if (IsGodotEngineRunning)
        {
            Godot.GD.Print(message);
        }
        else
        {
            Console.WriteLine(message);
        }
    }

    public static void LogErr(string message)
    {
        if (IsGodotEngineRunning)
        {
            Godot.GD.PrintErr(message);
        }
        else
        {
            Console.Error.WriteLine(message);
        }
    }

    private static readonly System.Threading.AsyncLocal<string?> AsyncLocalArchiveDirectory = new();

    public static string? ThreadLocalArchiveDirectory
    {
        get => AsyncLocalArchiveDirectory.Value;
        set => AsyncLocalArchiveDirectory.Value = value;
    }

    private static string? _configuredStoragePath;

    public static string? ConfiguredStoragePath
    {
        get
        {
            if (_configuredStoragePath != null)
            {
                return _configuredStoragePath;
            }

            try
            {
                string[] args = System.Environment.GetCommandLineArgs();
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i].StartsWith("--storage-path=", StringComparison.OrdinalIgnoreCase))
                    {
                        _configuredStoragePath = args[i].Substring("--storage-path=".Length).Trim('"');
                        return _configuredStoragePath;
                    }
                    if (string.Equals(args[i], "--storage-path", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
                    {
                        _configuredStoragePath = args[i + 1].Trim('"');
                        return _configuredStoragePath;
                    }
                }
            }
            catch { }

            return null;
        }
        set
        {
            _configuredStoragePath = value;
            _storage = null;
        }
    }

    private static Realm.Shared.Distribution.ContentAddressableStorage? _storage;
    public static Realm.Shared.Distribution.ContentAddressableStorage Storage
    {
        get
        {
            return _storage ??= new Realm.Shared.Distribution.ContentAddressableStorage(GlobalArchiveDirectory);
        }
    }

    public static string GlobalArchiveDirectory
    {
        get
        {
            if (ThreadLocalArchiveDirectory != null)
            {
                if (!Directory.Exists(ThreadLocalArchiveDirectory))
                {
                    Directory.CreateDirectory(ThreadLocalArchiveDirectory);
                }
                return ThreadLocalArchiveDirectory;
            }

            string? configured = ConfiguredStoragePath;
            if (!string.IsNullOrEmpty(configured))
            {
                if (!Directory.Exists(configured))
                {
                    Directory.CreateDirectory(configured);
                }
                return configured;
            }

            if (IsGodotEngineRunning)
            {
                return ProjectSettings.GlobalizePath("user://maps");
            }
            else
            {
                string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "user_maps");
                if (!Directory.Exists(path))
                {
                    Directory.CreateDirectory(path);
                }
                return path;
            }
        }
    }

    public static string GlobalArchiveFile => Path.Combine(GlobalArchiveDirectory, "global_assets.7z");

    public static string ComputeBlake3(byte[] bytes, string? extensionOrPath = null)
    {
        return RealmMetadataHelper.ComputeBlake3(bytes, extensionOrPath);
    }

    public static string ComputeBlake3(Stream stream, string? extensionOrPath = null)
    {
        return RealmMetadataHelper.ComputeBlake3(stream, extensionOrPath);
    }

    public static List<string> GetMissingHashes(IEnumerable<string> hashes)
    {
        var missing = new List<string>();
        var hashesToCheckArchive = new List<string>();

        foreach (var hash in hashes)
        {
            string norm = Realm.Shared.Distribution.ContentAddressableStorage.NormalizeBlake3Hash(hash);
            if (!Storage.HasAsset(norm))
            {
                hashesToCheckArchive.Add(hash);
            }
        }

        if (hashesToCheckArchive.Count == 0)
        {
            return missing;
        }

        lock (ArchiveLock)
        {
            if (!File.Exists(GlobalArchiveFile))
            {
                missing.AddRange(hashesToCheckArchive);
                return missing;
            }

            try
            {
                var existingHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                using (var archive = ArchiveFactory.OpenArchive(GlobalArchiveFile, null))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (!entry.IsDirectory)
                        {
                            existingHashes.Add(entry.Key);
                            string normKey = Realm.Shared.Distribution.ContentAddressableStorage.NormalizeBlake3Hash(entry.Key);
                            existingHashes.Add(normKey);
                        }
                    }
                }

                foreach (var hash in hashesToCheckArchive)
                {
                    string norm = Realm.Shared.Distribution.ContentAddressableStorage.NormalizeBlake3Hash(hash);
                    if (!existingHashes.Contains(hash) && !existingHashes.Contains(norm))
                    {
                        missing.Add(hash);
                    }
                }
            }
            catch (Exception ex)
            {
                MapAssetManager.LogErr($"[MapAssetManager] Error checking missing hashes: {ex.Message}");
                missing.AddRange(hashesToCheckArchive);
            }
        }
        return missing;
    }

    public static void IngestDeltaArchive(string delta7zPath)
    {
        lock (ArchiveLock)
        {
            try
            {
                if (!File.Exists(delta7zPath))
                {
                    MapAssetManager.LogErr($"[MapAssetManager] Delta archive not found at {delta7zPath}");
                    return;
                }

                var filesToInsert = new Dictionary<string, byte[]>();
                using (var deltaArchive = ArchiveFactory.OpenArchive(delta7zPath, null))
                {
                    foreach (var entry in deltaArchive.Entries)
                    {
                        if (entry.IsDirectory) continue;
                        
                        using (var ms = new MemoryStream())
                        {
                            using (var entryStream = entry.OpenEntryStream())
                            {
                                entryStream.CopyTo(ms);
                            }
                            filesToInsert[entry.Key] = ms.ToArray();
                        }
                    }
                }

                if (filesToInsert.Count > 0)
                {
                    AddOrUpdateGlobalArchive(filesToInsert);
                    MapAssetManager.Log($"[MapAssetManager] Successfully ingested {filesToInsert.Count} files from delta archive.");
                }
            }
            catch (Exception ex)
            {
                MapAssetManager.LogErr($"[MapAssetManager] Failed to ingest delta archive: {ex.Message}");
            }
        }
    }

    public static void AddOrUpdateGlobalArchive(Dictionary<string, byte[]> newFilesByHash)
    {
        if (newFilesByHash == null || newFilesByHash.Count == 0) return;

        foreach (var kvp in newFilesByHash)
        {
            string ext = Path.GetExtension(kvp.Key);
            Storage.StoreAsset(kvp.Value, ext);
        }

        if (!Directory.Exists(GlobalArchiveDirectory))
        {
            Directory.CreateDirectory(GlobalArchiveDirectory);
        }

        string tempFile = Path.Combine(GlobalArchiveDirectory, Guid.NewGuid().ToString() + ".7z");
        
        lock (ArchiveLock)
        {
            try
            {
                using (var newFs = File.Create(tempFile))
                {
                    using (var writer = new SevenZipWriter(newFs, new SevenZipWriterOptions() { CompressionType = CompressionType.LZMA }))
                    {
                        var writtenKeys = new HashSet<string>();


                        if (File.Exists(GlobalArchiveFile))
                        {
                            using (var oldArchive = ArchiveFactory.OpenArchive(GlobalArchiveFile, null))
                            {
                                foreach (var entry in oldArchive.Entries)
                                {
                                    if (entry.IsDirectory) continue;
                                    
                                    if (!writtenKeys.Contains(entry.Key))
                                    {
                                        using (var oldStream = entry.OpenEntryStream())
                                        {
                                            writer.Write(entry.Key, oldStream, entry.LastModifiedTime ?? DateTime.UtcNow);
                                        }
                                        writtenKeys.Add(entry.Key);
                                    }
                                }
                            }
                        }


                        foreach (var kvp in newFilesByHash)
                        {
                           if (!writtenKeys.Contains(kvp.Key))
                           {
                               using (var ms = new MemoryStream(kvp.Value))
                               {
                                   writer.Write(kvp.Key, ms, DateTime.UtcNow);
                               }
                               writtenKeys.Add(kvp.Key);
                           }
                        }
                    }
                }


                if (File.Exists(GlobalArchiveFile))
                {
                    File.Delete(GlobalArchiveFile);
                }
                File.Move(tempFile, GlobalArchiveFile);
            }
            catch (Exception ex)
            {
                MapAssetManager.LogErr($"[MapAssetManager] Error writing global archive: {ex.Message}");
                if (File.Exists(tempFile))
                {
                    try { File.Delete(tempFile); } catch { }
                }
            }
        }
    }

    public static string CreateTemporaryDeltaArchive(List<string> missingHashes)
    {
        if (!Directory.Exists(GlobalArchiveDirectory))
        {
            Directory.CreateDirectory(GlobalArchiveDirectory);
        }

        string tempDeltaPath = Path.Combine(GlobalArchiveDirectory, Guid.NewGuid().ToString() + "_delta.7z");
        
        lock (ArchiveLock)
        {
            try
            {
                using (var newFs = File.Create(tempDeltaPath))
                using (var writer = new SevenZipWriter(newFs, new SevenZipWriterOptions() { CompressionType = CompressionType.LZMA }))
                {
                    var entryMap = new Dictionary<string, IArchiveEntry>();
                    IArchive? hostArchive = null;
                    if (File.Exists(GlobalArchiveFile))
                    {
                        hostArchive = ArchiveFactory.OpenArchive(GlobalArchiveFile, null);
                        foreach (var entry in hostArchive.Entries)
                        {
                            if (!entry.IsDirectory)
                            {
                                entryMap[entry.Key] = entry;
                            }
                        }
                    }

                    try
                    {
                        foreach (var hash in missingHashes)
                        {
                            string norm = Realm.Shared.Distribution.ContentAddressableStorage.NormalizeBlake3Hash(hash);
                            byte[]? casBytes = Storage.GetAssetBytes(norm);
                            if (casBytes != null)
                            {
                                using var ms = new MemoryStream(casBytes);
                                writer.Write(hash, ms, DateTime.UtcNow);
                            }
                            else if (entryMap.TryGetValue(hash, out var hostEntry))
                            {
                                using (var entryStream = hostEntry.OpenEntryStream())
                                {
                                    writer.Write(hash, entryStream, hostEntry.LastModifiedTime ?? DateTime.UtcNow);
                                }
                            }
                            else
                            {
                                MapAssetManager.LogErr($"[MapAssetManager] Requested hash {hash} not found in host storage or archive.");
                            }
                        }
                    }
                    finally
                    {
                        hostArchive?.Dispose();
                    }
                }
                return tempDeltaPath;
            }
            catch (Exception ex)
            {
                MapAssetManager.LogErr($"[MapAssetManager] Failed to create temporary delta archive: {ex.Message}");
                if (File.Exists(tempDeltaPath))
                {
                    try { File.Delete(tempDeltaPath); } catch { }
                }
                throw;
            }
        }
    }

    public static void PruneGlobalArchive()
    {
        string archiveDir = GlobalArchiveDirectory;
        string archiveFile = GlobalArchiveFile;
        Task.Run(() => PruneGlobalArchiveInternal(archiveDir, archiveFile));
    }

    private static void PruneGlobalArchiveInternal(string archiveDir, string archiveFile)
    {
        lock (ArchiveLock)
        {
            try
            {
                MapAssetManager.Log("[MapAssetManager] Starting background pruning process...");
                
                if (!File.Exists(archiveFile))
                {
                    MapAssetManager.Log("[MapAssetManager] Global archive does not exist, skipping pruning.");
                    return;
                }

                if (!Directory.Exists(archiveDir))
                {
                    return;
                }

                var manifestFiles = Directory.GetFiles(archiveDir, "*.json");
                var referencedHashes = new HashSet<string>();

                foreach (var file in manifestFiles)
                {
                    if (Path.GetFileName(file) == "pck_cache.json" || Path.GetFileName(file) == "servers.json") continue;

                    try
                    {
                        string content = File.ReadAllText(file);
                        var manifest = JsonSerializer.Deserialize<MapManifest>(content);
                        if (manifest != null && manifest.Files != null)
                        {
                            foreach (var hash in manifest.Files.Values)
                            {
                                referencedHashes.Add(hash);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MapAssetManager.LogErr($"[MapAssetManager] Error reading manifest {file}: {ex.Message}");
                    }
                }

                MapAssetManager.Log($"[MapAssetManager] Total referenced BLAKE3 hashes found in manifests: {referencedHashes.Count}");

                bool needsPruning = false;
                using (var archive = ArchiveFactory.OpenArchive(archiveFile, null))
                {
                    foreach (var entry in archive.Entries)
                    {
                        if (entry.IsDirectory) continue;
                        if (!referencedHashes.Contains(entry.Key))
                        {
                            needsPruning = true;
                            MapAssetManager.Log($"[MapAssetManager] Unreferenced hash found in global archive: {entry.Key}");
                        }
                    }
                }

                if (needsPruning)
                {
                    MapAssetManager.Log("[MapAssetManager] Rebuilding global archive to prune unreferenced hashes...");
                    string tempFile = Path.Combine(archiveDir, Guid.NewGuid().ToString() + ".7z");
                    
                    using (var newFs = File.Create(tempFile))
                    using (var writer = new SevenZipWriter(newFs, new SevenZipWriterOptions() { CompressionType = CompressionType.LZMA }))
                    {
                        using (var oldArchive = ArchiveFactory.OpenArchive(archiveFile, null))
                        {
                            foreach (var entry in oldArchive.Entries)
                            {
                                if (entry.IsDirectory) continue;
                                if (referencedHashes.Contains(entry.Key))
                                {
                                    using (var oldStream = entry.OpenEntryStream())
                                    {
                                        writer.Write(entry.Key, oldStream, entry.LastModifiedTime ?? DateTime.UtcNow);
                                    }
                                }
                                else
                                {
                                    MapAssetManager.Log($"[MapAssetManager] Pruned entry: {entry.Key}");
                                }
                            }
                        }
                    }

                    if (File.Exists(archiveFile))
                    {
                        File.Delete(archiveFile);
                    }
                    File.Move(tempFile, archiveFile);
                    MapAssetManager.Log("[MapAssetManager] Pruning complete.");
                }
                else
                {
                    MapAssetManager.Log("[MapAssetManager] No pruning needed.");
                }
            }
            catch (Exception ex)
            {
                MapAssetManager.LogErr($"[MapAssetManager] Pruning failed: {ex.Message}");
            }
        }
    }

    public static MapManifest IngestHostMap(string mapPath)
    {
        var manifest = new MapManifest();
        manifest.MapName = Path.GetFileNameWithoutExtension(mapPath);

        var newFiles = new Dictionary<string, byte[]>();

        string? mapDir = Path.GetDirectoryName(mapPath);
        if (string.IsNullOrEmpty(mapDir)) mapDir = "Realm.MapScript";
        
        if (Directory.Exists(mapDir))
        {
            var files = Directory.GetFiles(mapDir, "*.*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                string relativePath = Path.GetRelativePath(mapDir, file).Replace("\\", "/");
                if (relativePath.StartsWith("bin/") || relativePath.StartsWith("obj/") || 
                    relativePath.EndsWith(".cs") || relativePath.EndsWith(".csproj"))
                {
                    continue;
                }

                byte[] bytes = File.ReadAllBytes(file);
                string ext = Path.GetExtension(file).ToLowerInvariant();
                string blake3 = RealmMetadataHelper.ComputeBlake3(bytes, ext);
                string assetKey = string.IsNullOrEmpty(ext) ? blake3 : $"{blake3}{ext}";
                newFiles[assetKey] = bytes;

                string virtualPath = "res://" + relativePath;
                manifest.Files[virtualPath] = assetKey;
            }
        }
        else if (File.Exists(mapPath))
        {
            byte[] bytes = File.ReadAllBytes(mapPath);
            string blake3 = RealmMetadataHelper.ComputeBlake3(bytes, ".json");
            string assetKey = $"{blake3}.json";
            newFiles[assetKey] = bytes;
            manifest.Files["res://map.json"] = assetKey;
        }
        else
        {
            byte[] bytes = Encoding.UTF8.GetBytes("{\"units\": []}");
            string blake3 = RealmMetadataHelper.ComputeBlake3(bytes, ".json");
            string assetKey = $"{blake3}.json";
            newFiles[assetKey] = bytes;
            manifest.Files["res://map.json"] = assetKey;
        }

        AddOrUpdateGlobalArchive(newFiles);

        try
        {
            string manifestPath = Path.Combine(GlobalArchiveDirectory, $"{manifest.MapName}_manifest.json");
            File.WriteAllText(manifestPath, JsonSerializer.Serialize(manifest));
            MapAssetManager.Log($"[MapAssetManager] Saved host manifest to: {manifestPath}");
        }
        catch (Exception ex)
        {
            MapAssetManager.LogErr($"[MapAssetManager] Failed to write host manifest: {ex.Message}");
        }

        return manifest;
    }

    public static void CompileAndLoadPck(string mapName)
    {
        lock (ArchiveLock)
        {
            try
            {
                MapAssetManager.Log($"[MapAssetManager] Compiling PCK file for map: {mapName}");

                string manifestPath = Path.Combine(GlobalArchiveDirectory, $"{mapName}_manifest.json");
                if (!File.Exists(manifestPath))
                {
                    manifestPath = Path.Combine(GlobalArchiveDirectory, "downloaded_map_manifest.json");
                    if (!File.Exists(manifestPath))
                    {
                        MapAssetManager.LogErr($"[MapAssetManager] Manifest for {mapName} not found at {manifestPath}");
                        return;
                    }
                }

                string manifestJson = File.ReadAllText(manifestPath);
                var manifest = JsonSerializer.Deserialize<MapManifest>(manifestJson);
                if (manifest == null || manifest.Files == null)
                {
                    MapAssetManager.LogErr("[MapAssetManager] Failed to parse map manifest.");
                    return;
                }

                if (!File.Exists(GlobalArchiveFile))
                {
                    MapAssetManager.LogErr("[MapAssetManager] Global archive file not found. Cannot compile PCK.");
                    return;
                }

                string tempDir = Path.Combine(GlobalArchiveDirectory, "temp_pck");
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
                Directory.CreateDirectory(tempDir);

                using (var archive = ArchiveFactory.OpenArchive(GlobalArchiveFile, null))
                {
                    var entryMap = new Dictionary<string, IArchiveEntry>();
                    foreach (var entry in archive.Entries)
                    {
                        if (!entry.IsDirectory)
                        {
                            entryMap[entry.Key] = entry;
                        }
                    }

                    foreach (var kvp in manifest.Files)
                    {
                        string virtualPath = kvp.Key;
                        string hash = kvp.Value;

                        if (entryMap.TryGetValue(hash, out var entry))
                        {
                            string tempFilePath = Path.Combine(tempDir, hash);
                            using (var entryStream = entry.OpenEntryStream())
                            using (var fs = File.Create(tempFilePath))
                            {
                                entryStream.CopyTo(fs);
                            }
                        }
                        else
                        {
                            MapAssetManager.LogErr($"[MapAssetManager] Required hash {hash} not found in global archive for file {virtualPath}");
                        }
                    }
                }

                string pckPath = Path.Combine(GlobalArchiveDirectory, $"{mapName}.pck");
                if (File.Exists(pckPath))
                {
                    try { File.Delete(pckPath); } catch { }
                }

                if (IsGodotEngineRunning)
                {
                    CompilePckInternal(pckPath, tempDir, manifest);
                }
                else
                {
                    File.WriteAllText(pckPath, "MOCK PCK CONTENT");
                    MapAssetManager.Log($"[MapAssetManager] [MOCK] Compiled mock PCK at: {pckPath}");
                }

                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }

                if (File.Exists(pckPath))
                {
                    if (IsGodotEngineRunning)
                    {
                        bool loaded = LoadResourcePackInternal(pckPath);
                        if (loaded)
                        {
                            MapAssetManager.Log($"[MapAssetManager] PCK for map {mapName} loaded into virtual filesystem successfully.");
                        }
                        else
                        {
                            MapAssetManager.LogErr($"[MapAssetManager] Failed to load PCK for map {mapName}.");
                        }
                    }
                    else
                    {
                        MapAssetManager.Log($"[MapAssetManager] [MOCK] Loaded mock PCK for map {mapName}.");
                    }
                }

                UpdatePckLastPlayed(mapName);
            }
            catch (Exception ex)
            {
                MapAssetManager.LogErr($"[MapAssetManager] Error compiling or loading PCK: {ex.Message}");
            }
        }
    }

    private static void UpdatePckLastPlayed(string mapName)
    {
        try
        {
            string cachePath = Path.Combine(GlobalArchiveDirectory, "pck_cache.json");
            Dictionary<string, long> cache = new();
            if (File.Exists(cachePath))
            {
                try
                {
                    string content = File.ReadAllText(cachePath);
                    var deserialized = JsonSerializer.Deserialize<Dictionary<string, long>>(content);
                    if (deserialized != null)
                    {
                        cache = deserialized;
                    }
                }
                catch { }
            }

            cache[mapName] = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            File.WriteAllText(cachePath, JsonSerializer.Serialize(cache));

            var pckFiles = Directory.GetFiles(GlobalArchiveDirectory, "*.pck");
            var pckInfoList = new List<(string path, string name, long lastPlayed)>();

            foreach (var pckFile in pckFiles)
            {
                string name = Path.GetFileNameWithoutExtension(pckFile);
                long lastPlayed = 0;
                if (!cache.TryGetValue(name, out lastPlayed))
                {
                    lastPlayed = new DateTimeOffset(File.GetLastWriteTimeUtc(pckFile)).ToUnixTimeSeconds();
                }
                pckInfoList.Add((pckFile, name, lastPlayed));
            }

            pckInfoList.Sort((a, b) => b.lastPlayed.CompareTo(a.lastPlayed));

            long sevenDaysAgo = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - (7 * 24 * 3600);

            for (int i = 0; i < pckInfoList.Count; i++)
            {
                var pck = pckInfoList[i];
                bool keep = (i < 5) && (pck.lastPlayed >= sevenDaysAgo);
                if (pck.name == mapName) keep = true;

                if (!keep)
                {
                    try
                    {
                        File.Delete(pck.path);
                        MapAssetManager.Log($"[MapAssetManager] Pruned old PCK file: {pck.path}");
                        cache.Remove(pck.name);
                    }
                    catch (Exception ex)
                    {
                        MapAssetManager.LogErr($"[MapAssetManager] Failed to delete old PCK {pck.path}: {ex.Message}");
                    }
                }
            }

            File.WriteAllText(cachePath, JsonSerializer.Serialize(cache));
        }
        catch (Exception ex)
        {
            MapAssetManager.LogErr($"[MapAssetManager] Error pruning old PCK files: {ex.Message}");
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static void CompilePckInternal(string pckPath, string tempDir, MapManifest manifest)
    {
        var packer = new PckPacker();
        Error err = packer.PckStart(pckPath);
        if (err == Error.Ok)
        {
            foreach (var kvp in manifest.Files)
            {
                string virtualPath = kvp.Key;
                string hash = kvp.Value;
                string tempFilePath = Path.Combine(tempDir, hash);
                if (File.Exists(tempFilePath))
                {
                    packer.AddFile(virtualPath, tempFilePath);
                }
            }
            packer.Flush();
            MapAssetManager.Log($"[MapAssetManager] Successfully compiled PCK at: {pckPath}");
        }
        else
        {
            MapAssetManager.LogErr($"[MapAssetManager] PckPacker failed to start: {err}");
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static bool LoadResourcePackInternal(string pckPath)
    {
        return ProjectSettings.LoadResourcePack(pckPath);
    }
}

