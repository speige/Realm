using System;
using System.Buffers.Binary;
using System.IO;
using System.Text;

namespace Realm.Shared.Metadata;

public static class RealmContainerHeader
{
	public const uint DefaultVersion = 1;
	public const int MinimumHeaderLength = 12;

	public static bool HasMagic(ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> expectedMagic)
	{
		if (bytes.Length < MinimumHeaderLength)
		{
			return false;
		}

		return bytes.Slice(0, 4).SequenceEqual(expectedMagic);
	}

	public static bool TryReadHeader(
		ReadOnlySpan<byte> bytes,
		ReadOnlySpan<byte> expectedMagic,
		out uint version,
		out string? metadataJson,
		out int payloadOffset)
	{
		version = 0;
		metadataJson = null;
		payloadOffset = 0;

		if (!HasMagic(bytes, expectedMagic))
		{
			return false;
		}

		version = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(4, 4));
		uint metadataLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(8, 4));

		int offset = MinimumHeaderLength;
		if (metadataLength > 0)
		{
			if (offset + (int)metadataLength > bytes.Length)
			{
				return false;
			}

			metadataJson = Encoding.UTF8.GetString(bytes.Slice(offset, (int)metadataLength));
			offset += (int)metadataLength;
		}

		payloadOffset = offset;
		return true;
	}

	public static (uint Version, string? MetadataJson, int PayloadOffset) ReadHeader(
		ReadOnlySpan<byte> bytes,
		ReadOnlySpan<byte> expectedMagic,
		string formatName = "container")
	{
		if (!TryReadHeader(bytes, expectedMagic, out uint version, out string? metadataJson, out int payloadOffset))
		{
			throw new InvalidOperationException($"Invalid {formatName} signature or truncated header.");
		}

		return (version, metadataJson, payloadOffset);
	}

	public static void WriteHeader(
		BinaryWriter writer,
		ReadOnlySpan<byte> magic,
		string? metadataJson,
		uint version = DefaultVersion)
	{
		byte[] metadataBytes = !string.IsNullOrEmpty(metadataJson)
			? Encoding.UTF8.GetBytes(metadataJson)
			: Array.Empty<byte>();

		writer.Write(magic);
		writer.Write(version);
		writer.Write((uint)metadataBytes.Length);
		if (metadataBytes.Length > 0)
		{
			writer.Write(metadataBytes);
		}
	}

	public static string? ExtractMetadata(ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> expectedMagic)
	{
		if (!HasMagic(bytes, expectedMagic))
		{
			return null;
		}

		uint metadataLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(8, 4));
		if (metadataLength == 0 || MinimumHeaderLength + (int)metadataLength > bytes.Length)
		{
			return null;
		}

		return Encoding.UTF8.GetString(bytes.Slice(MinimumHeaderLength, (int)metadataLength));
	}

	public static byte[] SetMetadata(
		ReadOnlySpan<byte> bytes,
		ReadOnlySpan<byte> expectedMagic,
		string? newMetadataJson,
		string formatName = "container")
	{
		var (version, _, payloadOffset) = ReadHeader(bytes, expectedMagic, formatName);
		var payload = bytes.Slice(payloadOffset);

		using var memoryStream = new MemoryStream(MinimumHeaderLength + (newMetadataJson?.Length ?? 0) + payload.Length);
		using var writer = new BinaryWriter(memoryStream);

		WriteHeader(writer, expectedMagic, newMetadataJson, version);
		writer.Write(payload);

		return memoryStream.ToArray();
	}
}
