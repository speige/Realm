using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using Blake3;
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
		bool isCompressed = RealmMetadataHelper.ExtractIsCompressed(metadataJson);
		ReadOnlySpan<byte> payloadSpan = bytes.Slice(offset);

		ReadOnlySpan<byte> tracksSpan = isCompressed
			? RealmCompressionHelper.Decompress(payloadSpan)
			: payloadSpan;

		var tracks = new List<byte[]>();
		int readOffset = 0;
		if (readOffset + 4 <= tracksSpan.Length)
		{
			uint trackCount = BinaryPrimitives.ReadUInt32LittleEndian(tracksSpan.Slice(readOffset, 4));
			readOffset += 4;

			for (int i = 0; i < trackCount; i++)
			{
				if (readOffset + 4 > tracksSpan.Length) break;
				uint trackLength = BinaryPrimitives.ReadUInt32LittleEndian(tracksSpan.Slice(readOffset, 4));
				readOffset += 4;

				if (readOffset + (int)trackLength > tracksSpan.Length) break;
				byte[] trackData = tracksSpan.Slice(readOffset, (int)trackLength).ToArray();
				tracks.Add(trackData);
				readOffset += (int)trackLength;
			}
		}

		return (metadataJson, tracks, version);
	}

	public static byte[] Build(string? metadataJson, IList<byte[]> tracks, bool? compressed = null, uint version = CurrentVersion)
	{
		bool isCompressed = compressed ?? false;
		if (compressed == null && !string.IsNullOrWhiteSpace(metadataJson))
		{
			isCompressed = RealmMetadataHelper.ExtractIsCompressed(metadataJson);
		}

		using var rawPayloadStream = new MemoryStream();
		using var rawWriter = new BinaryWriter(rawPayloadStream);

		rawWriter.Write((uint)(tracks?.Count ?? 0));
		if (tracks != null)
		{
			foreach (var track in tracks)
			{
				rawWriter.Write((uint)(track?.Length ?? 0));
				if (track != null && track.Length > 0)
				{
					rawWriter.Write(track);
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

		metaObj["format"] = "raud";
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

	public static byte[]? GetTrack(ReadOnlySpan<byte> bytes, int trackIndex = 0)
	{
		if (!IsRaudBytes(bytes)) return null;

		if (!RealmContainerHeader.TryReadHeader(bytes, Magic, out uint version, out string? metadataJson, out int payloadOffset))
		{
			return null;
		}

		bool isCompressed = RealmMetadataHelper.ExtractIsCompressed(metadataJson);
		ReadOnlySpan<byte> payloadSpan = bytes.Slice(payloadOffset);

		ReadOnlySpan<byte> tracksSpan = isCompressed
			? RealmCompressionHelper.Decompress(payloadSpan)
			: payloadSpan;

		if (tracksSpan.Length < 4) return null;
		uint trackCount = BinaryPrimitives.ReadUInt32LittleEndian(tracksSpan.Slice(0, 4));
		int offset = 4;

		if (trackIndex < 0 || trackIndex >= trackCount) return null;

		for (int i = 0; i < trackCount; i++)
		{
			if (offset + 4 > tracksSpan.Length) return null;
			uint trackLength = BinaryPrimitives.ReadUInt32LittleEndian(tracksSpan.Slice(offset, 4));
			offset += 4;

			if (i == trackIndex)
			{
				if (offset + (int)trackLength > tracksSpan.Length) return null;
				return tracksSpan.Slice(offset, (int)trackLength).ToArray();
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
			return GetTrack(memoryStream.ToArray(), trackIndex);
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
