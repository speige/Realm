using System;
using System.IO;
using System.Text.Json.Nodes;
using Realm.Shared.Metadata;

namespace Realm.Shared.ModelOptimization;

public class ModelConversionResult
{
	public bool Success { get; set; }
	public string InputPath { get; set; } = string.Empty;
	public string OutputPath { get; set; } = string.Empty;
	public string ErrorMessage { get; set; } = string.Empty;
	public bool SupportsTeamColor { get; set; }
	public string? AssetType { get; set; }
	public string? Author { get; set; }
	public string? PreferredFileName { get; set; }
	public int OriginalSize { get; set; }
	public int OptimizedSize { get; set; }
	public byte[]? OutputBytes { get; set; }
}

public static class ModelConverter
{
	public static readonly string[] SupportedModelExtensions =
	[
		".glb", ".gltf", ".rmod", ".fbx", ".obj"
	];

	public static bool IsModelFile(string filePath)
	{
		if (string.IsNullOrWhiteSpace(filePath)) return false;
		string ext = Path.GetExtension(filePath).ToLowerInvariant();
		return Array.Exists(SupportedModelExtensions, e => e.Equals(ext, StringComparison.OrdinalIgnoreCase));
	}

	public static OptimizationOptions GetAutomaticOptimizationOptions(string? assetType, bool forceReDecimate)
	{
		int maxRes = 1024;
		if (string.Equals(assetType, "Item", StringComparison.OrdinalIgnoreCase))
		{
			maxRes = 512;
		}

		return new OptimizationOptions
		{
			SimplificationRatio = 0.5f,
			MaxTextureResolution = maxRes,
			ForceReDecimate = forceReDecimate
		};
	}

	public static string InferAssetTypeFromPath(string filePath)
	{
		string lower = filePath.ToLowerInvariant().Replace('\\', '/');
		if (lower.Contains("/items/") || lower.Contains("/attachments/") || lower.Contains("/weapons/") || lower.Contains("/projectiles/"))
		{
			return "Item";
		}
		if (lower.Contains("/units/") || lower.Contains("/characters/"))
		{
			return "Character";
		}
		if (lower.Contains("/buildings/"))
		{
			return "Building";
		}
		return "Prop";
	}

	public static ModelConversionResult ConvertToRmod(
		string inputPath,
		string? outputPath = null,
		string? assetType = null,
		bool force = false,
		OptimizationOptions? options = null,
		string? author = null)
	{
		string fullInput = Path.GetFullPath(inputPath);
		var result = new ModelConversionResult { InputPath = fullInput };

		if (!File.Exists(fullInput))
		{
			result.Success = false;
			result.ErrorMessage = $"Input model file not found: {inputPath}";
			return result;
		}

		string targetRmod = !string.IsNullOrEmpty(outputPath)
			? Path.GetFullPath(outputPath)
			: Path.ChangeExtension(fullInput, ".rmod");
		result.OutputPath = targetRmod;

		try
		{
			byte[] inputBytes = File.ReadAllBytes(fullInput);
			result.OriginalSize = inputBytes.Length;

			string fileName = Path.GetFileName(fullInput);
			var convRes = ConvertToRmod(inputBytes, fileName, assetType, force, options, author);
			if (!convRes.Success || convRes.OutputBytes == null)
			{
				result.Success = false;
				result.ErrorMessage = convRes.ErrorMessage;
				return result;
			}

			string? targetDir = Path.GetDirectoryName(targetRmod);
			if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
			{
				Directory.CreateDirectory(targetDir);
			}

			File.WriteAllBytes(targetRmod, convRes.OutputBytes);

			result.Success = true;
			result.OutputBytes = convRes.OutputBytes;
			result.OptimizedSize = convRes.OptimizedSize;
			result.SupportsTeamColor = convRes.SupportsTeamColor;
			result.AssetType = convRes.AssetType;
			result.Author = convRes.Author;
			result.PreferredFileName = convRes.PreferredFileName;
			return result;
		}
		catch (Exception ex)
		{
			result.Success = false;
			result.ErrorMessage = ex.Message;
			return result;
		}
	}

