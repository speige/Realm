using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using Blake3;
using Realm.Shared.Metadata;

namespace Realm.Shared.Textures;

public static class RtexFile
{
	public static readonly byte[] Magic = [0x52, 0x54, 0x45, 0x58]; // "RTEX"
	public const uint CurrentVersion = 1;

	public static bool IsRtexBytes(ReadOnlySpan<byte> bytes)
	{
		return RealmContainerHeader.HasMagic(bytes, Magic);
	}

	public static (string? MetadataJson, List<byte[]> Layers, uint Version) Parse(ReadOnlySpan<byte> bytes)
	{
		var (version, metadataJson, offset) = RealmContainerHeader.ReadHeader(bytes, Magic, "RTEX");
		bool isCompressed = RealmMetadataHelper.ExtractIsCompressed(metadataJson);
		ReadOnlySpan<byte> payloadSpan = bytes.Slice(offset);

		ReadOnlySpan<byte> layersSpan = isCompressed
			? RealmCompressionHelper.Decompress(payloadSpan)
			: payloadSpan;

		var layers = new List<byte[]>();
		int readOffset = 0;
		if (readOffset + 4 <= layersSpan.Length)
		{
			uint layerCount = BinaryPrimitives.ReadUInt32LittleEndian(layersSpan.Slice(readOffset, 4));
			readOffset += 4;

			for (int i = 0; i < layerCount; i++)
			{
				if (readOffset + 4 > layersSpan.Length) break;
				uint layerLength = BinaryPrimitives.ReadUInt32LittleEndian(layersSpan.Slice(readOffset, 4));
				readOffset += 4;

				if (readOffset + (int)layerLength > layersSpan.Length) break;
				byte[] layerData = layersSpan.Slice(readOffset, (int)layerLength).ToArray();
				layers.Add(layerData);
				readOffset += (int)layerLength;
			}
		}

		return (metadataJson, layers, version);
	}

	public static byte[] Build(string? metadataJson, IList<byte[]> layers, bool? compressed = null, uint version = CurrentVersion)
	{
		bool isCompressed = compressed ?? false;
		if (compressed == null && !string.IsNullOrWhiteSpace(metadataJson))
		{
			isCompressed = RealmMetadataHelper.ExtractIsCompressed(metadataJson);
		}

		using var rawPayloadStream = new MemoryStream();
		using var rawWriter = new BinaryWriter(rawPayloadStream);

		rawWriter.Write((uint)(layers?.Count ?? 0));
		if (layers != null)
		{
			foreach (var layer in layers)
			{
				rawWriter.Write((uint)(layer?.Length ?? 0));
				if (layer != null && layer.Length > 0)
				{
					rawWriter.Write(layer);
				}
			}
		}
		rawWriter.Flush();
		byte[] rawPayloadBytes = rawPayloadStream.ToArray();

		byte[] payload = isCompressed
			? RealmCompressionHelper.Compress(rawPayloadBytes, RealmCompressionHelper.DefaultCompressionLevel)
			: rawPayloadBytes;

		string canonicalBlake3 = Hasher.Hash(payload).ToString();

		JsonObject metaObj;
		if (!string.IsNullOrWhiteSpace(metadataJson))
		{
			try
			{
				metaObj = JsonNode.Parse(metadataJson)?.AsObject() ?? new JsonObject();
			}
			catch
			{
				metaObj = new JsonObject();
			}
		}
		else
		{
			metaObj = new JsonObject();
		}

		if (!metaObj.ContainsKey("created_utc") || metaObj["created_utc"] == null)
		{
			metaObj["created_utc"] = DateTime.UtcNow.ToString("O");
		}

		metaObj["format"] = "rtex";
		metaObj["is_compressed"] = isCompressed;
		metaObj["blake3"] = canonicalBlake3;

		using var memoryStream = new MemoryStream();
		using var writer = new BinaryWriter(memoryStream);

		RealmContainerHeader.WriteHeader(writer, Magic, metaObj.ToJsonString(), version);
		if (payload.Length > 0)
		{
			writer.Write(payload);
		}

		return memoryStream.ToArray();
	}

