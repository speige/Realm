using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Realm.AssetPipeline;

public static class GlbManifestUtils
{
	private const uint GlbMagic = 0x46546C67; // 'glTF'
	private const uint JsonChunkType = 0x4E4F534A; // 'JSON'
	private const uint BinChunkType = 0x004E4942; // 'BIN\0'

	public static bool IsValidGlb(byte[] glbBytes)
	{
		if (glbBytes == null || glbBytes.Length < 20) return false;
		uint magic = BitConverter.ToUInt32(glbBytes, 0);
		return magic == GlbMagic;
	}

	public static (JsonNode? Json, byte[]? BinChunk, uint Version) ParseGlb(byte[] glbBytes)
	{
		if (!IsValidGlb(glbBytes)) return (null, null, 0);

		uint version = BitConverter.ToUInt32(glbBytes, 4);
		int currentOffset = 12;

		if (currentOffset + 8 > glbBytes.Length) return (null, null, version);

		uint jsonLength = BitConverter.ToUInt32(glbBytes, currentOffset);
		uint jsonType = BitConverter.ToUInt32(glbBytes, currentOffset + 4);
		if (jsonType != JsonChunkType || currentOffset + 8 + jsonLength > glbBytes.Length) return (null, null, version);

		string jsonString = Encoding.UTF8.GetString(glbBytes, currentOffset + 8, (int)jsonLength);
		var jsonNode = JsonNode.Parse(jsonString);

		int binOffset = currentOffset + 8 + (int)jsonLength;
		byte[]? binChunk = null;
		if (binOffset + 8 <= glbBytes.Length)
		{
			uint binLength = BitConverter.ToUInt32(glbBytes, binOffset);
			uint binType = BitConverter.ToUInt32(glbBytes, binOffset + 4);
			if (binType == BinChunkType && binOffset + 8 + binLength <= glbBytes.Length)
			{
				binChunk = new byte[binLength];
				Array.Copy(glbBytes, binOffset + 8, binChunk, 0, binLength);
			}
			else if (binOffset + 8 < glbBytes.Length)
			{
				int remaining = glbBytes.Length - (binOffset + 8);
				binChunk = new byte[remaining];
				Array.Copy(glbBytes, binOffset + 8, binChunk, 0, remaining);
			}
		}

		return (jsonNode, binChunk, version);
	}

	public static byte[] BuildGlb(JsonNode jsonNode, byte[]? binChunk, uint version = 2)
	{
		byte[] jsonBytes = Encoding.UTF8.GetBytes(jsonNode.ToJsonString());
		int paddedJsonLength = (jsonBytes.Length + 3) & ~3;
		byte[] paddedJson = new byte[paddedJsonLength];
		Array.Copy(jsonBytes, paddedJson, jsonBytes.Length);
		for (int i = jsonBytes.Length; i < paddedJsonLength; i++)
		{
			paddedJson[i] = 0x20; // GLTF JSON padding with ASCII space
		}

		int paddedBinLength = binChunk != null ? ((binChunk.Length + 3) & ~3) : 0;
		byte[]? paddedBin = null;
		if (binChunk != null && binChunk.Length > 0)
		{
			paddedBin = new byte[paddedBinLength];
			Array.Copy(binChunk, paddedBin, binChunk.Length);
		}

		using var ms = new MemoryStream();
		using var writer = new BinaryWriter(ms);

		writer.Write(GlbMagic);
		writer.Write(version);

		uint totalLength = 12 + 8 + (uint)paddedJsonLength;
		if (paddedBin != null)
		{
			totalLength += 8 + (uint)paddedBinLength;
		}
		writer.Write(totalLength);

		// JSON chunk
		writer.Write((uint)paddedJsonLength);
		writer.Write(JsonChunkType);
		writer.Write(paddedJson);

		// BIN chunk
		if (paddedBin != null)
		{
			writer.Write((uint)paddedBinLength);
			writer.Write(BinChunkType);
			writer.Write(paddedBin);
		}

		return ms.ToArray();
	}

	public static bool HasOptimizationFlag(byte[] glbBytes)
	{
		var (json, _, _) = ParseGlb(glbBytes);
		if (json is not JsonObject root) return false;

		if (root.TryGetPropertyValue("extras", out var extrasNode) && extrasNode is JsonObject extras)
		{
			if (extras.TryGetPropertyValue("realm_optimized", out var optNode) && optNode != null)
			{
				if (optNode.GetValue<bool>() == true) return true;
			}
			if (extras.TryGetPropertyValue("decimation_completed", out var decNode) && decNode != null)
			{
				if (decNode.GetValue<bool>() == true) return true;
			}
		}

		return false;
	}

