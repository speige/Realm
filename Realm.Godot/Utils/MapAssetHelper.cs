using Godot;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using Realm.Godot.Services;
using Realm.Shared.Distribution;
using Realm.Shared.Metadata;

namespace Realm.Godot.Utils;

public static class MapAssetHelper
{
	public static JsonObject LoadUnionedAssets(string mapDirectory)
	{
		string targetDirectory = string.IsNullOrEmpty(mapDirectory)
			? MapWorkspaceService.GetActiveWorkspacePath()
			: mapDirectory;

		var unionedAssets = new JsonObject();

		string manifestPath = Path.Combine(targetDirectory, "manifest.json");
		string metadataPath = Path.Combine(targetDirectory, "metadata.json");

		JsonObject? metadataRoot = null;
		if (File.Exists(metadataPath))
		{
			try
			{
				string metadataText = File.ReadAllText(metadataPath);
				metadataRoot = JsonNode.Parse(metadataText)?.AsObject();
			}
			catch (Exception exception)
			{
				GD.PrintErr($"[MapAssetHelper] Failed to read metadata.json for assets: {exception.Message}");
			}
		}

		if (File.Exists(manifestPath))
		{
			try
			{
				string manifestText = File.ReadAllText(manifestPath);
				var manifestRoot = JsonNode.Parse(manifestText)?.AsObject();
				if (manifestRoot != null)
				{
					if (manifestRoot["Assets"] is JsonObject manifestAssets)
					{
						MergeAssetsInto(unionedAssets, manifestAssets);
					}
				}
			}
			catch (Exception exception)
			{
				GD.PrintErr($"[MapAssetHelper] Failed to read manifest.json for assets: {exception.Message}");
			}
		}

		if (metadataRoot != null)
		{
			AttachMetadataAttributesToUnionedAssets(unionedAssets, metadataRoot, targetDirectory);
		}

		EnsureAllAssetsHaveBlake3Hashes(unionedAssets, targetDirectory);

		return unionedAssets;
	}

	public static void SaveAssetsToManifest(string mapDirectory, JsonObject assets, bool removeFromMetadata = true)
	{
		if (assets == null) return;

		string targetDirectory = string.IsNullOrEmpty(mapDirectory)
			? MapWorkspaceService.GetActiveWorkspacePath()
			: mapDirectory;

		if (!Directory.Exists(targetDirectory))
		{
			Directory.CreateDirectory(targetDirectory);
		}

		string manifestPath = Path.Combine(targetDirectory, "manifest.json");
		JsonObject manifestRoot;

		if (File.Exists(manifestPath))
		{
			try
			{
				manifestRoot = JsonNode.Parse(File.ReadAllText(manifestPath))?.AsObject() ?? new JsonObject();
			}
			catch
			{
				manifestRoot = new JsonObject();
			}
		}
		else
		{
			manifestRoot = new JsonObject();
		}

		var cleanAssetsForManifest = BuildCleanManifestAssets(assets);
		manifestRoot["Assets"] = cleanAssetsForManifest;
		manifestRoot.Remove("Files");
		manifestRoot.Remove("FileSizes");

		MapJsonFormatter.SaveFormattedJson(manifestPath, manifestRoot);

		SynchronizeAttributesToMetadata(targetDirectory, assets);
	}

	public static void UpdateManifestAsset(
		string mapDirectory,
		string category,
		string fileName,
		string blake3Hash,
		string? subCategory = null,
		Action<JsonObject>? customizeEntry = null)
	{
		string targetDirectory = string.IsNullOrEmpty(mapDirectory)
			? MapWorkspaceService.GetActiveWorkspacePath()
			: mapDirectory;

		var assets = LoadUnionedAssets(targetDirectory);
		string categoryKey = NormalizeCategoryKey(category);

		if (!assets.ContainsKey(categoryKey) || assets[categoryKey] is not JsonObject)
		{
			assets[categoryKey] = new JsonObject();
		}

		var categoryObject = assets[categoryKey]!.AsObject();

		if (categoryKey == "glb" || !string.IsNullOrEmpty(subCategory))
		{
			string subKey = NormalizeGlbSubCategory(subCategory ?? "props");
			if (!categoryObject.ContainsKey(subKey) || categoryObject[subKey] is not JsonObject)
			{
				categoryObject[subKey] = new JsonObject();
			}
			var subCategoryObject = categoryObject[subKey]!.AsObject();

			JsonObject itemObject;
			if (subCategoryObject.TryGetPropertyValue(fileName, out var existingNode) && existingNode is JsonObject existingObject)
			{
				itemObject = existingObject;
			}
			else
			{
				itemObject = new JsonObject();
			}

			itemObject["hash"] = blake3Hash;
			customizeEntry?.Invoke(itemObject);
			subCategoryObject[fileName] = itemObject;
		}
		else
		{
			JsonObject itemObject;
			if (categoryObject.TryGetPropertyValue(fileName, out var existingNode) && existingNode is JsonObject existingObject)
			{
				itemObject = existingObject;
			}
			else
			{
				itemObject = new JsonObject();
				if (existingNode is JsonValue value)
				{
					itemObject["hash"] = value.ToString();
				}
			}

			itemObject["hash"] = blake3Hash;
			customizeEntry?.Invoke(itemObject);

			if (customizeEntry != null || categoryKey is "textures" or "vfx_spritesheets" or "decals" or "noise_textures")
			{
				categoryObject[fileName] = itemObject;
			}
			else
			{
				categoryObject[fileName] = blake3Hash;
			}
		}

		SaveAssetsToManifest(targetDirectory, assets, removeFromMetadata: true);
	}