	public static byte[]? GetLayer(ReadOnlySpan<byte> bytes, int layerIndex)
	{
		if (!IsRtexBytes(bytes)) return null;

		if (!RealmContainerHeader.TryReadHeader(bytes, Magic, out uint version, out string? metadataJson, out int payloadOffset))
		{
			return null;
		}

		bool isCompressed = RealmMetadataHelper.ExtractIsCompressed(metadataJson);
		ReadOnlySpan<byte> payloadSpan = bytes.Slice(payloadOffset);

		ReadOnlySpan<byte> layersSpan = isCompressed
			? RealmCompressionHelper.Decompress(payloadSpan)
			: payloadSpan;

		if (layersSpan.Length < 4) return null;
		uint layerCount = BinaryPrimitives.ReadUInt32LittleEndian(layersSpan.Slice(0, 4));
		int offset = 4;

		if (layerIndex < 0 || layerIndex >= layerCount) return null;

		for (int i = 0; i < layerCount; i++)
		{
			if (offset + 4 > layersSpan.Length) return null;
			uint layerLength = BinaryPrimitives.ReadUInt32LittleEndian(layersSpan.Slice(offset, 4));
			offset += 4;

			if (i == layerIndex)
			{
				if (offset + (int)layerLength > layersSpan.Length) return null;
				return layersSpan.Slice(offset, (int)layerLength).ToArray();
			}

			offset += (int)layerLength;
		}

		return null;
	}

	public static byte[]? GetLayer(Stream stream, int layerIndex = 0)
	{
		Span<byte> header = stackalloc byte[RealmContainerHeader.MinimumHeaderLength];
		int bytesRead = 0;
		while (bytesRead < RealmContainerHeader.MinimumHeaderLength)
		{
			int r = stream.Read(header.Slice(bytesRead, RealmContainerHeader.MinimumHeaderLength - bytesRead));
			if (r <= 0) return null;
			bytesRead += r;
		}

		if (!RealmContainerHeader.HasMagic(header, Magic)) return null;

		uint metadataLength = BinaryPrimitives.ReadUInt32LittleEndian(header.Slice(8, 4));
		string? metadataJson = null;
		if (metadataLength > 0)
		{
			byte[] metaBytes = new byte[metadataLength];
			int metaRead = 0;
			while (metaRead < metadataLength)
			{
				int r = stream.Read(metaBytes, metaRead, (int)metadataLength - metaRead);
				if (r <= 0) return null;
				metaRead += r;
			}
			metadataJson = System.Text.Encoding.UTF8.GetString(metaBytes);
		}

		bool isCompressed = RealmMetadataHelper.ExtractIsCompressed(metadataJson);
		if (isCompressed)
		{
			using var memoryStream = new MemoryStream();
			stream.CopyTo(memoryStream);
			return GetLayer(memoryStream.ToArray(), layerIndex);
		}

		Span<byte> uintBuf = stackalloc byte[4];
		if (stream.Read(uintBuf) < 4) return null;
		uint layerCount = BinaryPrimitives.ReadUInt32LittleEndian(uintBuf);
		if (layerIndex < 0 || layerIndex >= layerCount) return null;

		for (int i = 0; i < layerCount; i++)
		{
			if (stream.Read(uintBuf) < 4) return null;
			uint layerLength = BinaryPrimitives.ReadUInt32LittleEndian(uintBuf);
			if (layerLength > 100 * 1024 * 1024) return null;

			if (i == layerIndex)
			{
				byte[] layerBytes = new byte[layerLength];
				int read = 0;
				while (read < layerLength)
				{
					int r = stream.Read(layerBytes, read, (int)layerLength - read);
					if (r <= 0) return null;
					read += r;
				}
				return layerBytes;
			}

			stream.Seek(layerLength, SeekOrigin.Current);
		}

		return null;
	}

	public static byte[]? GetLayerFromFile(string filePath, int layerIndex = 0)
	{
		if (!File.Exists(filePath)) return null;
		try
		{
			using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 8192);
			return GetLayer(stream, layerIndex);
		}
		catch
		{
			return null;
		}
	}

	public static string? ExtractMetadata(ReadOnlySpan<byte> bytes)
	{
		return RealmContainerHeader.ExtractMetadata(bytes, Magic);
	}

	public static string? ExtractMetadata(Stream stream)
	{
		return RealmContainerHeader.ExtractMetadata(stream, Magic);
	}

	public static string? ExtractMetadataFromFile(string filePath)
	{
		return RealmContainerHeader.ExtractMetadataFromFile(filePath, Magic);
	}

	public static byte[] SetMetadata(ReadOnlySpan<byte> bytes, string? newMetadataJson)
	{
		return RealmContainerHeader.SetMetadata(bytes, Magic, newMetadataJson, "RTEX");
	}
}
