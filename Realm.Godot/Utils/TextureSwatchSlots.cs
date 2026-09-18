using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Nodes;
using Godot;

namespace Realm.Godot.Utils;

public readonly struct SwatchSlotInfo
{
	public readonly int SlotIndex;
	public readonly string? BaseName;
	public readonly string? FileName;
	public readonly bool IsFiller;
	public readonly JsonNode? MetadataNode;

	public SwatchSlotInfo(int slotIndex, string? baseName, string? fileName, bool isFiller, JsonNode? metadataNode)
	{
		SlotIndex = slotIndex;
		BaseName = baseName;
		FileName = fileName;
		IsFiller = isFiller;
		MetadataNode = metadataNode;
	}
}

public static class TextureSwatchSlots
{
	public const int MaxSlots = 32;

	public static bool ValidateCategory(string fileName, JsonNode? node = null)
	{
		if (string.IsNullOrWhiteSpace(fileName))
		{
			return false;
		}

		string normalized = fileName.Replace('\\', '/').ToLowerInvariant();

		if (normalized.Contains("ribbons/") || normalized.Contains("ribbon_textures/") ||
			normalized.Contains("decals/") || normalized.Contains("icons/") ||
			normalized.Contains("skyboxes/") || normalized.Contains("noise/") ||
			normalized.Contains("noise_textures/") || normalized.Contains("vfx/") ||
			normalized.Contains("vfx_spritesheets/"))
		{
			return false;
		}

		string baseName = Path.GetFileNameWithoutExtension(normalized);
		if (baseName.EndsWith("_trail") || baseName.EndsWith("_flare") ||
			baseName.EndsWith("_beam") || baseName.EndsWith("_pulse") ||
			baseName.EndsWith("_streak") || baseName.EndsWith("_ether_trace"))
		{
			if (normalized.Contains("ribbon"))
			{
				return false;
			}
		}

		return true;
	}

	public static int FirstFreeSlot(bool[] occupiedSlots)
	{
		for (int i = 0; i < MaxSlots; i++)
		{
			if (!occupiedSlots[i])
			{
				return i;
			}
		}
		return -1;
	}

	public static SwatchSlotInfo[] ResolveSlots(JsonObject? texturesObj, string mapDir)
	{
		var result = new SwatchSlotInfo[MaxSlots];
		var occupied = new bool[MaxSlots];

		if (texturesObj == null)
		{
			for (int i = 0; i < MaxSlots; i++)
			{
				result[i] = new SwatchSlotInfo(i, null, null, true, null);
			}
			return result;
		}

		var candidateItems = new List<(string BaseName, string FileName, int RequestedSlot, JsonNode? Node)>();

		foreach (var kvp in texturesObj)
		{
			string fileName = kvp.Key;
			if (!ValidateCategory(fileName, kvp.Value))
			{
				continue;
			}

			string baseName = Path.GetFileNameWithoutExtension(fileName);
			int requestedSlot = -1;

			if (kvp.Value is JsonObject sObj)
			{
				if (sObj.TryGetPropertyValue("swatchIndex", out var idxNode) && idxNode != null && int.TryParse(idxNode.ToString(), out int parsed))
				{
					requestedSlot = parsed;
				}
				else if (sObj.TryGetPropertyValue("swatch_index", out var idxNode2) && idxNode2 != null && int.TryParse(idxNode2.ToString(), out int parsed2))
				{
					requestedSlot = parsed2;
				}
				else if (sObj.TryGetPropertyValue("SwatchIndex", out var idxNode3) && idxNode3 != null && int.TryParse(idxNode3.ToString(), out int parsed3))
				{
					requestedSlot = parsed3;
				}
			}

			candidateItems.Add((baseName, fileName, requestedSlot, kvp.Value));
		}

		var pendingReassign = new List<(string BaseName, string FileName, JsonNode? Node)>();

		foreach (var item in candidateItems)
		{
			if (item.RequestedSlot >= 0 && item.RequestedSlot < MaxSlots && !occupied[item.RequestedSlot])
			{
				occupied[item.RequestedSlot] = true;
				result[item.RequestedSlot] = new SwatchSlotInfo(item.RequestedSlot, item.BaseName, item.FileName, false, item.Node);
			}
			else
			{
				pendingReassign.Add((item.BaseName, item.FileName, item.Node));
			}
		}

		foreach (var pending in pendingReassign)
		{
			int freeSlot = FirstFreeSlot(occupied);
			if (freeSlot >= 0)
			{
				occupied[freeSlot] = true;
				result[freeSlot] = new SwatchSlotInfo(freeSlot, pending.BaseName, pending.FileName, false, pending.Node);
				GD.Print($"[TextureSwatchSlots] Assigned texture '{pending.FileName}' to free slot {freeSlot}.");
			}
			else
			{
				GD.PrintErr($"[TextureSwatchSlots] Cannot assign texture '{pending.FileName}', maximum 32 slots reached.");
			}
		}

		for (int i = 0; i < MaxSlots; i++)
		{
			if (!occupied[i])
			{
				result[i] = new SwatchSlotInfo(i, null, null, true, null);
			}
		}

		return result;
	}
}
