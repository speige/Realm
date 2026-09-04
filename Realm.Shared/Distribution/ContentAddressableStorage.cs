using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Realm.Shared.Metadata;

namespace Realm.Shared.Distribution;

public class ContentAddressableStorage
{
    public const long MaximumAssetSizeBytes = 15 * 1024 * 1024;
    private readonly string _rootDirectory;
    private readonly string _assetsDirectory;
    private readonly string _sidecarCacheDirectory;
    private readonly ConcurrentDictionary<string, object> _fileLocks = new();

    public string RootDirectory => _rootDirectory;
    public string AssetsDirectory => _assetsDirectory;
    public string SidecarCacheDirectory => _sidecarCacheDirectory;

    public ContentAddressableStorage(string rootDirectory)
    {
        _rootDirectory = Path.GetFullPath(rootDirectory);
        _assetsDirectory = Path.Combine(_rootDirectory, "assets");
        _sidecarCacheDirectory = Path.Combine(_rootDirectory, ".sidecarcache");

        Directory.CreateDirectory(_assetsDirectory);
        Directory.CreateDirectory(_sidecarCacheDirectory);
    }

    public static string NormalizeBlake3Hash(string hashOrFileName)
    {
        string fileName = Path.GetFileName(hashOrFileName);
        int dotIndex = fileName.IndexOf('.');
        return (dotIndex >= 0 ? fileName.Substring(0, dotIndex) : fileName).Trim().ToLowerInvariant();
    }

    public string? FindAssetFilePath(string blake3Hash)
    {
        string normalizedHash = NormalizeBlake3Hash(blake3Hash);
        if (normalizedHash.Length < 2)
        {
            return null;
        }

        string shardDirectory = Path.Combine(_assetsDirectory, normalizedHash.Substring(0, 2));
        if (!Directory.Exists(shardDirectory))
        {
            return null;
        }

        string[] matchingFiles = Directory.GetFiles(shardDirectory, $"{normalizedHash}*");
        if (matchingFiles.Length > 0)
        {
            return matchingFiles[0];
        }

        return null;
    }

    public bool HasAsset(string blake3Hash)
    {
        return FindAssetFilePath(blake3Hash) != null;
    }

    public byte[]? GetAssetBytes(string blake3Hash)
    {
        string? filePath = FindAssetFilePath(blake3Hash);
        if (filePath == null || !File.Exists(filePath))
        {
            return null;
        }

        string normalizedHash = NormalizeBlake3Hash(blake3Hash);
        object fileLock = _fileLocks.GetOrAdd(normalizedHash, _ => new object());

        lock (fileLock)
        {
            return File.ReadAllBytes(filePath);
        }
    }

    public Stream? OpenAssetReadStream(string blake3Hash)
    {
        string? filePath = FindAssetFilePath(blake3Hash);
        if (filePath == null || !File.Exists(filePath))
        {
            return null;
        }

        return new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    }

    public string? GetAssetMetadata(string blake3Hash)
    {
        string normalizedHash = NormalizeBlake3Hash(blake3Hash);
        string sidecarPath = GetSidecarCachePath(normalizedHash);

        if (File.Exists(sidecarPath))
        {
            try
            {
                return File.ReadAllText(sidecarPath);
            }
            catch
            {
            }
        }

        string? filePath = FindAssetFilePath(normalizedHash);
        if (filePath == null || !File.Exists(filePath))
        {
            return null;
        }

        string? extractedMetadata = RealmMetadataHelper.ExtractMetadata(filePath);
        if (!string.IsNullOrWhiteSpace(extractedMetadata))
        {
            UpdateSidecarCache(normalizedHash, extractedMetadata);
        }

        return extractedMetadata;
    }

    public bool CheckFreeDiskSpaceAcceptingUploads()
    {
        try
        {
            string rootPath = Path.GetPathRoot(_rootDirectory) ?? _rootDirectory;
            var driveInfo = new DriveInfo(rootPath);
            if (driveInfo.TotalSize <= 0)
            {
                return true;
            }

            double freePercentage = (double)driveInfo.AvailableFreeSpace / driveInfo.TotalSize;
            return freePercentage >= 0.10;
        }
        catch
        {
            return true;
        }
    }

