using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using CommandLine;
using Realm.Shared;
using Realm.Shared.Animation;
using Realm.Shared.Audio;
using Realm.Shared.Metadata;
using Realm.Shared.ModelOptimization;
using Realm.Shared.Textures;

namespace Realm.Tools.Cli;

[Verb("mesh_convert", HelpText = "Convert and optimize 3D models (GLB, RMESH, OBJ, FBX) into Realm .rmesh format (or export .rmesh to .glb).")]
public class MeshConvertOptions
{
	[Option('i', "input", Required = true, HelpText = "Path to 3D model file (.glb, .rmesh, .obj, .fbx) or directory containing assets.")]
	public string Input { get; set; } = string.Empty;

	[Option('o', "output", Required = false, HelpText = "Output destination file or directory.")]
	public string? Output { get; set; }

	[Option("in-place", Required = false, Default = false, HelpText = "Modify files in-place.")]
	public bool InPlace { get; set; }

	[Option('r', "recursive", Required = false, Default = false, HelpText = "Process directories recursively.")]
	public bool Recursive { get; set; }

	[Option('f', "force", Required = false, Default = false, HelpText = "Force re-optimization even if already optimized.")]
	public bool Force { get; set; }

	[Option('t', "type", Required = false, HelpText = "Asset type for model: Character, Building, Prop, Item.")]
	public string? AssetType { get; set; }
}

[Verb("texture_convert", HelpText = "Convert textures between standard image formats and .rtex format.")]
public class TextureConvertOptions
{
	[Option('i', "input", Required = true, HelpText = "Path to input image / .rtex file or directory.")]
	public string Input { get; set; } = string.Empty;

	[Option('o', "output", Required = false, HelpText = "Output destination file or directory.")]
	public string? Output { get; set; }

	[Option('t', "type", Required = false, HelpText = "Asset type for textures: Decal, Icon, Noise, Ribbon, Skybox, Spritesheet, Terrain, vfx_radial, vfx_vertical. If omitted, attempts to read type from image metadata.")]
	public string? AssetType { get; set; }

	[Option("columns", Required = false, HelpText = "Number of grid columns for spritesheets or animated decals (default 4 for spritesheets, 1 for decals).")]
	public int? Columns { get; set; }

	[Option("rows", Required = false, HelpText = "Number of grid rows for spritesheets or animated decals (default 4 for spritesheets, 1 for decals).")]
	public int? Rows { get; set; }

	[Option("in-place", Required = false, Default = false, HelpText = "Write output alongside input file.")]
	public bool InPlace { get; set; }

	[Option('r', "recursive", Required = false, Default = false, HelpText = "Process directories recursively.")]
	public bool Recursive { get; set; }
}

[Verb("audio_convert", HelpText = "Convert audio files (mp3, wav, flac, aac, ogg) to .raud format (or extract .raud to .ogg).")]
public class AudioConvertOptions
{
	[Option('i', "input", Required = true, HelpText = "Path to input audio file or directory containing audio files.")]
	public string Input { get; set; } = string.Empty;

	[Option('o', "output", Required = false, HelpText = "Output destination file or directory.")]
	public string? Output { get; set; }

	[Option('t', "type", Required = false, HelpText = "Asset type for audio: Music, SoundEffect.")]
	public string? AssetType { get; set; }

	[Option('r', "recursive", Required = false, Default = false, HelpText = "Process directories recursively.")]
	public bool Recursive { get; set; }
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

[Verb("ranim_render", HelpText = "Render .ranim skeletal animation files to animated GIF, PNG spritesheet, or high-quality WebP spritesheet.")]
public class RanimRenderOptions
{
	[Option('i', "input", Required = true, HelpText = "Path to input .ranim file or directory.")]
	public string Input { get; set; } = string.Empty;

	[Option('o', "output", Required = false, HelpText = "Output destination file or directory.")]
	public string? Output { get; set; }

	[Option('f', "format", Required = false, Default = "auto", HelpText = "Output format: auto (default), gif, spritesheet, webp.")]
	public string Format { get; set; } = "auto";

	[Option("fps", Required = false, Default = 12.0f, HelpText = "Target frames per second (default 12).")]
	public float Fps { get; set; } = 12.0f;

	[Option("max-frames", Required = false, HelpText = "Maximum frame count (uses modulus to skip intermediate frames).")]
	public int? MaxFrames { get; set; }

	[Option("size", Required = false, Default = 128, HelpText = "Frame width and height in pixels (default 128).")]
	public int Size { get; set; } = 128;

	[Option("scale", Required = false, Default = 1.0f, HelpText = "Model scale factor (default 1.0).")]
	public float Scale { get; set; } = 1.0f;

	[Option('m', "model", Required = false, HelpText = "Optional path to rigged humanoid .rmesh or .glb model to render instead of skeleton.")]
	public string? Model { get; set; }

	[Option('r', "recursive", Required = false, Default = false, HelpText = "Process directories recursively.")]
	public bool Recursive { get; set; }

	[Option("no-border", Required = false, Default = false, HelpText = "Disable frame border.")]
	public bool NoBorder { get; set; }

	[Option("no-shadow", Required = false, Default = false, HelpText = "Disable floor shadow.")]
	public bool NoShadow { get; set; }

	[Option('q', "quality", Required = false, Default = 95, HelpText = "Encoding quality for WebP output (1-100, default 95).")]
	public int Quality { get; set; } = 95;

	[Option("lossless", Required = false, Default = false, HelpText = "Use lossless compression for WebP spritesheet output.")]
	public bool Lossless { get; set; }
}

[Verb("metadata", HelpText = "Manage embedded Realm metadata (read, add, remove) in .rmesh, .raud, .rtex, .ranim, .glb, or .ogg files.")]
public class MetadataOptions
{
	[Option('m', "mode", Required = false, Default = "read", HelpText = "Operation mode: read (default), add, update, remove.")]
	public string Mode { get; set; } = "read";

	[Option('i', "input", Required = true, HelpText = "Path to asset file or directory containing asset files.")]
	public string Input { get; set; } = string.Empty;

	[Option('d', "data", Required = false, HelpText = "JSON string or path to JSON file containing metadata to embed (for add/update mode).")]
	public string? Data { get; set; }

