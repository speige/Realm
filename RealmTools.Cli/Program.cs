using System;
using System.IO;
using Realm.AssetPipeline;

namespace RealmTools.Cli;

public static class Program
{
	public static int Main(string[] args)
	{
		string? inputPath = null;
		string? outputPath = null;
		bool inPlace = false;
		string mode = "optimize";
		float qualityRatio = 0.5f;
		int maxRes = 1024;
		bool recursive = false;
		bool force = false;

		for (int i = 0; i < args.Length; i++)
		{
			string arg = args[i];
			if (arg is "-i" or "--input" && i + 1 < args.Length)
			{
				inputPath = args[++i];
			}
			else if (arg is "-o" or "--output" && i + 1 < args.Length)
			{
				outputPath = args[++i];
			}
			else if (arg is "--in-place")
			{
				inPlace = true;
			}
			else if (arg is "-m" or "--mode" && i + 1 < args.Length)
			{
				mode = args[++i].ToLowerInvariant();
			}
			else if (arg is "-q" or "--quality" or "--lod-ratio" && i + 1 < args.Length)
			{
				if (float.TryParse(args[++i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float q))
				{
					qualityRatio = q;
				}
			}
			else if (arg is "--max-res" && i + 1 < args.Length)
			{
				if (int.TryParse(args[++i], out int res))
				{
					maxRes = res;
				}
			}
			else if (arg is "-r" or "--recursive")
			{
				recursive = true;
			}
			else if (arg is "-f" or "--force")
			{
				force = true;
			}
			else if (arg is "-h" or "--help")
			{
				PrintHelp();
				return 0;
			}
		}

		if (string.IsNullOrEmpty(inputPath))
		{
			Console.Error.WriteLine("Error: Input path is required (--input / -i).");
			PrintHelp();
			return 1;
		}

		var optimizer = new GlbOptimizer();
		var options = new OptimizationOptions
		{
			SimplificationRatio = qualityRatio,
			MaxTextureResolution = maxRes,
			ForceReDecimate = force
		};

		if (File.Exists(inputPath))
		{
			return ProcessSingleFile(optimizer, inputPath, outputPath, inPlace, mode, options);
		}
		else if (Directory.Exists(inputPath))
		{
			return ProcessDirectory(optimizer, inputPath, outputPath, inPlace, mode, recursive, options);
		}
		else
		{
			Console.Error.WriteLine($"Error: Input path does not exist: {inputPath}");
			return 1;
		}
	}

	private static int ProcessSingleFile(
		GlbOptimizer optimizer,
		string filePath,
		string? outputPath,
		bool inPlace,
		string mode,
		OptimizationOptions options)
	{
		string target = inPlace || string.IsNullOrEmpty(outputPath) ? filePath : outputPath;

		if (mode is "info")
		{
			byte[] bytes = File.ReadAllBytes(filePath);
			var meta = optimizer.GetMetadata(bytes);
			Console.WriteLine($"File: {filePath}");
			Console.WriteLine($"  Optimized: {meta.IsOptimized}");
			Console.WriteLine($"  Realm Version: {meta.RealmVersion ?? "N/A"}");
			Console.WriteLine($"  Meshes: {meta.MeshCount}, Nodes: {meta.NodeCount}, Materials: {meta.MaterialCount}, Images: {meta.ImageCount}");
			return 0;
		}
		else if (mode is "unoptimize" or "revert")
		{
			Console.WriteLine($"Unoptimizing: {filePath} -> {target}");
			var unopt = optimizer.UnoptimizeFile(filePath, target);
			if (unopt.Success)
			{
				Console.WriteLine($"Successfully unoptimized: {target} (WasOptimized: {unopt.WasOptimized})");
				return 0;
			}
			else
			{
				Console.Error.WriteLine($"Failed to unoptimize {filePath}: {unopt.ErrorMessage}");
				return 1;
			}
		}
		else
		{
			Console.WriteLine($"Optimizing: {filePath} -> {target}");
			var result = optimizer.OptimizeFile(filePath, target, options);
			if (result.Success)
			{
				if (result.DecimationSkipped)
				{
					Console.WriteLine($"Skipped (already optimized): {filePath}");
				}
				else
				{
					Console.WriteLine($"Successfully optimized: {target} ({result.OriginalSize} -> {result.OptimizedSize} bytes)");
				}
				return 0;
			}
			else
			{
				Console.Error.WriteLine($"Failed to optimize {filePath}: {result.ErrorMessage}");
				return 1;
			}
		}
	}

	private static int ProcessDirectory(
		GlbOptimizer optimizer,
		string dirPath,
		string? outputDir,
		bool inPlace,
		string mode,
		bool recursive,
		OptimizationOptions options)
	{
		var searchOpt = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
		string[] files = Directory.GetFiles(dirPath, "*.glb", searchOpt);

		Console.WriteLine($"Found {files.Length} .glb file(s) in {dirPath}");
		int successCount = 0;
		int failCount = 0;

		foreach (var file in files)
		{
			string target;
			if (inPlace || string.IsNullOrEmpty(outputDir))
			{
				target = file;
			}
			else
			{
				string rel = Path.GetRelativePath(dirPath, file);
				target = Path.Combine(outputDir, rel);
			}

			int res = ProcessSingleFile(optimizer, file, target, false, mode, options);
			if (res == 0) successCount++;
			else failCount++;
		}

		Console.WriteLine($"Finished. Processed: {successCount} succeeded, {failCount} failed.");
		return failCount > 0 ? 1 : 0;
	}

	private static void PrintHelp()
	{
		Console.WriteLine("RealmTools CLI");
		Console.WriteLine("Usage: realm-tools [options]");
		Console.WriteLine("Options:");
		Console.WriteLine("  -i, --input <path>         Path to .glb file or folder containing assets (required)");
		Console.WriteLine("  -o, --output <path>        Output destination file or directory");
		Console.WriteLine("  --in-place                 Modify files in-place");
		Console.WriteLine("  -m, --mode <mode>          Operation mode: optimize (default), unoptimize/revert, info");
		Console.WriteLine("  -q, --quality <ratio>      Simplification ratio (default 0.5)");
		Console.WriteLine("  --max-res <pixels>         Max texture resolution (default 1024)");
		Console.WriteLine("  -r, --recursive            Process directories recursively");
		Console.WriteLine("  -f, --force                Force re-optimization even if already optimized");
		Console.WriteLine("  -h, --help                 Show help message");
	}
}
