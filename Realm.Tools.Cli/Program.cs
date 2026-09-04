using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
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

	[Option('t', "type", Required = false, HelpText = "Asset type for GLB: Attachment, Character, Building, Environment, Projectile, Prop. If specified, sets or updates the embedded asset_type metadata.")]
	public string? AssetType { get; set; }
}

[Verb("texture_convert", HelpText = "Convert textures between standard image formats and .rtex format.")]
public class TextureConvertOptions
{
	[Option('i', "input", Required = true, HelpText = "Path to input image / .rtex file or directory.")]
	public string Input { get; set; } = string.Empty;

	[Option('o', "output", Required = false, HelpText = "Output destination file or directory.")]
	public string? Output { get; set; }

	[Option('t', "type", Required = false, HelpText = "Asset type for textures: Decal, Icon, Noise, Ribbon, Skybox, SpellSpritesheet, Tilesheet. If omitted, attempts to read type from image metadata.")]
	public string? AssetType { get; set; }

	[Option("columns", Required = false, Default = 4, HelpText = "Number of grid columns for spritesheets (default 4).")]
	public int Columns { get; set; } = 4;

	[Option("rows", Required = false, Default = 4, HelpText = "Number of grid rows for spritesheets (default 4).")]
	public int Rows { get; set; } = 4;

	[Option("in-place", Required = false, Default = false, HelpText = "Write output alongside input file.")]
	public bool InPlace { get; set; }

	[Option('r', "recursive", Required = false, Default = false, HelpText = "Process directories recursively.")]
	public bool Recursive { get; set; }
}

[Verb("audio_convert", HelpText = "Convert audio files (mp3, wav, flac, aac, etc.) to .ogg format.")]
public class AudioConvertOptions
{
	[Option('i', "input", Required = true, HelpText = "Path to input audio file or directory containing audio files.")]
	public string Input { get; set; } = string.Empty;

	[Option('o', "output", Required = false, HelpText = "Output destination file or directory.")]
	public string? Output { get; set; }

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

[Verb("ranim_render", HelpText = "Render .ranim skeletal animation files to animated GIF or PNG spritesheet.")]
public class RanimRenderOptions
{
	[Option('i', "input", Required = true, HelpText = "Path to input .ranim file or directory.")]
	public string Input { get; set; } = string.Empty;

	[Option('o', "output", Required = false, HelpText = "Output destination file or directory.")]
	public string? Output { get; set; }

	[Option('f', "format", Required = false, Default = "auto", HelpText = "Output format: auto (default), gif, spritesheet.")]
	public string Format { get; set; } = "auto";

	[Option("fps", Required = false, Default = 12.0f, HelpText = "Target frames per second (default 12).")]
	public float Fps { get; set; } = 12.0f;

	[Option("max-frames", Required = false, HelpText = "Maximum frame count (uses modulus to skip intermediate frames).")]
	public int? MaxFrames { get; set; }

	[Option("size", Required = false, Default = 128, HelpText = "Frame width and height in pixels (default 128).")]
	public int Size { get; set; } = 128;

	[Option("scale", Required = false, Default = 1.0f, HelpText = "Model scale factor (default 1.0).")]
	public float Scale { get; set; } = 1.0f;

	[Option('r', "recursive", Required = false, Default = false, HelpText = "Process directories recursively.")]
	public bool Recursive { get; set; }

	[Option("no-border", Required = false, Default = false, HelpText = "Disable frame border.")]
	public bool NoBorder { get; set; }

	[Option("no-shadow", Required = false, Default = false, HelpText = "Disable floor shadow.")]
	public bool NoShadow { get; set; }
}

[Verb("metadata", HelpText = "Manage embedded Realm metadata (read, add, remove) in .glb, .rtex, .ranim, or .ogg files.")]
public class MetadataOptions
{
	[Option('m', "mode", Required = false, Default = "read", HelpText = "Operation mode: read (default), add, update, remove.")]
	public string Mode { get; set; } = "read";

	[Option('i', "input", Required = true, HelpText = "Path to asset file or directory containing asset files.")]
	public string Input { get; set; } = string.Empty;

	[Option('d', "data", Required = false, HelpText = "JSON string or path to JSON file containing metadata to embed (for add/update mode).")]
	public string? Data { get; set; }