	public static void RemoveManifestAsset(
		string mapDirectory,
		string category,
		string fileName,
		string? subCategory = null)
	{
		string targetDirectory = string.IsNullOrEmpty(mapDirectory)
			? MapWorkspaceService.GetActiveWorkspacePath()
			: mapDirectory;

		var assets = LoadUnionedAssets(targetDirectory);
		string categoryKey = NormalizeCategoryKey(category);

		if (assets.ContainsKey(categoryKey) && assets[categoryKey] is JsonObject categoryObject)
		{
			if (categoryKey == "glb" || !string.IsNullOrEmpty(subCategory))
			{
				string subKey = NormalizeGlbSubCategory(subCategory ?? "props");
				if (categoryObject.ContainsKey(subKey) && categoryObject[subKey] is JsonObject subCategoryObject)
				{
					subCategoryObject.Remove(fileName);
				}
				foreach (var fallbackSubCategory in new[] { "units", "buildings", "resources", "props", "projectiles", "attachments", "weapons" })
				{
					if (categoryObject.ContainsKey(fallbackSubCategory) && categoryObject[fallbackSubCategory] is JsonObject fallbackSubObject)
					{
						fallbackSubObject.Remove(fileName);
					}
				}
			}
			else
			{
				categoryObject.Remove(fileName);
			}
		}

		SaveAssetsToManifest(targetDirectory, assets, removeFromMetadata: true);

		RemoveAssetFromMetadata(targetDirectory, categoryKey, fileName);
	}

	public static void EnsureManifestJson(string directory)
	{
		if (string.IsNullOrEmpty(directory)) return;

		string manifestPath = Path.Combine(directory, "manifest.json");
		if (File.Exists(manifestPath) && new FileInfo(manifestPath).Length > 0)
		{
			try
			{
				var existingRoot = JsonNode.Parse(File.ReadAllText(manifestPath))?.AsObject();
				if (existingRoot != null)
				{
					if (existingRoot.ContainsKey("Assets"))
					{
						return;
					}
				}
			}
			catch
			{
			}
		}

		var unionedAssets = LoadUnionedAssets(directory);
		SaveAssetsToManifest(directory, unionedAssets, removeFromMetadata: false);
	}

	private static JsonObject BuildCleanManifestAssets(JsonObject sourceAssets)
	{
		var cleanAssets = new JsonObject();

		foreach (var categoryKeyValuePair in sourceAssets)
		{
			string category = NormalizeCategoryKey(categoryKeyValuePair.Key);
			if (category == "glb" && categoryKeyValuePair.Value is JsonObject glbObject)
			{
				var cleanGlb = new JsonObject();
				cleanAssets["glb"] = cleanGlb;

				foreach (var subCategoryKeyValuePair in glbObject)
				{
					string subCategory = NormalizeGlbSubCategory(subCategoryKeyValuePair.Key);
					if (subCategoryKeyValuePair.Value is JsonObject subCategoryObject)
					{
						var cleanSub = new JsonObject();
						cleanGlb[subCategory] = cleanSub;

						foreach (var itemKeyValuePair in subCategoryObject)
						{
							string hash = ExtractHashString(itemKeyValuePair.Value);
							if (string.IsNullOrEmpty(hash))
							{
								hash = RealmMetadataHelper.ComputeBlake3(System.Text.Encoding.UTF8.GetBytes(itemKeyValuePair.Key), Path.GetExtension(itemKeyValuePair.Key));
							}
							cleanSub[itemKeyValuePair.Key] = hash;
						}
					}
				}
			}
			else if (categoryKeyValuePair.Value is JsonObject categoryObject)
			{
				var cleanCategory = new JsonObject();
				cleanAssets[category] = cleanCategory;

				foreach (var itemKeyValuePair in categoryObject)
				{
					string hash = ExtractHashString(itemKeyValuePair.Value);
					if (string.IsNullOrEmpty(hash))
					{
						hash = RealmMetadataHelper.ComputeBlake3(System.Text.Encoding.UTF8.GetBytes(itemKeyValuePair.Key), Path.GetExtension(itemKeyValuePair.Key));
					}
					cleanCategory[itemKeyValuePair.Key] = hash;
				}
			}
		}

		return cleanAssets;
	}

	private static string ExtractHashString(JsonNode? node)
	{
		if (node is JsonValue value)
		{
			return value.ToString();
		}
		if (node is JsonObject jsonObject && jsonObject.TryGetPropertyValue("hash", out var hashNode) && hashNode != null)
		{
			return hashNode.ToString();
		}
		return string.Empty;
	}

	private static void SynchronizeAttributesToMetadata(string targetDirectory, JsonObject assets)
	{
		string metadataPath = Path.Combine(targetDirectory, "metadata.json");
		JsonObject metadataRoot;

		if (File.Exists(metadataPath))
		{
			try
			{
				metadataRoot = JsonNode.Parse(File.ReadAllText(metadataPath))?.AsObject() ?? new JsonObject();
			}
			catch
			{
				metadataRoot = new JsonObject();
			}
		}
		else
		{
			metadataRoot = new JsonObject();
		}

		metadataRoot.Remove("Assets");
		if (metadataRoot["MapProperties"] is JsonObject mapPropertiesObject)
		{
			mapPropertiesObject.Remove("Assets");
		}

		SyncCategoryAttributesToMetadata(metadataRoot, assets, "textures");
		SyncCategoryAttributesToMetadata(metadataRoot, assets, "decals");
		SyncCategoryAttributesToMetadata(metadataRoot, assets, "vfx_spritesheets");
		SyncCategoryAttributesToMetadata(metadataRoot, assets, "noise_textures");

		SyncModelAttributesToMetadata(metadataRoot, assets);

		SaveLoadService.CleanMetadataJsonSchema(metadataRoot);
		MapJsonFormatter.SaveFormattedJson(metadataPath, metadataRoot);
	}