	[Option('t', "type", Required = false, HelpText = "Asset type to embed: Character, Building, Prop, Item, Decal, Icon, Noise, Ribbon, Skybox, Spritesheet, Terrain, vfx_radial, vfx_vertical, Animation, Music, SoundEffect.")]
	public string? AssetType { get; set; }

	[Option('o', "output", Required = false, HelpText = "Output destination file to write extracted JSON (for read mode).")]
	public string? Output { get; set; }

	[Option('r', "recursive", Required = false, Default = false, HelpText = "Process directories recursively.")]
	public bool Recursive { get; set; }
}

[Verb("blake3", HelpText = "Calculate canonical BLAKE3 hash of an asset file or directory (with Realm metadata stripped ephemerally in RAM).")]
public class Blake3Options
{
	[Option('i', "input", Required = true, HelpText = "Path to asset file or directory containing asset files.")]
	public string Input { get; set; } = string.Empty;

	[Option('r', "recursive", Required = false, Default = false, HelpText = "Process directories recursively.")]
	public bool Recursive { get; set; }

	[Option("raw", Required = false, Default = false, HelpText = "Calculate raw BLAKE3 hash without stripping metadata.")]
	public bool Raw { get; set; }
}

[Verb("mesh_player_color", HelpText = "Extract #FF00FF prompt artifacts from a 3D model, isolate player-color area via face topology, desaturate albedo, pack mask into Red channel of ORM texture, and re-optimize into .rmesh.")]
public class MeshPlayerColorCliOptions
{
	[Option('i', "input", Required = true, HelpText = "Path to .rmesh or .glb file or directory containing model files.")]
	public string Input { get; set; } = string.Empty;

	[Option('o', "output", Required = false, HelpText = "Output destination file or directory.")]
	public string? Output { get; set; }

	[Option("in-place", Required = false, Default = false, HelpText = "Overwrite the source file directly.")]
	public bool InPlace { get; set; }

	[Option('r', "recursive", Required = false, Default = false, HelpText = "Process directories recursively.")]
	public bool Recursive { get; set; }

	[Option('t', "type", Required = false, HelpText = "Asset type for model: Character, Building, Prop, Item.")]
	public string? AssetType { get; set; }

	[Option("target-hex", Required = false, Default = "#FF00FF", HelpText = "Target prompt color in hex (default: #FF00FF).")]
	public string TargetHex { get; set; } = "#FF00FF";

	[Option("core-threshold", Required = false, Default = 0.88f, HelpText = "Chromaticity dot-product threshold for high-confidence core texels (default: 0.88).")]
	public float CoreThreshold { get; set; } = 0.88f;

	[Option("fringe-threshold", Required = false, Default = 0.80f, HelpText = "Chromaticity dot-product threshold for fringe/edge expansion (default: 0.80).")]
	public float FringeThreshold { get; set; } = 0.80f;

	[Option("min-cluster-faces", Required = false, Default = 10, HelpText = "Minimum connected 3D face count to keep a cluster (default: 10).")]
	public int MinClusterFaces { get; set; } = 10;

	[Option("dilation-radius", Required = false, Default = 3, HelpText = "UV gutter dilation radius in pixels (default: 3).")]
	public int DilationRadius { get; set; } = 3;
}

[Verb("rig_humanoid", HelpText = "Auto-rig a humanoid 3D model with a Mixamo skeleton using the Make-It-Animatable pipeline and optimize into .rmesh.")]
public class RigHumanoidOptions
{
	[Option('i', "input", Required = true, HelpText = "Path to input .rmesh or .glb file or directory containing model files.")]
	public string Input { get; set; } = string.Empty;

	[Option('o', "output", Required = false, HelpText = "Output destination file or directory.")]
	public string? Output { get; set; }

	[Option("in-place", Required = false, Default = false, HelpText = "Overwrite the source file directly.")]
	public bool InPlace { get; set; }

	[Option('r', "recursive", Required = false, Default = false, HelpText = "Process directories recursively.")]
	public bool Recursive { get; set; }

	[Option('t', "type", Required = false, HelpText = "Asset type for model: Character, Building, Prop, Item.")]
	public string? AssetType { get; set; }

	[Option("no-fingers", Required = false, Default = true, HelpText = "Model does not have ten separate fingers (default: true).")]
	public bool NoFingers { get; set; } = true;

	[Option("use-normals", Required = false, Default = false, HelpText = "Use normals to improve skinning when limbs are close together (default: false).")]
	public bool UseNormals { get; set; } = false;

	[Option("weight-postprocess", Required = false, Default = true, HelpText = "Apply empirical post-processing to blend weights (default: true).")]
	public bool WeightPostprocess { get; set; } = true;

	[Option("mia-dir", Required = false, HelpText = "Path to the ComfyUI_Make-It-Animatable directory. Falls back to MIA_DIR env var, then searches common relative paths.")]
	public string? MiaDir { get; set; }
}

public static class Program
{
	public static int Main(string[] args)
	{
		if (args.Any(argument => string.Equals(argument, "--eula-accept", StringComparison.OrdinalIgnoreCase)))
		{
			_assetAgreementAccepted = true;
			args = args.Where(argument => !string.Equals(argument, "--eula-accept", StringComparison.OrdinalIgnoreCase)).ToArray();
		}

		return Parser.Default.ParseArguments<MeshConvertOptions, TextureConvertOptions, AudioConvertOptions, FbxToRanimOptions, RanimRenderOptions, MetadataOptions, Blake3Options, MeshPlayerColorCliOptions, RigHumanoidOptions>(args)
			.MapResult(
				(MeshConvertOptions options) => ExecuteMeshConvert(options),
				(TextureConvertOptions options) => ExecuteTextureConvert(options),
				(AudioConvertOptions options) => ExecuteAudioConvert(options),
				(FbxToRanimOptions options) => ExecuteFbxToRanim(options),
				(RanimRenderOptions options) => ExecuteRanimRender(options),
				(MetadataOptions options) => ExecuteMetadata(options),
				(Blake3Options options) => ExecuteBlake3(options),
				(MeshPlayerColorCliOptions options) => ExecuteMeshPlayerColor(options),
				(RigHumanoidOptions options) => ExecuteRigHumanoid(options),
				errors => 1);
	}

