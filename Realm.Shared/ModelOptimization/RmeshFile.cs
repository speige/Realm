using System;
using System.Buffers.Binary;
using System.IO;
using Realm.Shared.Metadata;

namespace Realm.Shared.ModelOptimization;

public static class RmeshFile
{
	public static readonly byte[] Magic = [0x52, 0x4D, 0x53, 0x48]; // "RMSH"
	public const uint CurrentVersion = 1;

	public static bool IsRmeshBytes(ReadOnlySpan<byte> bytes)
	{
		return RealmContainerHeader.HasMagic(bytes, Magic);
	}

	public static (string? MetadataJson, byte[] GlbBytes, uint Version) Parse(ReadOnlySpan<byte> bytes)
	{
		var (version, metadataJson, offset) = RealmContainerHeader.ReadHeader(bytes, Magic, "RMESH");

		byte[] glbBytes = Array.Empty<byte>();
		if (offset + 4 <= bytes.Length)
		{
			uint glbLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, 4));
			offset += 4;
			if (glbLength > 0 && offset + (int)glbLength <= bytes.Length)
			{
				glbBytes = bytes.Slice(offset, (int)glbLength).ToArray();
			}
			else if (offset < bytes.Length)
			{
				glbBytes = bytes.Slice(offset).ToArray();
			}
		}
		else if (offset < bytes.Length)
		{
			glbBytes = bytes.Slice(offset).ToArray();
		}

		return (metadataJson, glbBytes, version);
	}

	public static byte[] Build(string? metadataJson, byte[] glbBytes, uint version = CurrentVersion)
	{
		using var memoryStream = new MemoryStream();
		using var writer = new BinaryWriter(memoryStream);

		RealmContainerHeader.WriteHeader(writer, Magic, metadataJson, version);

		writer.Write((uint)(glbBytes?.Length ?? 0));
		if (glbBytes != null && glbBytes.Length > 0)
		{
			writer.Write(glbBytes);
		}

		return memoryStream.ToArray();
	}

	public static byte[]? GetGlbBytes(ReadOnlySpan<byte> bytes)
	{
		if (!IsRmeshBytes(bytes)) return null;

		uint metadataLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(8, 4));
		int offset = RealmContainerHeader.MinimumHeaderLength + (int)metadataLength;

		if (offset + 4 > bytes.Length) return null;
		uint glbLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, 4));
		offset += 4;

		if (offset + (int)glbLength > bytes.Length)
		{
			if (offset < bytes.Length)
			{
				return bytes.Slice(offset).ToArray();
			}
			return null;
		}

		return bytes.Slice(offset, (int)glbLength).ToArray();
	}

	public static string? ExtractMetadata(ReadOnlySpan<byte> bytes)
	{
		return RealmContainerHeader.ExtractMetadata(bytes, Magic);
	}

	public static byte[] SetMetadata(ReadOnlySpan<byte> bytes, string? newMetadataJson)
	{
		return RealmContainerHeader.SetMetadata(bytes, Magic, newMetadataJson, "RMESH");
	}
}
