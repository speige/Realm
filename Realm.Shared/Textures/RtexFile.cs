using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Realm.Shared.Textures;

public static class RtexFile
{
	public static readonly byte[] Magic = [0x52, 0x54, 0x45, 0x58]; // "RTEX"
	public const uint CurrentVersion = 1;

	public static bool IsRtexBytes(ReadOnlySpan<byte> bytes)
	{
		if (bytes.Length < 16) return false;
		return bytes.Slice(0, 4).SequenceEqual(Magic);
	}

	public static (string? MetadataJson, List<byte[]> Layers, uint Version) Parse(ReadOnlySpan<byte> bytes)
	{
		if (!IsRtexBytes(bytes))
		{
			throw new InvalidOperationException("Invalid RTEX signature.");
		}

		uint version = BitConverter.ToUInt32(bytes.Slice(4, 4));
		uint metadataLen = BitConverter.ToUInt32(bytes.Slice(8, 4));

		int offset = 12;
		string? metadataJson = null;
		if (metadataLen > 0 && offset + (int)metadataLen <= bytes.Length)
		{
			metadataJson = Encoding.UTF8.GetString(bytes.Slice(offset, (int)metadataLen));
			offset += (int)metadataLen;
		}

		var layers = new List<byte[]>();
		if (offset + 4 <= bytes.Length)
		{
			uint layerCount = BitConverter.ToUInt32(bytes.Slice(offset, 4));
			offset += 4;

			for (int i = 0; i < layerCount; i++)
			{
				if (offset + 4 > bytes.Length) break;
				uint layerLen = BitConverter.ToUInt32(bytes.Slice(offset, 4));
				offset += 4;

				if (offset + (int)layerLen > bytes.Length) break;
				byte[] layerData = bytes.Slice(offset, (int)layerLen).ToArray();
				layers.Add(layerData);
				offset += (int)layerLen;
			}
		}

		return (metadataJson, layers, version);
	}

	public static byte[] Build(string? metadataJson, IList<byte[]> layers, uint version = CurrentVersion)
	{
		byte[] metaBytes = !string.IsNullOrEmpty(metadataJson)
			? Encoding.UTF8.GetBytes(metadataJson)
			: Array.Empty<byte>();

		using var ms = new MemoryStream();
		using var writer = new BinaryWriter(ms);

		writer.Write(Magic);
		writer.Write(version);
		writer.Write((uint)metaBytes.Length);
		if (metaBytes.Length > 0)
		{
			writer.Write(metaBytes);
		}

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

		return ms.ToArray();
	}

	public static byte[]? GetLayer(ReadOnlySpan<byte> bytes, int layerIndex)
	{
		if (!IsRtexBytes(bytes)) return null;

		uint metadataLen = BitConverter.ToUInt32(bytes.Slice(8, 4));
		int offset = 12 + (int)metadataLen;

		if (offset + 4 > bytes.Length) return null;
		uint layerCount = BitConverter.ToUInt32(bytes.Slice(offset, 4));
		offset += 4;

		if (layerIndex < 0 || layerIndex >= layerCount) return null;

		for (int i = 0; i < layerCount; i++)
		{
			if (offset + 4 > bytes.Length) return null;
			uint layerLen = BitConverter.ToUInt32(bytes.Slice(offset, 4));
			offset += 4;

			if (i == layerIndex)
			{
				if (offset + (int)layerLen > bytes.Length) return null;
				return bytes.Slice(offset, (int)layerLen).ToArray();
			}

			offset += (int)layerLen;
		}

		return null;
	}

	public static string? ExtractMetadata(ReadOnlySpan<byte> bytes)
	{
		if (!IsRtexBytes(bytes)) return null;
		uint metadataLen = BitConverter.ToUInt32(bytes.Slice(8, 4));
		if (metadataLen == 0 || 12 + (int)metadataLen > bytes.Length) return null;
		return Encoding.UTF8.GetString(bytes.Slice(12, (int)metadataLen));
	}

	public static byte[] SetMetadata(ReadOnlySpan<byte> bytes, string? newMetadataJson)
	{
		var (_, layers, version) = Parse(bytes);
		return Build(newMetadataJson, layers, version);
	}
}
