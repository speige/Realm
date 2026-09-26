using System;
using System.Buffers.Binary;
using System.IO;
using System.Text.Json.Nodes;
using Blake3;
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
		bool isCompressed = RealmMetadataHelper.ExtractIsCompressed(metadataJson);
		ReadOnlySpan<byte> payloadSpan = bytes.Slice(offset);

		byte[] glbBytes;
		if (isCompressed)
		{
			glbBytes = RealmCompressionHelper.Decompress(payloadSpan);
		}
		else
		{
			if (payloadSpan.Length >= 4)
			{
				uint glbLength = BinaryPrimitives.ReadUInt32LittleEndian(payloadSpan.Slice(0, 4));
				if (glbLength > 0 && 4 + (int)glbLength <= payloadSpan.Length)
				{
					glbBytes = payloadSpan.Slice(4, (int)glbLength).ToArray();
				}
				else
				{
					glbBytes = payloadSpan.ToArray();
				}
			}
			else
			{
				glbBytes = payloadSpan.ToArray();
			}
		}

		return (metadataJson, glbBytes, version);
	}

	public static byte[] Build(string? metadataJson, byte[] glbBytes, bool? compressed = null, uint version = CurrentVersion)
	{
		bool isCompressed = compressed ?? true;
		if (compressed == null && !string.IsNullOrWhiteSpace(metadataJson))
		{
			isCompressed = RealmMetadataHelper.ExtractIsCompressed(metadataJson);
		}

		byte[] payload;
		if (isCompressed)
		{
			payload = RealmCompressionHelper.Compress(glbBytes ?? Array.Empty<byte>(), RealmCompressionHelper.DefaultCompressionLevel);
		}
		else
		{
			if (glbBytes == null || glbBytes.Length == 0)
			{
				payload = Array.Empty<byte>();
			}
			else
			{
				payload = new byte[4 + glbBytes.Length];
				BinaryPrimitives.WriteUInt32LittleEndian(payload.AsSpan(0, 4), (uint)glbBytes.Length);
				Buffer.BlockCopy(glbBytes, 0, payload, 4, glbBytes.Length);
			}
		}

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

		metaObj["format"] = "rmesh";
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

	public static byte[]? GetGlbBytes(ReadOnlySpan<byte> bytes)
	{
		if (!IsRmeshBytes(bytes)) return null;

		if (!RealmContainerHeader.TryReadHeader(bytes, Magic, out uint version, out string? metadataJson, out int payloadOffset))
		{
			return null;
		}

		bool isCompressed = RealmMetadataHelper.ExtractIsCompressed(metadataJson);
		ReadOnlySpan<byte> payloadSpan = bytes.Slice(payloadOffset);

		if (isCompressed)
		{
			return RealmCompressionHelper.Decompress(payloadSpan);
		}
		else
		{
			if (payloadSpan.Length >= 4)
			{
				uint glbLength = BinaryPrimitives.ReadUInt32LittleEndian(payloadSpan.Slice(0, 4));
				if (glbLength > 0 && 4 + (int)glbLength <= payloadSpan.Length)
				{
					return payloadSpan.Slice(4, (int)glbLength).ToArray();
				}
			}
			return payloadSpan.ToArray();
		}
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
			return RealmCompressionHelper.Decompress(memoryStream.ToArray());
		}
		else
		{
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
	}

	public static byte[]? GetGlbBytesFromFile(string filePath)
	{
		if (!File.Exists(filePath)) return null;
		try
		{
			byte[] bytes = File.ReadAllBytes(filePath);
			return GetGlbBytes(bytes);
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
