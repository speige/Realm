using System;
using System.IO;
using System.Text;

namespace Realm.Shared.ModelOptimization;

public static class RmodFile
{
	public static readonly byte[] Magic = [0x52, 0x4D, 0x4F, 0x44]; // "RMOD"
	public const uint CurrentVersion = 1;

	public static bool IsRmodBytes(ReadOnlySpan<byte> bytes)
	{
		if (bytes.Length < 16) return false;
		return bytes.Slice(0, 4).SequenceEqual(Magic);
	}

	public static (string? MetadataJson, byte[] GlbBytes, uint Version) Parse(ReadOnlySpan<byte> bytes)
	{
		if (!IsRmodBytes(bytes))
		{
			throw new InvalidOperationException("Invalid RMOD signature.");
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

		byte[] glbBytes = Array.Empty<byte>();
		if (offset + 4 <= bytes.Length)
		{
			uint glbLen = BitConverter.ToUInt32(bytes.Slice(offset, 4));
			offset += 4;
			if (glbLen > 0 && offset + (int)glbLen <= bytes.Length)
			{
				glbBytes = bytes.Slice(offset, (int)glbLen).ToArray();
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

		writer.Write((uint)(glbBytes?.Length ?? 0));
		if (glbBytes != null && glbBytes.Length > 0)
		{
			writer.Write(glbBytes);
		}

		return ms.ToArray();
	}

	public static byte[]? GetGlbBytes(ReadOnlySpan<byte> bytes)
	{
		if (!IsRmodBytes(bytes)) return null;

		uint metadataLen = BitConverter.ToUInt32(bytes.Slice(8, 4));
		int offset = 12 + (int)metadataLen;

		if (offset + 4 > bytes.Length) return null;
		uint glbLen = BitConverter.ToUInt32(bytes.Slice(offset, 4));
		offset += 4;

		if (offset + (int)glbLen > bytes.Length)
		{
			if (offset < bytes.Length)
			{
				return bytes.Slice(offset).ToArray();
			}
			return null;
		}

		return bytes.Slice(offset, (int)glbLen).ToArray();
	}

	public static string? ExtractMetadata(ReadOnlySpan<byte> bytes)
	{
		if (!IsRmodBytes(bytes)) return null;
		uint metadataLen = BitConverter.ToUInt32(bytes.Slice(8, 4));
		if (metadataLen == 0 || 12 + (int)metadataLen > bytes.Length) return null;
		return Encoding.UTF8.GetString(bytes.Slice(12, (int)metadataLen));
	}

	public static byte[] SetMetadata(ReadOnlySpan<byte> bytes, string? newMetadataJson)
	{
		var (_, glbBytes, version) = Parse(bytes);
		return Build(newMetadataJson, glbBytes, version);
	}
}
