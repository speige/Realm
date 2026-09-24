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

	public static byte[]? GetGlbBytes(Stream stream)
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
		if (metadataLength > 0)
		{
			stream.Seek(metadataLength, SeekOrigin.Current);
		}

		Span<byte> uintBuf = stackalloc byte[4];
		if (stream.Read(uintBuf) < 4) return null;
		uint glbLength = BinaryPrimitives.ReadUInt32LittleEndian(uintBuf);
		if (glbLength == 0 || glbLength > 500 * 1024 * 1024) return null;

		byte[] glbBytes = new byte[glbLength];
		int read = 0;
		while (read < glbLength)
		{
			int r = stream.Read(glbBytes, read, (int)glbLength - read);
			if (r <= 0) return null;
			read += r;
		}
		return glbBytes;
	}

	public static byte[]? GetGlbBytesFromFile(string filePath)
	{
		if (!File.Exists(filePath)) return null;
		try
		{
			using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 8192);
			return GetGlbBytes(stream);
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
		return RealmContainerHeader.SetMetadata(bytes, Magic, newMetadataJson, "RMESH");
	}
}