	public static ModelConversionResult ConvertToRmod(
		ReadOnlySpan<byte> inputBytes,
		string? inputFileName = null,
		string? assetType = null,
		bool force = false,
		OptimizationOptions? options = null,
		string? author = null)
	{
		var result = new ModelConversionResult
		{
			OriginalSize = inputBytes.Length
		};

		if (inputBytes.Length == 0)
		{
			result.Success = false;
			result.ErrorMessage = "Empty input bytes.";
			return result;
		}

		try
		{
			byte[] rawGlbBytes;
			string? existingMetaJson = null;

			if (RmodFile.IsRmodBytes(inputBytes))
			{
				var (parsedMeta, parsedGlb, _) = RmodFile.Parse(inputBytes);
				existingMetaJson = parsedMeta;
				rawGlbBytes = parsedGlb;
			}
			else
			{
				rawGlbBytes = inputBytes.ToArray();
				existingMetaJson = RealmMetadataHelper.ExtractMetadataFromGlbBytes(rawGlbBytes);
			}

			JsonObject metaObj;
			if (!string.IsNullOrWhiteSpace(existingMetaJson))
			{
				try
				{
					metaObj = JsonNode.Parse(existingMetaJson)?.AsObject() ?? new JsonObject();
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

			string? effectiveAssetType = assetType;
			if (string.IsNullOrEmpty(effectiveAssetType))
			{
				string? existingType = metaObj["asset_type"]?.ToString() ?? metaObj["default_asset_type"]?.ToString() ?? metaObj["type"]?.ToString();
				if (!string.IsNullOrEmpty(existingType) && RealmMetadataHelper.IsValidAssetTypeForExtension(".rmod", existingType, out string canonical, out _))
				{
					effectiveAssetType = canonical;
				}
				else if (!string.IsNullOrEmpty(inputFileName))
				{
					effectiveAssetType = InferAssetTypeFromPath(inputFileName);
				}
				else
				{
					effectiveAssetType = "Prop";
				}
			}

			var opt = options ?? GetAutomaticOptimizationOptions(effectiveAssetType, force);
			var optimizer = new GlbOptimizer();
			var optResult = optimizer.Optimize(rawGlbBytes, opt);

			byte[] finalGlbBytes = (optResult.Success && optResult.OutputGlbBytes != null)
				? optResult.OutputGlbBytes
				: rawGlbBytes;

			bool supportsTeamColor = GlbPlayerColorProcessor.DetectSupportsTeamColor(finalGlbBytes);

			if (!metaObj.ContainsKey("created_utc") || metaObj["created_utc"] == null)
			{
				metaObj["created_utc"] = DateTime.UtcNow.ToString("O");
			}

			metaObj["format"] = "rmod";
			metaObj["asset_type"] = effectiveAssetType;
			metaObj["supports_team_color"] = supportsTeamColor;

			if (!string.IsNullOrEmpty(inputFileName))
			{
				metaObj["preferred_file_name"] = Path.GetFileName(inputFileName);
			}

			if (!string.IsNullOrEmpty(author) && (!metaObj.ContainsKey("author") || string.IsNullOrWhiteSpace(metaObj["author"]?.ToString())))
			{
				metaObj["author"] = author;
			}

			string blake3Hash = RealmMetadataHelper.ComputeBlake3(finalGlbBytes, ".glb");
			metaObj["blake3"] = blake3Hash;

			byte[] rmodBytes = RmodFile.Build(metaObj.ToJsonString(), finalGlbBytes);

			result.Success = true;
			result.OutputBytes = rmodBytes;
			result.OptimizedSize = rmodBytes.Length;
			result.SupportsTeamColor = supportsTeamColor;
			result.AssetType = effectiveAssetType;
			result.Author = metaObj["author"]?.ToString();
			result.PreferredFileName = metaObj["preferred_file_name"]?.ToString();
			return result;
		}
		catch (Exception ex)
		{
			result.Success = false;
			result.ErrorMessage = ex.Message;
			return result;
		}
	}

	public static byte[]? ExtractGlbFromRmod(ReadOnlySpan<byte> rmodBytes)
	{
		return RmodFile.GetGlbBytes(rmodBytes);
	}

	public static ModelConversionResult ExtractGlbFromRmod(string inputRmodPath, string? outputGlbPath = null)
	{
		string fullInput = Path.GetFullPath(inputRmodPath);
		var result = new ModelConversionResult { InputPath = fullInput };

		if (!File.Exists(fullInput))
		{
			result.Success = false;
			result.ErrorMessage = $"Input RMOD file not found: {inputRmodPath}";
			return result;
		}

		string targetGlb = !string.IsNullOrEmpty(outputGlbPath)
			? Path.GetFullPath(outputGlbPath)
			: Path.ChangeExtension(fullInput, ".glb");
		result.OutputPath = targetGlb;

		try
		{
			byte[] rmodBytes = File.ReadAllBytes(fullInput);
			result.OriginalSize = rmodBytes.Length;

			byte[]? glbBytes = ExtractGlbFromRmod(rmodBytes);
			if (glbBytes == null || glbBytes.Length == 0)
			{
				result.Success = false;
				result.ErrorMessage = "Failed to extract GLB payload from RMOD file.";
				return result;
			}

			string? targetDir = Path.GetDirectoryName(targetGlb);
			if (!string.IsNullOrEmpty(targetDir) && !Directory.Exists(targetDir))
			{
				Directory.CreateDirectory(targetDir);
			}

			File.WriteAllBytes(targetGlb, glbBytes);

			result.Success = true;
			result.OutputBytes = glbBytes;
			result.OptimizedSize = glbBytes.Length;
			return result;
		}
		catch (Exception ex)
		{
			result.Success = false;
			result.ErrorMessage = ex.Message;
			return result;
		}
	}

	public static int ConvertModelDirectory(
		string inputDir,
		string? outputDir = null,
		string? assetType = null,
		bool recursive = false,
		bool force = false)
	{
		string fullInputDir = Path.GetFullPath(inputDir);
		string? fullOutputDir = !string.IsNullOrEmpty(outputDir) ? Path.GetFullPath(outputDir) : null;

		var searchOpt = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
		string[] files = Directory.GetFiles(fullInputDir, "*.*", searchOpt);
		int successCount = 0;
		int failCount = 0;

		foreach (var file in files)
		{
			if (!IsModelFile(file)) continue;

			string target;
			if (string.IsNullOrEmpty(fullOutputDir))
			{
				target = Path.ChangeExtension(file, ".rmod");
			}
			else
			{
				string rel = Path.GetRelativePath(fullInputDir, file);
				target = Path.Combine(fullOutputDir, Path.ChangeExtension(rel, ".rmod"));
			}

			var res = ConvertToRmod(file, target, assetType, force);
			if (res.Success)
			{
				Console.WriteLine($"Converted: {file} -> {target}");
				successCount++;
			}
			else
			{
				Console.Error.WriteLine($"Failed to convert {file}: {res.ErrorMessage}");
				failCount++;
			}
		}

		Console.WriteLine($"Finished model conversion. {successCount} succeeded, {failCount} failed.");
		return failCount > 0 ? 1 : 0;
	}
}