	private static bool _assetAgreementAccepted = false;

	private static void EnsureAssetAgreementAccepted()
	{
		if (_assetAgreementAccepted) return;

		Console.WriteLine("The Realm Asset Agreement states that files cannot be used outside the Realm UGC platform unless you are the original author of the asset. Do you understand? Y/N");
		string? response = Console.ReadLine()?.Trim();
		if (string.Equals(response, "Y", StringComparison.OrdinalIgnoreCase))
		{
			_assetAgreementAccepted = true;
		}
		else
		{
			Console.WriteLine("Task cancelled.");
			Environment.Exit(0);
		}
	}

	private static bool EnsurePathExists(string inputPath)
	{
		if (File.Exists(inputPath) || Directory.Exists(inputPath))
		{
			return true;
		}

		Console.Error.WriteLine($"Error: Input path does not exist: {inputPath}");
		return false;
	}

	private static IEnumerable<string> TraverseFiles(string inputPath, bool recursive, Func<string, bool>? filter = null)
	{
		if (File.Exists(inputPath))
		{
			if (filter == null || filter(inputPath))
			{
				yield return inputPath;
			}
		}
		else if (Directory.Exists(inputPath))
		{
			var searchOption = recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
			foreach (string file in Directory.EnumerateFiles(inputPath, "*.*", searchOption))
			{
				if (filter == null || filter(file))
				{
					yield return file;
				}
			}
		}
	}

	private static string ResolveTargetOutputPath(
		string inputRoot,
		string currentFile,
		string? outputDestination,
		bool inPlace,
		Func<string, string>? extensionResolver = null)
	{
		string targetExtension = extensionResolver != null
			? extensionResolver(currentFile)
			: Path.GetExtension(currentFile);

		if (inPlace)
		{
			return extensionResolver != null
				? Path.ChangeExtension(currentFile, targetExtension)
				: currentFile;
		}

		bool isDirectoryInput = Directory.Exists(inputRoot);

		if (isDirectoryInput)
		{
			string relativePath = Path.GetRelativePath(inputRoot, currentFile);
			if (!string.IsNullOrEmpty(outputDestination))
			{
				string relativeTarget = Path.ChangeExtension(relativePath, targetExtension);
				return Path.Combine(outputDestination, relativeTarget);
			}

			return Path.ChangeExtension(currentFile, targetExtension);
		}

		if (!string.IsNullOrEmpty(outputDestination))
		{
			if (Directory.Exists(outputDestination) ||
			    outputDestination.EndsWith(Path.DirectorySeparatorChar) ||
			    outputDestination.EndsWith(Path.AltDirectorySeparatorChar))
			{
				string fileName = Path.ChangeExtension(Path.GetFileName(currentFile), targetExtension);
				return Path.Combine(outputDestination, fileName);
			}

			return extensionResolver != null
				? Path.ChangeExtension(outputDestination, targetExtension)
				: outputDestination;
		}

		return Path.ChangeExtension(currentFile, targetExtension);
	}

	private static int ProcessTraversedFiles(
		string inputPath,
		string? outputPath,
		bool recursive,
		bool inPlace,
		Func<string, bool> fileFilter,
		Func<string, string>? extensionResolver,
		Func<string, string, int> processFile,
		Func<string, string, string>? customPathResolver = null,
		string? summaryActionName = null)
	{
		if (!EnsurePathExists(inputPath))
		{
			return 1;
		}

		if (File.Exists(inputPath))
		{
			if (!fileFilter(inputPath))
			{
				string ext = Path.GetExtension(inputPath);
				Console.Error.WriteLine($"Error: Unsupported file format '{ext}' for input '{inputPath}'.");
				return 1;
			}

			string targetPath = customPathResolver != null
				? customPathResolver(inputPath, inputPath)
				: ResolveTargetOutputPath(inputPath, inputPath, outputPath, inPlace, extensionResolver);

			string? outDir = Path.GetDirectoryName(targetPath);
			if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

			return processFile(inputPath, targetPath);
		}

		string fullInputDir = Path.GetFullPath(inputPath);
		var matchingFiles = TraverseFiles(fullInputDir, recursive, fileFilter).ToArray();

		if (matchingFiles.Length == 0)
		{
			Console.WriteLine($"No matching files found in: {inputPath}");
			return 0;
		}

		int successCount = 0;
		int failCount = 0;

		foreach (string file in matchingFiles)
		{
			string targetPath = customPathResolver != null
				? customPathResolver(fullInputDir, file)
				: ResolveTargetOutputPath(fullInputDir, file, outputPath, inPlace, extensionResolver);

			string? outDir = Path.GetDirectoryName(targetPath);
			if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

			int exitCode = processFile(file, targetPath);
			if (exitCode == 0)
			{
				successCount++;
			}
			else
			{
				failCount++;
			}
		}

		if (!string.IsNullOrEmpty(summaryActionName))
		{
			Console.WriteLine($"Finished {summaryActionName}. {successCount} succeeded, {failCount} failed.");
		}

		return failCount > 0 ? 1 : 0;
	}