	private static void SyncCategoryAttributesToMetadata(JsonObject metadataRoot, JsonObject assets, string category)
	{
		string categoryKey = NormalizeCategoryKey(category);
		if (!assets.ContainsKey(categoryKey) || assets[categoryKey] is not JsonObject sourceCategoryObject)
		{
			return;
		}

		if (!metadataRoot.ContainsKey(categoryKey) || metadataRoot[categoryKey] is not JsonObject)
		{
			metadataRoot[categoryKey] = new JsonObject();
		}
		var targetCategoryObject = metadataRoot[categoryKey]!.AsObject();

		var currentAssetKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		foreach (var itemKeyValuePair in sourceCategoryObject)
		{
			string fileName = itemKeyValuePair.Key;
			currentAssetKeys.Add(fileName);

			if (itemKeyValuePair.Value is JsonObject sourceItemObject)
			{
				JsonObject destinationItemObject;
				if (targetCategoryObject.TryGetPropertyValue(fileName, out var existingNode) && existingNode is JsonObject existingObject)
				{
					destinationItemObject = existingObject;
				}
				else
				{
					destinationItemObject = new JsonObject();
					targetCategoryObject[fileName] = destinationItemObject;
				}

				foreach (var property in sourceItemObject)
				{
					if (string.Equals(property.Key, "hash", StringComparison.OrdinalIgnoreCase))
					{
						continue;
					}
					destinationItemObject[property.Key] = property.Value?.DeepClone();
				}
				destinationItemObject.Remove("hash");
			}
		}

		var keysToRemove = targetCategoryObject.Select(pair => pair.Key).Where(key => !currentAssetKeys.Contains(key)).ToList();
		foreach (var key in keysToRemove)
		{
			targetCategoryObject.Remove(key);
		}
	}

	private static void SyncModelAttributesToMetadata(JsonObject metadataRoot, JsonObject assets)
	{
		if (!assets.ContainsKey("glb") || assets["glb"] is not JsonObject glbObject)
		{
			return;
		}

		EnsureMetadataTopLevelObject(metadataRoot, "ModelOffsets");
		EnsureMetadataTopLevelObject(metadataRoot, "ModelScales");
		EnsureMetadataTopLevelObject(metadataRoot, "ModelCollisionCircleRatios");
		EnsureMetadataTopLevelObject(metadataRoot, "ModelObstacleRadii");
		EnsureMetadataTopLevelObject(metadataRoot, "ModelBrightness");
		EnsureMetadataTopLevelObject(metadataRoot, "ModelNormalModes");
		EnsureMetadataTopLevelObject(metadataRoot, "ModelNormalizeLuminance");
		EnsureMetadataTopLevelObject(metadataRoot, "ModelIgnorePlayerColor");
		EnsureMetadataTopLevelObject(metadataRoot, "ModelSpawnShaders");
		EnsureMetadataTopLevelObject(metadataRoot, "ModelDeathShaders");

		var offsetsObject = metadataRoot["ModelOffsets"]!.AsObject();
		var scalesObject = metadataRoot["ModelScales"]!.AsObject();
		var collisionCircleObject = metadataRoot["ModelCollisionCircleRatios"]!.AsObject();
		var obstacleRadiiObject = metadataRoot["ModelObstacleRadii"]!.AsObject();
		var brightnessObject = metadataRoot["ModelBrightness"]!.AsObject();
		var normalModesObject = metadataRoot["ModelNormalModes"]!.AsObject();
		var normalizeLuminanceObject = metadataRoot["ModelNormalizeLuminance"]!.AsObject();
		var ignorePlayerColorObject = metadataRoot["ModelIgnorePlayerColor"]!.AsObject();
		var spawnShadersObject = metadataRoot["ModelSpawnShaders"]!.AsObject();
		var deathShadersObject = metadataRoot["ModelDeathShaders"]!.AsObject();

		foreach (var subCategoryKeyValuePair in glbObject)
		{
			if (subCategoryKeyValuePair.Value is JsonObject subCategoryObject)
			{
				foreach (var itemKeyValuePair in subCategoryObject)
				{
					string fileName = itemKeyValuePair.Key;
					if (itemKeyValuePair.Value is JsonObject modelProperties)
					{
						if (modelProperties.TryGetPropertyValue("y_offset", out var yOffsetNode) && yOffsetNode != null && float.TryParse(yOffsetNode.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float yOffset))
						{
							offsetsObject[fileName] = yOffset;
						}
						if (modelProperties.TryGetPropertyValue("scale", out var scaleNode) && scaleNode != null && float.TryParse(scaleNode.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float scale))
						{
							scalesObject[fileName] = scale;
						}
						if (modelProperties.TryGetPropertyValue("collision_circle_ratio", out var ratioNode) && ratioNode != null && float.TryParse(ratioNode.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float ratio))
						{
							collisionCircleObject[fileName] = ratio;
						}
						if (modelProperties.TryGetPropertyValue("collision_radius", out var radiusNode) && radiusNode != null && float.TryParse(radiusNode.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float radius))
						{
							obstacleRadiiObject[fileName] = radius;
						}
						if (modelProperties.TryGetPropertyValue("brightness", out var brightnessNode) && brightnessNode != null && float.TryParse(brightnessNode.ToString(), NumberStyles.Float, CultureInfo.InvariantCulture, out float brightness))
						{
							brightnessObject[fileName] = brightness;
						}
						if (modelProperties.TryGetPropertyValue("normal_mode", out var normalModeNode) && normalModeNode != null)
						{
							normalModesObject[fileName] = normalModeNode.ToString();
						}
						if (modelProperties.TryGetPropertyValue("normalize_luminance", out var normalizeLuminanceNode) && normalizeLuminanceNode != null && bool.TryParse(normalizeLuminanceNode.ToString(), out bool normalizeLuminance))
						{
							normalizeLuminanceObject[fileName] = normalizeLuminance;
						}
						if (modelProperties.TryGetPropertyValue("ignore_player_color", out var ignorePlayerColorNode) && ignorePlayerColorNode != null && bool.TryParse(ignorePlayerColorNode.ToString(), out bool ignorePlayerColor))
						{
							ignorePlayerColorObject[fileName] = ignorePlayerColor;
						}
						if (modelProperties.TryGetPropertyValue("spawn_shader", out var spawnShaderNode) && spawnShaderNode != null)
						{
							string spawnShader = spawnShaderNode.ToString();
							if (!string.IsNullOrWhiteSpace(spawnShader)) spawnShadersObject[fileName] = spawnShader;
						}
						if (modelProperties.TryGetPropertyValue("death_shader", out var deathShaderNode) && deathShaderNode != null)
						{
							string deathShader = deathShaderNode.ToString();
							if (!string.IsNullOrWhiteSpace(deathShader)) deathShadersObject[fileName] = deathShader;
						}
					}
				}
			}
		}
	}

