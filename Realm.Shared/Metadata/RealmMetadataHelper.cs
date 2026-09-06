using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Blake3;
using Realm.Shared.Textures;

namespace Realm.Shared.Metadata;

public static class RealmMetadataHelper
{
	private static readonly uint[] OggCrcTable = InitializeOggCrcTable();

	private static uint[] InitializeOggCrcTable()
	{
		uint[] table = new uint[256];
		for (uint i = 0; i < 256; i++)
		{
			uint r = i << 24;
			for (int j = 0; j < 8; j++)
			{
				if ((r & 0x80000000) != 0) r = (r << 1) ^ 0x04C11DB7;
				else r <<= 1;
			}
			table[i] = r;
		}
		return table;
	}

	public static uint CalculateOggCrc(ReadOnlySpan<byte> data)
	{
		uint crc = 0;
		foreach (byte b in data) crc = (crc << 8) ^ OggCrcTable[((crc >> 24) ^ b) & 0xFF];
		return crc;
	}

	public static bool SupportsMetadata(string extensionOrPath)
	{
		string ext = Path.GetExtension(extensionOrPath).ToLowerInvariant();
		if (string.IsNullOrEmpty(ext) && extensionOrPath.StartsWith('.')) ext = extensionOrPath.ToLowerInvariant();
		return ext is ".glb" or ".rtex" or ".ranim" or ".ogg";
	}

	public static string? ExtractMetadata(string filePath)
	{
		if (!File.Exists(filePath)) return null;
		string ext = Path.GetExtension(filePath).ToLowerInvariant();
		return ext switch
		{
			".glb" => ExtractMetadataFromGlb(filePath),
			".rtex" => ExtractMetadataFromRtex(filePath),
			".ranim" => ExtractMetadataFromRanim(filePath),
			".ogg" => ExtractMetadataFromOgg(filePath),
			_ => null
		};
	}

	public static bool HasRealmMetadata(string filePath)
	{
		if (!File.Exists(filePath)) return false;
		try
		{
			string? meta = ExtractMetadata(filePath);
			return !string.IsNullOrWhiteSpace(meta);
		}
		catch
		{
			return false;
		}
	}

	public static bool EnsureMetadata(string filePath, string? defaultMetadataJson = null)
	{
		if (!File.Exists(filePath)) return false;
		if (HasRealmMetadata(filePath)) return true;

		string ext = Path.GetExtension(filePath).ToLowerInvariant();
		string canonicalBlake3 = ComputeBlake3(filePath);
		JsonObject metaObj;
		if (!string.IsNullOrEmpty(defaultMetadataJson))
		{
			try
			{
				metaObj = JsonNode.Parse(defaultMetadataJson)?.AsObject() ?? new JsonObject();
			}
			catch
			{
				metaObj = new JsonObject();
			}
		}
		else
		{
			metaObj = new JsonObject();
			metaObj["created_utc"] = DateTime.UtcNow.ToString("O");
			metaObj["format"] = ext.TrimStart('.');
		}
		metaObj["blake3"] = canonicalBlake3;

		try
		{
			return AddMetadata(filePath, metaObj.ToJsonString());
		}
		catch
		{
			return false;
		}
	}

	private static readonly Dictionary<string, string[]> ValidAssetTypesByExtension = new(StringComparer.OrdinalIgnoreCase)
	{
		[".rtex"] = new[] { "Decal", "Icon", "Noise", "Ribbon", "Skybox", "SpellSpritesheet", "Tilesheet", "vfx_radial", "vfx_vertical" },
		[".glb"] = new[] { "Character", "Building", "Environment", "Projectile", "Prop", "Attachment", "Weapon" },
		[".ranim"] = new[] { "Animation" },
		[".ogg"] = new[] { "Music", "SoundEffect" }
	};

	public static string[] GetValidAssetTypesForExtension(string extensionOrPath)
	{
		string ext = Path.GetExtension(extensionOrPath).ToLowerInvariant();
		if (string.IsNullOrEmpty(ext) && extensionOrPath.StartsWith('.')) ext = extensionOrPath.ToLowerInvariant();
		if (ValidAssetTypesByExtension.TryGetValue(ext, out var types))
		{
			return types;
		}
		return Array.Empty<string>();
	}

	public static bool IsValidAssetTypeForExtension(string extensionOrPath, string? assetType, out string canonicalType, out string[] validTypes)
	{
		validTypes = GetValidAssetTypesForExtension(extensionOrPath);
		canonicalType = string.Empty;
		if (string.IsNullOrWhiteSpace(assetType)) return false;

		string norm = assetType.Trim().Replace("_", "").ToLowerInvariant();

		string ext = Path.GetExtension(extensionOrPath).ToLowerInvariant();
		if (string.IsNullOrEmpty(ext) && extensionOrPath.StartsWith('.')) ext = extensionOrPath.ToLowerInvariant();

		if (ext is ".rtex")
		{
			if (norm.Contains("radial")) { canonicalType = "vfx_radial"; return true; }
			if (norm.Contains("vertical")) { canonicalType = "vfx_vertical"; return true; }
			if (norm.Contains("tile") || norm.Contains("terrain")) { canonicalType = "Tilesheet"; return true; }
			if (norm.Contains("decal")) { canonicalType = "Decal"; return true; }
			if (norm.Contains("icon")) { canonicalType = "Icon"; return true; }
			if (norm.Contains("noise")) { canonicalType = "Noise"; return true; }
			if (norm.Contains("ribbon")) { canonicalType = "Ribbon"; return true; }
			if (norm.Contains("skybox")) { canonicalType = "Skybox"; return true; }
			if (norm.Contains("sprite") || norm.Contains("vfx") || norm.Contains("spell")) { canonicalType = "SpellSpritesheet"; return true; }
			return false;
		}
		else if (ext is ".glb")
		{
			if (norm.Contains("character") || norm.Contains("unit")) { canonicalType = "Character"; return true; }
			if (norm.Contains("building")) { canonicalType = "Building"; return true; }
			if (norm.Contains("environment") || norm.Contains("resource")) { canonicalType = "Environment"; return true; }
			if (norm.Contains("projectile")) { canonicalType = "Projectile"; return true; }
			if (norm.Contains("prop")) { canonicalType = "Prop"; return true; }
			if (norm.Contains("attachment") || norm.Contains("object")) { canonicalType = "Attachment"; return true; }
			if (norm.Contains("weapon")) { canonicalType = "Weapon"; return true; }
			return false;
		}
		else if (ext is ".ranim")
		{
			canonicalType = "Animation";
			return true;
		}
		else if (ext is ".ogg")
		{
			if (norm.Contains("music")) { canonicalType = "Music"; return true; }
			if (norm.Contains("sound") || norm.Contains("sfx")) { canonicalType = "SoundEffect"; return true; }
			return false;
		}

		return false;
	}