	public static byte[] InjectOptimizationMetadata(
		byte[] glbBytes,
		string version = "0.1.0-alpha",
		Dictionary<string, object>? extraStats = null)
	{
		var (json, bin, glbVer) = ParseGlb(glbBytes);
		if (json is not JsonObject root) return glbBytes;

		JsonObject extras;
		if (root.TryGetPropertyValue("extras", out var extrasNode) && extrasNode is JsonObject existingExtras)
		{
			extras = existingExtras;
		}
		else
		{
			extras = new JsonObject();
			root["extras"] = extras;
		}

		extras["realm_optimized"] = true;
		extras["realm_version"] = version;
		extras["decimation_completed"] = true;
		extras["optimization_timestamp"] = DateTime.UtcNow.ToString("o");

		if (extraStats != null)
		{
			foreach (var kvp in extraStats)
			{
				if (kvp.Value is int i) extras[kvp.Key] = i;
				else if (kvp.Value is float f) extras[kvp.Key] = f;
				else if (kvp.Value is bool b) extras[kvp.Key] = b;
				else if (kvp.Value is string s) extras[kvp.Key] = s;
			}
		}

		return BuildGlb(root, bin, glbVer);
	}

	public static (byte[] UnoptimizedBytes, bool WasModified) StripOptimizationMetadata(byte[] glbBytes)
	{
		var (json, bin, glbVer) = ParseGlb(glbBytes);
		if (json is not JsonObject root) return (glbBytes, false);

		bool modified = false;

		if (root.TryGetPropertyValue("extras", out var extrasNode) && extrasNode is JsonObject extras)
		{
			if (extras.Remove("realm_optimized")) modified = true;
			if (extras.Remove("realm_version")) modified = true;
			if (extras.Remove("decimation_completed")) modified = true;
			if (extras.Remove("optimization_timestamp")) modified = true;
			if (extras.Remove("msft_lod_embedded")) modified = true;
		}

		// Remove MSFT_lod extension from extensionsUsed and extensionsRequired if present
		if (root.TryGetPropertyValue("extensionsUsed", out var extUsedNode) && extUsedNode is JsonArray extUsed)
		{
			for (int i = extUsed.Count - 1; i >= 0; i--)
			{
				if (extUsed[i]?.GetValue<string>() == "MSFT_lod")
				{
					extUsed.RemoveAt(i);
					modified = true;
				}
			}
		}

		if (root.TryGetPropertyValue("extensionsRequired", out var extReqNode) && extReqNode is JsonArray extReq)
		{
			for (int i = extReq.Count - 1; i >= 0; i--)
			{
				if (extReq[i]?.GetValue<string>() == "MSFT_lod")
				{
					extReq.RemoveAt(i);
					modified = true;
				}
			}
		}

		// Remove LOD nodes (e.g. *_LOD1, *_LOD2, *_LOD3)
		if (root.TryGetPropertyValue("nodes", out var nodesNode) && nodesNode is JsonArray nodesArray)
		{
			var lodIndices = new HashSet<int>();
			for (int i = 0; i < nodesArray.Count; i++)
			{
				if (nodesArray[i] is JsonObject nodeObj && nodeObj.TryGetPropertyValue("name", out var nameVal))
				{
					string nameStr = nameVal?.GetValue<string>() ?? "";
					if (nameStr.Contains("_LOD1") || nameStr.Contains("_LOD2") || nameStr.Contains("_LOD3"))
					{
						lodIndices.Add(i);
					}
				}
			}

			if (lodIndices.Count > 0)
			{
				// Remove LOD children references from remaining nodes
				foreach (var n in nodesArray)
				{
					if (n is JsonObject parentObj && parentObj.TryGetPropertyValue("children", out var childrenVal) && childrenVal is JsonArray childrenArr)
					{
						for (int c = childrenArr.Count - 1; c >= 0; c--)
						{
							int childIdx = childrenArr[c]?.GetValue<int>() ?? -1;
							if (lodIndices.Contains(childIdx))
							{
								childrenArr.RemoveAt(c);
								modified = true;
							}
						}
					}
				}
			}
		}

		if (!modified) return (glbBytes, false);

		return (BuildGlb(root, bin, glbVer), true);
	}

	public static byte[] SanitizeMaterials(byte[] glbBytes)
	{
		var (json, bin, glbVer) = ParseGlb(glbBytes);
		if (json is not JsonObject root) return glbBytes;

		if (root.TryGetPropertyValue("materials", out var matNode) && matNode is JsonArray matArray)
		{
			foreach (var m in matArray)
			{
				if (m is JsonObject matObj && matObj.TryGetPropertyValue("pbrMetallicRoughness", out var pbrVal) && pbrVal is JsonObject pbrObj)
				{
					if (pbrObj.ContainsKey("baseColorTexture") && !pbrObj.ContainsKey("baseColorFactor"))
					{
						pbrObj["baseColorFactor"] = new JsonArray(1.0, 1.0, 1.0, 1.0);
					}
				}
			}
		}

		return BuildGlb(root, bin, glbVer);
	}
}
