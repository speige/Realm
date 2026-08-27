using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Realm.Shared.Metadata;

public static class RealmMetadataHelper
{
	private static readonly uint[] CrcTable = InitializeCrcTable();
	private static readonly uint[] OggCrcTable = InitializeOggCrcTable();

	private static uint[] InitializeCrcTable()
	{
		uint[] table = new uint[256];
		for (uint i = 0; i < 256; i++)
		{
			uint c = i;
			for (int k = 0; k < 8; k++)
			{
				if ((c & 1) != 0) c = 0xEDB88320 ^ (c >> 1);
				else c >>= 1;
			}
			table[i] = c;
		}
		return table;
	}

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

	public static uint CalculatePngCrc(ReadOnlySpan<byte> typeBytes, ReadOnlySpan<byte> dataBytes)
	{
		uint crc = 0xFFFFFFFF;
		foreach (byte b in typeBytes) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
		foreach (byte b in dataBytes) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
		return crc ^ 0xFFFFFFFF;
	}

	public static uint CalculateOggCrc(ReadOnlySpan<byte> data)
	{
		uint crc = 0;
		foreach (byte b in data) crc = (crc << 8) ^ OggCrcTable[((crc >> 24) ^ b) & 0xFF];
		return crc;
	}

	public static string? ExtractMetadata(string filePath)
	{
		if (!File.Exists(filePath)) return null;
		string ext = Path.GetExtension(filePath).ToLowerInvariant();
		return ext switch
		{
			".glb" => ExtractMetadataFromGlb(filePath),
			".png" => ExtractMetadataFromPng(filePath),
			".ktx2" or ".ktx" => ExtractMetadataFromKtx2(filePath),
			".ranim" => ExtractMetadataFromRanim(filePath),
			".ogg" => ExtractMetadataFromOgg(filePath),
			_ => null
		};
	}

