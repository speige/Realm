using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Blake3;
using Realm.Shared.Audio;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Textures;

namespace Realm.Shared.Metadata;

public static class RealmMetadataHelper
{
	public static bool SupportsMetadata(string extensionOrPath)
	{
		string ext = Path.GetExtension(extensionOrPath).ToLowerInvariant();
		if (string.IsNullOrEmpty(ext) && extensionOrPath.StartsWith('.')) ext = extensionOrPath.ToLowerInvariant();
		return ext is ".rtex" or ".ranim" or ".rmesh" or ".raud" or ".rkey";
	}

	public static string? ExtractMetadata(string filePath)
	{
		if (!File.Exists(filePath)) return null;
		string ext = Path.GetExtension(filePath).ToLowerInvariant();
		return ext switch
		{
			".rmesh" => ExtractMetadataFromRmesh(filePath),
			".rtex" => ExtractMetadataFromRtex(filePath),
			".ranim" => ExtractMetadataFromRanim(filePath),
			".raud" => ExtractMetadataFromRaud(filePath),
			".rkey" => ExtractMetadataFromRkey(filePath),
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
		[".rtex"] = new[] { "Decal", "Icon", "Noise", "Ribbon", "Skybox", "Spritesheet", "Terrain", "vfx_radial", "vfx_vertical" },
		[".rmesh"] = new[] { "Character", "Building", "Prop", "Item" },
		[".ranim"] = new[] { "Animation" },
		[".raud"] = new[] { "Music", "SoundEffect" }
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
			if (norm.Contains("tile") || norm.Contains("terrain")) { canonicalType = "Terrain"; return true; }
			if (norm.Contains("decal")) { canonicalType = "Decal"; return true; }
			if (norm.Contains("icon")) { canonicalType = "Icon"; return true; }
			if (norm.Contains("noise")) { canonicalType = "Noise"; return true; }
			if (norm.Contains("ribbon")) { canonicalType = "Ribbon"; return true; }
			if (norm.Contains("skybox")) { canonicalType = "Skybox"; return true; }
			if (norm.Contains("sprite") || norm.Contains("vfx") || norm.Contains("spell")) { canonicalType = "Spritesheet"; return true; }
			return false;
		}
		else if (ext is ".rmesh")
		{
			if (norm.Contains("character") || norm.Contains("unit")) { canonicalType = "Character"; return true; }
			if (norm.Contains("building") || norm.Contains("structure")) { canonicalType = "Building"; return true; }
			if (norm.Contains("environment") || norm.Contains("resource") || norm.Contains("prop")) { canonicalType = "Prop"; return true; }
			if (norm.Contains("item") || norm.Contains("attachment") || norm.Contains("weapon") || norm.Contains("projectile") || norm.Contains("gear") || norm.Contains("equipment") || norm.Contains("accessory") || norm.Contains("object")) { canonicalType = "Item"; return true; }
			return false;
		}
		else if (ext is ".ranim")
		{
			canonicalType = "Animation";
			return true;
		}
		else if (ext is ".raud")
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

	public static string? ExtractAuthor(string filePath)
	{
		string? metaJson = ExtractMetadata(filePath);
		if (string.IsNullOrEmpty(metaJson)) return null;
		try
		{
			var node = JsonNode.Parse(metaJson);
			return node?["author"]?.ToString() ?? node?["Author"]?.ToString();
		}
		catch { }
		return null;
	}

	public static bool SetAuthor(string filePath, string author)
	{
		if (!File.Exists(filePath)) return false;
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

		metaObj["author"] = author;
		metaObj["blake3"] = ComputeBlake3(filePath);
		return AddMetadata(filePath, metaObj.ToJsonString());
	}

	public static string? ExtractPreferredFileName(string filePath)
	{
		string? metaJson = ExtractMetadata(filePath);
		if (string.IsNullOrEmpty(metaJson)) return null;
		try
		{
			var node = JsonNode.Parse(metaJson);
			return node?["preferred_file_name"]?.ToString() ?? node?["preferredFileName"]?.ToString();
		}
		catch { }
		return null;
	}

	public static bool SetPreferredFileName(string filePath, string preferredFileName)
	{
		if (!File.Exists(filePath)) return false;
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

		metaObj["preferred_file_name"] = preferredFileName;
		metaObj["blake3"] = ComputeBlake3(filePath);
		return AddMetadata(filePath, metaObj.ToJsonString());
	}

	public static bool? ExtractSupportsTeamColor(string filePath)
	{
		string? metaJson = ExtractMetadata(filePath);
		if (string.IsNullOrEmpty(metaJson)) return null;
		try
		{
			var node = JsonNode.Parse(metaJson);
			if (node is JsonObject obj)
			{
				if (obj.TryGetPropertyValue("team_color", out var tcVal) && tcVal != null)
				{
					if (tcVal.GetValueKind() == System.Text.Json.JsonValueKind.True) return true;
					if (tcVal.GetValueKind() == System.Text.Json.JsonValueKind.False) return false;
				}
				if (obj.TryGetPropertyValue("chroma_key", out var val) && val != null)
				{
					if (val.GetValueKind() == System.Text.Json.JsonValueKind.True) return true;
					if (val.GetValueKind() == System.Text.Json.JsonValueKind.False) return false;
					if (val.GetValueKind() == System.Text.Json.JsonValueKind.String)
					{
						string s = val.GetValue<string>();
						return !string.IsNullOrWhiteSpace(s);
					}
				}
			}
		}
		catch { }
		return null;
	}

	public static bool SetSupportsTeamColor(string filePath, bool supportsTeamColor)
	{
		if (!File.Exists(filePath)) return false;
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

		metaObj["team_color"] = supportsTeamColor;
		metaObj["blake3"] = ComputeBlake3(filePath);
		return AddMetadata(filePath, metaObj.ToJsonString());
	}

	public static string? ExtractChromaKey(string filePath)
	{
		string? metaJson = ExtractMetadata(filePath);
		return ExtractChromaKeyFromMetadataJson(metaJson);
	}

	public static string? ExtractChromaKeyFromMetadataJson(string? metaJson)
	{
		if (string.IsNullOrEmpty(metaJson)) return null;
		try
		{
			var node = JsonNode.Parse(metaJson);
			return node?["chroma_key"]?.ToString()
				?? node?["chromaKey"]?.ToString()
				?? node?["target_hex"]?.ToString()
				?? node?["targetHex"]?.ToString();
		}
		catch { }
		return null;
	}

	public static bool SetChromaKey(string filePath, string chromaKey)
	{
		if (!File.Exists(filePath)) return false;
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

		metaObj["chroma_key"] = chromaKey;
		metaObj["blake3"] = ComputeBlake3(filePath);
		return AddMetadata(filePath, metaObj.ToJsonString());
	}

	public static string? ExtractLicense(string filePath)
	{
		string? metaJson = ExtractMetadata(filePath);
		if (string.IsNullOrEmpty(metaJson)) return null;
		try
		{
			var node = JsonNode.Parse(metaJson);
			return node?["license"]?.ToString() ?? node?["License"]?.ToString();
		}
		catch { }
		return null;
	}

	public static bool SetLicense(string filePath, string license)
	{
		if (!File.Exists(filePath)) return false;
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

		metaObj["license"] = license;
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
		if (ext is not (".rtex" or ".ranim" or ".rmesh" or ".raud")) return false;

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
			case ".rmesh":
				AddMetadataToRmesh(filePath, realmMetadataJson);
				return true;
			case ".rtex":
				AddMetadataToRtex(filePath, realmMetadataJson);
				return true;
			case ".ranim":
				AddMetadataToRanim(filePath, realmMetadataJson);
				return true;
			case ".raud":
				AddMetadataToRaud(filePath, realmMetadataJson);
				return true;
			case ".rkey":
				AddMetadataToRkey(filePath, realmMetadataJson);
				return true;
			default:
				throw new NotSupportedException($"Unsupported file format '{ext}' for metadata. Supported formats: .rmesh, .rtex, .raud, .ranim, .rkey");
		}
	}

