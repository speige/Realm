using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ZstdSharp;

namespace Realm.Shared.Distribution;

public class ZstdAssetBundleHelper
{
    public const int ChunkSize = 64 * 1024;
    public const int PacketChunkSize = 64 * 1024;
    public const int MaxBundleChunkSize = 100 * 1024 * 1024;

    public static List<List<(string AssetKey, byte[] Data, string? Metadata)>> PartitionAssetsIntoChunks(
        IEnumerable<(string AssetKey, byte[] Data, string? Metadata)> assets,
        long maxChunkBytes = MaxBundleChunkSize)
    {
        var result = new List<List<(string AssetKey, byte[] Data, string? Metadata)>>();
        var currentChunk = new List<(string AssetKey, byte[] Data, string? Metadata)>();
        long currentChunkBytes = 0;

        foreach (var item in assets)
        {
            long estimatedAssetBytes = (item.Data?.Length ?? 0) + (item.AssetKey?.Length ?? 0) * 2 + (item.Metadata?.Length ?? 0) * 2 + 16;
            if (currentChunk.Count > 0 && currentChunkBytes + estimatedAssetBytes > maxChunkBytes)
            {
                result.Add(currentChunk);
                currentChunk = new List<(string AssetKey, byte[] Data, string? Metadata)>();
                currentChunkBytes = 0;
            }

            currentChunk.Add(item);
            currentChunkBytes += estimatedAssetBytes;
        }

        if (currentChunk.Count > 0)
        {
            result.Add(currentChunk);
        }

        return result;
    }

    public static void CreateBundleToFile(
        string destinationFilePath,
        IEnumerable<(string AssetKey, byte[] Data, string? Metadata)> assets,
        int compressionLevel = 1)
    {
        string? dir = Path.GetDirectoryName(destinationFilePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        using var fileStream = new FileStream(destinationFilePath, FileMode.Create, FileAccess.Write, FileShare.None, ChunkSize);
        using var zstdStream = new CompressionStream(fileStream, compressionLevel);
        using var writer = new BinaryWriter(zstdStream, Encoding.UTF8, leaveOpen: true);

        var assetList = assets as IList<(string, byte[], string?)> ?? new List<(string, byte[], string?)>(assets);
        writer.Write(assetList.Count);

        foreach (var (assetKey, data, metadata) in assetList)
        {
            writer.Write(assetKey);
            writer.Write(metadata ?? string.Empty);
            writer.Write(data.Length);
            writer.Write(data);
        }

        writer.Flush();
        zstdStream.Flush();
    }

    public static List<(string AssetKey, byte[] Data, string? Metadata)> ExtractBundleFromFile(string sourceFilePath)
    {
        using var fileStream = new FileStream(sourceFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, ChunkSize);
        using var zstdStream = new DecompressionStream(fileStream);
        using var reader = new BinaryReader(zstdStream, Encoding.UTF8, leaveOpen: true);

        int count = reader.ReadInt32();
        var result = new List<(string AssetKey, byte[] Data, string? Metadata)>(count);

        for (int i = 0; i < count; i++)
        {
            string assetKey = reader.ReadString();
            string metadata = reader.ReadString();
            int dataLength = reader.ReadInt32();
            byte[] data = ReadExactBytes(reader, dataLength);

            result.Add((assetKey, data, string.IsNullOrEmpty(metadata) ? null : metadata));
        }

        return result;
    }

    public static byte[] CreateBundleBytes(
        IEnumerable<(string AssetKey, byte[] Data, string? Metadata)> assets,
        int compressionLevel = 1)
    {
        using var memoryStream = new MemoryStream();
        using (var zstdStream = new CompressionStream(memoryStream, compressionLevel, leaveOpen: true))
        using (var writer = new BinaryWriter(zstdStream, Encoding.UTF8, leaveOpen: true))
        {
            var assetList = assets as IList<(string, byte[], string?)> ?? new List<(string, byte[], string?)>(assets);
            writer.Write(assetList.Count);

            foreach (var (assetKey, data, metadata) in assetList)
            {
                writer.Write(assetKey);
                writer.Write(metadata ?? string.Empty);
                writer.Write(data.Length);
                writer.Write(data);
            }
        }

        return memoryStream.ToArray();
    }

    public static List<(string AssetKey, byte[] Data, string? Metadata)> ExtractBundleBytes(byte[] compressedBytes)
    {
        using var memoryStream = new MemoryStream(compressedBytes);
        using var zstdStream = new DecompressionStream(memoryStream);
        using var reader = new BinaryReader(zstdStream, Encoding.UTF8, leaveOpen: true);

        int count = reader.ReadInt32();
        var result = new List<(string AssetKey, byte[] Data, string? Metadata)>(count);

        for (int i = 0; i < count; i++)
        {
            string assetKey = reader.ReadString();
            string metadata = reader.ReadString();
            int dataLength = reader.ReadInt32();
            byte[] data = ReadExactBytes(reader, dataLength);

            result.Add((assetKey, data, string.IsNullOrEmpty(metadata) ? null : metadata));
        }

        return result;
    }

    private static byte[] ReadExactBytes(BinaryReader reader, int count)
    {
        if (count == 0) return Array.Empty<byte>();
        byte[] buffer = new byte[count];
        int totalRead = 0;
        while (totalRead < count)
        {
            int read = reader.Read(buffer, totalRead, count - totalRead);
            if (read <= 0)
            {
                throw new EndOfStreamException($"Expected to read {count} bytes, but reached end of stream after {totalRead} bytes.");
            }
            totalRead += read;
        }
        return buffer;
    }
}
