using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
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

		var layers = new List<byte[]>();
		if (offset + 4 <= bytes.Length)
		{
			uint layerCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, 4));
			offset += 4;

			for (int i = 0; i < layerCount; i++)
			{
				if (offset + 4 > bytes.Length) break;
				uint layerLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, 4));
				offset += 4;

				if (offset + (int)layerLength > bytes.Length) break;
				byte[] layerData = bytes.Slice(offset, (int)layerLength).ToArray();
				layers.Add(layerData);
				offset += (int)layerLength;
			}
		}

		return (metadataJson, layers, version);
	}

	public static byte[] Build(string? metadataJson, IList<byte[]> layers, uint version = CurrentVersion)
	{
		using var memoryStream = new MemoryStream();
		using var writer = new BinaryWriter(memoryStream);

		RealmContainerHeader.WriteHeader(writer, Magic, metadataJson, version);

		writer.Write((uint)(layers?.Count ?? 0));
		if (layers != null)
		{
			foreach (var layer in layers)
			{
				writer.Write((uint)(layer?.Length ?? 0));
				if (layer != null && layer.Length > 0)
				{
					writer.Write(layer);
				}
			}
		}

		return memoryStream.ToArray();
	}

	public static byte[]? GetLayer(ReadOnlySpan<byte> bytes, int layerIndex)
	{
		if (!IsRtexBytes(bytes)) return null;

		uint metadataLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(8, 4));
		int offset = RealmContainerHeader.MinimumHeaderLength + (int)metadataLength;

		if (offset + 4 > bytes.Length) return null;
		uint layerCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, 4));
		offset += 4;

		if (layerIndex < 0 || layerIndex >= layerCount) return null;

		for (int i = 0; i < layerCount; i++)
		{
			if (offset + 4 > bytes.Length) return null;
			uint layerLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, 4));
			offset += 4;

			if (i == layerIndex)
			{
				if (offset + (int)layerLength > bytes.Length) return null;
				return bytes.Slice(offset, (int)layerLength).ToArray();
			}

			offset += (int)layerLength;
		}

		return null;
	}

	public static string? ExtractMetadata(ReadOnlySpan<byte> bytes)
	{
		return RealmContainerHeader.ExtractMetadata(bytes, Magic);
	}

	public static byte[] SetMetadata(ReadOnlySpan<byte> bytes, string? newMetadataJson)
	{
		return RealmContainerHeader.SetMetadata(bytes, Magic, newMetadataJson, "RTEX");
	}
}