	public static bool RemoveMetadata(string filePath)
	{
		if (!File.Exists(filePath)) return false;
		string ext = Path.GetExtension(filePath).ToLowerInvariant();
		switch (ext)
		{
			case ".rmesh":
				RemoveMetadataFromRmesh(filePath);
				return true;
			case ".rtex":
				RemoveMetadataFromRtex(filePath);
				return true;
			case ".ranim":
				RemoveMetadataFromRanim(filePath);
				return true;
			case ".raud":
				RemoveMetadataFromRaud(filePath);
				return true;
			case ".rkey":
				RemoveMetadataFromRkey(filePath);
				return true;
			default:
				throw new NotSupportedException($"Unsupported file format '{ext}' for metadata. Supported formats: .rmesh, .rtex, .raud, .ranim, .rkey");
		}
	}

	public static string? ExtractMetadataFromRmesh(string filePath)
	{
		return RmeshFile.ExtractMetadataFromFile(filePath);
	}

	public static void AddMetadataToRmesh(string filePath, string realmMetadataJson)
	{
		byte[] bytes = File.ReadAllBytes(filePath);
		byte[] updated = RmeshFile.SetMetadata(bytes, realmMetadataJson);
		File.WriteAllBytes(filePath, updated);
	}

	public static void RemoveMetadataFromRmesh(string filePath)
	{
		byte[] bytes = File.ReadAllBytes(filePath);
		byte[] updated = RmeshFile.SetMetadata(bytes, null);
		File.WriteAllBytes(filePath, updated);
	}