	private static void EnsureMetadataTopLevelObject(JsonObject metadataRoot, string propertyName)
	{
		if (!metadataRoot.ContainsKey(propertyName) || metadataRoot[propertyName] is not JsonObject)
		{
			metadataRoot[propertyName] = new JsonObject();
		}
	}

	private static void RemoveAssetFromMetadata(string targetDirectory, string categoryKey, string fileName)
	{
		string metadataPath = Path.Combine(targetDirectory, "metadata.json");
		if (!File.Exists(metadataPath)) return;

		try
		{
			var metadataRoot = JsonNode.Parse(File.ReadAllText(metadataPath))?.AsObject();
			if (metadataRoot == null) return;

			bool modified = false;
			if (metadataRoot.TryGetPropertyValue(categoryKey, out var categoryNode) && categoryNode is JsonObject categoryObject)
			{
				if (categoryObject.Remove(fileName))
				{
					modified = true;
				}
			}

			if (categoryKey == "glb")
			{
				foreach (var mapKey in new[] { "ModelOffsets", "ModelScales", "ModelCollisionCircleRatios", "ModelObstacleRadii", "ModelBrightness", "ModelNormalModes", "ModelNormalizeLuminance", "ModelIgnorePlayerColor", "ModelSpawnShaders", "ModelDeathShaders" })
				{
					if (metadataRoot.TryGetPropertyValue(mapKey, out var mapNode) && mapNode is JsonObject mapObject)
					{
						if (mapObject.Remove(fileName))
						{
							modified = true;
						}
					}
				}
			}

			if (modified)
			{
				SaveLoadService.CleanMetadataJsonSchema(metadataRoot);
				MapJsonFormatter.SaveFormattedJson(metadataPath, metadataRoot);
			}
		}
		catch (Exception exception)
		{
			GD.PrintErr($"[MapAssetHelper] RemoveAssetFromMetadata error: {exception.Message}");
		}
	}

	private static void AttachMetadataAttributesToUnionedAssets(JsonObject unionedAssets, JsonObject metadataRoot, string targetDirectory)
	{
		if (metadataRoot["textures"] is JsonObject texturesObject)
		{
			MergeCategoryAttributes(unionedAssets, "textures", texturesObject);
		}

		if (metadataRoot["decals"] is JsonObject decalsObject)
		{
			MergeCategoryAttributes(unionedAssets, "decals", decalsObject);
		}

		if (metadataRoot["vfx_spritesheets"] is JsonObject vfxObject)
		{
			MergeCategoryAttributes(unionedAssets, "vfx_spritesheets", vfxObject);
		}
		else if (metadataRoot["vfx"] is JsonObject vfxAltObject)
		{
			MergeCategoryAttributes(unionedAssets, "vfx_spritesheets", vfxAltObject);
		}

		if (metadataRoot["noise_textures"] is JsonObject noiseObject)
		{
			MergeCategoryAttributes(unionedAssets, "noise_textures", noiseObject);
		}
		else if (metadataRoot["noise"] is JsonObject noiseAltObject)
		{
			MergeCategoryAttributes(unionedAssets, "noise_textures", noiseAltObject);
		}

		if (metadataRoot["icons"] is JsonObject iconsObject)
		{
			MergeCategoryAttributes(unionedAssets, "icons", iconsObject);
		}

		if (metadataRoot["skyboxes"] is JsonObject skyboxesObject)
		{
			MergeCategoryAttributes(unionedAssets, "skyboxes", skyboxesObject);
		}

		if (metadataRoot["ribbons"] is JsonObject ribbonsObject)
		{
			MergeCategoryAttributes(unionedAssets, "ribbons", ribbonsObject);
		}
		else if (metadataRoot["ribbon_textures"] is JsonObject ribbonsAltObject)
		{
			MergeCategoryAttributes(unionedAssets, "ribbons", ribbonsAltObject);
		}

		if (metadataRoot["Assets"] is JsonObject legacyAssets)
		{
			MergeAssetsInto(unionedAssets, legacyAssets);
		}
		if (metadataRoot["MapProperties"]?["Assets"] is JsonObject legacyMapPropsAssets)
		{
			MergeAssetsInto(unionedAssets, legacyMapPropsAssets);
		}

		AttachCustomEntitiesToGlb(unionedAssets, metadataRoot, targetDirectory);
		AttachModelMetadataAttributes(unionedAssets, metadataRoot);
	}

