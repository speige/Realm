using System;
using System.IO;
using CommandLine;
using Realm.AssetPipeline;

namespace Realm.Tools.Cli;

[Verb("glb_optimize", HelpText = "Optimize glTF/GLB 3D model files with Draco compression, mesh decimation, and texture downsampling.")]
public class GlbOptimizeOptions
{
	[Option('i', "input", Required = true, HelpText = "Path to .glb file or directory containing assets.")]
	public string Input { get; set; } = string.Empty;

	[Option('o', "output", Required = false, HelpText = "Output destination file or directory.")]
	public string? Output { get; set; }

	[Option("in-place", Required = false, Default = false, HelpText = "Modify files in-place.")]
	public bool InPlace { get; set; }

	[Option('m', "mode", Required = false, Default = "optimize", HelpText = "Operation mode: optimize (default), unoptimize, revert, info.")]
	public string Mode { get; set; } = "optimize";

	[Option('q', "quality", Required = false, Default = 0.5f, HelpText = "Mesh simplification ratio (default 0.5).")]
	public float Quality { get; set; } = 0.5f;

	[Option("max-res", Required = false, Default = 1024, HelpText = "Maximum texture resolution (default 1024).")]
	public int MaxResolution { get; set; } = 1024;

	[Option('r', "recursive", Required = false, Default = false, HelpText = "Process directories recursively.")]
	public bool Recursive { get; set; }

	[Option('f', "force", Required = false, Default = false, HelpText = "Force re-optimization even if already optimized.")]
	public bool Force { get; set; }
}

public static class Program
{
	public static int Main(string[] args)
	{
		return Parser.Default.ParseArguments(args, typeof(GlbOptimizeOptions))
			.MapResult(
				(GlbOptimizeOptions options) => ExecuteGlbOptimize(options),
				errors => 1);
	}

	private static int ExecuteGlbOptimize(GlbOptimizeOptions options)
	{
		var optimizer = new GlbOptimizer();
		var optimizationOptions = new OptimizationOptions
		{
			SimplificationRatio = options.Quality,
			MaxTextureResolution = options.MaxResolution,
			ForceReDecimate = options.Force
		};

		if (File.Exists(options.Input))
		{
			return ProcessSingleFile(optimizer, options.Input, options.Output, options.InPlace, options.Mode, optimizationOptions);
		}
		else if (Directory.Exists(options.Input))
		{
			return ProcessDirectory(optimizer, options.Input, options.Output, options.InPlace, options.Mode, options.Recursive, optimizationOptions);
		}
		else
		{
			Console.Error.WriteLine($"Error: Input path does not exist: {options.Input}");
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

		if (mode.Equals("info", StringComparison.OrdinalIgnoreCase))
		{
			byte[] bytes = File.ReadAllBytes(filePath);
			var meta = optimizer.GetMetadata(bytes);
			Console.WriteLine($"File: {filePath}");
			Console.WriteLine($"  Optimized: {meta.IsOptimized}");
			Console.WriteLine($"  Realm Version: {meta.RealmVersion ?? "N/A"}");
			Console.WriteLine($"  Meshes: {meta.MeshCount}, Nodes: {meta.NodeCount}, Materials: {meta.MaterialCount}, Images: {meta.ImageCount}");
			return 0;
		}
		else if (mode.Equals("unoptimize", StringComparison.OrdinalIgnoreCase) || mode.Equals("revert", StringComparison.OrdinalIgnoreCase))
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
}