	[Option('t', "type", Required = false, HelpText = "Asset type to embed: Attachment, Character, Building, Environment, Projectile, Prop, Decal, Icon, Noise, Ribbon, Skybox, SpellSpritesheet, Tilesheet, Animation, Music, SoundEffect.")]
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

[Verb("glb_player_color", HelpText = "Extract #FF00FF prompt artifacts from a .glb model, isolate the player-color area via 3D face-topology clustering, desaturate the albedo, and pack the mask into the Red channel of the ORM texture.")]
public class GlbPlayerColorCliOptions
{
	[Option('i', "input", Required = true, HelpText = "Path to .glb file or directory containing .glb files.")]
	public string Input { get; set; } = string.Empty;

	[Option('o', "output", Required = false, HelpText = "Output destination file or directory. Defaults to <input>_masked.glb alongside the source.")]
	public string? Output { get; set; }

	[Option("in-place", Required = false, Default = false, HelpText = "Overwrite the source file directly.")]
	public bool InPlace { get; set; }

	[Option('r', "recursive", Required = false, Default = false, HelpText = "Process directories recursively.")]
	public bool Recursive { get; set; }

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

[Verb("rig_humanoid", HelpText = "Auto-rig a humanoid .glb model with a Mixamo skeleton using the Make-It-Animatable pipeline.")]
public class RigHumanoidOptions
{
	[Option('i', "input", Required = true, HelpText = "Path to input .glb file.")]
	public string Input { get; set; } = string.Empty;