    public (bool Success, string Message, bool Deduplicated, bool Merged, string Blake3Hash) StoreAsset(
        byte[] assetBytes,
        string? fileExtensionOrPath,
        string? metadataHeadersJson = null,
        string? authorPublicKey = null,
        string? authorSignature = null)
    {
        if (assetBytes == null || assetBytes.Length == 0)
        {
            return (false, "Empty asset payload.", false, false, string.Empty);
        }

        if (assetBytes.Length > MaximumAssetSizeBytes)
        {
            return (false, $"Asset exceeds maximum size limit of {MaximumAssetSizeBytes} bytes.", false, false, string.Empty);
        }

        string extension = Path.GetExtension(fileExtensionOrPath ?? string.Empty).ToLowerInvariant();
        string canonicalBlake3 = RealmMetadataHelper.ComputeBlake3(assetBytes, extension);
        string normalizedHash = NormalizeBlake3Hash(canonicalBlake3);

        object fileLock = _fileLocks.GetOrAdd(normalizedHash, _ => new object());

        lock (fileLock)
        {
            string? existingFilePath = FindAssetFilePath(normalizedHash);

            if (existingFilePath != null && File.Exists(existingFilePath))
            {
                bool merged = false;
                if (!string.IsNullOrWhiteSpace(metadataHeadersJson))
                {
                    merged = UpdateExistingAssetHeaders(existingFilePath, normalizedHash, metadataHeadersJson, authorPublicKey, authorSignature);
                }

                return (true, "Asset already exists (deduplicated).", true, merged, normalizedHash);
            }

            if (!CheckFreeDiskSpaceAcceptingUploads())
            {
                return (false, "Upload rejected: available disk space is less than 10%.", false, false, normalizedHash);
            }

            string shardDirectory = Path.Combine(_assetsDirectory, normalizedHash.Substring(0, 2));
            Directory.CreateDirectory(shardDirectory);

            string finalExtension = !string.IsNullOrEmpty(extension) ? extension : ".bin";
            string finalFilePath = Path.Combine(shardDirectory, $"{normalizedHash}{finalExtension}");
            string temporaryFilePath = Path.Combine(shardDirectory, $"{Guid.NewGuid():N}_tmp{finalExtension}");

            byte[] bytesToWrite = assetBytes;
            string? metadataToEmbed = metadataHeadersJson;

            if (!string.IsNullOrWhiteSpace(authorPublicKey) && !string.IsNullOrWhiteSpace(authorSignature))
            {
                metadataToEmbed = InjectAuthorKeysIntoMetadata(metadataToEmbed, authorPublicKey, authorSignature);
            }

            if (!string.IsNullOrWhiteSpace(metadataToEmbed) && RealmMetadataHelper.SupportsMetadata(finalExtension))
            {
                bytesToWrite = RealmMetadataHelper.SyncBlake3MetadataBytes(assetBytes, finalExtension);
            }

            File.WriteAllBytes(temporaryFilePath, bytesToWrite);

            if (!string.IsNullOrWhiteSpace(metadataToEmbed) && RealmMetadataHelper.SupportsMetadata(finalExtension))
            {
                try
                {
                    RealmMetadataHelper.AddMetadata(temporaryFilePath, metadataToEmbed);
                }
                catch
                {
                }
            }

            File.Move(temporaryFilePath, finalFilePath, true);

            string? finalMetadata = RealmMetadataHelper.ExtractMetadata(finalFilePath) ?? metadataToEmbed;
            if (!string.IsNullOrWhiteSpace(finalMetadata))
            {
                UpdateSidecarCache(normalizedHash, finalMetadata);
            }

            return (true, "Asset stored successfully.", false, false, normalizedHash);
        }
    }