	public static string? ExtractMetadataFromRaud(string filePath)
	{
		return RaudFile.ExtractMetadataFromFile(filePath);
	}

	public static void AddMetadataToRaud(string filePath, string realmMetadataJson)
	{
		byte[] bytes = File.ReadAllBytes(filePath);
		byte[] updated = RaudFile.SetMetadata(bytes, realmMetadataJson);
		File.WriteAllBytes(filePath, updated);
	}

	public static void RemoveMetadataFromRaud(string filePath)
	{
		byte[] bytes = File.ReadAllBytes(filePath);
		byte[] updated = RaudFile.SetMetadata(bytes, null);
		File.WriteAllBytes(filePath, updated);
	}

	public static string? ExtractMetadataFromRtex(string filePath)
	{
		return Realm.Shared.Textures.RtexFile.ExtractMetadataFromFile(filePath);
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

	public static string? ExtractMetadataFromRkey(string filePath)
	{
		return RkeyFile.ExtractMetadataFromFile(filePath);
	}

	public static void AddMetadataToRkey(string filePath, string realmMetadataJson)
	{
		byte[] bytes = File.ReadAllBytes(filePath);
		byte[] updated = RkeyFile.SetMetadata(bytes, realmMetadataJson);
		File.WriteAllBytes(filePath, updated);
	}

	public static void RemoveMetadataFromRkey(string filePath)
	{
		byte[] bytes = File.ReadAllBytes(filePath);
		byte[] updated = RkeyFile.SetMetadata(bytes, null);
		File.WriteAllBytes(filePath, updated);
	}

	public static string? ExtractMetadataFromRanim(string filePath)
	{
		if (!File.Exists(filePath)) return null;
		try
		{
			using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, bufferSize: 4096);
			if (stream.Length == 0) return null;

			int firstByte = stream.ReadByte();
			if (firstByte == '{' || firstByte == '[')
			{
				stream.Position = 0;
				using var reader = new StreamReader(stream, Encoding.UTF8);
				string jsonText = reader.ReadToEnd();
				try
				{
					using var doc = JsonDocument.Parse(jsonText);
					if (doc.RootElement.TryGetProperty("Realm", out var realmProp) || doc.RootElement.TryGetProperty("realm", out realmProp))
					{
						return realmProp.ValueKind == JsonValueKind.String ? realmProp.GetString() : realmProp.GetRawText();
					}
					return jsonText;
				}
				catch { }
				return null;
			}

			if (stream.Length >= 8)
			{
				stream.Seek(-8, SeekOrigin.End);
				Span<byte> trailer = stackalloc byte[8];
				if (stream.Read(trailer) == 8)
				{
					if (trailer[4] == (byte)'R' && trailer[5] == (byte)'M' && trailer[6] == (byte)'E' && trailer[7] == (byte)'T')
					{
						uint metaLen = BinaryPrimitives.ReadUInt32LittleEndian(trailer.Slice(0, 4));
						if (metaLen > 0 && stream.Length >= 8 + metaLen && metaLen <= 10 * 1024 * 1024)
						{
							stream.Seek(-8 - (long)metaLen, SeekOrigin.End);
							byte[] metaBytes = new byte[metaLen];
							int read = 0;
							while (read < metaLen)
							{
								int r = stream.Read(metaBytes, read, (int)metaLen - read);
								if (r <= 0) break;
								read += r;
							}
							if (read == (int)metaLen)
							{
								return Encoding.UTF8.GetString(metaBytes);
							}
						}
					}
				}
			}
			return null;
		}
		catch
		{
			return null;
		}
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

	public static bool IsRmeshBytes(ReadOnlySpan<byte> bytes)
	{
		return RmeshFile.IsRmeshBytes(bytes);
	}

	public static bool IsRtexBytes(ReadOnlySpan<byte> bytes)
	{
		return Realm.Shared.Textures.RtexFile.IsRtexBytes(bytes);
	}

	public static bool IsRaudBytes(ReadOnlySpan<byte> bytes)
	{
		return RaudFile.IsRaudBytes(bytes);
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
			if (extension == ".rmesh" || ((string.IsNullOrEmpty(extension) || extension == ".bin") && IsRmeshBytes(bytes)))
			{
				return RmeshFile.SetMetadata(bytes, null);
			}
			if (extension == ".raud" || ((string.IsNullOrEmpty(extension) || extension == ".bin") && IsRaudBytes(bytes)))
			{
				return RaudFile.SetMetadata(bytes, null);
			}
			if (extension == ".rtex" || ((string.IsNullOrEmpty(extension) || extension == ".bin") && IsRtexBytes(bytes)))
			{
				return Realm.Shared.Textures.RtexFile.SetMetadata(bytes, null);
			}
			if (extension == ".ranim" || ((string.IsNullOrEmpty(extension) || extension == ".bin") && IsRanimBytes(bytes)))
			{
				return RemoveMetadataFromRanimBytes(bytes);
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

	public static bool SyncBlake3Metadata(string filePath, string? precomputedCanonicalBlake3 = null)
	{
		if (!File.Exists(filePath)) return false;
		string ext = Path.GetExtension(filePath).ToLowerInvariant();
		if (ext is not (".rtex" or ".ranim" or ".rmesh" or ".raud")) return false;

		try
		{
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

			if (metaObj.TryGetPropertyValue("blake3", out var existingB3) && existingB3 != null)
			{
				string existingHashStr = existingB3.ToString();
				if (!string.IsNullOrEmpty(precomputedCanonicalBlake3) && string.Equals(existingHashStr, precomputedCanonicalBlake3, StringComparison.OrdinalIgnoreCase))
				{
					return true;
				}
				if (string.IsNullOrEmpty(precomputedCanonicalBlake3))
				{
					string canonical = ComputeBlake3(filePath);
					if (string.Equals(existingHashStr, canonical, StringComparison.OrdinalIgnoreCase))
					{
						return true;
					}
					precomputedCanonicalBlake3 = canonical;
				}
			}

			string canonicalBlake3 = !string.IsNullOrEmpty(precomputedCanonicalBlake3) ? precomputedCanonicalBlake3 : ComputeBlake3(filePath);
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
		if (ext is not (".rtex" or ".ranim" or ".rmesh" or ".raud")) return bytes;

		try
		{
			string canonicalBlake3 = ComputeBlake3(bytes, ext);
			string? existingMeta = null;
			if (ext == ".rmesh") existingMeta = RmeshFile.ExtractMetadata(bytes);
			else if (ext == ".rtex") existingMeta = RtexFile.ExtractMetadata(bytes);
			else if (ext == ".ranim") existingMeta = ExtractMetadataFromRanimBytes(bytes);
			else if (ext == ".raud") existingMeta = RaudFile.ExtractMetadata(bytes);

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

			if (metaObj.TryGetPropertyValue("blake3", out var existingB3) && existingB3 != null && string.Equals(existingB3.ToString(), canonicalBlake3, StringComparison.OrdinalIgnoreCase))
			{
				return bytes;
			}

			metaObj["blake3"] = canonicalBlake3;
			string newMetaJson = metaObj.ToJsonString();

			return ext switch
			{
				".rmesh" => RmeshFile.SetMetadata(bytes, newMetaJson),
				".rtex" => RtexFile.SetMetadata(bytes, newMetaJson),
				".ranim" => AddMetadataToRanimBytes(bytes, newMetaJson),
				".raud" => RaudFile.SetMetadata(bytes, newMetaJson),
				_ => bytes
			};
		}
		catch
		{
			return bytes;
		}
	}
}
