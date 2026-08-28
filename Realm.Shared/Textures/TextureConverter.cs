using System;
using System.IO;

namespace Realm.Shared.Textures;

public class TextureConversionResult
{
	public bool Success { get; set; }
	public string InputPath { get; set; } = string.Empty;
	public string OutputPath { get; set; } = string.Empty;
	public string ErrorMessage { get; set; } = string.Empty;
}

public static class TextureConverter
{
	public static TextureConversionResult ConvertPngToKtx2(
		string inputPngPath,
		string outputKtx2Path,
		string format = "R8G8B8A8_UNORM",
		bool generateMipmaps = true)
	{
		var result = new TextureConversionResult
		{
			InputPath = inputPngPath,
			OutputPath = outputKtx2Path
		};

		if (!File.Exists(inputPngPath))
		{
			result.Success = false;
			result.ErrorMessage = $"Input file not found: {inputPngPath}";
			return result;
		}

		string? ktxTool = NativeToolRunner.FindKtxPath();
		if (string.IsNullOrEmpty(ktxTool))
		{
			result.Success = false;
			result.ErrorMessage = "ktx executable not found.";
			return result;
		}

		string? outDir = Path.GetDirectoryName(outputKtx2Path);
		if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
		{
			Directory.CreateDirectory(outDir);
		}

		string mipmapFlag = generateMipmaps ? "--generate-mipmap" : "";
		string args = $"create --format {format} --encode basis-lz {mipmapFlag} \"{inputPngPath}\" \"{outputKtx2Path}\"";

		var runResult = NativeToolRunner.RunTool(ktxTool, args);
		if (runResult.ExitCode == 0 && File.Exists(outputKtx2Path))
		{
			result.Success = true;
			return result;
		}

		result.Success = false;
		result.ErrorMessage = $"ktx create failed (exit code {runResult.ExitCode}): {runResult.Stderr}\n{runResult.Stdout}";
		return result;
	}

	public static TextureConversionResult ExtractPngFromKtx2(
		string inputKtx2Path,
		string outputPngPath,
		int layer = 0,
		int level = 0)
	{
		var result = new TextureConversionResult
		{
			InputPath = inputKtx2Path,
			OutputPath = outputPngPath
		};

		if (!File.Exists(inputKtx2Path))
		{
			result.Success = false;
			result.ErrorMessage = $"Input file not found: {inputKtx2Path}";
			return result;
		}

		string? ktxTool = NativeToolRunner.FindKtxPath();
		if (string.IsNullOrEmpty(ktxTool))
		{
			result.Success = false;
			result.ErrorMessage = "ktx executable not found.";
			return result;
		}

		string? outDir = Path.GetDirectoryName(outputPngPath);
		if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
		{
			Directory.CreateDirectory(outDir);
		}

		string args = $"extract --layer {layer} --level {level} --transcode rgba8 \"{inputKtx2Path}\" \"{outputPngPath}\"";

		var runResult = NativeToolRunner.RunTool(ktxTool, args);
		if (runResult.ExitCode == 0 && File.Exists(outputPngPath))
		{
			result.Success = true;
			return result;
		}

		result.Success = false;
		result.ErrorMessage = $"ktx extract failed (exit code {runResult.ExitCode}): {runResult.Stderr}\n{runResult.Stdout}";
		return result;
	}

	public static TextureConversionResult ConvertTextureFile(
		string inputPath,
		string? outputPath,
		string mode = "auto")
	{
		string ext = Path.GetExtension(inputPath).ToLowerInvariant();
		bool isExtract = mode.Equals("extract", StringComparison.OrdinalIgnoreCase) ||
						 mode.Equals("to_png", StringComparison.OrdinalIgnoreCase) ||
						 (mode.Equals("auto", StringComparison.OrdinalIgnoreCase) && ext == ".ktx2");

		if (isExtract)
		{
			string targetPng = string.IsNullOrEmpty(outputPath)
				? Path.ChangeExtension(inputPath, ".png")
				: outputPath;
			return ExtractPngFromKtx2(inputPath, targetPng);
		}
		else
		{
			string targetKtx2 = string.IsNullOrEmpty(outputPath)
				? Path.ChangeExtension(inputPath, ".ktx2")
				: outputPath;
			return ConvertPngToKtx2(inputPath, targetKtx2);
		}
	}

	public static int ConvertTextureDirectory(
		string inputDir,
		string? outputDir,
		string mode,
		bool recursive)
	{
		var searchOpt = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
		string searchPattern = mode.Equals("extract", StringComparison.OrdinalIgnoreCase) || mode.Equals("to_png", StringComparison.OrdinalIgnoreCase)
			? "*.ktx2"
			: (mode.Equals("encode", StringComparison.OrdinalIgnoreCase) || mode.Equals("to_ktx2", StringComparison.OrdinalIgnoreCase) ? "*.png" : "*.*");

		string[] files = Directory.GetFiles(inputDir, searchPattern, searchOpt);
		int successCount = 0;
		int failCount = 0;

		foreach (var file in files)
		{
			string fileExt = Path.GetExtension(file).ToLowerInvariant();
			if (fileExt != ".png" && fileExt != ".ktx2") continue;

			string targetExt = fileExt == ".png" ? ".ktx2" : ".png";
			string target;
			if (string.IsNullOrEmpty(outputDir))
			{
				target = Path.ChangeExtension(file, targetExt);
			}
			else
			{
				string rel = Path.GetRelativePath(inputDir, file);
				target = Path.Combine(outputDir, Path.ChangeExtension(rel, targetExt));
			}

			var res = ConvertTextureFile(file, target, mode);
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

		Console.WriteLine($"Finished texture conversion. {successCount} succeeded, {failCount} failed.");
		return failCount > 0 ? 1 : 0;
	}
}
