using System;
using System.IO;
using CommandLine;
using Realm.Shared;
using Realm.Shared.Animation;
using Realm.Shared.Metadata;
using Realm.Shared.Textures;

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

[Verb("texture_convert", HelpText = "Convert textures between PNG (albedo) and KTX2 format.")]
public class TextureConvertOptions
{
	[Option('i', "input", Required = true, HelpText = "Path to input .png / .ktx2 file or directory.")]
	public string Input { get; set; } = string.Empty;

	[Option('o', "output", Required = false, HelpText = "Output destination file or directory.")]
	public string? Output { get; set; }

	[Option('m', "mode", Required = false, Default = "auto", HelpText = "Conversion mode: auto (default), to_ktx2, to_png, extract, encode.")]
	public string Mode { get; set; } = "auto";

	[Option("format", Required = false, Default = "R8G8B8A8_UNORM", HelpText = "KTX2 compression format (default: R8G8B8A8_UNORM).")]
	public string Format { get; set; } = "R8G8B8A8_UNORM";

	[Option("in-place", Required = false, Default = false, HelpText = "Write output alongside input file.")]
	public bool InPlace { get; set; }

	[Option('r', "recursive", Required = false, Default = false, HelpText = "Process directories recursively.")]
	public bool Recursive { get; set; }

	[Option("no-mipmaps", Required = false, Default = false, HelpText = "Disable mipmap generation.")]
	public bool NoMipmaps { get; set; }
}

[Verb("fbx_to_ranim", HelpText = "Convert Mixamo FBX skeletal animation files to .ranim format.")]
public class FbxToRanimOptions
{
	[Option('i', "input", Required = true, HelpText = "Path to input .fbx file or directory containing .fbx files.")]
	public string Input { get; set; } = string.Empty;

	[Option('o', "output", Required = false, HelpText = "Output destination .ranim file or directory.")]
	public string? Output { get; set; }

	[Option('r', "recursive", Required = false, Default = false, HelpText = "Process directories recursively.")]
	public bool Recursive { get; set; }
}

[Verb("metadata_read", HelpText = "Extract and display embedded metadata from .glb, .png, .ktx2, .ranim, or .ogg files.")]
public class MetadataReadOptions
{
	[Option('i', "input", Required = true, HelpText = "Path to asset file or directory containing asset files.")]
	public string Input { get; set; } = string.Empty;

	[Option('o', "output", Required = false, HelpText = "Output destination file to write extracted JSON.")]
	public string? Output { get; set; }

	[Option('r', "recursive", Required = false, Default = false, HelpText = "Process directories recursively.")]
	public bool Recursive { get; set; }
}

[Verb("metadata_add", HelpText = "Embed metadata JSON into .glb, .png, .ktx2, .ranim, or .ogg files.")]
public class MetadataAddOptions
{
	[Option('i', "input", Required = true, HelpText = "Path to asset file or directory containing asset files.")]
	public string Input { get; set; } = string.Empty;

	[Option('d', "data", Required = true, HelpText = "JSON string or path to JSON file containing metadata to embed.")]
	public string Data { get; set; } = string.Empty;

	[Option('r', "recursive", Required = false, Default = false, HelpText = "Process directories recursively.")]
	public bool Recursive { get; set; }
}

[Verb("metadata_remove", HelpText = "Remove embedded metadata from .glb, .png, .ktx2, .ranim, or .ogg files.")]
public class MetadataRemoveOptions
{
	[Option('i', "input", Required = true, HelpText = "Path to asset file or directory containing asset files.")]
	public string Input { get; set; } = string.Empty;

	[Option('r', "recursive", Required = false, Default = false, HelpText = "Process directories recursively.")]
	public bool Recursive { get; set; }
}

public static class Program
{
	public static int Main(string[] args)
	{
		return Parser.Default.ParseArguments<GlbOptimizeOptions, TextureConvertOptions, FbxToRanimOptions, MetadataReadOptions, MetadataAddOptions, MetadataRemoveOptions>(args)
			.MapResult(
				(GlbOptimizeOptions options) => ExecuteGlbOptimize(options),
				(TextureConvertOptions options) => ExecuteTextureConvert(options),
				(FbxToRanimOptions options) => ExecuteFbxToRanim(options),
				(MetadataReadOptions options) => ExecuteMetadataRead(options),
				(MetadataAddOptions options) => ExecuteMetadataAdd(options),
				(MetadataRemoveOptions options) => ExecuteMetadataRemove(options),
				errors => 1);
	}

	private static int ExecuteMetadataRead(MetadataReadOptions options)
	{
		if (File.Exists(options.Input))
		{
			string? meta = RealmMetadataHelper.ExtractMetadata(options.Input);
			if (meta == null)
			{
				Console.WriteLine($"No embedded Realm metadata found in: {options.Input}");
				return 0;
			}

			Console.WriteLine($"Metadata for {options.Input}:");
			Console.WriteLine(meta);

			if (!string.IsNullOrEmpty(options.Output))
			{
				string? dir = Path.GetDirectoryName(options.Output);
				if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
				File.WriteAllText(options.Output, meta);
				Console.WriteLine($"Saved metadata to: {options.Output}");
			}
			return 0;
		}
		else if (Directory.Exists(options.Input))
		{
			var searchOpt = options.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
			string[] files = Directory.GetFiles(options.Input, "*.*", searchOpt);
			int foundCount = 0;
			foreach (var file in files)
			{
				string? meta = RealmMetadataHelper.ExtractMetadata(file);
				if (meta != null)
				{
					Console.WriteLine($"--- {file} ---");
					Console.WriteLine(meta);
					foundCount++;
				}
			}
			Console.WriteLine($"Extracted metadata from {foundCount} file(s).");
			return 0;
		}
		else
		{
			Console.Error.WriteLine($"Error: Input path does not exist: {options.Input}");
			return 1;
		}
	}

