using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using Realm.Shared.Metadata;

namespace Realm.Shared.Audio;

public static class RaudFile
{
	public static readonly byte[] Magic = [0x52, 0x41, 0x55, 0x44]; // "RAUD"
	public const uint CurrentVersion = 1;

	public static bool IsRaudBytes(ReadOnlySpan<byte> bytes)
	{
		return RealmContainerHeader.HasMagic(bytes, Magic);
	}

	public static (string? MetadataJson, List<byte[]> Tracks, uint Version) Parse(ReadOnlySpan<byte> bytes)
	{
		var (version, metadataJson, offset) = RealmContainerHeader.ReadHeader(bytes, Magic, "RAUD");

		var tracks = new List<byte[]>();
		if (offset + 4 <= bytes.Length)
		{
			uint trackCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, 4));
			offset += 4;

			for (int i = 0; i < trackCount; i++)
			{
				if (offset + 4 > bytes.Length) break;
				uint trackLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, 4));
				offset += 4;

				if (offset + (int)trackLength > bytes.Length) break;
				byte[] trackData = bytes.Slice(offset, (int)trackLength).ToArray();
				tracks.Add(trackData);
				offset += (int)trackLength;
			}
		}

		return (metadataJson, tracks, version);
	}

	public static byte[] Build(string? metadataJson, IList<byte[]> tracks, uint version = CurrentVersion)
	{
		using var memoryStream = new MemoryStream();
		using var writer = new BinaryWriter(memoryStream);

		RealmContainerHeader.WriteHeader(writer, Magic, metadataJson, version);

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

		return memoryStream.ToArray();
	}

	public static byte[]? GetTrack(ReadOnlySpan<byte> bytes, int trackIndex = 0)
	{
		if (!IsRaudBytes(bytes)) return null;

		uint metadataLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(8, 4));
		int offset = RealmContainerHeader.MinimumHeaderLength + (int)metadataLength;

		if (offset + 4 > bytes.Length) return null;
		uint trackCount = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, 4));
		offset += 4;

		if (trackIndex < 0 || trackIndex >= trackCount) return null;

		for (int i = 0; i < trackCount; i++)
		{
			if (offset + 4 > bytes.Length) return null;
			uint trackLength = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, 4));
			offset += 4;

			if (i == trackIndex)
			{
				if (offset + (int)trackLength > bytes.Length) return null;
				return bytes.Slice(offset, (int)trackLength).ToArray();
			}

			offset += (int)trackLength;
		}

		return null;
	}

	public static byte[]? GetTrack(Stream stream, int trackIndex = 0)
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
		uint trackCount = BinaryPrimitives.ReadUInt32LittleEndian(uintBuf);
		if (trackIndex < 0 || trackIndex >= trackCount) return null;

		for (int i = 0; i < trackCount; i++)
		{
			if (stream.Read(uintBuf) < 4) return null;
			uint trackLength = BinaryPrimitives.ReadUInt32LittleEndian(uintBuf);
			if (trackLength > 100 * 1024 * 1024) return null;

			if (i == trackIndex)
			{
				byte[] trackBytes = new byte[trackLength];
				int read = 0;
				while (read < trackLength)
				{
					int r = stream.Read(trackBytes, read, (int)trackLength - read);
					if (r <= 0) return null;
					read += r;
				}
				return trackBytes;
			}

			stream.Seek(trackLength, SeekOrigin.Current);
		}

		return null;
	}

	public static byte[]? GetTrackFromFile(string filePath, int trackIndex = 0)
	{
		if (!File.Exists(filePath)) return null;
		try
		{
			using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 8192);
			return GetTrack(stream, trackIndex);
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
		return RealmContainerHeader.SetMetadata(bytes, Magic, newMetadataJson, "RAUD");
	}
}