	public static bool AddMetadata(string filePath, string realmMetadataJson)
	{
		if (!File.Exists(filePath)) return false;
		string ext = Path.GetExtension(filePath).ToLowerInvariant();
		try
		{
			switch (ext)
			{
				case ".glb":
					AddMetadataToGlb(filePath, realmMetadataJson);
					return true;
				case ".png":
					AddMetadataToPng(filePath, realmMetadataJson);
					return true;
				case ".ktx2" or ".ktx":
					AddMetadataToKtx2(filePath, realmMetadataJson);
					return true;
				case ".ranim":
					AddMetadataToRanim(filePath, realmMetadataJson);
					return true;
				case ".ogg":
					AddMetadataToOgg(filePath, realmMetadataJson);
					return true;
				default:
					return false;
			}
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"Failed to add metadata to '{filePath}': {ex.Message}");
			return false;
		}
	}

	public static bool RemoveMetadata(string filePath)
	{
		if (!File.Exists(filePath)) return false;
		string ext = Path.GetExtension(filePath).ToLowerInvariant();
		try
		{
			switch (ext)
			{
				case ".glb":
					RemoveMetadataFromGlb(filePath);
					return true;
				case ".png":
					RemoveMetadataFromPng(filePath);
					return true;
				case ".ktx2" or ".ktx":
					RemoveMetadataFromKtx2(filePath);
					return true;
				case ".ranim":
					RemoveMetadataFromRanim(filePath);
					return true;
				case ".ogg":
					RemoveMetadataFromOgg(filePath);
					return true;
				default:
					return false;
			}
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"Failed to remove metadata from '{filePath}': {ex.Message}");
			return false;
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
		uint magic = BitConverter.ToUInt32(bytes.Slice(0, 4));
		if (magic != 0x46546C67) return null;

		uint chunk0Length = BitConverter.ToUInt32(bytes.Slice(12, 4));
		uint chunk0Type = BitConverter.ToUInt32(bytes.Slice(16, 4));
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
		uint magic = BitConverter.ToUInt32(bytes, 0);
		if (magic != 0x46546C67) throw new InvalidOperationException("Invalid GLB magic header.");

		uint version = BitConverter.ToUInt32(bytes, 4);
		uint chunk0Length = BitConverter.ToUInt32(bytes, 12);
		uint chunk0Type = BitConverter.ToUInt32(bytes, 16);
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
		using var ms = new MemoryStream();
		using var writer = new BinaryWriter(ms);
		writer.Write(magic);
		writer.Write(version);
		writer.Write(newTotalLength);
		writer.Write((uint)paddedJson.Length);
		writer.Write(chunk0Type);
		writer.Write(paddedJson);
		if (chunk1RemainingLength > 0)
		{
			writer.Write(bytes, chunk1Offset, chunk1RemainingLength);
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
		uint magic = BitConverter.ToUInt32(bytes, 0);
		if (magic != 0x46546C67) return bytes;

		uint version = BitConverter.ToUInt32(bytes, 4);
		uint chunk0Length = BitConverter.ToUInt32(bytes, 12);
		uint chunk0Type = BitConverter.ToUInt32(bytes, 16);
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
		using var ms = new MemoryStream();
		using var writer = new BinaryWriter(ms);
		writer.Write(magic);
		writer.Write(version);
		writer.Write(newTotalLength);
		writer.Write((uint)paddedJson.Length);
		writer.Write(chunk0Type);
		writer.Write(paddedJson);
		if (chunk1RemainingLength > 0)
		{
			writer.Write(bytes, chunk1Offset, chunk1RemainingLength);
		}
		return ms.ToArray();
	}

	public static string? ExtractMetadataFromPng(string filePath)
	{
		if (!File.Exists(filePath)) return null;
		byte[] bytes = File.ReadAllBytes(filePath);
		return ExtractMetadataFromPngBytes(bytes);
	}

	public static string? ExtractMetadataFromPngBytes(ReadOnlySpan<byte> bytes)
	{
		if (bytes.Length < 8) return null;
		ReadOnlySpan<byte> pngSig = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
		if (!bytes.Slice(0, 8).SequenceEqual(pngSig)) return null;

		int offset = 8;
		while (offset + 8 <= bytes.Length)
		{
			int length = (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
			string chunkType = Encoding.ASCII.GetString(bytes.Slice(offset + 4, 4));
			int dataOffset = offset + 8;
			if (dataOffset + length + 4 > bytes.Length) break;

			if (chunkType == "tEXt" && length > 0)
			{
				var dataSpan = bytes.Slice(dataOffset, length);
				int nullIdx = dataSpan.IndexOf((byte)0);
				if (nullIdx > 0)
				{
					string keyword = Encoding.ASCII.GetString(dataSpan.Slice(0, nullIdx));
					if (keyword.Equals("Realm", StringComparison.OrdinalIgnoreCase))
					{
						return Encoding.UTF8.GetString(dataSpan.Slice(nullIdx + 1));
					}
				}
			}
			else if (chunkType == "iTXt" && length > 5)
			{
				var dataSpan = bytes.Slice(dataOffset, length);
				int nullIdx = dataSpan.IndexOf((byte)0);
				if (nullIdx > 0)
				{
					string keyword = Encoding.ASCII.GetString(dataSpan.Slice(0, nullIdx));
					if (keyword.Equals("Realm", StringComparison.OrdinalIgnoreCase))
					{
						int cur = nullIdx + 1;
						if (cur + 2 <= dataSpan.Length)
						{
							byte compFlag = dataSpan[cur];
							cur += 2;
							while (cur < dataSpan.Length && dataSpan[cur] != 0) cur++;
							cur++;
							while (cur < dataSpan.Length && dataSpan[cur] != 0) cur++;
							cur++;

							if (cur <= dataSpan.Length)
							{
								var textBytes = dataSpan.Slice(cur);
								if (compFlag == 0)
								{
									return Encoding.UTF8.GetString(textBytes);
								}
								else
								{
									try
									{
										using var compMs = new MemoryStream(textBytes.ToArray());
										using var zlib = new ZLibStream(compMs, CompressionMode.Decompress);
										using var outMs = new MemoryStream();
										zlib.CopyTo(outMs);
										return Encoding.UTF8.GetString(outMs.ToArray());
									}
									catch { }
								}
							}
						}
					}
				}
			}
			else if (chunkType == "IEND")
			{
				break;
			}

			offset += 8 + length + 4;
		}
		return null;
	}

	public static void AddMetadataToPng(string filePath, string realmMetadataJson)
	{
		byte[] bytes = File.ReadAllBytes(filePath);
		byte[] updated = AddMetadataToPngBytes(bytes, realmMetadataJson);
		File.WriteAllBytes(filePath, updated);
	}

	public static byte[] AddMetadataToPngBytes(byte[] bytes, string realmMetadataJson)
	{
		if (bytes.Length < 8) throw new InvalidOperationException("Invalid PNG file.");
		byte[] pngSig = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
		for (int i = 0; i < 8; i++)
			if (bytes[i] != pngSig[i]) throw new InvalidOperationException("Invalid PNG signature.");

		byte[] keyBytes = Encoding.ASCII.GetBytes("Realm\0");
		byte[] jsonBytes = Encoding.UTF8.GetBytes(realmMetadataJson);
		byte[] textChunkData = new byte[keyBytes.Length + jsonBytes.Length];
		Buffer.BlockCopy(keyBytes, 0, textChunkData, 0, keyBytes.Length);
		Buffer.BlockCopy(jsonBytes, 0, textChunkData, keyBytes.Length, jsonBytes.Length);

		var chunks = new List<(string Type, byte[] Data)>();
		int offset = 8;
		bool inserted = false;

		while (offset + 8 <= bytes.Length)
		{
			int length = (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
			string chunkType = Encoding.ASCII.GetString(bytes, offset + 4, 4);
			int dataOffset = offset + 8;
			if (dataOffset + length + 4 > bytes.Length) break;

			byte[] chunkData = new byte[length];
			Buffer.BlockCopy(bytes, dataOffset, chunkData, 0, length);

			bool isOldRealm = false;
			if (chunkType == "tEXt" || chunkType == "iTXt")
			{
				int nullIdx = Array.IndexOf(chunkData, (byte)0);
				if (nullIdx > 0 && Encoding.ASCII.GetString(chunkData, 0, nullIdx).Equals("Realm", StringComparison.OrdinalIgnoreCase))
				{
					isOldRealm = true;
				}
			}

			if (!isOldRealm)
			{
				if (!inserted && (chunkType == "IDAT" || chunkType == "IEND"))
				{
					chunks.Add(("tEXt", textChunkData));
					inserted = true;
				}
				chunks.Add((chunkType, chunkData));
			}

			offset += 8 + length + 4;
		}

		if (!inserted)
		{
			chunks.Add(("tEXt", textChunkData));
		}

		using var ms = new MemoryStream();
		ms.Write(pngSig, 0, 8);
		foreach (var chunk in chunks)
		{
			byte[] chunkTypeBytes = Encoding.ASCII.GetBytes(chunk.Type);
			uint crc = CalculatePngCrc(chunkTypeBytes, chunk.Data);

			byte[] lenBytes = new byte[4] {
				(byte)((chunk.Data.Length >> 24) & 0xFF),
				(byte)((chunk.Data.Length >> 16) & 0xFF),
				(byte)((chunk.Data.Length >> 8) & 0xFF),
				(byte)(chunk.Data.Length & 0xFF)
			};
			byte[] crcBytes = new byte[4] {
				(byte)((crc >> 24) & 0xFF),
				(byte)((crc >> 16) & 0xFF),
				(byte)((crc >> 8) & 0xFF),
				(byte)(crc & 0xFF)
			};

			ms.Write(lenBytes, 0, 4);
			ms.Write(chunkTypeBytes, 0, 4);
			ms.Write(chunk.Data, 0, chunk.Data.Length);
			ms.Write(crcBytes, 0, 4);
		}
		return ms.ToArray();
	}

	public static void RemoveMetadataFromPng(string filePath)
	{
		byte[] bytes = File.ReadAllBytes(filePath);
		byte[] updated = RemoveMetadataFromPngBytes(bytes);
		File.WriteAllBytes(filePath, updated);
	}

	public static byte[] RemoveMetadataFromPngBytes(byte[] bytes)
	{
		if (bytes.Length < 8) return bytes;
		byte[] pngSig = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
		for (int i = 0; i < 8; i++)
			if (bytes[i] != pngSig[i]) return bytes;

		var chunks = new List<(string Type, byte[] Data)>();
		int offset = 8;
		bool changed = false;

		while (offset + 8 <= bytes.Length)
		{
			int length = (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
			string chunkType = Encoding.ASCII.GetString(bytes, offset + 4, 4);
			int dataOffset = offset + 8;
			if (dataOffset + length + 4 > bytes.Length) break;

			byte[] chunkData = new byte[length];
			Buffer.BlockCopy(bytes, dataOffset, chunkData, 0, length);

			bool isOldRealm = false;
			if (chunkType == "tEXt" || chunkType == "iTXt")
			{
				int nullIdx = Array.IndexOf(chunkData, (byte)0);
				if (nullIdx > 0 && Encoding.ASCII.GetString(chunkData, 0, nullIdx).Equals("Realm", StringComparison.OrdinalIgnoreCase))
				{
					isOldRealm = true;
					changed = true;
				}
			}

			if (!isOldRealm)
			{
				chunks.Add((chunkType, chunkData));
			}

			offset += 8 + length + 4;
		}

		if (!changed) return bytes;

		using var ms = new MemoryStream();
		ms.Write(pngSig, 0, 8);
		foreach (var chunk in chunks)
		{
			byte[] chunkTypeBytes = Encoding.ASCII.GetBytes(chunk.Type);
			uint crc = CalculatePngCrc(chunkTypeBytes, chunk.Data);

			byte[] lenBytes = new byte[4] {
				(byte)((chunk.Data.Length >> 24) & 0xFF),
				(byte)((chunk.Data.Length >> 16) & 0xFF),
				(byte)((chunk.Data.Length >> 8) & 0xFF),
				(byte)(chunk.Data.Length & 0xFF)
			};
			byte[] crcBytes = new byte[4] {
				(byte)((crc >> 24) & 0xFF),
				(byte)((crc >> 16) & 0xFF),
				(byte)((crc >> 8) & 0xFF),
				(byte)(crc & 0xFF)
			};

			ms.Write(lenBytes, 0, 4);
			ms.Write(chunkTypeBytes, 0, 4);
			ms.Write(chunk.Data, 0, chunk.Data.Length);
			ms.Write(crcBytes, 0, 4);
		}
		return ms.ToArray();
	}

	public static string? ExtractMetadataFromKtx2(string filePath)
	{
		if (!File.Exists(filePath)) return null;
		byte[] bytes = File.ReadAllBytes(filePath);
		return ExtractMetadataFromKtx2Bytes(bytes);
	}

	public static string? ExtractMetadataFromKtx2Bytes(ReadOnlySpan<byte> bytes)
	{
		if (bytes.Length < 80) return null;
		ReadOnlySpan<byte> ktx2Sig = new byte[] { 0xAB, 0x4B, 0x54, 0x58, 0x20, 0x32, 0x30, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A };
		if (!bytes.Slice(0, 12).SequenceEqual(ktx2Sig)) return null;

		uint kvdByteOffset = BitConverter.ToUInt32(bytes.Slice(60, 4));
		uint kvdByteLength = BitConverter.ToUInt32(bytes.Slice(64, 4));

		if (kvdByteLength == 0 || kvdByteOffset == 0 || kvdByteOffset + kvdByteLength > bytes.Length)
			return null;

		var kvdSpan = bytes.Slice((int)kvdByteOffset, (int)kvdByteLength);
		int cur = 0;
		while (cur + 4 <= kvdSpan.Length)
		{
			uint keyAndValueByteLength = BitConverter.ToUInt32(kvdSpan.Slice(cur, 4));
			cur += 4;
			if (cur + keyAndValueByteLength > kvdSpan.Length) break;

			var entry = kvdSpan.Slice(cur, (int)keyAndValueByteLength);
			int nullIdx = entry.IndexOf((byte)0);
			if (nullIdx > 0)
			{
				string key = Encoding.UTF8.GetString(entry.Slice(0, nullIdx));
				if (key.Equals("Realm", StringComparison.OrdinalIgnoreCase))
				{
					return Encoding.UTF8.GetString(entry.Slice(nullIdx + 1));
				}
			}

			cur += (int)keyAndValueByteLength;
			int padding = (4 - (cur % 4)) % 4;
			cur += padding;
		}
		return null;
	}

	public static void AddMetadataToKtx2(string filePath, string realmMetadataJson)
	{
		byte[] bytes = File.ReadAllBytes(filePath);
		byte[] updated = AddMetadataToKtx2Bytes(bytes, realmMetadataJson);
		File.WriteAllBytes(filePath, updated);
	}

	public static byte[] AddMetadataToKtx2Bytes(byte[] bytes, string realmMetadataJson)
	{
		if (bytes.Length < 80) throw new InvalidOperationException("Invalid KTX2 file.");
		byte[] ktx2Sig = new byte[] { 0xAB, 0x4B, 0x54, 0x58, 0x20, 0x32, 0x30, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A };
		for (int i = 0; i < 12; i++)
			if (bytes[i] != ktx2Sig[i]) throw new InvalidOperationException("Invalid KTX2 signature.");

		uint kvdByteOffset = BitConverter.ToUInt32(bytes, 60);
		uint kvdByteLength = BitConverter.ToUInt32(bytes, 64);
		ulong sgdByteOffset = BitConverter.ToUInt64(bytes, 68);
		uint levelCount = BitConverter.ToUInt32(bytes, 32);
		if (levelCount == 0) levelCount = 1;

		var existingEntries = new List<byte[]>();
		if (kvdByteLength > 0 && kvdByteOffset > 0 && kvdByteOffset + kvdByteLength <= (uint)bytes.Length)
		{
			int cur = (int)kvdByteOffset;
			int end = (int)(kvdByteOffset + kvdByteLength);
			while (cur + 4 <= end)
			{
				uint keyAndValueByteLength = BitConverter.ToUInt32(bytes, cur);
				if (cur + 4 + keyAndValueByteLength > end) break;
				byte[] entry = new byte[keyAndValueByteLength];
				Buffer.BlockCopy(bytes, cur + 4, entry, 0, (int)keyAndValueByteLength);

				int nullIdx = Array.IndexOf(entry, (byte)0);
				bool isRealm = false;
				if (nullIdx > 0)
				{
					string key = Encoding.UTF8.GetString(entry, 0, nullIdx);
					if (key.Equals("Realm", StringComparison.OrdinalIgnoreCase))
						isRealm = true;
				}
				if (!isRealm)
				{
					existingEntries.Add(entry);
				}

				cur += 4 + (int)keyAndValueByteLength;
				int padding = (4 - (cur % 4)) % 4;
				cur += padding;
			}
		}

		byte[] keyBytes = Encoding.UTF8.GetBytes("Realm\0");
		byte[] valBytes = Encoding.UTF8.GetBytes(realmMetadataJson);
		byte[] realmEntry = new byte[keyBytes.Length + valBytes.Length];
		Buffer.BlockCopy(keyBytes, 0, realmEntry, 0, keyBytes.Length);
		Buffer.BlockCopy(valBytes, 0, realmEntry, keyBytes.Length, valBytes.Length);
		existingEntries.Add(realmEntry);

		using var kvdMs = new MemoryStream();
		foreach (var entry in existingEntries)
		{
			uint len = (uint)entry.Length;
			kvdMs.Write(BitConverter.GetBytes(len), 0, 4);
			kvdMs.Write(entry, 0, entry.Length);
			int pad = (4 - (int)(kvdMs.Position % 4)) % 4;
			for (int p = 0; p < pad; p++) kvdMs.WriteByte(0);
		}
		byte[] newKvdBytes = kvdMs.ToArray();

		if (kvdByteOffset == 0 || kvdByteLength == 0)
		{
			uint targetOffset = (uint)bytes.Length;
			byte[] resultNoPrev = new byte[bytes.Length + newKvdBytes.Length];
			Buffer.BlockCopy(bytes, 0, resultNoPrev, 0, bytes.Length);
			Buffer.BlockCopy(newKvdBytes, 0, resultNoPrev, (int)targetOffset, newKvdBytes.Length);

			Buffer.BlockCopy(BitConverter.GetBytes(targetOffset), 0, resultNoPrev, 60, 4);
			Buffer.BlockCopy(BitConverter.GetBytes((uint)newKvdBytes.Length), 0, resultNoPrev, 64, 4);
			return resultNoPrev;
		}

		int delta = newKvdBytes.Length - (int)kvdByteLength;
		byte[] result = new byte[bytes.Length + delta];

		Buffer.BlockCopy(bytes, 0, result, 0, (int)kvdByteOffset);
		Buffer.BlockCopy(newKvdBytes, 0, result, (int)kvdByteOffset, newKvdBytes.Length);
		int afterKvdOffset = (int)(kvdByteOffset + kvdByteLength);
		if (afterKvdOffset < bytes.Length && kvdByteOffset > 0)
		{
			Buffer.BlockCopy(bytes, afterKvdOffset, result, (int)kvdByteOffset + newKvdBytes.Length, bytes.Length - afterKvdOffset);
		}

		Buffer.BlockCopy(BitConverter.GetBytes(kvdByteOffset), 0, result, 60, 4);
		Buffer.BlockCopy(BitConverter.GetBytes((uint)newKvdBytes.Length), 0, result, 64, 4);

		if (delta != 0)
		{
			if (sgdByteOffset > kvdByteOffset)
			{
				ulong newSgd = sgdByteOffset + (ulong)delta;
				Buffer.BlockCopy(BitConverter.GetBytes(newSgd), 0, result, 68, 8);
			}

			for (int l = 0; l < (int)levelCount; l++)
			{
				int lvlEntryOffset = 80 + (l * 24);
				if (lvlEntryOffset + 24 <= result.Length)
				{
					ulong lvlByteOffset = BitConverter.ToUInt64(result, lvlEntryOffset);
					if (lvlByteOffset > kvdByteOffset)
					{
						lvlByteOffset += (ulong)delta;
						Buffer.BlockCopy(BitConverter.GetBytes(lvlByteOffset), 0, result, lvlEntryOffset, 8);
					}
				}
			}
		}

		return result;
	}

	public static void RemoveMetadataFromKtx2(string filePath)
	{
		byte[] bytes = File.ReadAllBytes(filePath);
		byte[] updated = RemoveMetadataFromKtx2Bytes(bytes);
		File.WriteAllBytes(filePath, updated);
	}

	public static byte[] RemoveMetadataFromKtx2Bytes(byte[] bytes)
	{
		if (bytes.Length < 80) return bytes;
		byte[] ktx2Sig = new byte[] { 0xAB, 0x4B, 0x54, 0x58, 0x20, 0x32, 0x30, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A };
		for (int i = 0; i < 12; i++)
			if (bytes[i] != ktx2Sig[i]) return bytes;

		uint kvdByteOffset = BitConverter.ToUInt32(bytes, 60);
		uint kvdByteLength = BitConverter.ToUInt32(bytes, 64);
		ulong sgdByteOffset = BitConverter.ToUInt64(bytes, 68);
		uint levelCount = BitConverter.ToUInt32(bytes, 32);
		if (levelCount == 0) levelCount = 1;

		if (kvdByteLength == 0 || kvdByteOffset == 0 || kvdByteOffset + kvdByteLength > (uint)bytes.Length)
			return bytes;

		var existingEntries = new List<byte[]>();
		int cur = (int)kvdByteOffset;
		int end = (int)(kvdByteOffset + kvdByteLength);
		bool removed = false;

		while (cur + 4 <= end)
		{
			uint keyAndValueByteLength = BitConverter.ToUInt32(bytes, cur);
			if (cur + 4 + keyAndValueByteLength > end) break;
			byte[] entry = new byte[keyAndValueByteLength];
			Buffer.BlockCopy(bytes, cur + 4, entry, 0, (int)keyAndValueByteLength);

			int nullIdx = Array.IndexOf(entry, (byte)0);
			bool isRealm = false;
			if (nullIdx > 0)
			{
				string key = Encoding.UTF8.GetString(entry, 0, nullIdx);
				if (key.Equals("Realm", StringComparison.OrdinalIgnoreCase))
				{
					isRealm = true;
					removed = true;
				}
			}
			if (!isRealm)
			{
				existingEntries.Add(entry);
			}

			cur += 4 + (int)keyAndValueByteLength;
			int padding = (4 - (cur % 4)) % 4;
			cur += padding;
		}

		if (!removed) return bytes;

		using var kvdMs = new MemoryStream();
		foreach (var entry in existingEntries)
		{
			uint len = (uint)entry.Length;
			kvdMs.Write(BitConverter.GetBytes(len), 0, 4);
			kvdMs.Write(entry, 0, entry.Length);
			int pad = (4 - (int)(kvdMs.Position % 4)) % 4;
			for (int p = 0; p < pad; p++) kvdMs.WriteByte(0);
		}
		byte[] newKvdBytes = kvdMs.ToArray();

		int delta = newKvdBytes.Length - (int)kvdByteLength;
		byte[] result = new byte[bytes.Length + delta];

		Buffer.BlockCopy(bytes, 0, result, 0, (int)kvdByteOffset);
		Buffer.BlockCopy(newKvdBytes, 0, result, (int)kvdByteOffset, newKvdBytes.Length);
		int afterKvdOffset = (int)(kvdByteOffset + kvdByteLength);
		if (afterKvdOffset < bytes.Length && kvdByteOffset > 0)
		{
			Buffer.BlockCopy(bytes, afterKvdOffset, result, (int)kvdByteOffset + newKvdBytes.Length, bytes.Length - afterKvdOffset);
		}

		Buffer.BlockCopy(BitConverter.GetBytes(kvdByteOffset), 0, result, 60, 4);
		Buffer.BlockCopy(BitConverter.GetBytes((uint)newKvdBytes.Length), 0, result, 64, 4);

		if (delta != 0)
		{
			if (sgdByteOffset > kvdByteOffset)
			{
				ulong newSgd = sgdByteOffset + (ulong)delta;
				Buffer.BlockCopy(BitConverter.GetBytes(newSgd), 0, result, 68, 8);
			}

			for (int l = 0; l < (int)levelCount; l++)
			{
				int lvlEntryOffset = 80 + (l * 24);
				if (lvlEntryOffset + 24 <= result.Length)
				{
					ulong lvlByteOffset = BitConverter.ToUInt64(result, lvlEntryOffset);
					if (lvlByteOffset > kvdByteOffset)
					{
						lvlByteOffset += (ulong)delta;
						Buffer.BlockCopy(BitConverter.GetBytes(lvlByteOffset), 0, result, lvlEntryOffset, 8);
					}
				}
			}
		}

		return result;
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
				uint metaLen = BitConverter.ToUInt32(bytes.Slice(bytes.Length - 8, 4));
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
			uint oldLen = BitConverter.ToUInt32(bytes, bytes.Length - 8);
			if (baseLength >= 8 + (int)oldLen)
			{
				baseLength = baseLength - 8 - (int)oldLen;
			}
		}

		byte[] jsonBytes = Encoding.UTF8.GetBytes(realmMetadataJson);
		byte[] result = new byte[baseLength + jsonBytes.Length + 4 + 4];
		Buffer.BlockCopy(bytes, 0, result, 0, baseLength);
		Buffer.BlockCopy(jsonBytes, 0, result, baseLength, jsonBytes.Length);
		Buffer.BlockCopy(BitConverter.GetBytes((uint)jsonBytes.Length), 0, result, baseLength + jsonBytes.Length, 4);
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
			uint oldLen = BitConverter.ToUInt32(bytes, bytes.Length - 8);
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

	public static string? ExtractMetadataFromOgg(string filePath)
	{
		if (!File.Exists(filePath)) return null;
		byte[] bytes = File.ReadAllBytes(filePath);
		return ExtractMetadataFromOggBytes(bytes);
	}

	public static string? ExtractMetadataFromOggBytes(ReadOnlySpan<byte> bytes)
	{
		if (bytes.Length < 4) return null;

		byte[] vorbisCommentTag = new byte[] { 0x03, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s' };
		byte[] opusTags = Encoding.ASCII.GetBytes("OpusTags");

		int tagIdx = bytes.IndexOf(vorbisCommentTag);
		int headerLen = 7;
		if (tagIdx < 0)
		{
			tagIdx = bytes.IndexOf(opusTags);
			headerLen = 8;
		}

		if (tagIdx >= 0)
		{
			int cur = tagIdx + headerLen;
			if (cur + 4 <= bytes.Length)
			{
				uint vendorLen = BitConverter.ToUInt32(bytes.Slice(cur, 4));
				cur += 4 + (int)vendorLen;
				if (cur + 4 <= bytes.Length)
				{
					uint commentCount = BitConverter.ToUInt32(bytes.Slice(cur, 4));
					cur += 4;
					for (uint i = 0; i < commentCount && cur + 4 <= bytes.Length; i++)
					{
						uint cLen = BitConverter.ToUInt32(bytes.Slice(cur, 4));
						cur += 4;
						if (cur + cLen > bytes.Length) break;
						string comment = Encoding.UTF8.GetString(bytes.Slice(cur, (int)cLen));
						cur += (int)cLen;

						if (comment.StartsWith("REALM=", StringComparison.OrdinalIgnoreCase))
						{
							return comment.Substring(6);
						}
					}
				}
			}
		}

		byte[] realmTag = Encoding.UTF8.GetBytes("REALM=");
		int maxSearch = Math.Min(bytes.Length, 65536);
		int rIdx = bytes.Slice(0, maxSearch).IndexOf(realmTag);
		if (rIdx >= 0)
		{
			int start = rIdx + 6;
			int end = start;
			while (end < maxSearch && bytes[end] != 0 && bytes[end] != '\r' && bytes[end] != '\n') end++;
			string val = Encoding.UTF8.GetString(bytes.Slice(start, end - start));
			if (val.TrimStart().StartsWith("{") || val.TrimStart().StartsWith("["))
				return val;
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
		byte[] vorbisCommentTag = new byte[] { 0x03, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s' };
		byte[] opusTags = Encoding.ASCII.GetBytes("OpusTags");

		int tagIdx = bytes.AsSpan().IndexOf(vorbisCommentTag);
		string headerType = "vorbis";
		int headerLen = 7;

		if (tagIdx < 0)
		{
			tagIdx = bytes.AsSpan().IndexOf(opusTags);
			headerType = "opus";
			headerLen = 8;
		}

		if (tagIdx < 0)
		{
			return bytes;
		}

		int pageStart = tagIdx;
		while (pageStart >= 0)
		{
			if (pageStart + 4 <= bytes.Length &&
				bytes[pageStart] == 0x4F && bytes[pageStart + 1] == 0x67 &&
				bytes[pageStart + 2] == 0x67 && bytes[pageStart + 3] == 0x53)
			{
				break;
			}
			pageStart--;
		}
		if (pageStart < 0) return bytes;

		int numSegments = bytes[pageStart + 26];
		int pageHeaderLen = 27 + numSegments;
		int payloadLen = 0;
		for (int s = 0; s < numSegments; s++) payloadLen += bytes[pageStart + 27 + s];
		int pageEnd = pageStart + pageHeaderLen + payloadLen;

		int cur = tagIdx + headerLen;
		uint vendorLen = BitConverter.ToUInt32(bytes, cur);
		string vendorString = Encoding.UTF8.GetString(bytes, cur + 4, (int)vendorLen);
		cur += 4 + (int)vendorLen;

		uint commentCount = BitConverter.ToUInt32(bytes, cur);
		cur += 4;

		var comments = new List<string>();
		for (uint i = 0; i < commentCount && cur + 4 <= bytes.Length; i++)
		{
			uint cLen = BitConverter.ToUInt32(bytes, cur);
			cur += 4;
			if (cur + cLen > bytes.Length) break;
			string comment = Encoding.UTF8.GetString(bytes, cur, (int)cLen);
			cur += (int)cLen;

			if (!comment.StartsWith("REALM=", StringComparison.OrdinalIgnoreCase))
			{
				comments.Add(comment);
			}
		}

		comments.Add("REALM=" + realmMetadataJson);

		using var packetMs = new MemoryStream();
		if (headerType == "vorbis")
			packetMs.Write(vorbisCommentTag, 0, 7);
		else
			packetMs.Write(opusTags, 0, 8);

		byte[] vendorBytes = Encoding.UTF8.GetBytes(vendorString);
		packetMs.Write(BitConverter.GetBytes((uint)vendorBytes.Length), 0, 4);
		packetMs.Write(vendorBytes, 0, vendorBytes.Length);

		packetMs.Write(BitConverter.GetBytes((uint)comments.Count), 0, 4);
		foreach (var c in comments)
		{
			byte[] cBytes = Encoding.UTF8.GetBytes(c);
			packetMs.Write(BitConverter.GetBytes((uint)cBytes.Length), 0, 4);
			packetMs.Write(cBytes, 0, cBytes.Length);
		}
		packetMs.WriteByte(1);

		byte[] newPacketData = packetMs.ToArray();

		var segments = new List<byte>();
		int rem = newPacketData.Length;
		while (rem >= 255)
		{
			segments.Add(255);
			rem -= 255;
		}
		segments.Add((byte)rem);

		if (segments.Count > 255)
		{
			return bytes;
		}

		using var newPageMs = new MemoryStream();
		newPageMs.Write(bytes, pageStart, 26);
		newPageMs.WriteByte((byte)segments.Count);
		foreach (var seg in segments) newPageMs.WriteByte(seg);
		newPageMs.Write(newPacketData, 0, newPacketData.Length);

		byte[] newPageBytes = newPageMs.ToArray();

		newPageBytes[22] = 0;
		newPageBytes[23] = 0;
		newPageBytes[24] = 0;
		newPageBytes[25] = 0;

		uint pageCrc = CalculateOggCrc(newPageBytes);
		Buffer.BlockCopy(BitConverter.GetBytes(pageCrc), 0, newPageBytes, 22, 4);

		using var finalMs = new MemoryStream();
		finalMs.Write(bytes, 0, pageStart);
		finalMs.Write(newPageBytes, 0, newPageBytes.Length);
		if (pageEnd < bytes.Length)
		{
			finalMs.Write(bytes, pageEnd, bytes.Length - pageEnd);
		}
		return finalMs.ToArray();
	}

	public static void RemoveMetadataFromOgg(string filePath)
	{
		byte[] bytes = File.ReadAllBytes(filePath);
		byte[] updated = RemoveMetadataFromOggBytes(bytes);
		File.WriteAllBytes(filePath, updated);
	}

	public static byte[] RemoveMetadataFromOggBytes(byte[] bytes)
	{
		byte[] vorbisCommentTag = new byte[] { 0x03, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s' };
		byte[] opusTags = Encoding.ASCII.GetBytes("OpusTags");

		int tagIdx = bytes.AsSpan().IndexOf(vorbisCommentTag);
		string headerType = "vorbis";
		int headerLen = 7;

		if (tagIdx < 0)
		{
			tagIdx = bytes.AsSpan().IndexOf(opusTags);
			headerType = "opus";
			headerLen = 8;
		}

		if (tagIdx < 0)
		{
			return bytes;
		}

		int pageStart = tagIdx;
		while (pageStart >= 0)
		{
			if (pageStart + 4 <= bytes.Length &&
				bytes[pageStart] == 0x4F && bytes[pageStart + 1] == 0x67 &&
				bytes[pageStart + 2] == 0x67 && bytes[pageStart + 3] == 0x53)
			{
				break;
			}
			pageStart--;
		}
		if (pageStart < 0) return bytes;

		int numSegments = bytes[pageStart + 26];
		int pageHeaderLen = 27 + numSegments;
		int payloadLen = 0;
		for (int s = 0; s < numSegments; s++) payloadLen += bytes[pageStart + 27 + s];
		int pageEnd = pageStart + pageHeaderLen + payloadLen;

		int cur = tagIdx + headerLen;
		uint vendorLen = BitConverter.ToUInt32(bytes, cur);
		string vendorString = Encoding.UTF8.GetString(bytes, cur + 4, (int)vendorLen);
		cur += 4 + (int)vendorLen;

		uint commentCount = BitConverter.ToUInt32(bytes, cur);
		cur += 4;

		var comments = new List<string>();
		bool removed = false;
		for (uint i = 0; i < commentCount && cur + 4 <= bytes.Length; i++)
		{
			uint cLen = BitConverter.ToUInt32(bytes, cur);
			cur += 4;
			if (cur + cLen > bytes.Length) break;
			string comment = Encoding.UTF8.GetString(bytes, cur, (int)cLen);
			cur += (int)cLen;

			if (!comment.StartsWith("REALM=", StringComparison.OrdinalIgnoreCase))
			{
				comments.Add(comment);
			}
			else
			{
				removed = true;
			}
		}

		if (!removed) return bytes;

		using var packetMs = new MemoryStream();
		if (headerType == "vorbis")
			packetMs.Write(vorbisCommentTag, 0, 7);
		else
			packetMs.Write(opusTags, 0, 8);

		byte[] vendorBytes = Encoding.UTF8.GetBytes(vendorString);
		packetMs.Write(BitConverter.GetBytes((uint)vendorBytes.Length), 0, 4);
		packetMs.Write(vendorBytes, 0, vendorBytes.Length);

		packetMs.Write(BitConverter.GetBytes((uint)comments.Count), 0, 4);
		foreach (var c in comments)
		{
			byte[] cBytes = Encoding.UTF8.GetBytes(c);
			packetMs.Write(BitConverter.GetBytes((uint)cBytes.Length), 0, 4);
			packetMs.Write(cBytes, 0, cBytes.Length);
		}
		packetMs.WriteByte(1);

		byte[] newPacketData = packetMs.ToArray();

		var segments = new List<byte>();
		int rem = newPacketData.Length;
		while (rem >= 255)
		{
			segments.Add(255);
			rem -= 255;
		}
		segments.Add((byte)rem);

		if (segments.Count > 255)
		{
			return bytes;
		}

		using var newPageMs = new MemoryStream();
		newPageMs.Write(bytes, pageStart, 26);
		newPageMs.WriteByte((byte)segments.Count);
		foreach (var seg in segments) newPageMs.WriteByte(seg);
		newPageMs.Write(newPacketData, 0, newPacketData.Length);

		byte[] newPageBytes = newPageMs.ToArray();

		newPageBytes[22] = 0;
		newPageBytes[23] = 0;
		newPageBytes[24] = 0;
		newPageBytes[25] = 0;

		uint pageCrc = CalculateOggCrc(newPageBytes);
		Buffer.BlockCopy(BitConverter.GetBytes(pageCrc), 0, newPageBytes, 22, 4);

		using var finalMs = new MemoryStream();
		finalMs.Write(bytes, 0, pageStart);
		finalMs.Write(newPageBytes, 0, newPageBytes.Length);
		if (pageEnd < bytes.Length)
		{
			finalMs.Write(bytes, pageEnd, bytes.Length - pageEnd);
		}
		return finalMs.ToArray();
	}
}