	private static void AttachCustomEntitiesToGlb(JsonObject unionedAssets, JsonObject metadataRoot, string targetDirectory)
	{
		var arrayMappings = new (string ArrayKey, string SubCategory)[]
		{
			("CustomUnits", "units"),
			("Units", "units"),
			("CustomBuildings", "buildings"),
			("Buildings", "buildings"),
			("CustomResources", "resources"),
			("Resources", "resources"),
			("CustomProps", "props"),
			("Props", "props"),
			("CustomAttachments", "attachments"),
			("Attachments", "attachments"),
			("CustomWeapons", "weapons"),
			("Weapons", "weapons")
		};

		foreach (var (arrayKey, subCat) in arrayMappings)
		{
			if (metadataRoot.TryGetPropertyValue(arrayKey, out var arrNode) && arrNode is JsonArray arr)
			{
				foreach (var itemNode in arr)
				{
					if (itemNode is JsonObject entityObj)
					{
						string modelPath = entityObj["ModelPath"]?.ToString() ?? "";
						if (string.IsNullOrEmpty(modelPath))
						{
							modelPath = entityObj["UnitId"]?.ToString() ?? entityObj["AttachmentId"]?.ToString() ?? entityObj["WeaponId"]?.ToString() ?? "";
						}

						if (!string.IsNullOrEmpty(modelPath))
						{
							string fileName = Path.GetFileName(modelPath);
							if (!fileName.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) && !fileName.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase))
							{
								fileName += ".glb";
							}

							string? diskPath = FindModelOnDisk(targetDirectory, subCat, fileName);
							EnsureGlbEntryExists(unionedAssets, subCat, fileName, diskPath);
						}
					}
				}
			}
		}

		string[] modelDictNames = new[]
		{
			"ModelOffsets", "ModelScales", "ModelCollisionCircleRatios", "ModelObstacleRadii",
			"ModelBrightness", "ModelNormalModes", "ModelNormalizeLuminance",
			"ModelIgnorePlayerColor", "ModelSpawnShaders", "ModelDeathShaders"
		};

		foreach (var dictName in modelDictNames)
		{
			if (metadataRoot.TryGetPropertyValue(dictName, out var dictNode) && dictNode is JsonObject dictObj)
			{
				foreach (var prop in dictObj)
				{
					string rawName = prop.Key;
					string fileName = rawName.EndsWith(".glb", StringComparison.OrdinalIgnoreCase) || rawName.EndsWith(".gltf", StringComparison.OrdinalIgnoreCase)
						? rawName
						: rawName + ".glb";

					string? existingSub = FindExistingGlbSubCategory(unionedAssets, fileName);
					if (string.IsNullOrEmpty(existingSub))
					{
						string? diskPath = FindModelOnDisk(targetDirectory, null, fileName, out string foundSub);
						existingSub = !string.IsNullOrEmpty(foundSub) ? foundSub : "props";
						EnsureGlbEntryExists(unionedAssets, existingSub, fileName, diskPath);
					}
				}
			}
		}
	}

	private static string DetermineTextureCategory(string relativeToAssets)
	{
		string lower = relativeToAssets.ToLowerInvariant();
		if (lower.Contains("decals/")) return "decals";
		if (lower.Contains("icons/")) return "icons";
		if (lower.Contains("skyboxes/")) return "skyboxes";
		if (lower.Contains("vfx/") || lower.Contains("vfx_spritesheets/")) return "vfx_spritesheets";
		if (lower.Contains("noise/") || lower.Contains("noise_textures/")) return "noise_textures";
		if (lower.Contains("ribbons/") || lower.Contains("ribbon_textures/")) return "ribbons";
		return "textures";
	}

	private static void EnsureGlbEntryExists(JsonObject unionedAssets, string subCategory, string fileName, string? diskPath = null)
	{
		if (!unionedAssets.ContainsKey("glb") || unionedAssets["glb"] is not JsonObject)
		{
			unionedAssets["glb"] = new JsonObject();
		}
		var glbObject = unionedAssets["glb"]!.AsObject();

		string normalizedSub = NormalizeGlbSubCategory(subCategory);
		if (!glbObject.ContainsKey(normalizedSub) || glbObject[normalizedSub] is not JsonObject)
		{
			glbObject[normalizedSub] = new JsonObject();
		}
		var subCategoryObject = glbObject[normalizedSub]!.AsObject();

		if (!subCategoryObject.ContainsKey(fileName))
		{
			var itemObject = new JsonObject();
			if (!string.IsNullOrEmpty(diskPath) && File.Exists(diskPath))
			{
				string hash = RealmMetadataHelper.ComputeBlake3(diskPath);
				if (!string.IsNullOrEmpty(hash))
				{
					itemObject["hash"] = hash;
				}
			}
			subCategoryObject[fileName] = itemObject;
		}
		else if (subCategoryObject[fileName] is JsonObject existingItem && !existingItem.ContainsKey("hash") && !string.IsNullOrEmpty(diskPath) && File.Exists(diskPath))
		{
			string hash = RealmMetadataHelper.ComputeBlake3(diskPath);
			if (!string.IsNullOrEmpty(hash))
			{
				existingItem["hash"] = hash;
			}
		}
	}

	private static void EnsureCategoryEntryExists(JsonObject unionedAssets, string category, string fileName, string? diskPath = null)
	{
		string categoryKey = NormalizeCategoryKey(category);
		if (!unionedAssets.ContainsKey(categoryKey) || unionedAssets[categoryKey] is not JsonObject)
		{
			unionedAssets[categoryKey] = new JsonObject();
		}
		var categoryObject = unionedAssets[categoryKey]!.AsObject();

		if (!categoryObject.ContainsKey(fileName))
		{
			string hash = (!string.IsNullOrEmpty(diskPath) && File.Exists(diskPath))
				? RealmMetadataHelper.ComputeBlake3(diskPath)
				: string.Empty;

			if (categoryKey is "textures" or "decals" or "vfx_spritesheets" or "noise_textures")
			{
				var itemObject = new JsonObject();
				if (!string.IsNullOrEmpty(hash))
				{
					itemObject["hash"] = hash;
				}
				categoryObject[fileName] = itemObject;
			}
			else
			{
				categoryObject[fileName] = !string.IsNullOrEmpty(hash) ? hash : string.Empty;
			}
		}
		else if (categoryObject[fileName] is JsonObject existingObject && !existingObject.ContainsKey("hash") && !string.IsNullOrEmpty(diskPath) && File.Exists(diskPath))
		{
			string hash = RealmMetadataHelper.ComputeBlake3(diskPath);
			if (!string.IsNullOrEmpty(hash))
			{
				existingObject["hash"] = hash;
			}
		}
		else if (categoryObject[fileName] is JsonValue val && string.IsNullOrEmpty(val.ToString()) && !string.IsNullOrEmpty(diskPath) && File.Exists(diskPath))
		{
			string hash = RealmMetadataHelper.ComputeBlake3(diskPath);
			if (!string.IsNullOrEmpty(hash))
			{
				categoryObject[fileName] = hash;
			}
		}
	}

	private static string? FindModelOnDisk(string targetDirectory, string? preferredSubCategory, string fileName, out string resolvedSubCategory)
	{
		resolvedSubCategory = !string.IsNullOrEmpty(preferredSubCategory) ? preferredSubCategory : "props";
		if (string.IsNullOrEmpty(targetDirectory) || !Directory.Exists(targetDirectory))
		{
			return null;
		}

		string modelsDir = Path.Combine(targetDirectory, "Assets", "models");
		if (!Directory.Exists(modelsDir))
		{
			return null;
		}

		if (!string.IsNullOrEmpty(preferredSubCategory))
		{
			string prefSub = NormalizeGlbSubCategory(preferredSubCategory);
			string preferredPath = Path.Combine(modelsDir, prefSub, fileName);
			if (File.Exists(preferredPath))
			{
				resolvedSubCategory = prefSub;
				return preferredPath;
			}
		}

		string[] subCategories = new[] { "units", "buildings", "resources", "props", "projectiles", "attachments", "weapons" };
		foreach (var sub in subCategories)
		{
			string candPath = Path.Combine(modelsDir, sub, fileName);
			if (File.Exists(candPath))
			{
				resolvedSubCategory = sub;
				return candPath;
			}
		}

		string directPath = Path.Combine(modelsDir, fileName);
		if (File.Exists(directPath))
		{
			return directPath;
		}

		return null;
	}

	private static string? FindModelOnDisk(string targetDirectory, string? preferredSubCategory, string fileName)
	{
		return FindModelOnDisk(targetDirectory, preferredSubCategory, fileName, out _);
	}

	private static string? FindExistingGlbSubCategory(JsonObject unionedAssets, string fileName)
	{
		if (unionedAssets["glb"] is JsonObject glbObj)
		{
			foreach (var subPair in glbObj)
			{
				if (subPair.Value is JsonObject subObj && subObj.ContainsKey(fileName))
				{
					return subPair.Key;
				}
			}
		}
		return null;
	}

	private static void EnsureAllAssetsHaveBlake3Hashes(JsonObject unionedAssets, string targetDirectory)
	{
		string assetsDir = Path.Combine(targetDirectory, "Assets");
		bool hasAssetsDir = Directory.Exists(assetsDir);

		foreach (var categoryPair in unionedAssets)
		{
			string category = NormalizeCategoryKey(categoryPair.Key);
			if (category == "glb" && categoryPair.Value is JsonObject glbObj)
			{
				foreach (var subPair in glbObj)
				{
					string subCat = NormalizeGlbSubCategory(subPair.Key);
					if (subPair.Value is JsonObject subObj)
					{
						foreach (var itemPair in subObj)
						{
							string fileName = itemPair.Key;
							string hash = ExtractHashString(itemPair.Value);
							if (string.IsNullOrEmpty(hash))
							{
								string? diskPath = hasAssetsDir ? FindModelOnDisk(targetDirectory, subCat, fileName) : null;
								if (!string.IsNullOrEmpty(diskPath) && File.Exists(diskPath))
								{
									hash = RealmMetadataHelper.ComputeBlake3(diskPath);
								}
								else
								{
									hash = RealmMetadataHelper.ComputeBlake3(System.Text.Encoding.UTF8.GetBytes(fileName), Path.GetExtension(fileName));
								}

								if (itemPair.Value is JsonObject itemObj)
								{
									itemObj["hash"] = hash;
								}
								else
								{
									subObj[fileName] = hash;
								}
							}
						}
					}
				}
			}
			else if (categoryPair.Value is JsonObject catObj)
			{
				foreach (var itemPair in catObj)
				{
					string fileName = itemPair.Key;
					string hash = ExtractHashString(itemPair.Value);
					if (string.IsNullOrEmpty(hash))
					{
						string subFolder = category switch
						{
							"vfx_spritesheets" => "vfx",
							"animations" => "animations",
							"sfx" => "audio/sfx",
							"music" => "audio/music",
							"icons" => "icons",
							"decals" => "decals",
							"ribbons" => "ribbons",
							"noise_textures" => "noise",
							"skyboxes" => "skyboxes",
							_ => "textures"
						};
						string diskPath = Path.Combine(assetsDir, subFolder, fileName);
						if (!File.Exists(diskPath) && subFolder is "audio/sfx" or "audio/music")
						{
							diskPath = Path.Combine(assetsDir, subFolder.Substring(6), fileName);
						}
						if (!File.Exists(diskPath))
						{
							diskPath = Path.Combine(assetsDir, fileName);
						}

						if (File.Exists(diskPath))
						{
							hash = RealmMetadataHelper.ComputeBlake3(diskPath);
						}
						else
						{
							hash = RealmMetadataHelper.ComputeBlake3(System.Text.Encoding.UTF8.GetBytes(fileName), Path.GetExtension(fileName));
						}

						if (itemPair.Value is JsonObject itemObj)
						{
							itemObj["hash"] = hash;
						}
						else
						{
							catObj[fileName] = hash;
						}
					}
				}
			}
		}
	}

	private static void MergeCategoryAttributes(JsonObject unionedAssets, string category, JsonObject sourceObject)
	{
		string categoryKey = NormalizeCategoryKey(category);
		if (!unionedAssets.ContainsKey(categoryKey) || unionedAssets[categoryKey] is not JsonObject)
		{
			unionedAssets[categoryKey] = new JsonObject();
		}
		var targetCategoryObject = unionedAssets[categoryKey]!.AsObject();

		foreach (var itemKeyValuePair in sourceObject)
		{
			string fileName = itemKeyValuePair.Key;
			JsonObject targetItemObject;

			if (targetCategoryObject.TryGetPropertyValue(fileName, out var existingNode) && existingNode is JsonObject existingObject)
			{
				targetItemObject = existingObject;
			}
			else
			{
				targetItemObject = new JsonObject();
				if (existingNode is JsonValue val)
				{
					targetItemObject["hash"] = val.ToString();
				}
				targetCategoryObject[fileName] = targetItemObject;
			}

			if (itemKeyValuePair.Value is JsonObject sourceAttributes)
			{
				foreach (var attributeProperty in sourceAttributes)
				{
					if (string.Equals(attributeProperty.Key, "hash", StringComparison.OrdinalIgnoreCase))
					{
						if (!targetItemObject.ContainsKey("hash"))
						{
							targetItemObject["hash"] = attributeProperty.Value?.DeepClone();
						}
					}
					else
					{
						targetItemObject[attributeProperty.Key] = attributeProperty.Value?.DeepClone();
					}
				}
			}
		}
	}

	private static void AttachModelMetadataAttributes(JsonObject unionedAssets, JsonObject metadataRoot)
	{
		if (!unionedAssets.ContainsKey("glb") || unionedAssets["glb"] is not JsonObject glbObject)
		{
			return;
		}

		var offsetsObject = metadataRoot["ModelOffsets"] as JsonObject;
		var scalesObject = metadataRoot["ModelScales"] as JsonObject;
		var collisionCircleObject = metadataRoot["ModelCollisionCircleRatios"] as JsonObject;
		var obstacleRadiiObject = metadataRoot["ModelObstacleRadii"] as JsonObject;
		var brightnessObject = metadataRoot["ModelBrightness"] as JsonObject;
		var normalModesObject = metadataRoot["ModelNormalModes"] as JsonObject;
		var normalizeLuminanceObject = metadataRoot["ModelNormalizeLuminance"] as JsonObject;
		var ignorePlayerColorObject = metadataRoot["ModelIgnorePlayerColor"] as JsonObject;
		var spawnShadersObject = metadataRoot["ModelSpawnShaders"] as JsonObject;
		var deathShadersObject = metadataRoot["ModelDeathShaders"] as JsonObject;

		foreach (var subCategoryKeyValuePair in glbObject)
		{
			if (subCategoryKeyValuePair.Value is JsonObject subCategoryObject)
			{
				foreach (var itemKeyValuePair in subCategoryObject)
				{
					string fileName = itemKeyValuePair.Key;
					string baseName = Path.GetFileNameWithoutExtension(fileName);
					JsonObject modelObject;

					if (itemKeyValuePair.Value is JsonObject existingObject)
					{
						modelObject = existingObject;
					}
					else
					{
						modelObject = new JsonObject();
						if (itemKeyValuePair.Value is JsonValue value)
						{
							modelObject["hash"] = value.ToString();
						}
						subCategoryObject[fileName] = modelObject;
					}

					if (offsetsObject != null && (offsetsObject.TryGetPropertyValue(fileName, out var offsetNode) || offsetsObject.TryGetPropertyValue(baseName, out offsetNode)))
					{
						modelObject["y_offset"] = offsetNode?.DeepClone();
					}
					if (scalesObject != null && (scalesObject.TryGetPropertyValue(fileName, out var scaleNode) || scalesObject.TryGetPropertyValue(baseName, out scaleNode)))
					{
						modelObject["scale"] = scaleNode?.DeepClone();
					}
					if (collisionCircleObject != null && (collisionCircleObject.TryGetPropertyValue(fileName, out var circleNode) || collisionCircleObject.TryGetPropertyValue(baseName, out circleNode)))
					{
						modelObject["collision_circle_ratio"] = circleNode?.DeepClone();
					}
					if (obstacleRadiiObject != null && (obstacleRadiiObject.TryGetPropertyValue(fileName, out var radiusNode) || obstacleRadiiObject.TryGetPropertyValue(baseName, out radiusNode)))
					{
						modelObject["collision_radius"] = radiusNode?.DeepClone();
					}
					if (brightnessObject != null && (brightnessObject.TryGetPropertyValue(fileName, out var brightNode) || brightnessObject.TryGetPropertyValue(baseName, out brightNode)))
					{
						modelObject["brightness"] = brightNode?.DeepClone();
					}
					if (normalModesObject != null && (normalModesObject.TryGetPropertyValue(fileName, out var normalNode) || normalModesObject.TryGetPropertyValue(baseName, out normalNode)))
					{
						modelObject["normal_mode"] = normalNode?.DeepClone();
					}
					if (normalizeLuminanceObject != null && (normalizeLuminanceObject.TryGetPropertyValue(fileName, out var lumNode) || normalizeLuminanceObject.TryGetPropertyValue(baseName, out lumNode)))
					{
						modelObject["normalize_luminance"] = lumNode?.DeepClone();
					}
					if (ignorePlayerColorObject != null && (ignorePlayerColorObject.TryGetPropertyValue(fileName, out var ipcNode) || ignorePlayerColorObject.TryGetPropertyValue(baseName, out ipcNode)))
					{
						modelObject["ignore_player_color"] = ipcNode?.DeepClone();
					}
					if (spawnShadersObject != null && (spawnShadersObject.TryGetPropertyValue(fileName, out var spawnNode) || spawnShadersObject.TryGetPropertyValue(baseName, out spawnNode)))
					{
						modelObject["spawn_shader"] = spawnNode?.DeepClone();
					}
					if (deathShadersObject != null && (deathShadersObject.TryGetPropertyValue(fileName, out var deathNode) || deathShadersObject.TryGetPropertyValue(baseName, out deathNode)))
					{
						modelObject["death_shader"] = deathNode?.DeepClone();
					}
				}
			}
		}
	}

	private static void MergeAssetsInto(JsonObject target, JsonObject source)
	{
		foreach (var categoryKeyValuePair in source)
		{
			string category = NormalizeCategoryKey(categoryKeyValuePair.Key);
			if (category == "glb" && categoryKeyValuePair.Value is JsonObject glbSource)
			{
				if (!target.ContainsKey("glb") || target["glb"] is not JsonObject)
				{
					target["glb"] = new JsonObject();
				}
				var glbTarget = target["glb"]!.AsObject();

				foreach (var subCategoryKeyValuePair in glbSource)
				{
					string subCategory = NormalizeGlbSubCategory(subCategoryKeyValuePair.Key);
					if (subCategoryKeyValuePair.Value is JsonObject subSource)
					{
						if (!glbTarget.ContainsKey(subCategory) || glbTarget[subCategory] is not JsonObject)
						{
							glbTarget[subCategory] = new JsonObject();
						}
						var subTarget = glbTarget[subCategory]!.AsObject();

						foreach (var itemKeyValuePair in subSource)
						{
							MergeItemInto(subTarget, itemKeyValuePair.Key, itemKeyValuePair.Value);
						}
					}
				}
			}
			else if (categoryKeyValuePair.Value is JsonObject categorySource)
			{
				MergeCategoryInto(target, category, categorySource);
			}
		}
	}

	private static void MergeCategoryInto(JsonObject target, string category, JsonObject source)
	{
		string categoryKey = NormalizeCategoryKey(category);
		if (!target.ContainsKey(categoryKey) || target[categoryKey] is not JsonObject)
		{
			target[categoryKey] = new JsonObject();
		}
		var categoryTarget = target[categoryKey]!.AsObject();

		foreach (var itemKeyValuePair in source)
		{
			MergeItemInto(categoryTarget, itemKeyValuePair.Key, itemKeyValuePair.Value);
		}
	}

	private static void MergeItemInto(JsonObject targetContainer, string key, JsonNode? sourceNode)
	{
		if (sourceNode == null) return;

		if (targetContainer.ContainsKey(key) && targetContainer[key] is JsonObject existingObject && sourceNode is JsonObject sourceObject)
		{
			foreach (var property in sourceObject)
			{
				existingObject[property.Key] = property.Value?.DeepClone();
			}
		}
		else
		{
			targetContainer[key] = sourceNode.DeepClone();
		}
	}

	public static string NormalizeCategoryKey(string category)
	{
		string lower = category.ToLowerInvariant();
		return lower switch
		{
			"vfx" or "vfx_spritesheets" or "spritesheets" => "vfx_spritesheets",
			"ribbon" or "ribbons" or "ribbon_textures" => "ribbons",
			"noise" or "noise_textures" => "noise_textures",
			"sound" or "sounds" or "audio" => "sfx",
			_ => lower
		};
	}

	public static string NormalizeGlbSubCategory(string subCategory)
	{
		string lower = subCategory.ToLowerInvariant();
		if (lower.StartsWith("glb_")) lower = lower.Substring(4);
		return lower switch
		{
			"unit" or "units" or "character" => "units",
			"building" or "buildings" => "buildings",
			"resource" or "resources" or "environment" => "resources",
			"prop" or "props" => "props",
			"projectile" or "projectiles" => "projectiles",
			"attachment" or "attachments" => "attachments",
			"weapon" or "weapons" => "weapons",
			_ => lower
		};
	}
}