	[Option('o', "output", Required = true, HelpText = "Path for output rigged .glb file.")]
	public string Output { get; set; } = string.Empty;

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
		return Parser.Default.ParseArguments<GlbOptimizeOptions, TextureConvertOptions, AudioConvertOptions, FbxToRanimOptions, RanimRenderOptions, MetadataOptions, Blake3Options, GlbPlayerColorCliOptions, RigHumanoidOptions>(args)
			.MapResult(
				(GlbOptimizeOptions options) => ExecuteGlbOptimize(options),
				(TextureConvertOptions options) => ExecuteTextureConvert(options),
				(AudioConvertOptions options) => ExecuteAudioConvert(options),
				(FbxToRanimOptions options) => ExecuteFbxToRanim(options),
				(RanimRenderOptions options) => ExecuteRanimRender(options),
				(MetadataOptions options) => ExecuteMetadata(options),
				(Blake3Options options) => ExecuteBlake3(options),
				(GlbPlayerColorCliOptions options) => ExecuteGlbPlayerColor(options),
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

	private static int ExecuteRanimRender(RanimRenderOptions options)
	{
		EnsureAssetAgreementAccepted();

		RanimOutputFormat outputFormat = RanimOutputFormat.Gif;
		if (options.Format.Equals("spritesheet", StringComparison.OrdinalIgnoreCase) || options.Format.Equals("png", StringComparison.OrdinalIgnoreCase))
		{
			outputFormat = RanimOutputFormat.Spritesheet;
		}
		else if (options.Format.Equals("auto", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrEmpty(options.Output))
		{
			if (options.Output.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
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
			DrawShadow = !options.NoShadow
		};

		if (File.Exists(options.Input))
		{
			string extension = outputFormat == RanimOutputFormat.Spritesheet ? ".png" : ".gif";
			string target = string.IsNullOrEmpty(options.Output)
				? Path.ChangeExtension(options.Input, extension)
				: options.Output;

			var result = RanimRenderer.ExportFile(options.Input, target, renderOptions);
			if (result.Success)
			{
				Console.WriteLine($"Successfully rendered ({result.FrameCount} frames): {options.Input} -> {target}");
				return 0;
			}
			else
			{
				Console.Error.WriteLine($"Failed to render {options.Input}: {result.ErrorMessage}");
				return 1;
			}
		}
		else if (Directory.Exists(options.Input))
		{
			return RanimRenderer.ExportDirectory(options.Input, options.Output, renderOptions, options.Recursive);
		}
		else
		{
			Console.Error.WriteLine($"Error: Input path does not exist: {options.Input}");
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

	private static int ExecuteMetadataRead(MetadataOptions options)
	{
		if (File.Exists(options.Input))
		{
			string ext = Path.GetExtension(options.Input).ToLowerInvariant();
			if (ext is not (".glb" or ".rtex" or ".ranim" or ".ogg"))
			{
				Console.Error.WriteLine($"Error: Unsupported file format '{ext}' for metadata. Supported formats: .glb, .rtex, .ogg, .ranim");
				return 1;
			}

			string? rawMeta = RealmMetadataHelper.ExtractMetadata(options.Input);
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
				string canonicalBlake3 = RealmMetadataHelper.ComputeBlake3(options.Input);
				metaObj["blake3"] = canonicalBlake3;
			}

			string metaToDisplay = metaObj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

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
		else if (Directory.Exists(options.Input))
		{
			var searchOpt = options.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
			string[] files = Directory.GetFiles(options.Input, "*.*", searchOpt);
			int foundCount = 0;
			foreach (var file in files)
			{
				string ext = Path.GetExtension(file).ToLowerInvariant();
				if (ext is not (".glb" or ".rtex" or ".ranim" or ".ogg")) continue;

				string? rawMeta = RealmMetadataHelper.ExtractMetadata(file);
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
					string canonicalBlake3 = RealmMetadataHelper.ComputeBlake3(file);
					metaObj["blake3"] = canonicalBlake3;
				}

				string metaToDisplay = metaObj.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

				Console.WriteLine($"--- {file} ---");
				Console.WriteLine(metaToDisplay);
				foundCount++;
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
		if (string.IsNullOrEmpty(options.Data))
		{
			if (!string.IsNullOrWhiteSpace(options.AssetType))
			{
				options.Data = new JsonObject { ["asset_type"] = options.AssetType }.ToJsonString();
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
		else if (Directory.Exists(options.Input))
		{
			var searchOpt = options.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
			string[] files = Directory.GetFiles(options.Input, "*.*", searchOpt);
			int successCount = 0;
			int failCount = 0;

			foreach (var file in files)
			{
				string ext = Path.GetExtension(file).ToLowerInvariant();
				if (ext is not (".glb" or ".rtex" or ".ranim" or ".ogg")) continue;

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
		else
		{
			Console.Error.WriteLine($"Error: Input path does not exist: {options.Input}");
			return 1;
		}
	}

	private static int ExecuteMetadataRemove(MetadataOptions options)
	{
		if (File.Exists(options.Input))
		{
			string ext = Path.GetExtension(options.Input).ToLowerInvariant();
			if (ext is not (".glb" or ".rtex" or ".ranim" or ".ogg"))
			{
				Console.Error.WriteLine($"Error: Unsupported file format '{ext}' for metadata. Supported formats: .glb, .rtex, .ogg, .ranim");
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
		else if (Directory.Exists(options.Input))
		{
			var searchOpt = options.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
			string[] files = Directory.GetFiles(options.Input, "*.*", searchOpt);
			int successCount = 0;
			int failCount = 0;

			foreach (var file in files)
			{
				string ext = Path.GetExtension(file).ToLowerInvariant();
				if (ext is not (".glb" or ".rtex" or ".ranim" or ".ogg")) continue;

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
		else
		{
			Console.Error.WriteLine($"Error: Input path does not exist: {options.Input}");
			return 1;
		}
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

			if (File.Exists(options.Input))
			{
				string fileExt = Path.GetExtension(options.Input).ToLowerInvariant();
				string defaultExt = fileExt == ".rtex" ? ".webp" : ".rtex";

				string target = options.InPlace || string.IsNullOrEmpty(options.Output)
					? Path.ChangeExtension(options.Input, defaultExt)
					: options.Output;

				if (fileExt == ".rtex" && !Path.GetExtension(target).Equals(".rtex", StringComparison.OrdinalIgnoreCase))
				{
					EnsureAssetAgreementAccepted();
				}

				var res = TextureConverter.ConvertTextureFile(
					options.Input,
					target,
					options.AssetType,
					options.Columns,
					options.Rows);

				if (res.Success)
				{
					if (Path.GetExtension(target).Equals(".rtex", StringComparison.OrdinalIgnoreCase))
					{
						RealmMetadataHelper.SyncBlake3Metadata(target);
					}
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
				var searchOpt = options.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
				if (Directory.EnumerateFiles(options.Input, "*.rtex", searchOpt).Any())
				{
					EnsureAssetAgreementAccepted();
				}

				return TextureConverter.ConvertTextureDirectory(
					options.Input,
					options.Output,
					options.AssetType,
					options.Recursive,
					options.Columns,
					options.Rows);
			}
			else
			{
				Console.Error.WriteLine($"Error: Input path does not exist: {options.Input}");
				return 1;
			}
		}
		catch (Exception ex)
		{
			Console.Error.WriteLine($"Error: {ex.Message}");
			return 1;
		}
	}

	private static int ExecuteAudioConvert(AudioConvertOptions options)
	{
		if (File.Exists(options.Input))
		{
			string target = string.IsNullOrEmpty(options.Output)
				? Path.ChangeExtension(options.Input, ".ogg")
				: options.Output;

			var res = Realm.Shared.Audio.AudioConverter.ConvertToOgg(options.Input, target);
			if (res.Success)
			{
				if (Path.GetExtension(target).Equals(".ogg", StringComparison.OrdinalIgnoreCase))
				{
					RealmMetadataHelper.SyncBlake3Metadata(target);
				}
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
			return Realm.Shared.Audio.AudioConverter.ConvertAudioDirectory(options.Input, options.Output, options.Recursive);
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
				if (File.Exists(res.OutputPath) && Path.GetExtension(res.OutputPath).Equals(".ranim", StringComparison.OrdinalIgnoreCase))
				{
					RealmMetadataHelper.SyncBlake3Metadata(res.OutputPath);
				}
				else if (Directory.Exists(target))
				{
					foreach (var ranimFile in Directory.GetFiles(target, "*.ranim"))
					{
						RealmMetadataHelper.SyncBlake3Metadata(ranimFile);
					}
				}
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
		if (!string.IsNullOrWhiteSpace(options.AssetType))
		{
			if (!RealmMetadataHelper.IsValidAssetTypeForExtension(".glb", options.AssetType, out string canonical, out var validTypes))
			{
				Console.Error.WriteLine($"Error: Invalid asset_type '{options.AssetType}' for GLB. Valid asset_type values for .glb are: {string.Join(", ", validTypes)}.");
				return 1;
			}
			options.AssetType = canonical;
		}

		var optimizer = new GlbOptimizer();
		var optimizationOptions = new OptimizationOptions
		{
			SimplificationRatio = options.Quality,
			MaxTextureResolution = options.MaxResolution,
			ForceReDecimate = options.Force
		};

		if (File.Exists(options.Input))
		{
			return ProcessSingleFile(optimizer, options.Input, options.Output, options.InPlace, options.Mode, optimizationOptions, options.AssetType);
		}
		else if (Directory.Exists(options.Input))
		{
			return ProcessDirectory(optimizer, options.Input, options.Output, options.InPlace, options.Mode, options.Recursive, optimizationOptions, options.AssetType);
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
		OptimizationOptions options,
		string? assetType = null)
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
				if (!string.IsNullOrEmpty(assetType))
				{
					RealmMetadataHelper.SetAssetType(target, assetType);
				}
				else
				{
					RealmMetadataHelper.SyncBlake3Metadata(target);
				}
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
				if (!string.IsNullOrEmpty(assetType))
				{
					RealmMetadataHelper.SetAssetType(target, assetType);
				}
				else
				{
					RealmMetadataHelper.SyncBlake3Metadata(target);
				}
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
		OptimizationOptions options,
		string? assetType = null)
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

			int res = ProcessSingleFile(optimizer, file, target, false, mode, options, assetType);
			if (res == 0) successCount++;
			else failCount++;
		}

		Console.WriteLine($"Finished. Processed: {successCount} succeeded, {failCount} failed.");
		return failCount > 0 ? 1 : 0;
	}

	private static int ExecuteBlake3(Blake3Options options)
	{
		if (File.Exists(options.Input))
		{
			byte[] bytes = File.ReadAllBytes(options.Input);
			string hash = options.Raw
				? Blake3.Hasher.Hash(bytes).ToString()
				: RealmMetadataHelper.ComputeBlake3(bytes, options.Input);
			Console.WriteLine($"{hash}  {options.Input}");
			return 0;
		}
		else if (Directory.Exists(options.Input))
		{
			var searchOpt = options.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
			string[] files = Directory.GetFiles(options.Input, "*.*", searchOpt);
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
		else
		{
			Console.Error.WriteLine($"Error: Input path does not exist: {options.Input}");
			return 1;
		}
	}

	private static int ExecuteGlbPlayerColor(GlbPlayerColorCliOptions options)
	{
		var processorOptions = new Realm.Shared.GlbPlayerColorOptions
		{
			TargetHex = options.TargetHex,
			CoreThreshold = options.CoreThreshold,
			FringeThreshold = options.FringeThreshold,
			MinClusterFaces = options.MinClusterFaces,
			DilationRadius = options.DilationRadius
		};

		if (File.Exists(options.Input))
		{
			string target = ResolveOutputPath(options.Input, options.Output, options.InPlace);
			return ProcessSingleGlbPlayerColor(options.Input, target, processorOptions);
		}
		else if (Directory.Exists(options.Input))
		{
			var searchOpt = options.Recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
			string[] files = Directory.GetFiles(options.Input, "*.glb", searchOpt);
			Console.WriteLine($"Found {files.Length} .glb file(s) in {options.Input}");

			int successCount = 0;
			int failCount = 0;

			foreach (var file in files)
			{
				string target;
				if (options.InPlace || string.IsNullOrEmpty(options.Output))
				{
					target = ResolveOutputPath(file, null, options.InPlace);
				}
				else
				{
					string rel = Path.GetRelativePath(options.Input, file);
					target = Path.Combine(options.Output, rel);
				}

				int res = ProcessSingleGlbPlayerColor(file, target, processorOptions);
				if (res == 0) successCount++;
				else failCount++;
			}

			Console.WriteLine($"Finished. {successCount} succeeded, {failCount} failed.");
			return failCount > 0 ? 1 : 0;
		}
		else
		{
			Console.Error.WriteLine($"Error: Input path does not exist: {options.Input}");
			return 1;
		}
	}

	private static int ProcessSingleGlbPlayerColor(
		string inputPath,
		string outputPath,
		Realm.Shared.GlbPlayerColorOptions processorOptions)
	{
		Console.WriteLine($"Processing: {inputPath} -> {outputPath}");

		var optimizer = new GlbOptimizer();

		byte[] sourceBytes = File.ReadAllBytes(inputPath);
		bool wasOptimized = optimizer.IsOptimized(sourceBytes);

		string colorSourcePath = inputPath;
		string? tempUnoptimizedPath = null;

		try
		{
			if (wasOptimized)
			{
				Console.WriteLine($"  Detected pre-optimized GLB — unoptimizing first to restore mesh topology...");
				var unoptResult = optimizer.Unoptimize(sourceBytes);
				if (!unoptResult.Success || unoptResult.OutputGlbBytes == null)
				{
					Console.Error.WriteLine($"  Failed to unoptimize {inputPath}: {unoptResult.ErrorMessage}");
					return 1;
				}

				tempUnoptimizedPath = Path.Combine(
					Path.GetDirectoryName(inputPath) ?? string.Empty,
					$"__tmp_unopt_{Path.GetFileName(inputPath)}");
				File.WriteAllBytes(tempUnoptimizedPath, unoptResult.OutputGlbBytes);
				colorSourcePath = tempUnoptimizedPath;
			}

			var colorResult = Realm.Shared.GlbPlayerColorProcessor.ProcessFile(colorSourcePath, outputPath, processorOptions);
			if (!colorResult.Success)
			{
				Console.Error.WriteLine($"  Failed player-color processing: {colorResult.ErrorMessage}");
				return 1;
			}

			Console.WriteLine($"  Player-color mask applied (masked faces: {colorResult.MaskedFaceCount}/{colorResult.TotalFaceCount})");
		}
		finally
		{
			if (tempUnoptimizedPath != null && File.Exists(tempUnoptimizedPath))
			{
				try { File.Delete(tempUnoptimizedPath); } catch { }
			}

			if (tempUnoptimizedPath != null)
			{
				string tempUnoptDir = Path.Combine(
					Path.GetDirectoryName(tempUnoptimizedPath) ?? string.Empty,
					Path.GetFileNameWithoutExtension(tempUnoptimizedPath));
				if (Directory.Exists(tempUnoptDir))
				{
					try { Directory.Delete(tempUnoptDir, true); } catch { }
				}
			}
		}

		Console.WriteLine($"  Re-optimizing output (LODs regenerated from corrected textures)...");
		var optimizeResult = optimizer.OptimizeFile(
			outputPath,
			outputPath,
			new OptimizationOptions { ForceReDecimate = true });

		if (!optimizeResult.Success)
		{
			Console.Error.WriteLine($"  Warning: Re-optimization failed: {optimizeResult.ErrorMessage}");
			Console.Error.WriteLine($"  Output saved without optimization: {outputPath}");
			return 1;
		}

		Console.WriteLine($"  Successfully optimized: {outputPath} ({optimizeResult.OriginalSize} -> {optimizeResult.OptimizedSize} bytes)");
		return 0;
	}

	private static string ResolveOutputPath(string inputPath, string? explicitOutput, bool inPlace)
	{
		if (inPlace) return inputPath;
		if (!string.IsNullOrEmpty(explicitOutput)) return explicitOutput;
		string dir = Path.GetDirectoryName(inputPath) ?? string.Empty;
		string nameWithoutExt = Path.GetFileNameWithoutExtension(inputPath);
		return Path.Combine(dir, nameWithoutExt + "_masked.glb");
	}

	private static int ExecuteRigHumanoid(RigHumanoidOptions options)
	{
		var result = GlbAutoRigger.RigHumanoid(
			options.Input,
			options.Output,
			new GlbAutoRiggerOptions
			{
				NoFingers = options.NoFingers,
				UseNormals = options.UseNormals,
				WeightPostprocess = options.WeightPostprocess
			});

		if (!result.Success)
		{
			Console.Error.WriteLine($"Error: {result.ErrorMessage}");
			return 1;
		}

		return 0;
	}
}