	public static string? ExtractAssetType(string filePath)
	{
		string? metaJson = ExtractMetadata(filePath);
		if (string.IsNullOrEmpty(metaJson)) return null;
		try
		{
			var node = JsonNode.Parse(metaJson);
			if (node is JsonObject obj)
			{
				string? typeVal = obj["asset_type"]?.ToString()
					?? obj["AssetType"]?.ToString()
					?? obj["type"]?.ToString()
					?? obj["default_asset_type"]?.ToString();
				if (!string.IsNullOrEmpty(typeVal) && IsValidAssetTypeForExtension(filePath, typeVal, out string canonical, out _))
				{
					return canonical;
				}
			}
		}
		catch { }
		return null;
	}

	public static bool SetAssetType(string filePath, string assetType)
	{
		if (!File.Exists(filePath)) return false;
		if (!IsValidAssetTypeForExtension(filePath, assetType, out string canonical, out _))
		{
			return false;
		}

		string? existingMeta = ExtractMetadata(filePath);
		JsonObject metaObj;
		if (!string.IsNullOrEmpty(existingMeta))
		{
			try
			{
				metaObj = JsonNode.Parse(existingMeta)?.AsObject() ?? new JsonObject();
			}
			catch
			{
				metaObj = new JsonObject();
			}
		}
		else
		{
			metaObj = new JsonObject();
			string ext = Path.GetExtension(filePath).ToLowerInvariant();
			metaObj["created_utc"] = DateTime.UtcNow.ToString("O");
			metaObj["format"] = ext.TrimStart('.');
		}

		metaObj["asset_type"] = canonical;
		metaObj["blake3"] = ComputeBlake3(filePath);
		return AddMetadata(filePath, metaObj.ToJsonString());
	}

	public static List<string> ExtractTags(string filePath)
	{
		var result = new List<string>();
		string? metaJson = ExtractMetadata(filePath);
		if (string.IsNullOrEmpty(metaJson)) return result;
		try
		{
			var node = JsonNode.Parse(metaJson);
			if (node is JsonObject obj && obj["tags"] is JsonArray tagsArray)
			{
				foreach (var tagNode in tagsArray)
				{
					string? tag = tagNode?.ToString()?.Trim();
					if (!string.IsNullOrEmpty(tag) && !result.Contains(tag, StringComparer.OrdinalIgnoreCase))
					{
						result.Add(tag);
					}
				}
			}
		}
		catch { }
		return result;
	}

	public static bool SetTags(string filePath, IEnumerable<string> tags)
	{
		if (!File.Exists(filePath)) return false;
		string ext = Path.GetExtension(filePath).ToLowerInvariant();
		if (ext is not (".glb" or ".rtex" or ".ranim" or ".ogg")) return false;

		string? existingMeta = ExtractMetadata(filePath);
		JsonObject metaObj;
		if (!string.IsNullOrEmpty(existingMeta))
		{
			try
			{
				metaObj = JsonNode.Parse(existingMeta)?.AsObject() ?? new JsonObject();
			}
			catch
			{
				metaObj = new JsonObject();
			}
		}
		else
		{
			metaObj = new JsonObject();
			metaObj["created_utc"] = DateTime.UtcNow.ToString("O");
			metaObj["format"] = ext.TrimStart('.');
		}

		var cleanTags = (tags ?? Array.Empty<string>())
			.Where(t => !string.IsNullOrWhiteSpace(t))
			.Select(t => t.Trim().ToLowerInvariant())
			.Distinct(StringComparer.OrdinalIgnoreCase)
			.Select(t => (JsonNode)JsonValue.Create(t)!)
			.ToArray();

		metaObj["tags"] = new JsonArray(cleanTags);
		metaObj["blake3"] = ComputeBlake3(filePath);
		return AddMetadata(filePath, metaObj.ToJsonString());
	}

	public static bool AddMetadata(string filePath, string realmMetadataJson)
	{
		if (!File.Exists(filePath)) return false;
		string ext = Path.GetExtension(filePath).ToLowerInvariant();
		switch (ext)
		{
			case ".glb":
				AddMetadataToGlb(filePath, realmMetadataJson);
				return true;
			case ".rtex":
				AddMetadataToRtex(filePath, realmMetadataJson);
				return true;
			case ".ranim":
				AddMetadataToRanim(filePath, realmMetadataJson);
				return true;
			case ".ogg":
				AddMetadataToOgg(filePath, realmMetadataJson);
				return true;
			default:
				throw new NotSupportedException($"Unsupported file format '{ext}' for metadata. Supported formats: .glb, .rtex, .ogg, .ranim");
		}
	}

	public static bool RemoveMetadata(string filePath)
	{
		if (!File.Exists(filePath)) return false;
		string ext = Path.GetExtension(filePath).ToLowerInvariant();
		switch (ext)
		{
			case ".glb":
				RemoveMetadataFromGlb(filePath);
				return true;
			case ".rtex":
				RemoveMetadataFromRtex(filePath);
				return true;
			case ".ranim":
				RemoveMetadataFromRanim(filePath);
				return true;
			case ".ogg":
				RemoveMetadataFromOgg(filePath);
				return true;
			default:
				throw new NotSupportedException($"Unsupported file format '{ext}' for metadata. Supported formats: .glb, .rtex, .ogg, .ranim");
		}
	}

	public static string? ExtractMetadataFromGlb(string filePath)
	{
		if (!File.Exists(filePath)) return null;
		byte[] bytes = File.ReadAllBytes(filePath);
		return ExtractMetadataFromGlbBytes(bytes);
	}

	public static string? ExtractMetadataFromGlbBytes(ReadOnlySpan<byte> bytes)
	{
		if (bytes.Length < 20) return null;
		uint magic = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(0, 4));
		if (magic != 0x46546C67) return null;