	private static int ExecuteMetadataAdd(MetadataAddOptions options)
	{
		string jsonContent = options.Data;
		if (File.Exists(options.Data))
		{
			jsonContent = File.ReadAllText(options.Data);
		}

		if (File.Exists(options.Input))
		{
			bool success = RealmMetadataHelper.AddMetadata(options.Input, jsonContent);
			if (success)
			{
				Console.WriteLine($"Successfully added metadata to: {options.Input}");
				return 0;
			}
			else
			{
				Console.Error.WriteLine($"Failed to add metadata to: {options.Input}");
				return 1;
			}
		}
		else if (Directory.Exists(options.Input))
		{
			var searchOpt = options.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
			string[] files = Directory.GetFiles(options.Input, "*.*", searchOpt);
			int successCount = 0;
			int failCount = 0;

			foreach (var file in files)
			{
				string ext = Path.GetExtension(file).ToLowerInvariant();
				if (ext is not (".glb" or ".png" or ".ktx2" or ".ktx" or ".ranim" or ".ogg")) continue;

				if (RealmMetadataHelper.AddMetadata(file, jsonContent))
				{
					Console.WriteLine($"Added metadata to: {file}");
					successCount++;
				}
				else
				{
					Console.Error.WriteLine($"Failed to add metadata to: {file}");
					failCount++;
				}
			}

			Console.WriteLine($"Finished adding metadata. {successCount} succeeded, {failCount} failed.");
			return failCount > 0 ? 1 : 0;
		}
		else
		{
			Console.Error.WriteLine($"Error: Input path does not exist: {options.Input}");
			return 1;
		}
	}

	private static int ExecuteMetadataRemove(MetadataRemoveOptions options)
	{
		if (File.Exists(options.Input))
		{
			bool success = RealmMetadataHelper.RemoveMetadata(options.Input);
			if (success)
			{
				Console.WriteLine($"Successfully removed metadata from: {options.Input}");
				return 0;
			}
			else
			{
				Console.Error.WriteLine($"Failed to remove metadata from: {options.Input}");
				return 1;
			}
		}
		else if (Directory.Exists(options.Input))
		{
			var searchOpt = options.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
			string[] files = Directory.GetFiles(options.Input, "*.*", searchOpt);
			int successCount = 0;
			int failCount = 0;

			foreach (var file in files)
			{
				string ext = Path.GetExtension(file).ToLowerInvariant();
				if (ext is not (".glb" or ".png" or ".ktx2" or ".ktx" or ".ranim" or ".ogg")) continue;

				if (RealmMetadataHelper.RemoveMetadata(file))
				{
					Console.WriteLine($"Removed metadata from: {file}");
					successCount++;
				}
				else
				{
					Console.Error.WriteLine($"Failed to remove metadata from: {file}");
					failCount++;
				}
			}

			Console.WriteLine($"Finished removing metadata. {successCount} succeeded, {failCount} failed.");
			return failCount > 0 ? 1 : 0;
		}
		else
		{
			Console.Error.WriteLine($"Error: Input path does not exist: {options.Input}");
			return 1;
		}
	}

	private static int ExecuteTextureConvert(TextureConvertOptions options)
	{
		if (File.Exists(options.Input))
		{
			string target = options.InPlace || string.IsNullOrEmpty(options.Output)
				? Path.ChangeExtension(options.Input, options.Input.EndsWith(".ktx2", StringComparison.OrdinalIgnoreCase) ? ".png" : ".ktx2")
				: options.Output;

			var res = TextureConverter.ConvertTextureFile(options.Input, target, options.Mode);
			if (res.Success)
			{
				Console.WriteLine($"Successfully converted: {options.Input} -> {target}");
				return 0;
			}
			else
			{
				Console.Error.WriteLine($"Failed to convert {options.Input}: {res.ErrorMessage}");
				return 1;
			}
		}
		else if (Directory.Exists(options.Input))
		{
			return TextureConverter.ConvertTextureDirectory(options.Input, options.Output, options.Mode, options.Recursive);
		}
		else
		{
			Console.Error.WriteLine($"Error: Input path does not exist: {options.Input}");
			return 1;
		}
	}

	private static int ExecuteFbxToRanim(FbxToRanimOptions options)
	{
		if (File.Exists(options.Input))
		{
			string target = string.IsNullOrEmpty(options.Output)
				? Path.ChangeExtension(options.Input, ".ranim")
				: options.Output;

			var res = MixamoFbxConverter.ConvertFbxFile(options.Input, target);
			if (res.Success)
			{
				Console.WriteLine($"Successfully converted: {options.Input} -> {res.OutputPath} ({string.Join(", ", res.ConvertedAnimationNames)})");
				return 0;
			}
			else
			{
				Console.Error.WriteLine($"Failed to convert {options.Input}: {res.ErrorMessage}");
				return 1;
			}
		}
		else if (Directory.Exists(options.Input))
		{
			return MixamoFbxConverter.ConvertFbxDirectory(options.Input, options.Output, options.Recursive);
		}
		else
		{
			Console.Error.WriteLine($"Error: Input path does not exist: {options.Input}");
			return 1;
		}
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
