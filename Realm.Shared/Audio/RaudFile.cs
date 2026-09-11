using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Realm.Shared.Audio;

public static class RaudFile
{
	public static readonly byte[] Magic = [0x52, 0x41, 0x55, 0x44]; // "RAUD"
	public const uint CurrentVersion = 1;

	public static bool IsRaudBytes(ReadOnlySpan<byte> bytes)
	{
		if (bytes.Length < 16) return false;
		return bytes.Slice(0, 4).SequenceEqual(Magic);
	}

	public static (string? MetadataJson, List<byte[]> Tracks, uint Version) Parse(ReadOnlySpan<byte> bytes)
	{
		if (!IsRaudBytes(bytes))
		{
			throw new InvalidOperationException("Invalid RAUD signature.");
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

		var tracks = new List<byte[]>();
		if (offset + 4 <= bytes.Length)
		{
			uint trackCount = BitConverter.ToUInt32(bytes.Slice(offset, 4));
			offset += 4;

			for (int i = 0; i < trackCount; i++)
			{
				if (offset + 4 > bytes.Length) break;
				uint trackLen = BitConverter.ToUInt32(bytes.Slice(offset, 4));
				offset += 4;

				if (offset + (int)trackLen > bytes.Length) break;
				byte[] trackData = bytes.Slice(offset, (int)trackLen).ToArray();
				tracks.Add(trackData);
				offset += (int)trackLen;
			}
		}

		return (metadataJson, tracks, version);
	}

	public static byte[] Build(string? metadataJson, IList<byte[]> tracks, uint version = CurrentVersion)
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

		writer.Write((uint)(tracks?.Count ?? 0));
		if (tracks != null)
		{
			foreach (var track in tracks)
			{
				writer.Write((uint)(track?.Length ?? 0));
				if (track != null && track.Length > 0)
				{
					writer.Write(track);
				}
			}
		}

		return ms.ToArray();
	}

	public static byte[]? GetTrack(ReadOnlySpan<byte> bytes, int trackIndex = 0)
	{
		if (!IsRaudBytes(bytes)) return null;

		uint metadataLen = BitConverter.ToUInt32(bytes.Slice(8, 4));
		int offset = 12 + (int)metadataLen;

		if (offset + 4 > bytes.Length) return null;
		uint trackCount = BitConverter.ToUInt32(bytes.Slice(offset, 4));
		offset += 4;

		if (trackIndex < 0 || trackIndex >= trackCount) return null;

		for (int i = 0; i < trackCount; i++)
		{
			if (offset + 4 > bytes.Length) return null;
			uint trackLen = BitConverter.ToUInt32(bytes.Slice(offset, 4));
			offset += 4;

			if (i == trackIndex)
			{
				if (offset + (int)trackLen > bytes.Length) return null;
				return bytes.Slice(offset, (int)trackLen).ToArray();
			}

			offset += (int)trackLen;
		}

		return null;
	}

	public static string? ExtractMetadata(ReadOnlySpan<byte> bytes)
	{
		if (!IsRaudBytes(bytes)) return null;
		uint metadataLen = BitConverter.ToUInt32(bytes.Slice(8, 4));
		if (metadataLen == 0 || 12 + (int)metadataLen > bytes.Length) return null;
		return Encoding.UTF8.GetString(bytes.Slice(12, (int)metadataLen));
	}

	public static byte[] SetMetadata(ReadOnlySpan<byte> bytes, string? newMetadataJson)
	{
		var (_, tracks, version) = Parse(bytes);
		return Build(newMetadataJson, tracks, version);
	}
}