		uint chunk0Length = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(12, 4));
		uint chunk0Type = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(16, 4));
		if (chunk0Type != 0x4E4F534A) return null;
		if (bytes.Length < 20 + chunk0Length) return null;

		string jsonText = Encoding.UTF8.GetString(bytes.Slice(20, (int)chunk0Length));
		try
		{
			using var doc = JsonDocument.Parse(jsonText);
			var root = doc.RootElement;
			if (root.TryGetProperty("extras", out var extras) && extras.ValueKind == JsonValueKind.Object)
			{
				if (extras.TryGetProperty("Realm", out var realmProp) || extras.TryGetProperty("realm", out realmProp))
				{
					return realmProp.ValueKind == JsonValueKind.String ? realmProp.GetString() : realmProp.GetRawText();
				}
			}
			if (root.TryGetProperty("asset", out var asset) && asset.TryGetProperty("extras", out extras) && extras.ValueKind == JsonValueKind.Object)
			{
				if (extras.TryGetProperty("Realm", out var realmProp) || extras.TryGetProperty("realm", out realmProp))
				{
					return realmProp.ValueKind == JsonValueKind.String ? realmProp.GetString() : realmProp.GetRawText();
				}
			}
		}
		catch { }
		return null;
	}

	public static void AddMetadataToGlb(string filePath, string realmMetadataJson)
	{
		byte[] bytes = File.ReadAllBytes(filePath);
		byte[] updated = AddMetadataToGlbBytes(bytes, realmMetadataJson);
		File.WriteAllBytes(filePath, updated);
	}

	public static byte[] AddMetadataToGlbBytes(byte[] bytes, string realmMetadataJson)
	{
		if (bytes.Length < 20) throw new InvalidOperationException("Invalid GLB file.");
		uint magic = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0, 4));
		if (magic != 0x46546C67) throw new InvalidOperationException("Invalid GLB magic header.");

		uint version = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4));
		uint chunk0Length = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12, 4));
		uint chunk0Type = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(16, 4));
		if (chunk0Type != 0x4E4F534A) throw new InvalidOperationException("First GLB chunk is not JSON.");

		string jsonText = Encoding.UTF8.GetString(bytes, 20, (int)chunk0Length);
		var rootNode = JsonNode.Parse(jsonText) ?? new JsonObject();
		if (rootNode["extras"] == null || rootNode["extras"] is not JsonObject)
		{
			rootNode["extras"] = new JsonObject();
		}

		try
		{
			var realmNode = JsonNode.Parse(realmMetadataJson);
			rootNode["extras"]!["Realm"] = realmNode;
		}
		catch
		{
			rootNode["extras"]!["Realm"] = JsonValue.Create(realmMetadataJson);
		}

		byte[] newJsonBytes = Encoding.UTF8.GetBytes(rootNode.ToJsonString());
		int padLength = (4 - (newJsonBytes.Length % 4)) % 4;
		byte[] paddedJson = new byte[newJsonBytes.Length + padLength];
		Buffer.BlockCopy(newJsonBytes, 0, paddedJson, 0, newJsonBytes.Length);
		for (int i = 0; i < padLength; i++) paddedJson[newJsonBytes.Length + i] = 0x20;

		int chunk1Offset = 20 + (int)chunk0Length;
		int chunk1RemainingLength = bytes.Length - chunk1Offset;

		uint newTotalLength = 12 + 8 + (uint)paddedJson.Length + (uint)chunk1RemainingLength;
		using var ms = new MemoryStream((int)newTotalLength);
		Span<byte> uintBuffer = stackalloc byte[4];

		BinaryPrimitives.WriteUInt32LittleEndian(uintBuffer, magic);
		ms.Write(uintBuffer);
		BinaryPrimitives.WriteUInt32LittleEndian(uintBuffer, version);
		ms.Write(uintBuffer);
		BinaryPrimitives.WriteUInt32LittleEndian(uintBuffer, newTotalLength);
		ms.Write(uintBuffer);
		BinaryPrimitives.WriteUInt32LittleEndian(uintBuffer, (uint)paddedJson.Length);
		ms.Write(uintBuffer);
		BinaryPrimitives.WriteUInt32LittleEndian(uintBuffer, chunk0Type);
		ms.Write(uintBuffer);
		ms.Write(paddedJson, 0, paddedJson.Length);
		if (chunk1RemainingLength > 0)
		{
			ms.Write(bytes, chunk1Offset, chunk1RemainingLength);
		}
		return ms.ToArray();
	}

	public static void RemoveMetadataFromGlb(string filePath)
	{
		byte[] bytes = File.ReadAllBytes(filePath);
		byte[] updated = RemoveMetadataFromGlbBytes(bytes);
		File.WriteAllBytes(filePath, updated);
	}

	public static byte[] RemoveMetadataFromGlbBytes(byte[] bytes)
	{
		if (bytes.Length < 20) return bytes;
		uint magic = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(0, 4));
		if (magic != 0x46546C67) return bytes;

		uint version = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(4, 4));
		uint chunk0Length = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(12, 4));
		uint chunk0Type = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(16, 4));
		if (chunk0Type != 0x4E4F534A) return bytes;

		string jsonText = Encoding.UTF8.GetString(bytes, 20, (int)chunk0Length);
		var rootNode = JsonNode.Parse(jsonText);
		if (rootNode is not JsonObject rootObj) return bytes;

		bool changed = false;
		if (rootObj["extras"] is JsonObject extrasObj)
		{
			if (extrasObj.Remove("Realm") || extrasObj.Remove("realm"))
			{
				changed = true;
			}
			if (extrasObj.Count == 0)
			{
				rootObj.Remove("extras");
			}
		}

		if (rootObj["asset"] is JsonObject assetObj && assetObj["extras"] is JsonObject assetExtrasObj)
		{
			if (assetExtrasObj.Remove("Realm") || assetExtrasObj.Remove("realm"))
			{
				changed = true;
			}
			if (assetExtrasObj.Count == 0)
			{
				assetObj.Remove("extras");
			}
		}

		if (!changed) return bytes;

		byte[] newJsonBytes = Encoding.UTF8.GetBytes(rootObj.ToJsonString());
		int padLength = (4 - (newJsonBytes.Length % 4)) % 4;
		byte[] paddedJson = new byte[newJsonBytes.Length + padLength];
		Buffer.BlockCopy(newJsonBytes, 0, paddedJson, 0, newJsonBytes.Length);
		for (int i = 0; i < padLength; i++) paddedJson[newJsonBytes.Length + i] = 0x20;

		int chunk1Offset = 20 + (int)chunk0Length;
		int chunk1RemainingLength = bytes.Length - chunk1Offset;

		uint newTotalLength = 12 + 8 + (uint)paddedJson.Length + (uint)chunk1RemainingLength;
		using var ms = new MemoryStream((int)newTotalLength);
		Span<byte> uintBuffer = stackalloc byte[4];

		BinaryPrimitives.WriteUInt32LittleEndian(uintBuffer, magic);
		ms.Write(uintBuffer);
		BinaryPrimitives.WriteUInt32LittleEndian(uintBuffer, version);
		ms.Write(uintBuffer);
		BinaryPrimitives.WriteUInt32LittleEndian(uintBuffer, newTotalLength);
		ms.Write(uintBuffer);
		BinaryPrimitives.WriteUInt32LittleEndian(uintBuffer, (uint)paddedJson.Length);
		ms.Write(uintBuffer);
		BinaryPrimitives.WriteUInt32LittleEndian(uintBuffer, chunk0Type);
		ms.Write(uintBuffer);
		ms.Write(paddedJson, 0, paddedJson.Length);
		if (chunk1RemainingLength > 0)
		{
			ms.Write(bytes, chunk1Offset, chunk1RemainingLength);
		}
		return ms.ToArray();
	}



	public static string? ExtractMetadataFromRtex(string filePath)
	{
		if (!File.Exists(filePath)) return null;
		byte[] bytes = File.ReadAllBytes(filePath);
		return Realm.Shared.Textures.RtexFile.ExtractMetadata(bytes);
	}

	public static void AddMetadataToRtex(string filePath, string realmMetadataJson)
	{
		byte[] bytes = File.ReadAllBytes(filePath);
		byte[] updated = Realm.Shared.Textures.RtexFile.SetMetadata(bytes, realmMetadataJson);
		File.WriteAllBytes(filePath, updated);
	}

	public static void RemoveMetadataFromRtex(string filePath)
	{
		byte[] bytes = File.ReadAllBytes(filePath);
		byte[] updated = Realm.Shared.Textures.RtexFile.SetMetadata(bytes, null);
		File.WriteAllBytes(filePath, updated);
	}

	public static string? ExtractMetadataFromRanim(string filePath)
	{
		if (!File.Exists(filePath)) return null;
		byte[] bytes = File.ReadAllBytes(filePath);
		return ExtractMetadataFromRanimBytes(bytes);
	}

	public static string? ExtractMetadataFromRanimBytes(ReadOnlySpan<byte> bytes)
	{
		if (bytes.Length == 0) return null;

		if (bytes[0] == (byte)'{' || bytes[0] == (byte)'[')
		{
			try
			{
				string jsonText = Encoding.UTF8.GetString(bytes);
				using var doc = JsonDocument.Parse(jsonText);
				if (doc.RootElement.TryGetProperty("Realm", out var realmProp) || doc.RootElement.TryGetProperty("realm", out realmProp))
				{
					return realmProp.ValueKind == JsonValueKind.String ? realmProp.GetString() : realmProp.GetRawText();
				}
				return jsonText;
			}
			catch { }
		}

		if (bytes.Length >= 8)
		{
			if (bytes[bytes.Length - 4] == (byte)'R' &&
				bytes[bytes.Length - 3] == (byte)'M' &&
				bytes[bytes.Length - 2] == (byte)'E' &&
				bytes[bytes.Length - 1] == (byte)'T')
			{
				uint metaLen = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(bytes.Length - 8, 4));
				if (metaLen > 0 && bytes.Length >= 8 + metaLen)
				{
					int metaStart = bytes.Length - 8 - (int)metaLen;
					return Encoding.UTF8.GetString(bytes.Slice(metaStart, (int)metaLen));
				}
			}
		}

		return null;
	}

	public static void AddMetadataToRanim(string filePath, string realmMetadataJson)
	{
		byte[] bytes = File.ReadAllBytes(filePath);
		byte[] updated = AddMetadataToRanimBytes(bytes, realmMetadataJson);
		File.WriteAllBytes(filePath, updated);
	}

	public static byte[] AddMetadataToRanimBytes(byte[] bytes, string realmMetadataJson)
	{
		if (bytes.Length > 0 && (bytes[0] == (byte)'{' || bytes[0] == (byte)'['))
		{
			try
			{
				var node = JsonNode.Parse(Encoding.UTF8.GetString(bytes)) ?? new JsonObject();
				try { node["Realm"] = JsonNode.Parse(realmMetadataJson); }
				catch { node["Realm"] = JsonValue.Create(realmMetadataJson); }
				return Encoding.UTF8.GetBytes(node.ToJsonString());
			}
			catch { }
		}

		int baseLength = bytes.Length;
		if (bytes.Length >= 8 &&
			bytes[bytes.Length - 4] == (byte)'R' &&
			bytes[bytes.Length - 3] == (byte)'M' &&
			bytes[bytes.Length - 2] == (byte)'E' &&
			bytes[bytes.Length - 1] == (byte)'T')
		{
			uint oldLen = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(bytes.Length - 8, 4));
			if (baseLength >= 8 + (int)oldLen)
			{
				baseLength = baseLength - 8 - (int)oldLen;
			}
		}

		byte[] jsonBytes = Encoding.UTF8.GetBytes(realmMetadataJson);
		byte[] result = new byte[baseLength + jsonBytes.Length + 4 + 4];
		Buffer.BlockCopy(bytes, 0, result, 0, baseLength);
		Buffer.BlockCopy(jsonBytes, 0, result, baseLength, jsonBytes.Length);
		BinaryPrimitives.WriteUInt32LittleEndian(result.AsSpan(baseLength + jsonBytes.Length, 4), (uint)jsonBytes.Length);
		result[result.Length - 4] = (byte)'R';
		result[result.Length - 3] = (byte)'M';
		result[result.Length - 2] = (byte)'E';
		result[result.Length - 1] = (byte)'T';
		return result;
	}

	public static void RemoveMetadataFromRanim(string filePath)
	{
		byte[] bytes = File.ReadAllBytes(filePath);
		byte[] updated = RemoveMetadataFromRanimBytes(bytes);
		File.WriteAllBytes(filePath, updated);
	}

	public static byte[] RemoveMetadataFromRanimBytes(byte[] bytes)
	{
		if (bytes.Length > 0 && (bytes[0] == (byte)'{' || bytes[0] == (byte)'['))
		{
			try
			{
				var node = JsonNode.Parse(Encoding.UTF8.GetString(bytes));
				if (node is JsonObject rootObj)
				{
					if (rootObj.Remove("Realm") || rootObj.Remove("realm"))
					{
						return Encoding.UTF8.GetBytes(rootObj.ToJsonString());
					}
				}
			}
			catch { }
			return bytes;
		}

		if (bytes.Length >= 8 &&
			bytes[bytes.Length - 4] == (byte)'R' &&
			bytes[bytes.Length - 3] == (byte)'M' &&
			bytes[bytes.Length - 2] == (byte)'E' &&
			bytes[bytes.Length - 1] == (byte)'T')
		{
			uint oldLen = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(bytes.Length - 8, 4));
			if (bytes.Length >= 8 + (int)oldLen)
			{
				int baseLength = bytes.Length - 8 - (int)oldLen;
				byte[] result = new byte[baseLength];
				Buffer.BlockCopy(bytes, 0, result, 0, baseLength);
				return result;
			}
		}

		return bytes;
	}

	private class OggPageData
	{
		public int Offset { get; set; }
		public int TotalLength { get; set; }
		public byte Version { get; set; }
		public byte HeaderType { get; set; }
		public ulong GranulePosition { get; set; }
		public uint BitstreamSerialNumber { get; set; }
		public uint PageSequenceNumber { get; set; }
		public uint CrcChecksum { get; set; }
		public byte[] SegmentTable { get; set; } = Array.Empty<byte>();
		public byte[] Payload { get; set; } = Array.Empty<byte>();
	}

	private static List<OggPageData> ParseOggPages(byte[] bytes)
	{
		var pages = new List<OggPageData>();
		int pos = 0;
		while (pos + 27 <= bytes.Length)
		{
			if (bytes[pos] != 0x4F || bytes[pos + 1] != 0x67 || bytes[pos + 2] != 0x67 || bytes[pos + 3] != 0x53)
			{
				break;
			}

			byte version = bytes[pos + 4];
			byte headerType = bytes[pos + 5];
			ulong granule = BinaryPrimitives.ReadUInt64LittleEndian(bytes.AsSpan(pos + 6, 8));
			uint serial = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(pos + 14, 4));
			uint seq = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(pos + 18, 4));
			uint crc = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(pos + 22, 4));
			int numSegments = bytes[pos + 26];

			if (pos + 27 + numSegments > bytes.Length) break;

			byte[] segmentTable = new byte[numSegments];
			Buffer.BlockCopy(bytes, pos + 27, segmentTable, 0, numSegments);

			int payloadLength = 0;
			for (int i = 0; i < numSegments; i++) payloadLength += segmentTable[i];

			int headerLength = 27 + numSegments;
			int totalLength = headerLength + payloadLength;
			if (pos + totalLength > bytes.Length) break;

			byte[] payload = new byte[payloadLength];
			Buffer.BlockCopy(bytes, pos + headerLength, payload, 0, payloadLength);

			pages.Add(new OggPageData
			{
				Offset = pos,
				TotalLength = totalLength,
				Version = version,
				HeaderType = headerType,
				GranulePosition = granule,
				BitstreamSerialNumber = serial,
				PageSequenceNumber = seq,
				CrcChecksum = crc,
				SegmentTable = segmentTable,
				Payload = payload
			});

			pos += totalLength;
		}
		return pages;
	}

	private static bool TryExtractHeaderPackets(
		List<OggPageData> pages,
		out string codecType,
		out List<byte[]> headerPackets,
		out int headerPageCount)
	{
		codecType = string.Empty;
		headerPackets = new List<byte[]>();
		headerPageCount = 0;

		if (pages.Count == 0) return false;

		byte[] firstPagePayload = pages[0].Payload;
		int expectedPackets;
		if (firstPagePayload.Length >= 7 &&
			firstPagePayload[0] == 0x01 && firstPagePayload[1] == (byte)'v' && firstPagePayload[2] == (byte)'o' &&
			firstPagePayload[3] == (byte)'r' && firstPagePayload[4] == (byte)'b' && firstPagePayload[5] == (byte)'i' && firstPagePayload[6] == (byte)'s')
		{
			codecType = "vorbis";
			expectedPackets = 3;
		}
		else if (firstPagePayload.Length >= 8 &&
			firstPagePayload[0] == (byte)'O' && firstPagePayload[1] == (byte)'p' && firstPagePayload[2] == (byte)'u' &&
			firstPagePayload[3] == (byte)'s' && firstPagePayload[4] == (byte)'H' && firstPagePayload[5] == (byte)'e' &&
			firstPagePayload[6] == (byte)'a' && firstPagePayload[7] == (byte)'d')
		{
			codecType = "opus";
			expectedPackets = 2;
		}
		else
		{
			return false;
		}

		headerPackets.Add(firstPagePayload);
		int collectedPackets = 1;
		int pagesUsed = 1;

		using var currentPacket = new MemoryStream();

		for (int p = 1; p < pages.Count; p++)
		{
			var page = pages[p];
			pagesUsed++;
			int payloadOffset = 0;

			for (int s = 0; s < page.SegmentTable.Length; s++)
			{
				int segmentLength = page.SegmentTable[s];
				currentPacket.Write(page.Payload, payloadOffset, segmentLength);
				payloadOffset += segmentLength;

				if (segmentLength < 255)
				{
					headerPackets.Add(currentPacket.ToArray());
					currentPacket.SetLength(0);
					collectedPackets++;

					if (collectedPackets == expectedPackets)
					{
						headerPageCount = pagesUsed;
						return true;
					}
				}
			}
		}

		return false;
	}

	private static bool TryExtractCommentsFromPacket(
		byte[] packet,
		out string vendor,
		out List<string> comments,
		out string codecType)
	{
		vendor = string.Empty;
		comments = new List<string>();
		codecType = string.Empty;

		if (packet.Length >= 7 &&
			packet[0] == 0x03 && packet[1] == (byte)'v' && packet[2] == (byte)'o' &&
			packet[3] == (byte)'r' && packet[4] == (byte)'b' && packet[5] == (byte)'i' && packet[6] == (byte)'s')
		{
			codecType = "vorbis";
			int cur = 7;
			if (cur + 4 > packet.Length) return false;
			uint vendorLen = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(cur, 4));
			cur += 4;
			if (cur + vendorLen > (uint)packet.Length) return false;
			vendor = Encoding.UTF8.GetString(packet, cur, (int)vendorLen);
			cur += (int)vendorLen;

			if (cur + 4 > packet.Length) return false;
			uint commentCount = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(cur, 4));
			cur += 4;

			for (uint i = 0; i < commentCount; i++)
			{
				if (cur + 4 > packet.Length) return false;
				uint cLen = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(cur, 4));
				cur += 4;
				if (cur + cLen > (uint)packet.Length) return false;
				string c = Encoding.UTF8.GetString(packet, cur, (int)cLen);
				cur += (int)cLen;
				comments.Add(c);
			}

			return true;
		}

		if (packet.Length >= 8 &&
			packet[0] == (byte)'O' && packet[1] == (byte)'p' && packet[2] == (byte)'u' &&
			packet[3] == (byte)'s' && packet[4] == (byte)'T' && packet[5] == (byte)'a' &&
			packet[6] == (byte)'g' && packet[7] == (byte)'s')
		{
			codecType = "opus";
			int cur = 8;
			if (cur + 4 > packet.Length) return false;
			uint vendorLen = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(cur, 4));
			cur += 4;
			if (cur + vendorLen > (uint)packet.Length) return false;
			vendor = Encoding.UTF8.GetString(packet, cur, (int)vendorLen);
			cur += (int)vendorLen;

			if (cur + 4 > packet.Length) return false;
			uint commentCount = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(cur, 4));
			cur += 4;

			for (uint i = 0; i < commentCount; i++)
			{
				if (cur + 4 > packet.Length) return false;
				uint cLen = BinaryPrimitives.ReadUInt32LittleEndian(packet.AsSpan(cur, 4));
				cur += 4;
				if (cur + cLen > (uint)packet.Length) return false;
				string c = Encoding.UTF8.GetString(packet, cur, (int)cLen);
				cur += (int)cLen;
				comments.Add(c);
			}

			return true;
		}

		return false;
	}

	private static byte[] BuildVorbisCommentPacket(string vendor, List<string> comments)
	{
		using var ms = new MemoryStream();
		byte[] magic = new byte[] { 0x03, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s' };
		ms.Write(magic, 0, 7);

		Span<byte> uintBuffer = stackalloc byte[4];
		byte[] vendorBytes = Encoding.UTF8.GetBytes(vendor);
		BinaryPrimitives.WriteUInt32LittleEndian(uintBuffer, (uint)vendorBytes.Length);
		ms.Write(uintBuffer);
		ms.Write(vendorBytes, 0, vendorBytes.Length);

		BinaryPrimitives.WriteUInt32LittleEndian(uintBuffer, (uint)comments.Count);
		ms.Write(uintBuffer);
		foreach (var comment in comments)
		{
			byte[] commentBytes = Encoding.UTF8.GetBytes(comment);
			BinaryPrimitives.WriteUInt32LittleEndian(uintBuffer, (uint)commentBytes.Length);
			ms.Write(uintBuffer);
			ms.Write(commentBytes, 0, commentBytes.Length);
		}

		ms.WriteByte(0x01);
		return ms.ToArray();
	}

	private static byte[] BuildOpusCommentPacket(string vendor, List<string> comments)
	{
		using var ms = new MemoryStream();
		byte[] magic = Encoding.ASCII.GetBytes("OpusTags");
		ms.Write(magic, 0, 8);

		Span<byte> uintBuffer = stackalloc byte[4];
		byte[] vendorBytes = Encoding.UTF8.GetBytes(vendor);
		BinaryPrimitives.WriteUInt32LittleEndian(uintBuffer, (uint)vendorBytes.Length);
		ms.Write(uintBuffer);
		ms.Write(vendorBytes, 0, vendorBytes.Length);

		BinaryPrimitives.WriteUInt32LittleEndian(uintBuffer, (uint)comments.Count);
		ms.Write(uintBuffer);
		foreach (var comment in comments)
		{
			byte[] commentBytes = Encoding.UTF8.GetBytes(comment);
			BinaryPrimitives.WriteUInt32LittleEndian(uintBuffer, (uint)commentBytes.Length);
			ms.Write(uintBuffer);
			ms.Write(commentBytes, 0, commentBytes.Length);
		}

		return ms.ToArray();
	}

	private static List<byte[]> PackPacketsIntoPages(
		List<byte[]> packets,
		uint serial,
		uint startSeq)
	{
		var pages = new List<byte[]>();
		var currentSegments = new List<byte>();
		using var currentPayload = new MemoryStream();
		bool isContinued = false;
		uint seq = startSeq;

		void FlushPage(bool packetContinues)
		{
			byte headerType = (byte)(isContinued ? 0x01 : 0x00);
			byte[] pageBytes = CreateOggPageBytes(
				headerType,
				0UL,
				serial,
				seq++,
				currentSegments,
				currentPayload.ToArray());
			pages.Add(pageBytes);
			currentSegments.Clear();
			currentPayload.SetLength(0);
			isContinued = packetContinues;
		}

		foreach (var packet in packets)
		{
			if (packet.Length == 0)
			{
				if (currentSegments.Count == 255)
				{
					FlushPage(false);
				}
				currentSegments.Add(0);
				isContinued = false;
				continue;
			}

			int offset = 0;
			int remaining = packet.Length;

			while (remaining > 0)
			{
				if (currentSegments.Count == 255)
				{
					FlushPage(true);
				}

				int segLen = Math.Min(remaining, 255);
				currentSegments.Add((byte)segLen);
				currentPayload.Write(packet, offset, segLen);
				offset += segLen;
				remaining -= segLen;

				if (segLen < 255)
				{
					isContinued = false;
					break;
				}
				else if (remaining == 0)
				{
					if (currentSegments.Count == 255)
					{
						FlushPage(true);
					}
					currentSegments.Add(0);
					isContinued = false;
					break;
				}
			}
		}

		if (currentSegments.Count > 0)
		{
			FlushPage(false);
		}

		return pages;
	}

	private static byte[] CreateOggPageBytes(
		byte headerType,
		ulong granulePos,
		uint serial,
		uint seq,
		List<byte> segments,
		byte[] payload)
	{
		int numSegments = segments.Count;
		byte[] pageBytes = new byte[27 + numSegments + payload.Length];

		pageBytes[0] = 0x4F;
		pageBytes[1] = 0x67;
		pageBytes[2] = 0x67;
		pageBytes[3] = 0x53;

		pageBytes[4] = 0;
		pageBytes[5] = headerType;

		BinaryPrimitives.WriteUInt64LittleEndian(pageBytes.AsSpan(6, 8), granulePos);
		BinaryPrimitives.WriteUInt32LittleEndian(pageBytes.AsSpan(14, 4), serial);
		BinaryPrimitives.WriteUInt32LittleEndian(pageBytes.AsSpan(18, 4), seq);

		pageBytes[26] = (byte)numSegments;

		for (int i = 0; i < numSegments; i++)
		{
			pageBytes[27 + i] = segments[i];
		}

		Buffer.BlockCopy(payload, 0, pageBytes, 27 + numSegments, payload.Length);

		uint crc = CalculateOggCrc(pageBytes);
		BinaryPrimitives.WriteUInt32LittleEndian(pageBytes.AsSpan(22, 4), crc);

		return pageBytes;
	}

	private static byte[] ReassembleOggStream(
		byte[] originalBytes,
		List<OggPageData> originalPages,
		int originalHeaderPageCount,
		List<byte[]> newHeaderPages)
	{
		using var outputMs = new MemoryStream();

		outputMs.Write(originalBytes, originalPages[0].Offset, originalPages[0].TotalLength);

		foreach (var pageBytes in newHeaderPages)
		{
			outputMs.Write(pageBytes, 0, pageBytes.Length);
		}

		int newHeaderPageCountTotal = 1 + newHeaderPages.Count;
		int seqDelta = newHeaderPageCountTotal - originalHeaderPageCount;

		if (seqDelta == 0)
		{
			if (originalHeaderPageCount < originalPages.Count)
			{
				int audioStartOffset = originalPages[originalHeaderPageCount].Offset;
				int audioLength = originalBytes.Length - audioStartOffset;
				outputMs.Write(originalBytes, audioStartOffset, audioLength);
			}
		}
		else
		{
			for (int p = originalHeaderPageCount; p < originalPages.Count; p++)
			{
				var page = originalPages[p];
				byte[] pageBytes = new byte[page.TotalLength];
				Buffer.BlockCopy(originalBytes, page.Offset, pageBytes, 0, page.TotalLength);

				uint newSeq = (uint)((long)page.PageSequenceNumber + seqDelta);
				BinaryPrimitives.WriteUInt32LittleEndian(pageBytes.AsSpan(18, 4), newSeq);

				pageBytes[22] = 0;
				pageBytes[23] = 0;
				pageBytes[24] = 0;
				pageBytes[25] = 0;

				uint crc = CalculateOggCrc(pageBytes);
				BinaryPrimitives.WriteUInt32LittleEndian(pageBytes.AsSpan(22, 4), crc);

				outputMs.Write(pageBytes, 0, pageBytes.Length);
			}
		}

		return outputMs.ToArray();
	}

	public static string? ExtractMetadataFromOgg(string filePath)
	{
		if (!File.Exists(filePath)) return null;
		byte[] bytes = File.ReadAllBytes(filePath);
		return ExtractMetadataFromOggBytes(bytes);
	}

	public static string? ExtractMetadataFromOggBytes(ReadOnlySpan<byte> bytes)
	{
		if (bytes.Length < 27) return null;
		int pos = 0;
		int pageIndex = 0;
		uint targetSerial = 0;
		using var commentBuffer = new MemoryStream();
		int packetIndex = 0;
		int expectedHeaderPackets = 0;

		while (pos + 27 <= bytes.Length)
		{
			if (bytes[pos] != 0x4F || bytes[pos + 1] != 0x67 || bytes[pos + 2] != 0x67 || bytes[pos + 3] != 0x53)
				break;

			uint serial = BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(pos + 14, 4));
			int numSegments = bytes[pos + 26];
			if (pos + 27 + numSegments > bytes.Length) break;

			var segTable = bytes.Slice(pos + 27, numSegments);
			int payloadLen = 0;
			for (int s = 0; s < numSegments; s++) payloadLen += segTable[s];

			int headerLen = 27 + numSegments;
			if (pos + headerLen + payloadLen > bytes.Length) break;

			var payload = bytes.Slice(pos + headerLen, payloadLen);

			if (pageIndex == 0)
			{
				targetSerial = serial;
				if (payload.Length >= 7 && payload.Slice(0, 7).SequenceEqual("\x01vorbis"u8))
				{
					expectedHeaderPackets = 3;
				}
				else if (payload.Length >= 8 && payload.Slice(0, 8).SequenceEqual("OpusHead"u8))
				{
					expectedHeaderPackets = 2;
				}
				else
				{
					return null;
				}
				packetIndex = 1;
			}
			else if (serial == targetSerial)
			{
				int payloadOffset = 0;
				for (int s = 0; s < numSegments; s++)
				{
					int segLen = segTable[s];
					if (packetIndex == 1)
					{
						commentBuffer.Write(payload.Slice(payloadOffset, segLen));
					}
					payloadOffset += segLen;

					if (segLen < 255)
					{
						if (packetIndex == 1)
						{
							byte[] commentPacket = commentBuffer.ToArray();
							if (TryExtractCommentsFromPacket(commentPacket, out _, out var comments, out _))
							{
								foreach (var comment in comments)
								{
									if (comment.StartsWith("REALM=", StringComparison.OrdinalIgnoreCase))
									{
										return comment.Substring(6);
									}
								}
							}
							return null;
						}
						packetIndex++;
						if (packetIndex >= expectedHeaderPackets)
							return null;
					}
				}
			}

			pos += headerLen + payloadLen;
			pageIndex++;
		}

		return null;
	}

	public static void AddMetadataToOgg(string filePath, string realmMetadataJson)
	{
		byte[] bytes = File.ReadAllBytes(filePath);
		byte[] updated = AddMetadataToOggBytes(bytes, realmMetadataJson);
		File.WriteAllBytes(filePath, updated);
	}

	public static byte[] AddMetadataToOggBytes(byte[] bytes, string realmMetadataJson)
	{
		if (bytes == null || bytes.Length < 27) return bytes ?? Array.Empty<byte>();

		var pages = ParseOggPages(bytes);
		if (pages.Count == 0) return bytes;

		if (!TryExtractHeaderPackets(pages, out string codecType, out var headerPackets, out int headerPageCount))
		{
			return bytes;
		}

		if (headerPackets.Count < 2) return bytes;
		byte[] oldCommentPacket = headerPackets[1];

		if (!TryExtractCommentsFromPacket(oldCommentPacket, out string vendor, out var comments, out _))
		{
			return bytes;
		}

		var updatedComments = new List<string>();
		foreach (var comment in comments)
		{
			if (!comment.StartsWith("REALM=", StringComparison.OrdinalIgnoreCase))
			{
				updatedComments.Add(comment);
			}
		}
		updatedComments.Add("REALM=" + realmMetadataJson);

		byte[] newCommentPacket = codecType == "vorbis"
			? BuildVorbisCommentPacket(vendor, updatedComments)
			: BuildOpusCommentPacket(vendor, updatedComments);

		var newHeaderPacketsToPack = new List<byte[]>();
		newHeaderPacketsToPack.Add(newCommentPacket);
		for (int i = 2; i < headerPackets.Count; i++)
		{
			newHeaderPacketsToPack.Add(headerPackets[i]);
		}

		uint serial = pages[0].BitstreamSerialNumber;
		var newHeaderPages = PackPacketsIntoPages(newHeaderPacketsToPack, serial, 1);

		return ReassembleOggStream(bytes, pages, headerPageCount, newHeaderPages);
	}

	public static void RemoveMetadataFromOgg(string filePath)
	{
		byte[] bytes = File.ReadAllBytes(filePath);
		byte[] updated = RemoveMetadataFromOggBytes(bytes);
		File.WriteAllBytes(filePath, updated);
	}

	public static byte[] RemoveMetadataFromOggBytes(byte[] bytes)
	{
		if (bytes == null || bytes.Length < 27) return bytes ?? Array.Empty<byte>();

		var pages = ParseOggPages(bytes);
		if (pages.Count == 0) return bytes;

		if (!TryExtractHeaderPackets(pages, out string codecType, out var headerPackets, out int headerPageCount))
		{
			return bytes;
		}

		if (headerPackets.Count < 2) return bytes;
		byte[] oldCommentPacket = headerPackets[1];

		if (!TryExtractCommentsFromPacket(oldCommentPacket, out string vendor, out var comments, out _))
		{
			return bytes;
		}

		bool hasRealmComment = false;
		var updatedComments = new List<string>();
		foreach (var comment in comments)
		{
			if (comment.StartsWith("REALM=", StringComparison.OrdinalIgnoreCase))
			{
				hasRealmComment = true;
			}
			else
			{
				updatedComments.Add(comment);
			}
		}

		if (!hasRealmComment) return bytes;

		byte[] newCommentPacket = codecType == "vorbis"
			? BuildVorbisCommentPacket(vendor, updatedComments)
			: BuildOpusCommentPacket(vendor, updatedComments);

		var newHeaderPacketsToPack = new List<byte[]>();
		newHeaderPacketsToPack.Add(newCommentPacket);
		for (int i = 2; i < headerPackets.Count; i++)
		{
			newHeaderPacketsToPack.Add(headerPackets[i]);
		}

		uint serial = pages[0].BitstreamSerialNumber;
		var newHeaderPages = PackPacketsIntoPages(newHeaderPacketsToPack, serial, 1);

		return ReassembleOggStream(bytes, pages, headerPageCount, newHeaderPages);
	}

	public static bool IsGlbBytes(ReadOnlySpan<byte> bytes)
	{
		if (bytes.Length < 20) return false;
		return BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(0, 4)) == 0x46546C67;
	}

	public static bool IsRtexBytes(ReadOnlySpan<byte> bytes)
	{
		return Realm.Shared.Textures.RtexFile.IsRtexBytes(bytes);
	}

	public static bool IsOggBytes(ReadOnlySpan<byte> bytes)
	{
		if (bytes.Length < 4) return false;
		return bytes[0] == 0x4F && bytes[1] == 0x67 && bytes[2] == 0x67 && bytes[3] == 0x53;
	}

	public static bool IsRanimBytes(ReadOnlySpan<byte> bytes)
	{
		if (bytes.Length == 0) return false;
		if (bytes.Length >= 8 &&
			bytes[bytes.Length - 4] == (byte)'R' &&
			bytes[bytes.Length - 3] == (byte)'M' &&
			bytes[bytes.Length - 2] == (byte)'E' &&
			bytes[bytes.Length - 1] == (byte)'T')
		{
			return true;
		}

		if (bytes[0] == (byte)'{' || bytes[0] == (byte)'[')
		{
			try
			{
				string json = Encoding.UTF8.GetString(bytes);
				using var doc = JsonDocument.Parse(json);
				var root = doc.RootElement;
				if (root.TryGetProperty("AnimationName", out _) ||
					root.TryGetProperty("animationName", out _) ||
					root.TryGetProperty("Tracks", out _) ||
					root.TryGetProperty("tracks", out _) ||
					root.TryGetProperty("FrameRate", out _) ||
					root.TryGetProperty("frameRate", out _))
				{
					return true;
				}
			}
			catch { }
		}

		return false;
	}

	public static byte[] StripMetadataEphemeral(byte[] bytes, string? extensionOrPath = null)
	{
		if (bytes == null || bytes.Length == 0) return bytes ?? Array.Empty<byte>();

		string extension = string.Empty;
		if (!string.IsNullOrEmpty(extensionOrPath))
		{
			extension = Path.GetExtension(extensionOrPath).ToLowerInvariant();
		}

		try
		{
			if (extension == ".glb" || ((string.IsNullOrEmpty(extension) || extension == ".bin") && IsGlbBytes(bytes)))
			{
				return RemoveMetadataFromGlbBytes(bytes);
			}
			if (extension == ".rtex" || ((string.IsNullOrEmpty(extension) || extension == ".bin") && IsRtexBytes(bytes)))
			{
				return Realm.Shared.Textures.RtexFile.SetMetadata(bytes, null);
			}
			if (extension == ".ranim" || ((string.IsNullOrEmpty(extension) || extension == ".bin") && IsRanimBytes(bytes)))
			{
				return RemoveMetadataFromRanimBytes(bytes);
			}
			if (extension == ".ogg" || ((string.IsNullOrEmpty(extension) || extension == ".bin") && IsOggBytes(bytes)))
			{
				return RemoveMetadataFromOggBytes(bytes);
			}
		}
		catch
		{
			return bytes;
		}

		return bytes;
	}

	public static string ComputeBlake3(byte[] bytes, string? extensionOrPath = null)
	{
		if (bytes == null || bytes.Length == 0)
		{
			return Hasher.Hash(ReadOnlySpan<byte>.Empty).ToString();
		}

		byte[] canonicalBytes = StripMetadataEphemeral(bytes, extensionOrPath);
		var hash = Hasher.Hash(canonicalBytes);
		return hash.ToString();
	}

	public static string ComputeBlake3(string filePath)
	{
		if (!File.Exists(filePath)) return string.Empty;
		byte[] bytes = File.ReadAllBytes(filePath);
		return ComputeBlake3(bytes, filePath);
	}

	public static string ComputeBlake3(Stream stream, string? extensionOrPath = null)
	{
		using var memoryStream = new MemoryStream();
		stream.CopyTo(memoryStream);
		return ComputeBlake3(memoryStream.ToArray(), extensionOrPath);
	}

	public static string ComputeCanonicalAssetIdentifier(byte[] bytes, string extensionOrPath)
	{
		string extension = Path.GetExtension(extensionOrPath).ToLowerInvariant();
		string blake3Hash = ComputeBlake3(bytes, extension);
		return string.IsNullOrEmpty(extension) ? blake3Hash : $"{blake3Hash}{extension}";
	}

	public static bool SyncBlake3Metadata(string filePath)
	{
		if (!File.Exists(filePath)) return false;
		string ext = Path.GetExtension(filePath).ToLowerInvariant();
		if (ext is not (".glb" or ".rtex" or ".ranim" or ".ogg")) return false;

		try
		{
			string canonicalBlake3 = ComputeBlake3(filePath);
			string? existingMeta = ExtractMetadata(filePath);
			JsonObject metaObj;
			if (!string.IsNullOrWhiteSpace(existingMeta))
			{
				try
				{
					metaObj = JsonNode.Parse(existingMeta) as JsonObject ?? new JsonObject();
				}
				catch
				{
					metaObj = new JsonObject();
				}
			}
			else
			{
				metaObj = new JsonObject();
				metaObj["created_utc"] = DateTime.UtcNow.ToString("O");
				metaObj["format"] = ext.TrimStart('.');
			}

			metaObj["blake3"] = canonicalBlake3;
			return AddMetadata(filePath, metaObj.ToJsonString());
		}
		catch
		{
			return false;
		}
	}

	public static byte[] SyncBlake3MetadataBytes(byte[] bytes, string extensionOrPath)
	{
		if (bytes == null || bytes.Length == 0) return bytes ?? Array.Empty<byte>();
		string ext = Path.GetExtension(extensionOrPath).ToLowerInvariant();
		if (string.IsNullOrEmpty(ext) && extensionOrPath.StartsWith('.')) ext = extensionOrPath.ToLowerInvariant();
		if (ext is not (".glb" or ".rtex" or ".ranim" or ".ogg")) return bytes;

		try
		{
			string canonicalBlake3 = ComputeBlake3(bytes, ext);
			string? existingMeta = null;
			if (ext == ".glb") existingMeta = ExtractMetadataFromGlbBytes(bytes);
			else if (ext == ".rtex") existingMeta = RtexFile.ExtractMetadata(bytes);
			else if (ext == ".ranim") existingMeta = ExtractMetadataFromRanimBytes(bytes);
			else if (ext == ".ogg") existingMeta = ExtractMetadataFromOggBytes(bytes);

			JsonObject metaObj;
			if (!string.IsNullOrWhiteSpace(existingMeta))
			{
				try
				{
					metaObj = JsonNode.Parse(existingMeta) as JsonObject ?? new JsonObject();
				}
				catch
				{
					metaObj = new JsonObject();
				}
			}
			else
			{
				metaObj = new JsonObject();
				metaObj["created_utc"] = DateTime.UtcNow.ToString("O");
				metaObj["format"] = ext.TrimStart('.');
			}

			metaObj["blake3"] = canonicalBlake3;
			string newMetaJson = metaObj.ToJsonString();

			return ext switch
			{
				".glb" => AddMetadataToGlbBytes(bytes, newMetaJson),
				".rtex" => RtexFile.SetMetadata(bytes, newMetaJson),
				".ranim" => AddMetadataToRanimBytes(bytes, newMetaJson),
				".ogg" => AddMetadataToOggBytes(bytes, newMetaJson),
				_ => bytes
			};
		}
		catch
		{
			return bytes;
		}
	}
}
