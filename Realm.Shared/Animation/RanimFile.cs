using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using Blake3;
using MemoryPack;
using Realm.Shared.Metadata;

namespace Realm.Shared.Animation;

public static class RanimFile
{
	public static readonly byte[] Magic = [0x52, 0x41, 0x4E, 0x4D];
	public const uint CurrentVersion = 1;

	public static bool IsRanimBytes(ReadOnlySpan<byte> bytes)
	{
		return RealmContainerHeader.HasMagic(bytes, Magic);
	}

	public static (string? MetadataJson, RealmAnimationData AnimationData, uint Version) Parse(ReadOnlySpan<byte> bytes)
	{
		var (version, metadataJson, offset) = RealmContainerHeader.ReadHeader(bytes, Magic, "RANIM");
		bool isCompressed = RealmMetadataHelper.ExtractIsCompressed(metadataJson);
		ReadOnlySpan<byte> payloadSpan = bytes.Slice(offset);
		byte[] memoryPackBytes = isCompressed
			? RealmCompressionHelper.Decompress(payloadSpan)
			: payloadSpan.ToArray();

		var animationData = MemoryPackSerializer.Deserialize<RealmAnimationData>(memoryPackBytes) ?? new RealmAnimationData();
		return (metadataJson, animationData, version);
	}

	public static byte[] Build(string? metadataJson, byte[] memoryPackBytes, bool? compressed = null, uint version = CurrentVersion)
	{
		bool isCompressed = compressed ?? true;
		if (compressed == null && !string.IsNullOrWhiteSpace(metadataJson))
		{
			isCompressed = RealmMetadataHelper.ExtractIsCompressed(metadataJson);
		}

		byte[] payload = isCompressed
			? RealmCompressionHelper.Compress(memoryPackBytes ?? Array.Empty<byte>(), RealmCompressionHelper.DefaultCompressionLevel)
			: (memoryPackBytes ?? Array.Empty<byte>());

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

		metaObj["format"] = "ranim";
		metaObj["asset_type"] = "Animation";
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

	public static byte[] Build(string? metadataJson, RealmAnimationData animationData, bool? compressed = null, uint version = CurrentVersion)
	{
		byte[] memoryPackBytes = animationData != null
			? MemoryPackSerializer.Serialize(animationData)
			: Array.Empty<byte>();

		return Build(metadataJson, memoryPackBytes, compressed, version);
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
		return RealmContainerHeader.SetMetadata(bytes, Magic, newMetadataJson, "RANIM");
	}
}
