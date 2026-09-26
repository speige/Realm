using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using Realm.Shared.Metadata;
using ZstdSharp;

namespace Realm.Shared.Distribution;

public class RmapHeaderInfo
{
    public string MapName { get; set; } = string.Empty;
    public string Version { get; set; } = "1.0.0";
    public string GameBuildNumber { get; set; } = string.Empty;
    public string Author { get; set; } = "Unknown";
    public string Description { get; set; } = string.Empty;
    public List<string> Tags { get; set; } = new();
}

public static class MapArchiveHelper
{
    public static readonly byte[] RmapMagic = [0x52, 0x4D, 0x41, 0x50]; // "RMAP"
    public const uint CurrentVersion = 1;

    public static void CreateRmapArchive(
        string sourceDirectory,
        string destinationRmapPath,
        Action<float, string>? progressCallback = null,
        int compressionLevel = 1)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException($"Source directory '{sourceDirectory}' does not exist.");
        }

        string? destinationDir = Path.GetDirectoryName(destinationRmapPath);
        if (!string.IsNullOrEmpty(destinationDir) && !Directory.Exists(destinationDir))
        {
            Directory.CreateDirectory(destinationDir);
        }

        if (File.Exists(destinationRmapPath))
        {
            File.Delete(destinationRmapPath);
        }

        var headerInfo = ExtractRmapHeaderInfoFromDirectory(sourceDirectory);
        string headerJson = JsonSerializer.Serialize(headerInfo);

        var allFiles = Directory.GetFiles(sourceDirectory, "*.*", SearchOption.AllDirectories);
        var filesToArchive = new List<string>(allFiles.Length);
        foreach (var file in allFiles)
        {
            string relativePath = Path.GetRelativePath(sourceDirectory, file).Replace('\\', '/');
            if (relativePath.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) ||
                relativePath.StartsWith(".backups/", StringComparison.OrdinalIgnoreCase) ||
                relativePath.StartsWith("obj/", StringComparison.OrdinalIgnoreCase) ||
                relativePath.Contains("/obj/", StringComparison.OrdinalIgnoreCase) ||
                relativePath.StartsWith(".godot/", StringComparison.OrdinalIgnoreCase) ||
                relativePath.StartsWith(".sidecarcache/", StringComparison.OrdinalIgnoreCase) ||
                relativePath.StartsWith(".vscode/", StringComparison.OrdinalIgnoreCase) ||
                relativePath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
                relativePath.EndsWith(".rmap", StringComparison.OrdinalIgnoreCase) ||
                relativePath.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) ||
                relativePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                relativePath.EndsWith(".rar", StringComparison.OrdinalIgnoreCase) ||
                relativePath.EndsWith(".tar", StringComparison.OrdinalIgnoreCase) ||
                relativePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ||
                relativePath.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) ||
                relativePath.EndsWith(".backup", StringComparison.OrdinalIgnoreCase) ||
                relativePath.EndsWith(".rkey", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFileName(relativePath), "authorship_key.pem", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fileInfo = new FileInfo(file);
            if (!fileInfo.Exists || fileInfo.Length == 0)
            {
                continue;
            }

            filesToArchive.Add(file);
        }

        filesToArchive.Sort((a, b) =>
        {
            string relA = Path.GetRelativePath(sourceDirectory, a).Replace('\\', '/');
            string relB = Path.GetRelativePath(sourceDirectory, b).Replace('\\', '/');
            int priorityA = GetFilePriority(relA);
            int priorityB = GetFilePriority(relB);
            if (priorityA != priorityB) return priorityA.CompareTo(priorityB);
            return string.Compare(relA, relB, StringComparison.OrdinalIgnoreCase);
        });

        using var fileStream = new FileStream(destinationRmapPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920);
        using (var writer = new BinaryWriter(fileStream, Encoding.UTF8, leaveOpen: true))
        {
            RealmContainerHeader.WriteHeader(writer, RmapMagic, headerJson, version: CurrentVersion);
        }

        using var zstdStream = new CompressionStream(fileStream, compressionLevel);
        using var zstdWriter = new BinaryWriter(zstdStream, Encoding.UTF8, leaveOpen: true);

        zstdWriter.Write(filesToArchive.Count);
        byte[] buffer = new byte[81920];
        int total = filesToArchive.Count;

        for (int i = 0; i < total; i++)
        {
            string file = filesToArchive[i];
            string relativePath = Path.GetRelativePath(sourceDirectory, file).Replace('\\', '/');
            progressCallback?.Invoke((float)(i + 1) / Math.Max(1, total), relativePath);

            var fileInfo = new FileInfo(file);
            zstdWriter.Write(relativePath);
            zstdWriter.Write(fileInfo.Length);

            using var inputFs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, FileOptions.SequentialScan);
            long remaining = fileInfo.Length;
            while (remaining > 0)
            {
                int toRead = (int)Math.Min(buffer.Length, remaining);
                int read = inputFs.Read(buffer, 0, toRead);
                if (read <= 0) break;
                zstdStream.Write(buffer, 0, read);
                remaining -= read;
            }
        }

        zstdWriter.Flush();
        zstdStream.Flush();
    }

    public static RmapHeaderInfo? ReadHeaderFromRmap(string rmapFilePath)
    {
        if (string.IsNullOrWhiteSpace(rmapFilePath) || !File.Exists(rmapFilePath)) return null;
        string? metadataJson = RealmContainerHeader.ExtractMetadataFromFile(rmapFilePath, RmapMagic);
        if (string.IsNullOrEmpty(metadataJson)) return null;
        try
        {
            return JsonSerializer.Deserialize<RmapHeaderInfo>(metadataJson);
        }
        catch
        {
            return null;
        }
    }

    public static (uint Version, string? MetadataJson, int PayloadOffset) ReadRmapHeader(Stream stream)
    {
        Span<byte> header = stackalloc byte[RealmContainerHeader.MinimumHeaderLength];
        int bytesRead = 0;
        while (bytesRead < RealmContainerHeader.MinimumHeaderLength)
        {
            int r = stream.Read(header.Slice(bytesRead, RealmContainerHeader.MinimumHeaderLength - bytesRead));
            if (r <= 0) throw new InvalidOperationException("Invalid .rmap header (truncated).");
            bytesRead += r;
        }

        if (!RealmContainerHeader.HasMagic(header, RmapMagic))
        {
            throw new InvalidOperationException("Invalid .rmap magic signature.");
        }

        uint version = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(4, 4));
        uint metadataLength = System.Buffers.Binary.BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(8, 4));

        string? metadataJson = null;
        if (metadataLength > 0)
        {
            byte[] metaBytes = new byte[metadataLength];
            int metaRead = 0;
            while (metaRead < metadataLength)
            {
                int r = stream.Read(metaBytes, metaRead, (int)metadataLength - metaRead);
                if (r <= 0) throw new InvalidOperationException("Invalid .rmap header (truncated metadata).");
                metaRead += r;
            }
            metadataJson = Encoding.UTF8.GetString(metaBytes);
        }

        int payloadOffset = RealmContainerHeader.MinimumHeaderLength + (int)metadataLength;
        return (version, metadataJson, payloadOffset);
    }

    public static (string? ManifestJson, string RootPrefix) ReadManifestFromArchive(string archiveFilePath)
    {
        if (string.IsNullOrWhiteSpace(archiveFilePath) || !File.Exists(archiveFilePath))
        {
            return (null, string.Empty);
        }

        using var fileStream = new FileStream(archiveFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
        var (version, metaJson, payloadOffset) = ReadRmapHeader(fileStream);
        fileStream.Seek(payloadOffset, SeekOrigin.Begin);

        using var zstdStream = new DecompressionStream(fileStream);
        using var bufferedStream = new BufferedStream(zstdStream, 65536);
        using var reader = new BinaryReader(bufferedStream, Encoding.UTF8, leaveOpen: true);

        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            string entryKey = reader.ReadString();
            long fileLength = reader.ReadInt64();

            string norm = entryKey.Replace('\\', '/');
            if (norm.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase) &&
                (norm.Equals("manifest.json", StringComparison.OrdinalIgnoreCase) || norm.EndsWith("/manifest.json", StringComparison.OrdinalIgnoreCase)))
            {
                string rootPrefix = norm.Length > "manifest.json".Length
                    ? norm.Substring(0, norm.Length - "manifest.json".Length)
                    : string.Empty;

                using var boundedStream = new BoundedStream(bufferedStream, fileLength);
                using var textReader = new StreamReader(boundedStream, Encoding.UTF8);
                string manifestJson = textReader.ReadToEnd();
                return (manifestJson, rootPrefix);
            }

            using var skipStream = new BoundedStream(bufferedStream, fileLength);
            skipStream.SkipRemaining();
        }

        return (null, string.Empty);
    }

    public static void ProcessArchiveCandidates(
        string archiveFilePath,
        MapManifest manifest,
        string rootPrefix,
        Action<string, Stream> candidateHandler)
    {
        if (string.IsNullOrWhiteSpace(archiveFilePath) || !File.Exists(archiveFilePath))
        {
            return;
        }

        using var fileStream = new FileStream(archiveFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
        var (version, metaJson, payloadOffset) = ReadRmapHeader(fileStream);
        fileStream.Seek(payloadOffset, SeekOrigin.Begin);

        using var zstdStream = new DecompressionStream(fileStream);
        using var bufferedStream = new BufferedStream(zstdStream, 65536);
        using var reader = new BinaryReader(bufferedStream, Encoding.UTF8, leaveOpen: true);

        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            string entryKey = reader.ReadString();
            long fileLength = reader.ReadInt64();

            string norm = entryKey.Replace('\\', '/');
            if (!string.IsNullOrEmpty(rootPrefix) && norm.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
            {
                norm = norm.Substring(rootPrefix.Length);
            }
            norm = norm.TrimStart('/');

            using var boundedStream = new BoundedStream(bufferedStream, fileLength);
            if (manifest.IsCandidateFile(norm))
            {
                candidateHandler(norm, boundedStream);
            }
            boundedStream.SkipRemaining();
        }
    }

    public static void ExtractArchive(string archiveFilePath, string targetDirectory)
    {
        if (string.IsNullOrWhiteSpace(archiveFilePath) || !File.Exists(archiveFilePath))
        {
            throw new FileNotFoundException($"Archive file '{archiveFilePath}' not found.");
        }

        if (!Directory.Exists(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        using var fileStream = new FileStream(archiveFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
        var (version, metaJson, payloadOffset) = ReadRmapHeader(fileStream);
        fileStream.Seek(payloadOffset, SeekOrigin.Begin);

        using var zstdStream = new DecompressionStream(fileStream);
        using var bufferedStream = new BufferedStream(zstdStream, 65536);
        using var reader = new BinaryReader(bufferedStream, Encoding.UTF8, leaveOpen: true);

        int count = reader.ReadInt32();
        for (int i = 0; i < count; i++)
        {
            string entryKey = reader.ReadString();
            long fileLength = reader.ReadInt64();

            if (string.IsNullOrWhiteSpace(entryKey))
            {
                using var skipStream = new BoundedStream(bufferedStream, fileLength);
                skipStream.SkipRemaining();
                continue;
            }

            string destinationPath = Path.Combine(targetDirectory, entryKey.Replace('/', Path.DirectorySeparatorChar));
            string? destinationDir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destinationDir) && !Directory.Exists(destinationDir))
            {
                Directory.CreateDirectory(destinationDir);
            }

            using (var outStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 81920))
            using (var boundedStream = new BoundedStream(bufferedStream, fileLength))
            {
                boundedStream.CopyTo(outStream, 81920);
            }
        }
    }

    private static RmapHeaderInfo ExtractRmapHeaderInfoFromDirectory(string sourceDirectory)
    {
        string mapName = Path.GetFileName(sourceDirectory);
        string version = "1.0.0";
        string gameBuildNumber = RealmVersion.GameBuildNumber;
        string author = "Unknown";
        string description = string.Empty;
        var tags = new List<string>();

        string manifestPath = Path.Combine(sourceDirectory, "manifest.json");
        if (File.Exists(manifestPath))
        {
            try
            {
                string jsonText = File.ReadAllText(manifestPath);
                var manifest = MapManifest.LoadFromJson(jsonText);
                if (manifest != null)
                {
                    if (!string.IsNullOrWhiteSpace(manifest.MapName)) mapName = manifest.MapName.Trim();
                    if (!string.IsNullOrWhiteSpace(manifest.Version)) version = manifest.Version.Trim();
                    if (!string.IsNullOrWhiteSpace(manifest.Author)) author = manifest.Author.Trim();
                    if (!string.IsNullOrWhiteSpace(manifest.Description)) description = manifest.Description;
                    if (manifest.Tags != null) tags.AddRange(manifest.Tags);
                }

                using var doc = JsonDocument.Parse(jsonText);
                if (doc.RootElement.TryGetProperty("GameBuildNumber", out var gbnProp) && gbnProp.ValueKind == JsonValueKind.String)
                {
                    string? gbn = gbnProp.GetString();
                    if (!string.IsNullOrWhiteSpace(gbn)) gameBuildNumber = gbn.Trim();
                }
            }
            catch
            {
            }
        }

        string metadataPath = Path.Combine(sourceDirectory, "metadata.json");
        if (File.Exists(metadataPath))
        {
            try
            {
                string metaText = File.ReadAllText(metadataPath);
                using var metaDoc = JsonDocument.Parse(metaText);
                if (metaDoc.RootElement.TryGetProperty("GameBuildNumber", out var gbnProp) && gbnProp.ValueKind == JsonValueKind.String)
                {
                    string? gbn = gbnProp.GetString();
                    if (!string.IsNullOrWhiteSpace(gbn)) gameBuildNumber = gbn.Trim();
                }
            }
            catch
            {
            }
        }

        return new RmapHeaderInfo
        {
            MapName = mapName,
            Version = version,
            GameBuildNumber = string.IsNullOrWhiteSpace(gameBuildNumber) ? RealmVersion.GameBuildNumber : gameBuildNumber,
            Author = author,
            Description = description,
            Tags = tags
        };
    }

    private static int GetFilePriority(string relPath)
    {
        if (relPath.Equals("manifest.json", StringComparison.OrdinalIgnoreCase)) return 0;
        if (relPath.Equals("metadata.json", StringComparison.OrdinalIgnoreCase)) return 1;
        if (relPath.Equals("map.json", StringComparison.OrdinalIgnoreCase)) return 2;
        return 10;
    }

    private class BoundedStream : Stream
    {
        private readonly Stream _baseStream;
        private long _bytesRemaining;

        public BoundedStream(Stream baseStream, long length)
        {
            _baseStream = baseStream ?? throw new ArgumentNullException(nameof(baseStream));
            _bytesRemaining = length;
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _bytesRemaining;
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_bytesRemaining <= 0) return 0;
            int toRead = (int)Math.Min(count, _bytesRemaining);
            int read = _baseStream.Read(buffer, offset, toRead);
            _bytesRemaining -= read;
            return read;
        }

        public override int Read(Span<byte> buffer)
        {
            if (_bytesRemaining <= 0) return 0;
            int toRead = (int)Math.Min(buffer.Length, _bytesRemaining);
            int read = _baseStream.Read(buffer.Slice(0, toRead));
            _bytesRemaining -= read;
            return read;
        }

        public void SkipRemaining()
        {
            byte[] skipBuffer = new byte[81920];
            while (_bytesRemaining > 0)
            {
                int toRead = (int)Math.Min(skipBuffer.Length, _bytesRemaining);
                int read = _baseStream.Read(skipBuffer, 0, toRead);
                if (read <= 0) break;
                _bytesRemaining -= read;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                SkipRemaining();
            }
            base.Dispose(disposing);
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
