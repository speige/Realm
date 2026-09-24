using System;
using System.IO;
using System.Text.Json;

namespace Realm.Shared.Metadata;

public static class RkeyFile
{
	public static readonly byte[] Magic = [0x52, 0x4B, 0x45, 0x59];
	public const uint CurrentVersion = 1;

	public static bool IsRkeyBytes(ReadOnlySpan<byte> bytes)
	{
		return RealmContainerHeader.HasMagic(bytes, Magic);
	}

	public static (string? MetadataJson, uint Version) Parse(ReadOnlySpan<byte> bytes)
	{
		var (version, metadataJson, _) = RealmContainerHeader.ReadHeader(bytes, Magic, "RKEY");
		return (metadataJson, version);
	}

	public static AuthorshipKeyData? ParseKeyData(ReadOnlySpan<byte> bytes)
	{
		var (metadataJson, _) = Parse(bytes);
		if (string.IsNullOrWhiteSpace(metadataJson))
		{
			return null;
		}

		return JsonSerializer.Deserialize<AuthorshipKeyData>(metadataJson);
	}

	public static byte[] Build(string? metadataJson, uint version = CurrentVersion)
	{
		using var memoryStream = new MemoryStream();
		using var writer = new BinaryWriter(memoryStream);

		RealmContainerHeader.WriteHeader(writer, Magic, metadataJson, version);
		return memoryStream.ToArray();
	}

	public static byte[] Build(string userName, string publicKeyBase64, string privateKeyBase64, uint version = CurrentVersion)
	{
		var keyData = new AuthorshipKeyData
		{
			UserName = userName,
			PublicKey = publicKeyBase64,
			PrivateKey = privateKeyBase64
		};
		string json = JsonSerializer.Serialize(keyData, new JsonSerializerOptions { WriteIndented = true });
		return Build(json, version);
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
		return RealmContainerHeader.SetMetadata(bytes, Magic, newMetadataJson, "RKEY");
	}
}