    private bool UpdateExistingAssetHeaders(
        string existingFilePath,
        string normalizedHash,
        string incomingMetadataJson,
        string? authorPublicKey,
        string? authorSignature)
    {
        try
        {
            string? existingMetadata = GetAssetMetadata(normalizedHash);
            bool isAuthorized = false;

            if (!string.IsNullOrWhiteSpace(authorPublicKey) && !string.IsNullOrWhiteSpace(authorSignature))
            {
                bool signatureValid = AuthorSignatureHelper.VerifySignature(authorPublicKey, normalizedHash, authorSignature);
                if (signatureValid)
                {
                    string? existingAuthorKey = ExtractAuthorPublicKey(existingMetadata);
                    if (string.IsNullOrEmpty(existingAuthorKey) || string.Equals(existingAuthorKey, authorPublicKey, StringComparison.OrdinalIgnoreCase))
                    {
                        isAuthorized = true;
                    }
                }
            }

            string incomingWithKeys = InjectAuthorKeysIntoMetadata(incomingMetadataJson, authorPublicKey, authorSignature);
            string mergedMetadata = AuthorSignatureHelper.MergeMetadataHeaders(existingMetadata, incomingWithKeys, isAuthorized);

            if (RealmMetadataHelper.SupportsMetadata(existingFilePath))
            {
                try
                {
                    RealmMetadataHelper.AddMetadata(existingFilePath, mergedMetadata);
                }
                catch
                {
                }
            }

            UpdateSidecarCache(normalizedHash, mergedMetadata);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? ExtractAuthorPublicKey(string? metadataJson)
    {
        if (string.IsNullOrWhiteSpace(metadataJson))
        {
            return null;
        }

        try
        {
            using var document = JsonDocument.Parse(metadataJson);
            if (document.RootElement.TryGetProperty("AuthorPublicKey", out var property))
            {
                return property.GetString();
            }
            if (document.RootElement.TryGetProperty("author_public_key", out var snakeProperty))
            {
                return snakeProperty.GetString();
            }
        }
        catch
        {
        }

        return null;
    }

    private static string InjectAuthorKeysIntoMetadata(string? metadataJson, string? authorPublicKey, string? authorSignature)
    {
        JsonObject jsonObject;
        if (!string.IsNullOrWhiteSpace(metadataJson))
        {
            try
            {
                jsonObject = JsonNode.Parse(metadataJson)?.AsObject() ?? new JsonObject();
            }
            catch
            {
                jsonObject = new JsonObject();
            }
        }
        else
        {
            jsonObject = new JsonObject();
        }

        if (!string.IsNullOrWhiteSpace(authorPublicKey))
        {
            jsonObject["AuthorPublicKey"] = authorPublicKey;
        }

        if (!string.IsNullOrWhiteSpace(authorSignature))
        {
            jsonObject["AuthorSignature"] = authorSignature;
        }

        return jsonObject.ToJsonString();
    }

    private string GetSidecarCachePath(string normalizedHash)
    {
        string shardDirectory = Path.Combine(_sidecarCacheDirectory, normalizedHash.Substring(0, 2));
        Directory.CreateDirectory(shardDirectory);
        return Path.Combine(shardDirectory, $"{normalizedHash}.json");
    }

    private void UpdateSidecarCache(string normalizedHash, string metadataJson)
    {
        try
        {
            string path = GetSidecarCachePath(normalizedHash);
            File.WriteAllText(path, metadataJson, Encoding.UTF8);
        }
        catch
        {
        }
    }

    public void RebuildSidecarCache()
    {
        if (!Directory.Exists(_assetsDirectory))
        {
            return;
        }

        string[] files = Directory.GetFiles(_assetsDirectory, "*.*", SearchOption.AllDirectories);
        foreach (string file in files)
        {
            string normalizedHash = NormalizeBlake3Hash(Path.GetFileName(file));
            string? metadata = RealmMetadataHelper.ExtractMetadata(file);
            if (!string.IsNullOrWhiteSpace(metadata))
            {
                UpdateSidecarCache(normalizedHash, metadata);
            }
        }
    }

    public void DeleteSidecarCache()
    {
        if (Directory.Exists(_sidecarCacheDirectory))
        {
            Directory.Delete(_sidecarCacheDirectory, true);
            Directory.CreateDirectory(_sidecarCacheDirectory);
        }
    }

    public HashSet<string> GetAllStoredHashes()
    {
        var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(_assetsDirectory))
        {
            return hashes;
        }

        string[] files = Directory.GetFiles(_assetsDirectory, "*.*", SearchOption.AllDirectories);
        foreach (string file in files)
        {
            string normalizedHash = NormalizeBlake3Hash(Path.GetFileName(file));
            if (normalizedHash.Length == 64)
            {
                hashes.Add(normalizedHash);
            }
        }

        return hashes;
    }
}