	private static int ExecuteRanimRender(RanimRenderOptions options)
	{
		RanimOutputFormat outputFormat = RanimOutputFormat.Gif;
		string formatLower = options.Format.Trim().ToLowerInvariant();

		if (formatLower == "webp")
		{
			outputFormat = RanimOutputFormat.Webp;
		}
		else if (formatLower == "spritesheet" || formatLower == "png")
		{
			outputFormat = RanimOutputFormat.Spritesheet;
		}
		else if (formatLower == "gif")
		{
			outputFormat = RanimOutputFormat.Gif;
		}
		else if (formatLower == "auto" && !string.IsNullOrEmpty(options.Output))
		{
			if (options.Output.EndsWith(".webp", StringComparison.OrdinalIgnoreCase))
			{
				outputFormat = RanimOutputFormat.Webp;
			}
			else if (options.Output.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
			{
				outputFormat = RanimOutputFormat.Spritesheet;
			}
			else
			{
				outputFormat = RanimOutputFormat.Gif;
			}
		}

		var renderOptions = new Realm.Shared.Animation.RanimRenderOptions
		{
			Width = options.Size,
			Height = options.Size,
			Fps = options.Fps,
			MaxFrameCount = options.MaxFrames,
			Format = outputFormat,
			Scale = options.Scale,
			DrawBorder = !options.NoBorder,
			DrawShadow = !options.NoShadow,
			ModelPath = options.Model,
			Quality = options.Quality,
			Lossless = options.Lossless
		};

		string extension = outputFormat switch
		{
			RanimOutputFormat.Webp => ".webp",
			RanimOutputFormat.Spritesheet => ".png",
			_ => ".gif"
		};

		return ProcessTraversedFiles(
			options.Input,
			options.Output,
			options.Recursive,
			inPlace: false,
			file => Path.GetExtension(file).Equals(".ranim", StringComparison.OrdinalIgnoreCase),
			_ => extension,
			(inputFile, targetFile) => ProcessSingleRanimRender(inputFile, targetFile, renderOptions),
			summaryActionName: "rendering animations");
	}

	private static int ProcessSingleRanimRender(string inputFile, string targetFile, Realm.Shared.Animation.RanimRenderOptions renderOptions)
	{
		var result = RanimRenderer.ExportFile(inputFile, targetFile, renderOptions);
		if (result.Success)
		{
			Console.WriteLine($"Successfully rendered ({result.FrameCount} frames): {inputFile} -> {targetFile}");
			return 0;
		}
		else
		{
			Console.Error.WriteLine($"Failed to render {inputFile}: {result.ErrorMessage}");
			return 1;
		}
	}

	private static int ExecuteMetadata(MetadataOptions options)
	{
		string mode = options.Mode?.ToLowerInvariant() ?? "read";

		switch (mode)
		{
			case "add":
			case "update":
			case "set":
			case "write":
			case "embed":
				return ExecuteMetadataAdd(options);

			case "remove":
			case "delete":
			case "clear":
			case "strip":
				return ExecuteMetadataRemove(options);

			case "blake3":
			case "hash":
				return ExecuteBlake3(new Blake3Options { Input = options.Input, Recursive = options.Recursive });

			case "read":
			case "get":
			case "extract":
			case "show":
			default:
				return ExecuteMetadataRead(options);
		}
	}

	private static string FormatFileMetadata(string filePath)
	{
		string? rawMeta = RealmMetadataHelper.ExtractMetadata(filePath);
		JsonObject metaObj;
		if (!string.IsNullOrWhiteSpace(rawMeta))
		{
			try
			{
				metaObj = JsonNode.Parse(rawMeta) as JsonObject ?? new JsonObject();
			}
			catch
			{
				metaObj = new JsonObject();
				metaObj["raw"] = rawMeta;
			}
		}
		else
		{
			metaObj = new JsonObject();
		}

		if (!metaObj.ContainsKey("blake3") || string.IsNullOrWhiteSpace(metaObj["blake3"]?.ToString()))
		{
			string canonicalBlake3 = RealmMetadataHelper.ComputeBlake3(filePath);
			metaObj["blake3"] = canonicalBlake3;
		}

		return metaObj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
	}

	private static int ExecuteMetadataRead(MetadataOptions options)
	{
		if (!EnsurePathExists(options.Input)) return 1;

		if (File.Exists(options.Input))
		{
			string ext = Path.GetExtension(options.Input).ToLowerInvariant();
			if (!RealmMetadataHelper.SupportsMetadata(ext))
			{
				Console.Error.WriteLine($"Error: Unsupported file format '{ext}' for metadata. Supported formats: .rmesh, .raud, .rtex, .ranim, .glb, .ogg");
				return 1;
			}

			string metaToDisplay = FormatFileMetadata(options.Input);
			Console.WriteLine($"Metadata for {options.Input}:");
			Console.WriteLine(metaToDisplay);

			if (!string.IsNullOrEmpty(options.Output))
			{
				string? dir = Path.GetDirectoryName(options.Output);
				if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
				File.WriteAllText(options.Output, metaToDisplay);
				Console.WriteLine($"Saved metadata to: {options.Output}");
			}
			return 0;
		}

		var files = TraverseFiles(options.Input, options.Recursive, file => RealmMetadataHelper.SupportsMetadata(Path.GetExtension(file).ToLowerInvariant())).ToArray();
		int foundCount = 0;
		foreach (var file in files)
		{
			string metaToDisplay = FormatFileMetadata(file);
			Console.WriteLine($"--- {file} ---");
			Console.WriteLine(metaToDisplay);
			foundCount++;
		}

		Console.WriteLine($"Extracted metadata from {foundCount} file(s).");
		return 0;
	}

	private static bool PrepareMetadataJsonForFile(string targetPath, ref string inputJsonContent, bool isUpdate, string? explicitAssetType, out string error)
	{
		error = string.Empty;
		string ext = Path.GetExtension(targetPath).ToLowerInvariant();
		try
		{
			var parsedNode = JsonNode.Parse(inputJsonContent);
			if (parsedNode is not JsonObject inputObj)
			{
				error = "Metadata must be a valid JSON object.";
				return false;
			}

			string? rawAssetType = !string.IsNullOrWhiteSpace(explicitAssetType)
				? explicitAssetType
				: inputObj["asset_type"]?.ToString()
					?? inputObj["AssetType"]?.ToString()
					?? inputObj["default_asset_type"]?.ToString()
					?? inputObj["type"]?.ToString();

			if (!string.IsNullOrEmpty(rawAssetType))
			{
				if (!RealmMetadataHelper.IsValidAssetTypeForExtension(ext, rawAssetType, out string canonical, out var validTypes))
				{
					error = $"Invalid asset_type '{rawAssetType}' for format '{ext}'. Valid asset_type values for {ext} are: {string.Join(", ", validTypes)}.";
					return false;
				}

				inputObj["asset_type"] = canonical;
			}

			JsonObject finalObj;
			if (isUpdate)
			{
				string? existingMeta = RealmMetadataHelper.ExtractMetadata(targetPath);
				if (!string.IsNullOrEmpty(existingMeta))
				{
					try
					{
						finalObj = JsonNode.Parse(existingMeta) as JsonObject ?? new JsonObject();
					}
					catch
					{
						finalObj = new JsonObject();
					}
				}
				else
				{
					finalObj = new JsonObject();
				}

				foreach (var property in inputObj)
				{
					finalObj[property.Key] = property.Value?.DeepClone();
				}
			}
			else
			{
				finalObj = inputObj;
			}

			string canonicalBlake3 = RealmMetadataHelper.ComputeBlake3(targetPath);
			finalObj["blake3"] = canonicalBlake3;

			inputJsonContent = finalObj.ToJsonString();
			return true;
		}
		catch (Exception ex)
		{
			error = $"Invalid JSON metadata: {ex.Message}";
			return false;
		}
	}

	private static int ExecuteMetadataAdd(MetadataOptions options)
	{
		if (!EnsurePathExists(options.Input)) return 1;

		if (string.IsNullOrEmpty(options.Data))
		{
			var jsonNode = new JsonObject();
			if (!string.IsNullOrWhiteSpace(options.AssetType))
			{
				jsonNode["asset_type"] = options.AssetType;
			}

			if (jsonNode.Count > 0)
			{
				options.Data = jsonNode.ToJsonString();
			}
			else
			{
				Console.Error.WriteLine("Error: --data (-d) or --type (-t) option is required for add mode.");
				return 1;
			}
		}

		string jsonContent = options.Data;
		if (File.Exists(options.Data))
		{
			jsonContent = File.ReadAllText(options.Data);
		}

		string mode = options.Mode?.ToLowerInvariant() ?? "add";
		bool isUpdate = mode is "update" or "set";

		if (File.Exists(options.Input))
		{
			string processedJson = jsonContent;
			if (!PrepareMetadataJsonForFile(options.Input, ref processedJson, isUpdate, options.AssetType, out string error))
			{
				Console.Error.WriteLine($"Error: {error}");
				return 1;
			}

			bool success = RealmMetadataHelper.AddMetadata(options.Input, processedJson);
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

		var files = TraverseFiles(options.Input, options.Recursive, file => RealmMetadataHelper.SupportsMetadata(Path.GetExtension(file).ToLowerInvariant())).ToArray();
		int successCount = 0;
		int failCount = 0;

		foreach (var file in files)
		{
			string fileJson = jsonContent;
			if (!PrepareMetadataJsonForFile(file, ref fileJson, isUpdate, options.AssetType, out string error))
			{
				Console.Error.WriteLine($"Failed to add metadata to {file}: {error}");
				failCount++;
				continue;
			}

			if (RealmMetadataHelper.AddMetadata(file, fileJson))
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

	private static int ExecuteMetadataRemove(MetadataOptions options)
	{
		if (!EnsurePathExists(options.Input)) return 1;

		if (File.Exists(options.Input))
		{
			string ext = Path.GetExtension(options.Input).ToLowerInvariant();
			if (!RealmMetadataHelper.SupportsMetadata(ext))
			{
				Console.Error.WriteLine($"Error: Unsupported file format '{ext}' for metadata. Supported formats: .rmesh, .raud, .rtex, .ranim, .glb, .ogg");
				return 1;
			}

			RealmMetadataHelper.RemoveMetadata(options.Input);
			string canonicalBlake3 = RealmMetadataHelper.ComputeBlake3(options.Input);
			var metaObj = new JsonObject { ["blake3"] = canonicalBlake3 };
			bool success = RealmMetadataHelper.AddMetadata(options.Input, metaObj.ToJsonString());

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

		var files = TraverseFiles(options.Input, options.Recursive, file => RealmMetadataHelper.SupportsMetadata(Path.GetExtension(file).ToLowerInvariant())).ToArray();
		int successCount = 0;
		int failCount = 0;

		foreach (var file in files)
		{
			RealmMetadataHelper.RemoveMetadata(file);
			string canonicalBlake3 = RealmMetadataHelper.ComputeBlake3(file);
			var metaObj = new JsonObject { ["blake3"] = canonicalBlake3 };

			if (RealmMetadataHelper.AddMetadata(file, metaObj.ToJsonString()))
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

	private static int ExecuteTextureConvert(TextureConvertOptions options)
	{
		try
		{
			if (!string.IsNullOrWhiteSpace(options.AssetType))
			{
				if (!RealmMetadataHelper.IsValidAssetTypeForExtension(".rtex", options.AssetType, out string canonical, out var validTypes))
				{
					Console.Error.WriteLine($"Error: Invalid asset_type '{options.AssetType}' for textures. Valid asset_type values for textures (.rtex/.png) are: {string.Join(", ", validTypes)}.");
					return 1;
				}
				options.AssetType = canonical;
			}

			return ProcessTraversedFiles(
				options.Input,
				options.Output,
				options.Recursive,
				options.InPlace,
				ImageFormatConverter.IsImageFile,
				file => Path.GetExtension(file).Equals(".rtex", StringComparison.OrdinalIgnoreCase) ? ".webp" : ".rtex",
				(inputFile, targetFile) => ProcessSingleTextureConvert(inputFile, targetFile, options),
				summaryActionName: "texture conversion");
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"Error: {ex.Message}");
			return 1;
		}
	}

	private static int ProcessSingleTextureConvert(string inputFile, string targetFile, TextureConvertOptions options)
	{
		string fileExt = Path.GetExtension(inputFile).ToLowerInvariant();

		if (fileExt == ".rtex" && !Path.GetExtension(targetFile).Equals(".rtex", StringComparison.OrdinalIgnoreCase))
		{
			EnsureAssetAgreementAccepted();
		}

		var res = TextureConverter.ConvertTextureFile(
			inputFile,
			targetFile,
			options.AssetType,
			options.Columns,
			options.Rows);

		if (res.Success)
		{
			if (Path.GetExtension(targetFile).Equals(".rtex", StringComparison.OrdinalIgnoreCase))
			{
				RealmMetadataHelper.SyncBlake3Metadata(targetFile);
			}
			Console.WriteLine($"Successfully converted: {inputFile} -> {targetFile}");
			return 0;
		}
		else
		{
			Console.Error.WriteLine($"Failed to convert {inputFile}: {res.ErrorMessage}");
			return 1;
		}
	}

	private static int ExecuteAudioConvert(AudioConvertOptions options)
	{
		return ProcessTraversedFiles(
			options.Input,
			options.Output,
			options.Recursive,
			inPlace: false,
			AudioConverter.IsAudioFile,
			file => Path.GetExtension(file).Equals(".raud", StringComparison.OrdinalIgnoreCase) ? ".ogg" : ".raud",
			(inputFile, targetFile) => ProcessSingleAudioConvert(inputFile, targetFile, options),
			summaryActionName: "audio conversion");
	}

	private static int ProcessSingleAudioConvert(string inputFile, string targetFile, AudioConvertOptions options)
	{
		string fileExt = Path.GetExtension(inputFile).ToLowerInvariant();

		if (fileExt == ".raud" && !Path.GetExtension(targetFile).Equals(".raud", StringComparison.OrdinalIgnoreCase))
		{
			EnsureAssetAgreementAccepted();
		}

		var res = AudioConverter.ConvertAudioFile(
			inputFile,
			targetFile,
			options.AssetType);

		if (res.Success)
		{
			if (Path.GetExtension(targetFile).Equals(".raud", StringComparison.OrdinalIgnoreCase))
			{
				RealmMetadataHelper.SyncBlake3Metadata(targetFile);
			}
			Console.WriteLine($"Successfully converted: {inputFile} -> {targetFile}");
			return 0;
		}
		else
		{
			Console.Error.WriteLine($"Failed to convert {inputFile}: {res.ErrorMessage}");
			return 1;
		}
	}

	private static int ExecuteFbxToRanim(FbxToRanimOptions options)
	{
		return ProcessTraversedFiles(
			options.Input,
			options.Output,
			options.Recursive,
			inPlace: false,
			file => Path.GetExtension(file).Equals(".fbx", StringComparison.OrdinalIgnoreCase),
			_ => ".ranim",
			(inputFile, targetFile) => ProcessSingleFbxToRanim(inputFile, targetFile),
			summaryActionName: "FBX conversion");
	}

	private static int ProcessSingleFbxToRanim(string inputFile, string targetFile)
	{
		var res = MixamoFbxConverter.ConvertFbxFile(inputFile, targetFile);
		if (res.Success)
		{
			if (File.Exists(res.OutputPath) && Path.GetExtension(res.OutputPath).Equals(".ranim", StringComparison.OrdinalIgnoreCase))
			{
				RealmMetadataHelper.SyncBlake3Metadata(res.OutputPath);
			}
			else if (Directory.Exists(targetFile))
			{
				foreach (var ranimFile in Directory.GetFiles(targetFile, "*.ranim"))
				{
					RealmMetadataHelper.SyncBlake3Metadata(ranimFile);
				}
			}
			Console.WriteLine($"Successfully converted: {inputFile} -> {res.OutputPath} ({string.Join(", ", res.ConvertedAnimationNames)})");
			return 0;
		}
		else
		{
			Console.Error.WriteLine($"Failed to convert {inputFile}: {res.ErrorMessage}");
			return 1;
		}
	}

	private static int ExecuteMeshConvert(MeshConvertOptions options)
	{
		if (!string.IsNullOrWhiteSpace(options.AssetType))
		{
			if (!RealmMetadataHelper.IsValidAssetTypeForExtension(".rmesh", options.AssetType, out string canonical, out var validTypes))
			{
				Console.Error.WriteLine($"Error: Invalid asset_type '{options.AssetType}' for 3D model. Valid asset_type values for .rmesh/.glb are: {string.Join(", ", validTypes)}.");
				return 1;
			}
			options.AssetType = canonical;
		}

		return ProcessTraversedFiles(
			options.Input,
			options.Output,
			options.Recursive,
			options.InPlace,
			ModelConverter.IsModelFile,
			file => Path.GetExtension(file).Equals(".rmesh", StringComparison.OrdinalIgnoreCase) ? ".glb" : ".rmesh",
			(inputFile, targetFile) => ProcessSingleMeshConvert(inputFile, targetFile, options),
			summaryActionName: "model conversion");
	}

	private static int ProcessSingleMeshConvert(string inputFile, string targetFile, MeshConvertOptions options)
	{
		string fileExt = Path.GetExtension(inputFile).ToLowerInvariant();

		if (fileExt == ".rmesh" && !Path.GetExtension(targetFile).Equals(".rmesh", StringComparison.OrdinalIgnoreCase))
		{
			EnsureAssetAgreementAccepted();
		}

		if (Path.GetExtension(targetFile).Equals(".glb", StringComparison.OrdinalIgnoreCase) && fileExt == ".rmesh")
		{
			var res = ModelConverter.ExtractGlbFromRmesh(inputFile, targetFile);
			if (res.Success)
			{
				Console.WriteLine($"Successfully extracted GLB: {inputFile} -> {targetFile}");
				return 0;
			}
			else
			{
				Console.Error.WriteLine($"Failed to extract GLB from {inputFile}: {res.ErrorMessage}");
				return 1;
			}
		}
		else
		{
			var res = ModelConverter.ConvertToRmesh(
				inputFile,
				targetFile,
				options.AssetType,
				options.Force);

			if (res.Success)
			{
				Console.WriteLine($"Successfully converted model: {inputFile} -> {targetFile} (Size: {res.OptimizedSize} bytes, TeamColor: {res.SupportsTeamColor})");
				return 0;
			}
			else
			{
				Console.Error.WriteLine($"Failed to convert {inputFile}: {res.ErrorMessage}");
				return 1;
			}
		}
	}

	private static int ExecuteBlake3(Blake3Options options)
	{
		if (!EnsurePathExists(options.Input)) return 1;

		var files = TraverseFiles(options.Input, options.Recursive).ToArray();
		if (files.Length == 0)
		{
			Console.WriteLine($"No files found in: {options.Input}");
			return 0;
		}

		foreach (var file in files)
		{
			byte[] bytes = File.ReadAllBytes(file);
			string hash = options.Raw
				? Blake3.Hasher.Hash(bytes).ToString()
				: RealmMetadataHelper.ComputeBlake3(bytes, file);
			Console.WriteLine($"{hash}  {file}");
		}
		return 0;
	}

	private static int ExecuteMeshPlayerColor(MeshPlayerColorCliOptions options)
	{
		if (!string.IsNullOrWhiteSpace(options.AssetType))
		{
			if (!RealmMetadataHelper.IsValidAssetTypeForExtension(".rmesh", options.AssetType, out string canonical, out var validTypes))
			{
				Console.Error.WriteLine($"Error: Invalid asset_type '{options.AssetType}' for 3D model. Valid asset_type values for .rmesh/.glb are: {string.Join(", ", validTypes)}.");
				return 1;
			}
			options.AssetType = canonical;
		}

		var processorOptions = new Realm.Shared.GlbPlayerColorOptions
		{
			TargetHex = options.TargetHex,
			CoreThreshold = options.CoreThreshold,
			FringeThreshold = options.FringeThreshold,
			MinClusterFaces = options.MinClusterFaces,
			DilationRadius = options.DilationRadius
		};

		return ProcessTraversedFiles(
			options.Input,
			options.Output,
			options.Recursive,
			options.InPlace,
			file => file.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".glb", StringComparison.OrdinalIgnoreCase),
			_ => ".rmesh",
			(inputFile, targetFile) => ProcessSingleMeshPlayerColor(inputFile, targetFile, processorOptions, options.AssetType, options.InPlace),
			customPathResolver: (inputRoot, currentFile) =>
			{
				if (Directory.Exists(inputRoot))
				{
					if (options.InPlace || string.IsNullOrEmpty(options.Output))
					{
						return ResolveMeshPlayerColorOutputPath(currentFile, null, options.InPlace);
					}

					string relativePath = Path.GetRelativePath(inputRoot, currentFile);
					string relativeRmesh = Path.ChangeExtension(relativePath, ".rmesh");
					return Path.Combine(options.Output, relativeRmesh);
				}

				return ResolveMeshPlayerColorOutputPath(currentFile, options.Output, options.InPlace);
			},
			summaryActionName: "mesh player color processing");
	}

	private static int ProcessSingleMeshPlayerColor(
		string inputPath,
		string outputPath,
		Realm.Shared.GlbPlayerColorOptions processorOptions,
		string? assetType = null,
		bool inPlace = false)
	{
		outputPath = Path.ChangeExtension(outputPath, ".rmesh");
		Console.WriteLine($"Processing: {inputPath} -> {outputPath}");

		string ext = Path.GetExtension(inputPath).ToLowerInvariant();
		bool isRmesh = ext == ".rmesh";

		var optimizer = new GlbOptimizer();
		string? tempInputGlb = null;
		string? tempColorResultGlb = null;
		string? tempUnoptimizedPath = null;
		string? existingMeta = null;

		try
		{
			byte[] sourceGlbBytes;
			if (isRmesh)
			{
				byte[] rmeshBytes = File.ReadAllBytes(inputPath);
				var (meta, glbBytes, _) = RmeshFile.Parse(rmeshBytes);
				existingMeta = meta;
				sourceGlbBytes = glbBytes;
			}
			else
			{
				sourceGlbBytes = File.ReadAllBytes(inputPath);
				existingMeta = RealmMetadataHelper.ExtractMetadataFromGlbBytes(sourceGlbBytes);
			}

			bool wasOptimized = optimizer.IsOptimized(sourceGlbBytes);
			tempInputGlb = Path.Combine(Path.GetTempPath(), $"realm_pc_in_{Guid.NewGuid():N}.glb");
			File.WriteAllBytes(tempInputGlb, sourceGlbBytes);

			string colorSourcePath = tempInputGlb;
			if (wasOptimized)
			{
				Console.WriteLine($"  Detected pre-optimized GLB — unoptimizing first to restore mesh topology...");
				var unoptResult = optimizer.Unoptimize(sourceGlbBytes);
				if (!unoptResult.Success || unoptResult.OutputGlbBytes == null)
				{
					Console.Error.WriteLine($"  Failed to unoptimize {inputPath}: {unoptResult.ErrorMessage}");
					return 1;
				}

				tempUnoptimizedPath = Path.Combine(Path.GetTempPath(), $"realm_pc_unopt_{Guid.NewGuid():N}.glb");
				File.WriteAllBytes(tempUnoptimizedPath, unoptResult.OutputGlbBytes);
				colorSourcePath = tempUnoptimizedPath;
			}

			tempColorResultGlb = Path.Combine(Path.GetTempPath(), $"realm_pc_out_{Guid.NewGuid():N}.glb");
			var colorResult = Realm.Shared.GlbPlayerColorProcessor.ProcessFile(colorSourcePath, tempColorResultGlb, processorOptions);
			if (!colorResult.Success || !File.Exists(tempColorResultGlb))
			{
				Console.Error.WriteLine($"  Failed player-color processing: {colorResult.ErrorMessage}");
				return 1;
			}

			Console.WriteLine($"  Player-color mask applied (masked faces: {colorResult.MaskedFaceCount}/{colorResult.TotalFaceCount})");

			Console.WriteLine($"  Re-optimizing output (LODs regenerated from corrected textures)...");
			byte[] processedGlbBytes = File.ReadAllBytes(tempColorResultGlb);

			string? targetAssetType = !string.IsNullOrWhiteSpace(assetType)
				? assetType
				: null;
			string? targetAuthor = null;
			if (!string.IsNullOrEmpty(existingMeta))
			{
				try
				{
					var node = JsonNode.Parse(existingMeta);
					targetAssetType ??= node?["asset_type"]?.ToString() ?? node?["default_asset_type"]?.ToString();
					targetAuthor = node?["author"]?.ToString();
				}
				catch { }
			}

			var convResult = ModelConverter.ConvertToRmesh(
				processedGlbBytes,
				inputPath,
				targetAssetType,
				force: true,
				author: targetAuthor);

			if (!convResult.Success || convResult.OutputBytes == null)
			{
				Console.Error.WriteLine($"  Failed to repack into RMESH: {convResult.ErrorMessage}");
				return 1;
			}

			string? outDir = Path.GetDirectoryName(outputPath);
			if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

			File.WriteAllBytes(outputPath, convResult.OutputBytes);
			Console.WriteLine($"  Successfully saved RMESH: {outputPath} ({convResult.OptimizedSize} bytes)");

			if (inPlace && !string.Equals(inputPath, outputPath, StringComparison.OrdinalIgnoreCase) && File.Exists(inputPath))
			{
				try { File.Delete(inputPath); } catch { }
			}

			return 0;
		}
		finally
		{
			if (tempInputGlb != null && File.Exists(tempInputGlb)) try { File.Delete(tempInputGlb); } catch { }
			if (tempColorResultGlb != null && File.Exists(tempColorResultGlb)) try { File.Delete(tempColorResultGlb); } catch { }
			if (tempUnoptimizedPath != null && File.Exists(tempUnoptimizedPath)) try { File.Delete(tempUnoptimizedPath); } catch { }
		}
	}

	private static string ResolveMeshPlayerColorOutputPath(string inputPath, string? explicitOutput, bool inPlace)
	{
		if (inPlace) return Path.ChangeExtension(inputPath, ".rmesh");
		if (!string.IsNullOrEmpty(explicitOutput))
		{
			if (Directory.Exists(explicitOutput) ||
			    explicitOutput.EndsWith(Path.DirectorySeparatorChar) ||
			    explicitOutput.EndsWith(Path.AltDirectorySeparatorChar))
			{
				string nameWithoutExt = Path.GetFileNameWithoutExtension(inputPath);
				return Path.Combine(explicitOutput, $"{nameWithoutExt}_masked.rmesh");
			}
			return Path.ChangeExtension(explicitOutput, ".rmesh");
		}
		string dir = Path.GetDirectoryName(inputPath) ?? string.Empty;
		string nameWithoutExtDefault = Path.GetFileNameWithoutExtension(inputPath);
		return Path.Combine(dir, $"{nameWithoutExtDefault}_masked.rmesh");
	}

	private static int ExecuteRigHumanoid(RigHumanoidOptions options)
	{
		if (!string.IsNullOrWhiteSpace(options.AssetType))
		{
			if (!RealmMetadataHelper.IsValidAssetTypeForExtension(".rmesh", options.AssetType, out string canonical, out var validTypes))
			{
				Console.Error.WriteLine($"Error: Invalid asset_type '{options.AssetType}' for humanoid rig. Valid asset_type values for .rmesh/.glb are: {string.Join(", ", validTypes)}.");
				return 1;
			}
			options.AssetType = canonical;
		}

		if (!string.IsNullOrWhiteSpace(options.MiaDir))
		{
			Environment.SetEnvironmentVariable("MIA_DIR", options.MiaDir);
		}

		return ProcessTraversedFiles(
			options.Input,
			options.Output,
			options.Recursive,
			options.InPlace,
			file => file.EndsWith(".rmesh", StringComparison.OrdinalIgnoreCase) || file.EndsWith(".glb", StringComparison.OrdinalIgnoreCase),
			_ => ".rmesh",
			(inputFile, targetFile) => ProcessSingleRigHumanoid(inputFile, targetFile, options),
			summaryActionName: "humanoid rigging");
	}

	private static int ProcessSingleRigHumanoid(string inputPath, string targetOutput, RigHumanoidOptions options)
	{
		targetOutput = Path.ChangeExtension(targetOutput, ".rmesh");
		string ext = Path.GetExtension(inputPath).ToLowerInvariant();
		bool isRmesh = ext == ".rmesh";

		string tempGlbInput = inputPath;
		string? tempExtractedGlb = null;
		string? tempGlbOutput = null;
		string? existingMeta = null;

		try
		{
			if (isRmesh)
			{
				byte[] rmeshBytes = File.ReadAllBytes(inputPath);
				var (meta, glbBytes, _) = RmeshFile.Parse(rmeshBytes);
				existingMeta = meta;
				tempExtractedGlb = Path.Combine(Path.GetTempPath(), $"realm_rig_in_{Guid.NewGuid():N}.glb");
				File.WriteAllBytes(tempExtractedGlb, glbBytes);
				tempGlbInput = tempExtractedGlb;
			}

			tempGlbOutput = Path.Combine(Path.GetTempPath(), $"realm_rig_out_{Guid.NewGuid():N}.glb");

			var result = GlbAutoRigger.RigHumanoid(
				tempGlbInput,
				tempGlbOutput,
				new GlbAutoRiggerOptions
				{
					NoFingers = options.NoFingers,
					UseNormals = options.UseNormals,
					WeightPostprocess = options.WeightPostprocess
				});

			if (!result.Success || !File.Exists(tempGlbOutput))
			{
				Console.Error.WriteLine($"Error rigging {inputPath}: {result.ErrorMessage}");
				return 1;
			}

			string? targetAssetType = !string.IsNullOrWhiteSpace(options.AssetType)
				? options.AssetType
				: null;
			string? targetAuthor = null;
			if (!string.IsNullOrEmpty(existingMeta))
			{
				try
				{
					var node = JsonNode.Parse(existingMeta);
					targetAssetType ??= node?["asset_type"]?.ToString() ?? node?["default_asset_type"]?.ToString();
					targetAuthor = node?["author"]?.ToString();
				}
				catch { }
			}
			targetAssetType ??= "Character";

			byte[] riggedGlb = File.ReadAllBytes(tempGlbOutput);
			var convRes = ModelConverter.ConvertToRmesh(
				riggedGlb,
				inputPath,
				targetAssetType,
				force: true,
				author: targetAuthor);

			if (!convRes.Success || convRes.OutputBytes == null)
			{
				Console.Error.WriteLine($"Failed to pack rigged model to RMESH: {convRes.ErrorMessage}");
				return 1;
			}

			string? outDir = Path.GetDirectoryName(targetOutput);
			if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir)) Directory.CreateDirectory(outDir);

			File.WriteAllBytes(targetOutput, convRes.OutputBytes);
			Console.WriteLine($"Successfully rigged and saved RMESH: {targetOutput}");

			if (options.InPlace && !string.Equals(inputPath, targetOutput, StringComparison.OrdinalIgnoreCase) && File.Exists(inputPath))
			{
				try { File.Delete(inputPath); } catch { }
			}

			return 0;
		}
		finally
		{
			if (tempExtractedGlb != null && File.Exists(tempExtractedGlb)) try { File.Delete(tempExtractedGlb); } catch { }
			if (tempGlbOutput != null && File.Exists(tempGlbOutput)) try { File.Delete(tempGlbOutput); } catch { }
		}
	}
}
