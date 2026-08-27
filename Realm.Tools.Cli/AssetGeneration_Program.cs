using Blake3;
using PuppeteerSharp;
using System;
using System.IO;
using SkiaSharp;
using System.Diagnostics;
using System.IO.Compression;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;


namespace IconAutomation
{
	public enum AssetCategory
	{
		Characters,
		Buildings,
		Environment,
		Props,
		Items,
		Abilities,
		UI,
		Weapons,
		Projectiles
	}

	public enum AnimationCategory
	{
		Idle,
		Walk,
		Attack,
		Death,
		Spell_Cast,
		Labor,
		Other
	}

	public enum SeamlessMode
	{
		Tile2D,        // Wraps horizontally and vertically (shifts X and Y by half dimensions, crosshair seam)
		Horizontal1D,  // Wraps horizontally only (shifts X by half width, vertical seam) - used for ribbons/trails
		Vertical1D     // Wraps vertically only (shifts Y by half height, horizontal seam)
	}

	class Program
	{
		static async Task Main(string[] args)
		{
			if (args.Length == 0)
			{
				Console.WriteLine("Usage: Generation.exe <GenerateIcons|RefineIcons|RefineIconsByFilename|Generate3DModels_Random|Generate3DModels_Targeted|Refine3DModels|GenerateMusic|GenerateSoundEffects|GenerateIconSoundEffects|Generate3DModelSoundEffects|GenerateVoiceLines|GenerateTilesheets|GenerateRibbons|GenerateDecals|GenerateSpellSpritesheets|GenerateSkyboxes|GenerateMetadata|DownloadAnimations|AddAnimationsTo3DModels|Rig3DModels|FinalAssetCleanup>");
				return;
			}
			try
			{
				var app = new AssetGenerationApp();
				await app.Run(args[0]);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error: {ex.Message}");
				Console.WriteLine(ex.StackTrace);
			}
		}
	}

	public class AssetGenerationApp
	{
		private readonly JsonElement _config;
		private readonly ProcessManager _procManager;
		private readonly OllamaClient _ollama;
		private readonly ComfyUIClient _comfy;
		private readonly string _cacheDir;

		public AssetGenerationApp()
		{
			string configPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");
			var options = new JsonDocumentOptions
			{
				AllowTrailingCommas = true,
				CommentHandling = JsonCommentHandling.Skip
			};
			_config = JsonDocument.Parse(File.ReadAllText(configPath), options).RootElement;

			_cacheDir = (_config.TryGetProperty("CacheDir", out var cacheDirProp) ? cacheDirProp.GetString() : null) ?? @"C:\temp\Realm.Assets.cache";
			if (!Directory.Exists(_cacheDir)) Directory.CreateDirectory(_cacheDir);

			_procManager = new ProcessManager(_config);
			_ollama = new OllamaClient(_config, _cacheDir);
			_comfy = new ComfyUIClient(_config, _cacheDir, _procManager);
		}

		public async Task Run(string taskName)
		{
			if (taskName.Equals("GenerateIcons", StringComparison.OrdinalIgnoreCase))
				await RunGenerateIcons();
			else if (taskName.Equals("RefineIcons", StringComparison.OrdinalIgnoreCase))
				await RunRefineIcons();
			else if (taskName.Equals("RefineIconsByFilename", StringComparison.OrdinalIgnoreCase))
				await RunRefineIconsByFilename();
			else if (taskName.Equals("Generate3DModels_Random", StringComparison.OrdinalIgnoreCase))
				await RunGenerate3DModels_Random();
			else if (taskName.Equals("Generate3DModels_Targeted", StringComparison.OrdinalIgnoreCase))
				await RunGenerate3DModels_Targeted();
			else if (taskName.Equals("Refine3DModels", StringComparison.OrdinalIgnoreCase))
				await RunRefine3DModels();
			else if (taskName.Equals("GenerateMusic", StringComparison.OrdinalIgnoreCase))
				await RunGenerateMusic();
			else if (taskName.Equals("GenerateSoundEffects", StringComparison.OrdinalIgnoreCase))
				await RunGenerateSoundEffects();
			else if (taskName.Equals("GenerateIconSoundEffects", StringComparison.OrdinalIgnoreCase))
				await RunGenerateIconSoundEffects();
			else if (taskName.Equals("Generate3DModelSoundEffects", StringComparison.OrdinalIgnoreCase))
				await RunGenerate3DModelSoundEffects();
			else if (taskName.Equals("GenerateVoiceLines", StringComparison.OrdinalIgnoreCase))
				await RunGenerateVoiceLines();
			else if (taskName.Equals("GenerateTilesheets", StringComparison.OrdinalIgnoreCase))
				await RunGenerateTilesheets();
			else if (taskName.Equals("GenerateRibbons", StringComparison.OrdinalIgnoreCase))
				await RunGenerateRibbons();
			else if (taskName.Equals("GenerateDecals", StringComparison.OrdinalIgnoreCase))
				await RunGenerateDecals();
			else if (taskName.Equals("GenerateSpellSpritesheets", StringComparison.OrdinalIgnoreCase))
				await RunGenerateSpellSpritesheets();
			else if (taskName.Equals("GenerateSkyboxes", StringComparison.OrdinalIgnoreCase))
				await RunGenerateSkyboxes();
			else if (taskName.Equals("GenerateMetadata", StringComparison.OrdinalIgnoreCase))
				await RunGenerateMetadata();
			else if (taskName.Equals("DownloadAnimations", StringComparison.OrdinalIgnoreCase))
				await RunDownloadAnimations();
			else if (taskName.Equals("AddAnimationsTo3DModels", StringComparison.OrdinalIgnoreCase))
				await RunAddAnimationsTo3DModels();
			else if (taskName.Equals("Rig3DModels", StringComparison.OrdinalIgnoreCase))
				await RunRig3DModels();
			else if (taskName.Equals("FinalAssetCleanup", StringComparison.OrdinalIgnoreCase) || taskName.Equals("CleanupAssets", StringComparison.OrdinalIgnoreCase) || taskName.Equals("AssetCleanup", StringComparison.OrdinalIgnoreCase))
				await RunFinalAssetCleanup();
			else
				Console.WriteLine($"Unknown task: {taskName}");

			_procManager.StopAll();
		}

		private string GetFluxPromptFromResponse(string jsonResponse)
		{
			try
			{
				int start = jsonResponse.IndexOf("{");
				int end = jsonResponse.LastIndexOf("}");
				if (start >= 0 && end > start)
				{
					var doc = JsonDocument.Parse(jsonResponse.Substring(start, end - start + 1));
					if (doc.RootElement.TryGetProperty("fluxPrompt", out var prop))
						return prop.GetString();
				}
			}
			catch { }
			return null;
		}

		private async Task ProcessIconResultsAsync(List<(string FileName, AssetCategory Category, string FluxPrompt, string OriginalPath, bool IsRefined)> allPrompts, string outputFolder)
		{
			// Step 2: ComfyUI Batch
			await _procManager.EnsureComfyUI();
			var results = new List<(string ImagePath, string FileName, AssetCategory Category, string OriginalPath, bool IsRefined)>();

			foreach (var item in allPrompts)
			{
				Console.WriteLine($"Generating image for: {item.FileName}");
				string imagePath = await _comfy.GenerateImage(item.FluxPrompt, item.FileName, outputFolder, 512);
				if (!string.IsNullOrEmpty(imagePath))
					results.Add((imagePath, item.FileName, item.Category, item.OriginalPath, item.IsRefined));
			}

			// Step 3: PngQuant Batch
			_procManager.StopAll();
			foreach (var res in results)
			{
				await RunPngQuant(res.ImagePath);
			}

			// Step 4: Categorize and Move
			await _procManager.EnsureOllama();
			foreach (var res in results)
			{
				await CategorizeAndMoveImage(res.ImagePath, res.FileName, res.Category, res.OriginalPath, res.IsRefined);
			}
		}

		private async Task<(string FileName, AssetCategory Category, int Quality)> Categorize3DProfileImage(string imgPath, AssetCategory originalCategory)
		{
			Console.WriteLine($"Categorizing 3D Profile Image: {Path.GetFileName(imgPath)}");
			bool isRiggable = originalCategory == AssetCategory.Characters;

			string prompt = $@"Analyze the provided image, which is intended to be a fantasy RTS profile image that will be converted to a {(isRiggable ? "auto-rigged humanoid T-pose" : "")} 3d model.
{(isRiggable ? "" : "Complete_Buildings are large completed man-made structures. Architectural_Components are independent parts that must be combined to form a full structure, such as: doors, pillars, arches, walls. Environment is naturally occurring. Projectiles are ranged ammunition, spells, or thrown missiles. Props are a catch-all for anything that doesnt match another category")}
Return ONLY a valid JSON object with exactly these three fields:
- ""fileName"": a concise 1-3 word snake_case description of what the 3d model should be named. Do NOT include generic words like ""icon"", ""model"", ""game"", ""asset"".
- ""category"": one exact value from this list only: {(isRiggable ? "TPose_Riggable_Humanoid_Characters, NonRiggable_Characters" : "TPose_Riggable_Humanoid_Characters, NonRiggable_Characters, Complete_Buildings, Architectural_Components, Weapons, Environment, Projectiles, Props")}
- ""quality"": an integer 1, 2, or 3 where:
  3 = thematically fits a fantasy RTS game and should be converted to a 3d model
  2 = usable but has minor issues
  1 = does not fit the style, wouldn't make sense as a 3d model (is an icon or texture), is low quality {(isRiggable ? "or cant be auto-rigged" : "")}";

			string resultJson = await _ollama.AnalyzeImage(prompt, imgPath);
			try
			{
				int start = resultJson.IndexOf("{");
				int end = resultJson.LastIndexOf("}");
				if (start >= 0 && end > start)
				{
					var doc = JsonDocument.Parse(resultJson.Substring(start, end - start + 1));
					var root = doc.RootElement;
					string fileName = root.GetProperty("fileName").GetString().Trim().Replace(" ", "_").ToLower();
					string categoryInput = root.GetProperty("category").GetString().Trim();

					bool isRiggableCharacter = categoryInput.Equals("TPose_Riggable_Humanoid_Characters", StringComparison.OrdinalIgnoreCase);
					bool isNonRiggableCharacter = categoryInput.Equals("NonRiggable_Characters", StringComparison.OrdinalIgnoreCase);
					bool isCompleteBuilding = categoryInput.Equals("Complete_Buildings", StringComparison.OrdinalIgnoreCase);
					bool isArchitecturalComponent = categoryInput.Equals("Architectural_Components", StringComparison.OrdinalIgnoreCase);

					AssetCategory category;
					if (categoryInput.Contains("Characters", StringComparison.OrdinalIgnoreCase))
						category = AssetCategory.Characters;
					else if (categoryInput.Contains("Buildings", StringComparison.OrdinalIgnoreCase) || isArchitecturalComponent)
						category = AssetCategory.Buildings;
					else if (categoryInput.Equals("Weapons", StringComparison.OrdinalIgnoreCase))
						category = AssetCategory.Weapons;
					else if (categoryInput.Equals("Environment", StringComparison.OrdinalIgnoreCase))
						category = AssetCategory.Environment;
					else if (categoryInput.Equals("Projectiles", StringComparison.OrdinalIgnoreCase))
						category = AssetCategory.Projectiles;
					else
						category = AssetCategory.Props;

					int quality = 1;
					if (root.GetProperty("quality").ValueKind == JsonValueKind.Number)
						quality = root.GetProperty("quality").GetInt32();
					return (fileName, category, quality);
				}
			}
			catch { }
			return ("unknown", AssetCategory.Props, 1);
		}

		private string Get3DModelFluxPromptPrefix(AssetCategory category)
		{
			string prefix = "retro 3d RTS model game asset rendered as a single front-facing 3d orthographic model render image with centered composition filling frame against transparent background with even soft diffuse lighting from front, no harsh shadows or highlights, using vibrant bold limited palette high-contrast colors, bol sillhouette, simplified shapes, exaggerated proportions, clean lines. ";
			if (category == AssetCategory.Characters) prefix += "T-pose with arms extended straight out to the sides horizontally, parallel to the ground, open empty hands, facing directly forward. ";
			return prefix;
		}

		private async Task Process3DModelResultsAsync(List<(string FileName, AssetCategory Category, string FluxPrompt)> allPrompts, string outputFolder)
		{
			// Step 2: Flux Image Generation
			await _procManager.EnsureComfyUI();
			var imageResults = new List<(string ImagePath, string FileName, AssetCategory Category)>();

			foreach (var item in allPrompts)
			{
				Console.WriteLine($"Generating image for: {item.FileName}");
				string imagePath = await _comfy.GenerateImage(item.FluxPrompt, item.FileName, outputFolder, 1024, removeBackground: true);
				if (!string.IsNullOrEmpty(imagePath))
				{
					imageResults.Add((imagePath, item.FileName, item.Category));
				}
				else
				{
					Console.WriteLine($"Image failed to generate: {item.FileName}");
				}
			}

			// Step 2.5: Categorize 3D Profile Images
			await _procManager.EnsureOllama();
			var filteredImageResults = new List<(string ImagePath, string FileName, AssetCategory Category)>();
			foreach (var item in imageResults)
			{
				var (newFileName, _, quality) = await Categorize3DProfileImage(item.ImagePath, item.Category);
				if (quality > 1)
				{
					filteredImageResults.Add((item.ImagePath, newFileName, item.Category));
				}
				else
				{
					Console.WriteLine($"Filtering out poor quality image: {item.FileName}");
				}
			}

			// Step 3: Mesh Generation (Batch)
			await _procManager.EnsureComfyUI();
			var meshResults = new List<(string MeshPath, string ImagePath, string FileName, AssetCategory Category, string? TexturedMeshPath)>();

			foreach (var item in filteredImageResults)
			{
				Console.WriteLine($"Generating initial mesh for: {item.FileName}");
				int faceCount = item.Category == AssetCategory.Characters ? 6500 : (item.Category == AssetCategory.Buildings ? 5000 : (item.Category == AssetCategory.Projectiles ? 25 : 850));
				var overrides = new Dictionary<string, Dictionary<string, object>> {
					{ "2", new Dictionary<string, object> { { "image", item.ImagePath } } },
					{ "46", new Dictionary<string, object> { { "max_facenum", faceCount * 2 }, { "vertex_count", Math.Max(faceCount, 100) } } },
					{ "47", new Dictionary<string, object> { { "max_facenum", faceCount } } }
				};
				string meshPath = await _comfy.RunWorkflow(@"ComfyUI\Workflows\Hunyuan3d-v2.1_Mesh_From_Image.json", overrides, item.FileName, ".glb");
				if (!string.IsNullOrEmpty(meshPath))
				{
					try
					{
						var cleanedMeshPath = await RunGlCleanup(meshPath);
						meshResults.Add((cleanedMeshPath, item.ImagePath, item.FileName, item.Category, null));
					}
					catch
					{
						Console.WriteLine($"Failed to generate mesh");
					}
				}
			}

			// Step 4: Texture Painting (Batch)
			for (int i = 0; i < meshResults.Count; i++)
			{
				var item = meshResults[i];
				Console.WriteLine($"Painting textures for: {item.FileName}");
				var overrides = new Dictionary<string, Dictionary<string, object>> {
					{ "68", new Dictionary<string, object> { { "image", item.ImagePath } } },
					{ "183", new Dictionary<string, object> { { "glb_path", item.MeshPath } } },
					{ "153", new Dictionary<string, object> { { "value", 1024 } } }, // texture size
					{ "187", new Dictionary<string, object> { { "enabled", item.Category == AssetCategory.Characters || item.Category == AssetCategory.Buildings } } } // team color mask
                };

				string texturedMeshPath = await _comfy.RunWorkflow(@"ComfyUI\Workflows\Hunyuan3d-v2.1_Paint_Mesh.json", overrides, item.FileName + "_final", ".glb");
				meshResults[i] = (item.MeshPath, item.ImagePath, item.FileName, item.Category, texturedMeshPath);
			}

			// Step 5: Bone rigging (Batch)
			var finalMeshPaths = new List<string>();
			foreach (var item in meshResults)
			{
				var glbPath = item.TexturedMeshPath;
				if (item.Category == AssetCategory.Characters)
				{
					var riggedGlbPath = Path.Combine(Path.GetDirectoryName(glbPath), Path.GetFileNameWithoutExtension(glbPath) + "_rigged" + Path.GetExtension(glbPath));
					if (File.Exists(riggedGlbPath))
					{
						Console.WriteLine("Skipping model rigging, already completed: " + Path.GetFileNameWithoutExtension(glbPath));
					}
					else
					{
						glbPath = await RigHumanoid3DModel(item.TexturedMeshPath);
					}
				}
				if (!string.IsNullOrEmpty(glbPath))
				{
					await RunGltfPack(glbPath);
					finalMeshPaths.Add(glbPath);
				}
			}

			// Step 6: Categorize and Move 3D Models (Batch)
			await _procManager.EnsureOllama();
			foreach (var finalMeshPath in finalMeshPaths)
			{
				await CategorizeAndMove3DModel(finalMeshPath);
			}

			_procManager.StopAll();
		}

		private async Task<(string FileName, AssetCategory Category, string FluxPrompt)> Create3DModelPromptAsync(AssetCategory category, string fileName)
		{
			string description = fileName.Replace("_", " ");
			Console.WriteLine($"Generating flux prompt for: {fileName}");

			string assetType = category == AssetCategory.Characters ? " a single front-facing 3d orthographic model render of a unit in strict T-Pose for " : " a single front-facing 3d orthographic model render for ";
			string poseCriteria = category == AssetCategory.Characters ? "It should be in a standard T-pose with empty hands. There should be no floor. Do not include other objects besides the humanoid character and their outfit. " : "";
			string teamColorCriteria = (category == AssetCategory.Characters || category == AssetCategory.Buildings)
				? "Pick one specific small minor accent piece of the composition to act as the team-color placeholder and specify that this piece exclusively is to be colored in vibrant neon magenta (#FF00FF)."
				: "";

			string fluxPromptReq = $@"Generate a detailed flux prompt for rendering {assetType} a fantasy RTS game asset described as: {description}.
{poseCriteria}
{teamColorCriteria}
Avoid micro-detail
front-facing view only, centered, with no background or environment.
Output in JSON format with only this field: fluxPrompt";

			string fluxPromptJson = await _ollama.GenerateText(fluxPromptReq);
			string fluxPrompt = GetFluxPromptFromResponse(fluxPromptJson);

			if (string.IsNullOrEmpty(fluxPrompt)) fluxPrompt = description;

			return (fileName, category, Get3DModelFluxPromptPrefix(category) + fluxPrompt);
		}

		private async Task Generate3DModel(AssetCategory category, string fileName)
		{
			await Generate3DModels(category, new List<string> { fileName });
		}

		private async Task Generate3DModels(AssetCategory category, List<string> fileNames)
		{
			await _procManager.EnsureOllama();

			var genConfig = _config.GetProperty("Tasks").GetProperty("Generate3DModels_Random");
			string outputFolder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
				genConfig.ValueKind != JsonValueKind.Undefined && genConfig.TryGetProperty("OutputFolder", out var outProp) ? outProp.GetString() : "3DModels_Output"));
			if (!Directory.Exists(outputFolder)) Directory.CreateDirectory(outputFolder);

			var allPrompts = new List<(string FileName, AssetCategory Category, string FluxPrompt)>();
			foreach (var fileName in fileNames)
			{
				var prompt = await Create3DModelPromptAsync(category, fileName);
				allPrompts.Add(prompt);
			}

			if (allPrompts.Count > 0)
			{
				await Process3DModelResultsAsync(allPrompts, outputFolder);
			}
		}

		private async Task RunGenerate3DModels_Random()
		{
			Console.WriteLine("Starting Generate3DModels_Random task...");
			await _procManager.EnsureOllama();

			var genConfig = _config.GetProperty("Tasks").GetProperty("Generate3DModels_Random");
			string outputFolder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
				genConfig.ValueKind != JsonValueKind.Undefined && genConfig.TryGetProperty("OutputFolder", out var outProp) ? outProp.GetString() : "3DModels_Output"));
			if (!Directory.Exists(outputFolder)) Directory.CreateDirectory(outputFolder);

			int promptCountPerType = genConfig.GetProperty("PromptCount").GetInt32();
			var categories = new[] { AssetCategory.Characters, AssetCategory.Buildings, AssetCategory.Props, AssetCategory.Projectiles };

			var allPrompts = new List<(string FileName, AssetCategory Category, string FluxPrompt)>();

			foreach (var category in categories)
			{
				Console.WriteLine($"Generating filenames for category: {category}");
				string fileNamePrompt = $@"Timestamp: {DateTime.Now:yyyy-MM-dd}
Pick {promptCountPerType} random 3d model game assets for a fantasy RTS game that belong in the category '{category}'. For each object, generate a 2 words snake_case format filename to describe the asset.
Do not include the words from the category name in the filename.
Output only the {promptCountPerType} snake_case filenames each on a separate line.";

				string fileNamesRaw = await _ollama.GenerateText(fileNamePrompt);
				var fileNames = ParseSnakeCaseList(fileNamesRaw, promptCountPerType, requireUnderscore: true);

				foreach (var fileName in fileNames)
				{
					var promptSpec = await Create3DModelPromptAsync(category, fileName);
					allPrompts.Add(promptSpec);
				}
			}

			if (allPrompts.Count > 0)
			{
				await Process3DModelResultsAsync(allPrompts, outputFolder);
			}
		}

		private record ModelGenerationRequest(string Description, AssetCategory Category, int MaxFilenames);
		private async Task RunGenerate3DModels_Targeted()
		{
			Console.WriteLine("Starting Generate3DModels_Targeted task...");

			var genConfig = _config.GetProperty("Tasks").GetProperty("Generate3DModels_Targeted");
			string outputFolder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory,
				genConfig.ValueKind != JsonValueKind.Undefined && genConfig.TryGetProperty("OutputFolder", out var outProp) ? outProp.GetString() : "3DModels_Output"));
			if (!Directory.Exists(outputFolder)) Directory.CreateDirectory(outputFolder);

			string GeneratePrompt(ModelGenerationRequest item) => $@"Timestamp: {DateTime.Now:yyyy-MM-dd}
Generate a comprehensive list of every 3d object file for a fantasy RTS game of description '{item.Description}' that belong in a folder '{item.Category}' (max {item.MaxFilenames} items). For each object, generate a 2 words snake_case format filename to describe the asset.
Don't include the folder in the name.
Output only the snake_case filenames each on a separate line.";

			var requests = new List<ModelGenerationRequest>()
			{
                //new ModelGenerationRequest("bridge", AssetCategory.Environment, 25),
                // new ModelGenerationRequest("tree", AssetCategory.Environment, 25),
                // new ModelGenerationRequest("rock", AssetCategory.Environment, 25),
                new ModelGenerationRequest("slender fin-stabilized", AssetCategory.Projectiles, 25),
                new ModelGenerationRequest("self-propelled or guided", AssetCategory.Projectiles, 25),
                new ModelGenerationRequest("lobbed heavy impact munition", AssetCategory.Projectiles, 25),
                new ModelGenerationRequest("thrown, tumbling, or radial", AssetCategory.Projectiles, 25),
                new ModelGenerationRequest("magic spell, ability, or bio-organic core", AssetCategory.Projectiles, 25),
                new ModelGenerationRequest("artillery", AssetCategory.Projectiles, 25)
            };

			foreach (var request in requests)
			{
				await _procManager.EnsureOllama();
				var allPrompts = new List<(string FileName, AssetCategory Category, string FluxPrompt)>();
				Console.WriteLine($"Generating filenames for targeted category: {request.Category}");

				var prefix = ReplaceSpacesWithUnderscore(request.Description + " " + request.Category);
				var prompt = GeneratePrompt(request);
				string fileNamesRaw = await _ollama.GenerateText(prompt);
				var fileNames = ParseSnakeCaseList(fileNamesRaw, request.MaxFilenames)
					.Select(x => prefix + "_" + x)
					.ToList();

				foreach (var fileName in fileNames)
				{
					var promptSpec = await Create3DModelPromptAsync(request.Category, fileName);
					allPrompts.Add(promptSpec);
				}

				if (allPrompts.Count > 0)
				{
					await Process3DModelResultsAsync(allPrompts, outputFolder);
				}
			}
		}

		private string ReplaceSpacesWithUnderscore(string text)
		{
			return System.Text.RegularExpressions.Regex.Replace(text, @"\s+", "_", System.Text.RegularExpressions.RegexOptions.IgnoreCase).Trim('_');
		}

		private static string DeduplicateSnakeCaseTerms(string text)
		{
			if (string.IsNullOrWhiteSpace(text)) return string.Empty;
			var parts = text.Split('_', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
				.Distinct(StringComparer.OrdinalIgnoreCase)
				.ToList();
			return parts.Count > 0 ? parts.Aggregate((a, b) => $"{a}_{b}") : string.Empty;
		}

		private static List<string> ParseSnakeCaseList(IEnumerable<string> lines, int count, bool requireUnderscore = false)
		{
			if (lines == null) return new List<string>();

			return lines
				.Select(f => f.Trim(' ', '*', '-', '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', '.', '"', '\''))
				.Select(f => f.Replace(" ", "_").ToLower())
				.Select(DeduplicateSnakeCaseTerms)
				.Where(f => !string.IsNullOrWhiteSpace(f) && (!requireUnderscore || f.Contains("_")))
				.Distinct()
				.Take(count)
				.ToList();
		}

		private static List<string> ParseSnakeCaseList(string rawText, int count, bool requireUnderscore = false)
		{
			if (string.IsNullOrWhiteSpace(rawText)) return new List<string>();
			return ParseSnakeCaseList(rawText.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), count, requireUnderscore);
		}

		private async Task RunRefine3DModels()
		{
			Console.WriteLine("Starting Refine3DModels task...");
			await _procManager.EnsureOllama();

			var config = _config.GetProperty("Tasks").GetProperty("Refine3DModels");
			string referenceFolder = config.GetProperty("ReferenceImagesFolder").GetString();
			if (!Directory.Exists(referenceFolder))
			{
				referenceFolder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, referenceFolder));
			}

			string outputFolder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "3DModels_Output"));
			if (!Directory.Exists(outputFolder)) Directory.CreateDirectory(outputFolder);

			var categories = new[] { AssetCategory.Buildings, AssetCategory.Characters, AssetCategory.Environment, AssetCategory.Props, AssetCategory.Projectiles };
			var allPrompts = new List<(string FileName, AssetCategory Category, string FluxPrompt)>();

			foreach (var category in categories)
			{
				string categoryPath = Path.Combine(referenceFolder, category.ToString());
				if (!Directory.Exists(categoryPath)) continue;

				var models = Directory.GetFiles(categoryPath, "*.glb", SearchOption.AllDirectories);
				foreach (var glb in models)
				{
					string baseName = Path.GetFileNameWithoutExtension(glb);
					string glTurnaroundPng = await RunGlTurnaround(glb);

					if (string.IsNullOrEmpty(glTurnaroundPng) || !File.Exists(glTurnaroundPng))
					{
						Console.WriteLine($"Failed to generate turnaround for: {glb}");
						continue;
					}

					Console.WriteLine($"Extracting prompt from: {baseName}");
					string resultJson = await Analyze3DModelTurnaroundImage(glTurnaroundPng);

					try
					{
						int start = resultJson.IndexOf("{");
						int end = resultJson.LastIndexOf("}");
						if (start >= 0 && end > start)
						{
							var doc = JsonDocument.Parse(resultJson.Substring(start, end - start + 1));
							var originalFileName = ReplaceSpacesWithUnderscore(Path.GetFileNameWithoutExtension(glb));

							string fluxPrompt = doc.RootElement.GetProperty("fluxPrompt").GetString();
							var refinedPrompt = await Create3DModelPromptAsync(category, originalFileName + " " + fluxPrompt);

							allPrompts.Add((originalFileName, category, Get3DModelFluxPromptPrefix(category) + refinedPrompt.FluxPrompt));
						}
					}
					catch { }
				}
			}

			await Process3DModelResultsAsync(allPrompts, outputFolder);
		}

		private async Task<string> Analyze3DModelTurnaroundImage(string imagePath)
		{
			string prompt = "This is a 3d model game asset rendered as a multi-view orthographic character sheet. Please ignore the alternate angles and focus only on front view. Give me a 1-3 word snake-cased file name for the model, but don't include generic words, only describe the purpose of the asset so a user can easily find it later. Also, please determine true or false if the object is a riggable humanoid. Also, please output a flux prompt to re-generate ONLY the front-view for this character. Output in json format with 3 fields: fileName and fluxPrompt and riggable";
			return await _ollama.AnalyzeImage(prompt, imagePath);
		}


		private async Task RunGenerateIcons()
		{
			Console.WriteLine("Starting GenerateIcons task...");
			await _procManager.EnsureOllama();

			var genConfig = _config.GetProperty("Tasks").GetProperty("GenerateIcons");
			int promptCountPerCategory = genConfig.GetProperty("PromptCount").GetInt32();
			string outputFolder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, genConfig.GetProperty("OutputFolder").GetString()));
			if (!Directory.Exists(outputFolder)) Directory.CreateDirectory(outputFolder);

			var categoryStructure = new Dictionary<AssetCategory, string[]>
			{
				{ AssetCategory.Items, new[] { "Items", "Artifacts", "Crafting Materials", "Recipe Ingredients", "Props" } },
				{ AssetCategory.Abilities, new[] { "Abilities", "Magic Spell", "Research Technology Upgrades", "Support Buffing Aura" } },
				{ AssetCategory.UI, new[] { "UI HUD Commands" } },
				{ AssetCategory.Buildings, new[] { "Buildings", "Structures", "Architectural Pieces" } }
			};

			var categories = categoryStructure.SelectMany(kvp => kvp.Value.Select(sub => new { Parent = kvp.Key, Sub = sub }))
				.OrderBy(_ => Random.Shared.Next())
				.ToList();

			var allPrompts = new List<(string FileName, AssetCategory Category, string FluxPrompt, string OriginalPath, bool IsRefined)>();

			var excludedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (Directory.Exists(outputFolder))
			{
				foreach (var file in Directory.GetFiles(outputFolder, "*.png", SearchOption.AllDirectories))
				{
					excludedFileNames.Add(Path.GetFileNameWithoutExtension(file));
				}
			}

			foreach (var item in categories)
			{
				string categoryPromptName = item.Sub;
				AssetCategory parentCategory = item.Parent;
				Console.WriteLine($"Generating filenames for category: {categoryPromptName} (Parent: {parentCategory})");
				string fileNamePrompt = $@"Timestamp: {DateTime.Now:yyyy-MM-dd}
Pick {promptCountPerCategory} random 2d game icons for a fantasy RTS game that belong in the category '{categoryPromptName}'. For each icon, generate a 2 words snake_case format filename to describe the icon.
Do not include the words from the category name in the filename.
Output only the {promptCountPerCategory} snake_case filenames each on a separate line.";

				string fileNamesRaw = await _ollama.GenerateText(fileNamePrompt);
				var fileNames = ParseSnakeCaseList(fileNamesRaw, promptCountPerCategory, requireUnderscore: true);

				foreach (var fileName in fileNames)
				{
					if (excludedFileNames.Contains(fileName)) continue;

					string description = fileName.Replace("_", " ");
					Console.WriteLine($"Generating flux prompt for: {fileName}");

					string fluxPromptReq = $@"Generate a detailed flux prompt for creating a 2d game icon for a fantasy RTS game asset described as: {description}.
Output in JSON format with only this field: fluxPrompt";

					string fluxPromptJson = await _ollama.GenerateText(fluxPromptReq);
					string fluxPrompt = GetFluxPromptFromResponse(fluxPromptJson);

					if (string.IsNullOrEmpty(fluxPrompt)) fluxPrompt = description;

					string finalPrompt = "2d game icon for fantasy RTS, centered composition, limited color palette, high contrast, suitable for UI use, scalable to small sizes. " + fluxPrompt;
					allPrompts.Add((fileName, parentCategory, finalPrompt, null, false));
					excludedFileNames.Add(fileName);
				}
			}

			await ProcessIconResultsAsync(allPrompts, outputFolder);
		}

		private async Task RunRefineIcons()
		{
			Console.WriteLine("Starting RefineIcons task...");
			await _procManager.EnsureOllama();

			var refineConfig = _config.GetProperty("Tasks").GetProperty("RefineIcons");
			string referenceFolder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, refineConfig.GetProperty("ReferenceImagesFolder").GetString()));
			string outputFolder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, refineConfig.GetProperty("OutputFolder").GetString()));
			if (!Directory.Exists(outputFolder)) Directory.CreateDirectory(outputFolder);

			var allPrompts = new List<(string FileName, AssetCategory Category, string FluxPrompt, string OriginalPath, bool IsRefined)>();
			var images = Directory.GetFiles(referenceFolder, "*.png", SearchOption.AllDirectories);

			foreach (var img in images)
			{
				Console.WriteLine($"Extracting prompt from: {Path.GetFileName(img)}");
				string resultJson = await AnalyzeIconImage(img);
				try
				{
					int start = resultJson.IndexOf("{");
					int end = resultJson.LastIndexOf("}");
					if (start >= 0 && end > start)
					{
						var doc = JsonDocument.Parse(resultJson.Substring(start, end - start + 1));
						string fileName = doc.RootElement.GetProperty("fileName").GetString();
						string fluxPrompt = doc.RootElement.GetProperty("fluxPrompt").GetString();
						allPrompts.Add((fileName, AssetCategory.Props, "Fantasy RTS Game Icon " + fluxPrompt, img, true));
					}
				}
				catch { }
			}

			await ProcessIconResultsAsync(allPrompts, outputFolder);
		}

		private async Task<string> AnalyzeIconImage(string imagePath)
		{
			string prompt = "This is a Fantasy RTS Icon game asset. Give me a 1-3 word snake-cased file name for this image, but don't include generic words, only describe the purpose of the asset so a user can easily find it later. Also, please output a flux prompt to re-generate a similar image. Output in json format with 2 fields: fileName and fluxPrompt";
			return await _ollama.AnalyzeImage(prompt, imagePath);
		}

		private async Task RunRefineIconsByFilename()
		{
			Console.WriteLine("Starting RefineIconsByFilename task...");
			await _procManager.EnsureOllama();

			var refineConfig = _config.GetProperty("Tasks").GetProperty("RefineIcons");
			string referenceFolder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, refineConfig.GetProperty("ReferenceImagesFolder").GetString()));
			string outputFolder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, refineConfig.GetProperty("OutputFolder").GetString()));
			if (!Directory.Exists(outputFolder)) Directory.CreateDirectory(outputFolder);

			var allPrompts = new List<(string FileName, AssetCategory Category, string FluxPrompt, string OriginalPath, bool IsRefined)>();
			var images = Directory.GetFiles(referenceFolder, "*.png", SearchOption.AllDirectories);

			foreach (var img in images)
			{
				string baseName = Path.GetFileNameWithoutExtension(img);
				Console.WriteLine($"Generating prompt for filename: {baseName}");

				string prompt = $"FileName: {baseName} . This is a filename for a Fantasy RTS Icon game asset. Please output a flux prompt to generate the image that you think belongs in this file. Output in json format with 1 field: fluxPrompt";
				string resultJson = await _ollama.GenerateText(prompt);
				string fluxPrompt = GetFluxPromptFromResponse(resultJson);

				if (!string.IsNullOrEmpty(fluxPrompt))
				{
					allPrompts.Add((baseName, AssetCategory.Props, "Fantasy RTS Game Icon " + fluxPrompt, img, true));
				}
			}

			await ProcessIconResultsAsync(allPrompts, outputFolder);
		}

		private async Task RunGenerateMusic()
		{
			Console.WriteLine("Starting GenerateMusic task...");
			await _procManager.EnsureOllama();

			var genConfig = _config.GetProperty("Tasks").GetProperty("GenerateMusic");
			int promptCount = genConfig.GetProperty("PromptCount").GetInt32();
			string outputFolder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, genConfig.GetProperty("OutputFolder").GetString()));
			if (!Directory.Exists(outputFolder)) Directory.CreateDirectory(outputFolder);

			string tempMusicFolder = Path.Combine(AppContext.BaseDirectory, "GenerateMusic");
			if (!Directory.Exists(tempMusicFolder)) Directory.CreateDirectory(tempMusicFolder);

			string venvPath = await EnsureSharedPythonEnvironment();

			Console.WriteLine($"Generating {promptCount} music styles...");
			string inspirationPrompt = $@"Timestamp: {DateTime.Now:yyyy-MM-dd}
Pick {promptCount} random famous music tracks from video games of any genre. Output each on a separate line formatted: '{{SongTitle}}' from the game '{{GameTitle}}'";
			string inspirationRaw = await _ollama.GenerateText(inspirationPrompt);
			List<string> inspirations = inspirationRaw.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).ToList();

			string stylesPrompt = $@"Timestamp: {DateTime.Now:yyyy-MM-dd}
Pick {promptCount} random diverse music metadata that could be used to describe the comprehensive list of music needed to be composed for a new video game. Output each music composition on a separate line, where each line is a list of key/value pairs based on the following list of keys:
            song_title: Name of the new song
            game_world_location: Game event or setting or world location where the music should be played
            genre_style: The overarching musical genre, sub-genre, or cinematic style.
            tempo_bpm: The speed of the track, defined by a specific Beats Per Minute (BPM) or a traditional Italian tempo term.
            rhythm_feel: The groove, time signature, drum pattern structure, or rhythmic swing.
            primary_instruments: The lead and accompanying instruments or synthesizers featured in the generation.
            melodic_harmonic_technique: The method in which the notes are played (e.g., how the melody moves or how notes are articulated).
            key_signature: The musical key that establishes the tonal center and overall harmonic foundation.
            chord_progression_type: The complexity, voicing style, or modal framework of the underlying chords.
            dynamic_arrangement_movement: The structural changes in volume, intensity, or instrumentation over time.
            mood_emotion: The psychological vibe, feeling, or atmosphere the music invokes.
            production_effects_style: The specific studio effects, audio manipulation, or vintage/modern processing applied to the instruments.
            mix_master_quality: The final sonic texture, spatial positioning, and overall audio fidelity profile.";

			string stylesRaw = await _ollama.GenerateText(stylesPrompt);
			var styles = new Queue<string>(stylesRaw.Split(new char[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries));

			foreach (var inspiration in inspirations)
			{
				if (!styles.TryDequeue(out var style))
				{
					break;
				}

				Console.WriteLine($"Generating prompt for style");
				string stylePromptReq = $@"Generate a detailed music generation AI prompt for a song with this metadata: '{style}'.";
				string styleRaw = await _ollama.GenerateText(stylePromptReq);

				Console.WriteLine($"Generating prompt for inspiration");
				string trackPromptReq = $@"Generate a detailed music generation AI prompt for creating an alternate version of this specific video game song. Don't use any proprietary terms, just describe the music for: '{inspiration}'";
				string trackRaw = await _ollama.GenerateText(trackPromptReq);

				Console.WriteLine($"Generating combined song prompt");
				string songPromptReq = $@"Combine these 2 separate song descriptions into a single cohesive prompt for AI music generation, using terms an AI music model would likely understand, re-purposed for a fantasy RTS game track. Song1: '{styleRaw}' Song2: '{trackRaw}'.
Also, generate a 2-word snake_case filename for this track.
Output ONLY a JSON object with 'fileName' and 'prompt' fields.";

				string songJsonRaw = await _ollama.GenerateText(songPromptReq);
				try
				{
					int start = songJsonRaw.IndexOf("{");
					int end = songJsonRaw.LastIndexOf("}");
					if (start >= 0 && end > start)
					{
						var doc = JsonDocument.Parse(songJsonRaw.Substring(start, end - start + 1));
						string fileName = doc.RootElement.GetProperty("fileName").GetString();
						string prompt = doc.RootElement.GetProperty("prompt").GetString();

						if (string.IsNullOrEmpty(fileName) || string.IsNullOrEmpty(prompt)) continue;

						fileName = fileName.Split('.')[0];
						string finalOggPath = Path.Combine(outputFolder, $"{fileName}.ogg");

						if (File.Exists(finalOggPath))
						{
							Console.WriteLine($"Skipping existing music track: {fileName}");
							continue;
						}

						string tempWavPath = Path.Combine(tempMusicFolder, $"{fileName}.wav");

						Console.WriteLine($"Generating music for: {fileName} ({prompt})");
						await RunAudioGenerationPython(venvPath, "generate_music.py", prompt, tempWavPath, genConfig.GetProperty("model").GetString(), genConfig.GetProperty("TrackLengthInMinutes").GetInt32() * 60);

						string tempOggPath = Path.Combine(tempMusicFolder, $"{fileName}.ogg");
						if (File.Exists(tempOggPath))
						{
							File.Move(tempOggPath, finalOggPath, true);
							Console.WriteLine($"Moved final music track to: {finalOggPath}");
						}
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Failed to generate prompt for style {style}: {ex.Message}");
				}
			}
		}

		private async Task<string> EnsureSharedPythonEnvironment()
		{
			string venvPath = Path.Combine(AppContext.BaseDirectory, "venv_shared");
			string reqFile = Path.Combine(AppContext.BaseDirectory, "requirements_audio.txt");
			string reqContent = @"# PyTorch (with CUDA 12.8 support)
--extra-index-url https://download.pytorch.org/whl/cu128
torch==2.7.1+cu128
torchaudio==2.7.1+cu128
torchvision
kornia
timm

# DSP & audio processing
scipy
numpy
soundfile
pyloudnorm
pedalboard
qwen-tts
pillow
opencv-python
librosa
accelerate
transformers<5.0.0
";

			if (!File.Exists(reqFile) || await File.ReadAllTextAsync(reqFile) != reqContent)
			{
				await File.WriteAllTextAsync(reqFile, reqContent);
			}

			await _procManager.EnsurePythonEnv(venvPath, null, "3.12.11", "", reqFile);

			string pythonExe = Path.Combine(venvPath, OperatingSystem.IsWindows() ? "Scripts\\python.exe" : "bin/python");
			Console.WriteLine("[INFO] Installing stable-audio-3 from GitHub...");
			await _procManager.RunProcessAsync("uv", $"pip install git+https://github.com/Stability-AI/stable-audio-3.git --python \"{pythonExe}\"");

			Console.WriteLine("[INFO] Installing moss_soundeffect_v2 from GitHub...");
			await _procManager.RunProcessAsync("uv", $"pip install git+https://github.com/OpenMOSS/MOSS-TTS.git#subdirectory=moss_soundeffect_v2 --python \"{pythonExe}\"");

			Console.WriteLine("[INFO] Forcing transformers version to < 5.0.0...");
			await _procManager.RunProcessAsync("uv", $"pip install \"transformers<5.0.0\" --python \"{pythonExe}\"");

			return venvPath;
		}

		private async Task RunAudioGenerationPython(string venvPath, string scriptName, string prompt, string outputPath, string model, int duration)
		{
			string pythonExe = Path.Combine(venvPath, OperatingSystem.IsWindows() ? "Scripts\\python.exe" : "bin/python");
			string scriptPath = Path.Combine(AppContext.BaseDirectory, scriptName);

			if (!File.Exists(scriptPath))
			{
				Console.WriteLine($"Audio generation script not found at: {scriptPath}");
				return;
			}

			Console.WriteLine($"Executing {scriptName} for: {Path.GetFileName(outputPath)}...");
			int exitCode = await _procManager.RunProcessAsync(pythonExe, $"\"{scriptPath}\" --prompt \"{prompt}\" --output \"{outputPath}\" --model \"{model}\" --duration {duration}");

			if (exitCode != 0)
			{
				Console.WriteLine($"{scriptName} failed with exit code: {exitCode}");
			}
		}

		private async Task RunBatchAudioGenerationPython(string venvPath, string scriptName, List<object> batch, string model)
		{
			string pythonExe = Path.Combine(venvPath, OperatingSystem.IsWindows() ? "Scripts\\python.exe" : "bin/python");
			string scriptPath = Path.Combine(AppContext.BaseDirectory, scriptName);

			if (!File.Exists(scriptPath))
			{
				Console.WriteLine($"Audio generation script not found at: {scriptPath}");
				return;
			}

			string batchFilePath = Path.Combine(Path.GetTempPath(), $"audio_batch_{Guid.NewGuid()}.json");
			await File.WriteAllTextAsync(batchFilePath, JsonSerializer.Serialize(batch));

			Console.WriteLine($"Executing {scriptName} in batch mode for {batch.Count} items...");
			int exitCode = await _procManager.RunProcessAsync(pythonExe, $"\"{scriptPath}\" --batch \"{batchFilePath}\" --model \"{model}\"");

			if (File.Exists(batchFilePath))
			{
				try { File.Delete(batchFilePath); } catch { }
			}

			if (exitCode != 0)
			{
				Console.WriteLine($"{scriptName} failed with exit code: {exitCode}");
			}
		}

		private async Task RunGenerateSoundEffects()
		{
			Console.WriteLine("Starting GenerateSoundEffects task...");
			await _procManager.EnsureOllama();

			var genConfig = _config.GetProperty("Tasks").GetProperty("GenerateSoundEffects");
			int promptCount = genConfig.GetProperty("PromptCount").GetInt32();
			string outputFolder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, genConfig.GetProperty("OutputFolder").GetString()));
			if (!Directory.Exists(outputFolder)) Directory.CreateDirectory(outputFolder);

			var categories = new[] {
				new { Name = "User Interface (UI) & Global HUD", Folder = "UI", Duration = 1 },
				new { Name = "Unit Vocalizations", Folder = "Units", Duration = 3 },
				new { Name = "Movement & Foley", Folder = "Foley", Duration = 3 },
				new { Name = "Combat & Weapons", Folder = "Combat", Duration = 3 },
				new { Name = "Impacts & Destructibles", Folder = "Effects", Duration = 3 },
				new { Name = "Abilities, Spells, & Status Effects", Folder = "Abilities", Duration = 3 },
				new { Name = "Environment & Ambience", Folder = "Ambience", Duration = 5 }
			};

			var items = new List<(string FileName, string Description, string FinalOggPath, int Duration)>();

			foreach (var item in categories)
			{
				string category = item.Name;
				string folder = item.Folder;
				string categoryOutputFolder = Path.Combine(outputFolder, folder);

				Console.WriteLine($"Generating sound effect names for category: {category}");
				string namePrompt = $@"Timestamp: {DateTime.Now:yyyy-MM-dd}
Pick {promptCount} random sound effects for a fantasy RTS game that belong in the category '{category}'. 
For each sound effect, generate a 3 words snake_case format filename to describe it.
Do not include the words from the category name in the filename.
Output ONLY the snake_case filenames each on a separate line.";

				string namesRaw = await _ollama.GenerateText(namePrompt);
				var names = ParseSnakeCaseList(namesRaw, promptCount, requireUnderscore: true);

				foreach (var name in names)
				{
					string finalOggPath = Path.Combine(categoryOutputFolder, $"{name}.ogg");
					items.Add((name, category, finalOggPath, item.Duration));
				}
			}

			await GenerateSoundEffectsBatch(items);
		}

		private async Task RunGenerateIconSoundEffects()
		{
			Console.WriteLine("Starting GenerateIconSoundEffects task...");
			await _procManager.EnsureOllama();

			var refineConfig = _config.GetProperty("Tasks").GetProperty("RefineIcons");
			string referenceFolder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, refineConfig.GetProperty("ReferenceImagesFolder").GetString()));

			var genConfig = _config.GetProperty("Tasks").GetProperty("GenerateSoundEffects");
			string outputFolder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, genConfig.GetProperty("OutputFolder").GetString()));
			if (!Directory.Exists(outputFolder)) Directory.CreateDirectory(outputFolder);

			var images = Directory.GetFiles(referenceFolder, "*.png", SearchOption.AllDirectories);
			var items = new List<(string FileName, string Description, string FinalOggPath, int Duration)>();

			foreach (var img in images)
			{
				string classification = await _ollama.ClassifyImage(img);
				Console.WriteLine($"Analyzing icon for sound effects: {Path.GetFileName(img)}");
				string description = await AnalyzeIconImage(img);

				string relativePath = Path.GetRelativePath(referenceFolder, img);
				string? subFolder = Path.GetDirectoryName(relativePath);
				string finalOggFolder = string.IsNullOrEmpty(subFolder) || subFolder == "."
					? outputFolder
					: Path.Combine(outputFolder, subFolder);
				string finalOggPath = Path.Combine(finalOggFolder, $"{classification}.ogg");

				items.Add((classification, description, finalOggPath, 3));
			}

			await GenerateSoundEffectsBatch(items);
		}

		private async Task RunGenerate3DModelSoundEffects()
		{
			Console.WriteLine("Starting Generate3DModelSoundEffects task...");
			await _procManager.EnsureOllama();

			var config = _config.GetProperty("Tasks").GetProperty("Refine3DModels");
			string referenceFolder = config.GetProperty("ReferenceImagesFolder").GetString();
			if (!Directory.Exists(referenceFolder))
			{
				referenceFolder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, referenceFolder));
			}

			var genConfig = _config.GetProperty("Tasks").GetProperty("GenerateSoundEffects");
			string outputFolder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, genConfig.GetProperty("OutputFolder").GetString()));
			if (!Directory.Exists(outputFolder)) Directory.CreateDirectory(outputFolder);

			var categories = new[] { AssetCategory.Buildings, AssetCategory.Characters, AssetCategory.Environment, AssetCategory.Props, AssetCategory.Projectiles };
			var items = new List<(string FileName, string Description, string FinalOggPath, int Duration)>();

			foreach (var category in categories)
			{
				string categoryPath = Path.Combine(referenceFolder, category.ToString());
				if (!Directory.Exists(categoryPath)) continue;

				var soundDescriptionPrefixes = new List<(string SoundDescriptionPrefix, string FileNameSuffix)>();

				if (category == AssetCategory.Characters)
				{
					soundDescriptionPrefixes.Add(("Death", "Death"));
					soundDescriptionPrefixes.Add(("Damaged", "Damaged"));
					soundDescriptionPrefixes.Add(("Victory War Cry", "LastHit"));
					soundDescriptionPrefixes.Add(("Angry", "Aggro"));
					soundDescriptionPrefixes.Add(("Walking", "Move"));
					soundDescriptionPrefixes.Add(("Attacking", "Attack"));
				}
				else if (category == AssetCategory.Buildings)
				{
					soundDescriptionPrefixes.Add(("Demolished Destroyed Collapsed Death", "Death"));
					soundDescriptionPrefixes.Add(("Under Construction", "Construction"));
					soundDescriptionPrefixes.Add(("Ambient Noise", "Selected"));
				}
				else if (category == AssetCategory.Environment)
				{
					soundDescriptionPrefixes.Add(("Ambient Noise", "Selected"));
				}
				else if (category == AssetCategory.Props || category == AssetCategory.Projectiles)
				{
					soundDescriptionPrefixes.Add(("Ambient Noise", "Selected"));
				}

				if (soundDescriptionPrefixes.Count == 0) continue;

				var models = Directory.GetFiles(categoryPath, "*.glb", SearchOption.AllDirectories);
				foreach (var glb in models)
				{
					string baseName = Path.GetFileNameWithoutExtension(glb);
					string glTurnaroundPng = await RunGlTurnaround(glb);

					if (string.IsNullOrEmpty(glTurnaroundPng) || !File.Exists(glTurnaroundPng))
					{
						Console.WriteLine($"Failed to generate turnaround for: {glb}");
						continue;
					}

					string classification = await _ollama.ClassifyImage(glTurnaroundPng);

					Console.WriteLine($"Analyzing 3D Model turnaround for sound effects: {baseName}");
					string analyze3DModelTurnaroundImageResult = await Analyze3DModelTurnaroundImage(glTurnaroundPng);

					foreach (var prefix in soundDescriptionPrefixes)
					{
						string soundEffectFileName = $"{classification}_{prefix.FileNameSuffix}";
						string description = $"{prefix.SoundDescriptionPrefix} {analyze3DModelTurnaroundImageResult}";

						string relativePath = Path.GetRelativePath(referenceFolder, glb);
						string? subFolder = Path.GetDirectoryName(relativePath);
						string finalOggFolder = string.IsNullOrEmpty(subFolder) || subFolder == "."
							? outputFolder
							: Path.Combine(outputFolder, subFolder);
						string finalOggPath = Path.Combine(finalOggFolder, $"{soundEffectFileName}.ogg");

						items.Add((soundEffectFileName, description, finalOggPath, 3));
					}
				}
			}

			await GenerateSoundEffectsBatch(items);
		}

		private async Task RunGenerateVoiceLines()
		{
			Console.WriteLine("Starting GenerateVoiceLines task...");
			await _procManager.EnsureOllama();

			var config = _config.GetProperty("Tasks").GetProperty("Refine3DModels");
			string referenceFolder = config.GetProperty("ReferenceImagesFolder").GetString();
			if (!Directory.Exists(referenceFolder))
			{
				referenceFolder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, referenceFolder));
			}

			var genConfig = _config.GetProperty("Tasks").GetProperty("GenerateVoiceLines");
			string outputFolder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, genConfig.GetProperty("OutputFolder").GetString()));
			if (!Directory.Exists(outputFolder)) Directory.CreateDirectory(outputFolder);

			string tempAudioFolder = Path.Combine(AppContext.BaseDirectory, "GenerateVoiceLines");
			if (!Directory.Exists(tempAudioFolder)) Directory.CreateDirectory(tempAudioFolder);

			string characterPath = Path.Combine(referenceFolder, "Characters");
			if (!Directory.Exists(characterPath))
			{
				Console.WriteLine($"Characters path not found: {characterPath}");
				return;
			}

			var models = Directory.GetFiles(characterPath, "*.glb", SearchOption.AllDirectories);
			var batch = new List<object>();

			foreach (var glb in models)
			{
				string baseName = Path.GetFileNameWithoutExtension(glb);
				string characterVoiceDir = Path.Combine(outputFolder, baseName);

				// Quick precheck: if all voice lines already exist, we can skip processing this character entirely
				string[] actions = new[] { "selected", "commandaccepted", "attack", "aggro", "idle", "commandrejected", "death" };
				bool allExist = true;
				foreach (var action in actions)
				{
					for (int i = 0; i < 3; i++)
					{
						string expectedPath = Path.Combine(characterVoiceDir, $"{action}_{i}.ogg");
						if (!File.Exists(expectedPath))
						{
							allExist = false;
							break;
						}
					}
					if (!allExist) break;
				}

				if (allExist)
				{
					Console.WriteLine($"Voice lines for {baseName} already exist. Skipping.");
					continue;
				}

				string glTurnaroundPng = await RunGlTurnaround(glb);
				if (string.IsNullOrEmpty(glTurnaroundPng) || !File.Exists(glTurnaroundPng))
				{
					Console.WriteLine($"Failed to generate turnaround for: {glb}");
					continue;
				}

				string description = await Analyze3DModelTurnaroundImage(glTurnaroundPng);

				// Generate Voice Lines JSON via Ollama
				string linesPrompt = $@"For a fantasy RTS character described as: {description}.
Please generate 3 different variations of short spoken voice lines for each of the following action categories to avoid repetition. They should be a single-phrase maximum, never multiple sentences.:
- Selected: Greeting or selection acknowledgement (e.g. 'Yes, commander?', 'I await your orders.')
- CommandAccepted: Order acknowledgement (e.g. 'Right away.', 'Moving out.')
- Attack: Attack order confirmation or battle cry (e.g. 'For glory!', 'They will fall!')
- Aggro: Angry exclamation when spotting an enemy (e.g. 'Enemy sighted!', 'To arms!')
- Idle: Bored chatter when waiting (e.g. 'Still standing here...', 'Is there anything to do?')
- CommandRejected: Response when command cannot be executed (e.g. 'I cannot do that.', 'Not enough mana.')

Output ONLY a valid JSON object. No Markdown formatting, no code block backticks (like ```json), no intro or outro text.
The JSON object must match this exact schema:
{{
  ""Selected"": [""variation 1"", ""variation 2"", ""variation 3""],
  ""CommandAccepted"": [""variation 1"", ""variation 2"", ""variation 3""],
  ""Attack"": [""variation 1"", ""variation 2"", ""variation 3""],
  ""Aggro"": [""variation 1"", ""variation 2"", ""variation 3""],
  ""Idle"": [""variation 1"", ""variation 2"", ""variation 3""],
  ""CommandRejected"": [""variation 1"", ""variation 2"", ""variation 3""],
}}";

				string linesJsonRaw = await _ollama.GenerateText(linesPrompt);
				if (linesJsonRaw.Contains("```"))
				{
					int start = linesJsonRaw.IndexOf("{");
					int end = linesJsonRaw.LastIndexOf("}");
					if (start >= 0 && end > start)
					{
						linesJsonRaw = linesJsonRaw.Substring(start, end - start + 1);
					}
				}

				// Generate Voice Instruction via Ollama
				string voiceInstructionPrompt = $@"Analyze this character:
Description: {description}
Provide a voice design instruction and DSP post-processing parameters for this character.
Output ONLY a valid JSON object matching this schema. No markdown formatting, no code block backticks, no extra text.
{{
  ""voice_description"": ""A short, concise description of what their voice should sound like for a text-to-speech engine. E.g. 'A deep, resonant, booming male warrior voice.' or 'A high-pitched, screechy, energetic female goblin voice.'"",
  ""speed_factor"": 1.2, // A float representing the speed factor (e.g. 0.8 to 1.5). Default is 1.0.
  ""formant_shift"": 0.95, // A float representing formant scale (e.g. 0.5 to 1.5). < 1.0 makes vocal tract larger/monstrous, > 1.0 makes it smaller/goblin-like. Default is 1.0.
  ""pitch_steps"": -3.0, // A float representing pitch shift in semitones (e.g. -12.0 to 12.0) independent of formant shift. Try -2 to -5 semitones for Orcs. Default is 0.0.
  ""freq_shift"": -40.0, // A float representing flat frequency shift in Hz (e.g. -100.0 to 100.0) for inharmonic monstrous mutation. Default is 0.0.
  ""noise_growl_mix"": 0.25, // A float representing the mix level (0.0 to 1.0) of growl noise injection keyed to the voice envelope. Default is 0.0.
  ""noise_growl_type"": ""noise"", // Either ""noise"" or ""saw"" for the type of gravelly growl carrier. Default is ""noise"".
  ""delay_ms"": 15.0, // A float delay in ms (e.g. 10.0 to 30.0) for early reflection comb filtering room resonance. Default is 0.0.
  ""delay_feedback"": 0.35, // Feedback ratio for the comb filter delay (0.0 to 0.95). Default is 0.3.
  ""saturation"": 1.1, // A float representing saturation level (e.g. 1.0 to 2.0). Default is 1.0.
  ""ring_mod_freq"": 0.0, // A float representing ring modulation frequency in Hz. Try 30-150 Hz for vibrating/robotic/monstrous orcs/monsters. 0.0 means off. Default is 0.0.
  ""ring_mod_mix"": 0.5 // A float representing wet/dry mix for ring modulation (0.0 to 1.0). Default is 0.5.
}}";

				string voiceInstructionJson = await _ollama.GenerateText(voiceInstructionPrompt);

				if (voiceInstructionJson.Contains("```"))
				{
					int start = voiceInstructionJson.IndexOf("{");
					int end = voiceInstructionJson.LastIndexOf("}");
					if (start >= 0 && end > start)
					{
						voiceInstructionJson = voiceInstructionJson.Substring(start, end - start + 1).Trim();
					}
				}

				string voiceInstruction = "A clear, natural voice.";
				double speedFactor = 1.0;
				double formantShift = 1.0;
				double pitchSteps = 0.0;
				double freqShift = 0.0;
				double noiseGrowlMix = 0.0;
				string noiseGrowlType = "noise";
				double delayMs = 0.0;
				double delayFeedback = 0.3;
				double saturation = 1.0;
				double ringModFreq = 0.0;
				double ringModMix = 0.5;

				try
				{
					using var voiceDoc = JsonDocument.Parse(voiceInstructionJson);
					var voiceRoot = voiceDoc.RootElement;

					if (voiceRoot.TryGetProperty("voice_description", out var descProp))
						voiceInstruction = descProp.GetString() ?? voiceInstruction;

					if (voiceRoot.TryGetProperty("speed_factor", out var speedProp)) speedFactor = speedProp.GetDouble();
					if (voiceRoot.TryGetProperty("formant_shift", out var formantProp)) formantShift = formantProp.GetDouble();
					if (voiceRoot.TryGetProperty("pitch_steps", out var pitchProp)) pitchSteps = pitchProp.GetDouble();
					if (voiceRoot.TryGetProperty("freq_shift", out var freqSProp)) freqShift = freqSProp.GetDouble();
					if (voiceRoot.TryGetProperty("noise_growl_mix", out var ngMixProp)) noiseGrowlMix = ngMixProp.GetDouble();
					if (voiceRoot.TryGetProperty("noise_growl_type", out var ngTypeProp)) noiseGrowlType = ngTypeProp.GetString() ?? "noise";
					if (voiceRoot.TryGetProperty("delay_ms", out var delayProp)) delayMs = delayProp.GetDouble();
					if (voiceRoot.TryGetProperty("delay_feedback", out var feedbackProp)) delayFeedback = feedbackProp.GetDouble();
					if (voiceRoot.TryGetProperty("saturation", out var satProp)) saturation = satProp.GetDouble();
					if (voiceRoot.TryGetProperty("ring_mod_freq", out var freqProp)) ringModFreq = freqProp.GetDouble();
					if (voiceRoot.TryGetProperty("ring_mod_mix", out var mixProp)) ringModMix = mixProp.GetDouble();
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Error parsing voice instruction JSON for {baseName}: {ex.Message}. Raw: {voiceInstructionJson}");
				}

				// --- STEP 1: HARD CLAMPING WRONGS (Tightened Limits) ---
				speedFactor = Math.Clamp(speedFactor, 0.7, 1.4);
				formantShift = Math.Clamp(formantShift, 0.75, 1.3);
				pitchSteps = Math.Clamp(pitchSteps, -6.0, 6.0);
				freqShift = Math.Clamp(freqShift, -80.0, 80.0);
				noiseGrowlMix = Math.Clamp(noiseGrowlMix, 0.0, 0.5); // Dropped from 1.0; max noise is almost never viable for clean dialogue
				delayMs = Math.Clamp(delayMs, 0.0, 40.0);
				delayFeedback = Math.Clamp(delayFeedback, 0.0, 0.6); // Dropped from 0.7 to avoid infinite ringing/metallic buildup
				saturation = Math.Clamp(saturation, 1.0, 1.15);      // Dropped from 1.5; 1.2+ is highly distorted / too aggressive
				ringModFreq = Math.Clamp(ringModFreq, 0.0, 500.0);
				ringModMix = Math.Clamp(ringModMix, 0.0, 1.0);

				// --- STEP 2: DYNAMIC COMBINATION MITIGATION ---

				// Avoid the "Pinched Aggression" trap: High saturation + narrow formant shift = clipping digital mess.
				if (formantShift < 0.9 && saturation > 1.05)
				{
					// If the larynx is tight, back down the distortion
					saturation = Math.Min(saturation, 1.03);
				}

				if (ringModMix > 0.1 && ringModFreq > 0)
				{
					// High ring mod mix with low frequencies is brutal. Cap the mix if the freq is low.
					if (ringModFreq < 100.0)
					{
						ringModMix = Math.Min(ringModMix, 0.15);
					}

					// De-escalate noise growl and freq shift so the ring mod doesn't modulate absolute mud
					noiseGrowlMix *= 0.3;
					freqShift *= 0.2;

					// Ring mod + high saturation completely ruins clarity. Cap saturation when RM is active.
					saturation = Math.Min(saturation, 1.05);
				}

				if (Math.Abs(pitchSteps) > 2.0 && Math.Abs(freqShift) > 5.0)
				{
					double pitchSeverity = Math.Abs(pitchSteps) / 6.0; // Normalizes severity between 0 and 1
					freqShift = freqShift * (1.0 - pitchSeverity);     // High pitch shift = drastically reduced freq shift
				}

				if (noiseGrowlMix > 0.15)
				{
					// If there is heavy raspy noise, keep saturation low to prevent severe clipping artifacts
					saturation = Math.Min(saturation, 1.05);
				}

				if (!Directory.Exists(characterVoiceDir)) Directory.CreateDirectory(characterVoiceDir);

				try
				{
					using var doc = JsonDocument.Parse(linesJsonRaw);
					var root = doc.RootElement;
					foreach (var property in root.EnumerateObject())
					{
						string action = property.Name;
						var variations = property.Value;
						if (variations.ValueKind == JsonValueKind.Array)
						{
							int idx = 0;
							foreach (var item in variations.EnumerateArray())
							{
								string lineText = item.GetString();
								if (string.IsNullOrWhiteSpace(lineText)) continue;

								string finalOggPath = Path.Combine(characterVoiceDir, $"{action.ToLower()}_{idx + 1}.ogg");
								string tempWavPath = Path.Combine(tempAudioFolder, $"{baseName}_{action.ToLower()}_{idx + 1}.wav");
								batch.Add(new
								{
									text = lineText,
									voice_instruction = voiceInstruction,
									speed_factor = speedFactor,
									formant_shift = formantShift,
									pitch_steps = pitchSteps,
									freq_shift = freqShift,
									noise_growl_mix = noiseGrowlMix,
									noise_growl_type = noiseGrowlType,
									delay_ms = delayMs,
									delay_feedback = delayFeedback,
									saturation = saturation,
									ring_mod_freq = ringModFreq,
									ring_mod_mix = ringModMix,
									output = tempWavPath,
									final_path = finalOggPath,
									unit_name = baseName
								});
								idx++;
							}
						}
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Error parsing voice lines JSON for {baseName}: {ex.Message}. Raw: {linesJsonRaw}");
				}
			}

			if (batch.Count > 0)
			{
				string venvPath = await EnsureSharedPythonEnvironment();
				await RunBatchVoiceGenerationPython(venvPath, "generate_voiceLines.py", batch, genConfig.GetProperty("model").GetString(), genConfig.GetProperty("voice_design_model").GetString());

				// Post-generation zip creation: Group by unit name and compress to {{unit_name}}_audio.zip
				// All files per unit should be stored uncompressed (store-only) in the output folder.
				var grouped = batch.Cast<dynamic>().GroupBy(item => (string)item.unit_name);
				foreach (var group in grouped)
				{
					string unitName = group.Key;
					string zipPath = Path.Combine(outputFolder, $"{unitName}_audio.zip");
					if (File.Exists(zipPath)) File.Delete(zipPath);

					using (var archive = System.IO.Compression.ZipFile.Open(zipPath, System.IO.Compression.ZipArchiveMode.Create))
					{
						foreach (var item in group)
						{
							string oggPath = (string)item.final_path;
							if (File.Exists(oggPath))
							{
								string entryName = Path.GetFileName(oggPath);
								archive.CreateEntryFromFile(oggPath, entryName, System.IO.Compression.CompressionLevel.NoCompression);
							}
						}

						// Add ReferenceVoiceForCloning.ogg if it exists in the unit's directory
						string refVoiceOgg = Path.Combine(outputFolder, unitName, "ReferenceVoiceForCloning.ogg");
						if (File.Exists(refVoiceOgg))
						{
							archive.CreateEntryFromFile(refVoiceOgg, "ReferenceVoiceForCloning.ogg", System.IO.Compression.CompressionLevel.NoCompression);
						}
					}
					Console.WriteLine($"Created uncompressed archive: {zipPath}");
				}
			}
		}

		private async Task RunBatchVoiceGenerationPython(string venvPath, string scriptName, List<object> batch, string model, string voiceDesignModel)
		{
			string pythonExe = Path.Combine(venvPath, OperatingSystem.IsWindows() ? "Scripts\\python.exe" : "bin/python");
			string scriptPath = Path.Combine(AppContext.BaseDirectory, scriptName);

			if (!File.Exists(scriptPath))
			{
				Console.WriteLine($"Voice generation script not found at: {scriptPath}");
				return;
			}

			string batchFilePath = Path.Combine(Path.GetTempPath(), $"voice_batch_{Guid.NewGuid()}.json");
			await File.WriteAllTextAsync(batchFilePath, JsonSerializer.Serialize(batch));

			Console.WriteLine($"Executing {scriptName} in batch mode for {batch.Count} items...");
			int exitCode = await _procManager.RunProcessAsync(pythonExe, $"\"{scriptPath}\" --batch \"{batchFilePath}\" --model \"{model}\" --voice-design-model \"{voiceDesignModel}\"");

			if (File.Exists(batchFilePath))
			{
				try { File.Delete(batchFilePath); } catch { }
			}

			if (exitCode != 0)
			{
				Console.WriteLine($"{scriptName} failed with exit code: {exitCode}");
			}
		}

		private async Task GenerateSoundEffectsBatch(List<(string FileName, string Description, string FinalOggPath, int Duration)> items)
		{
			if (items == null || items.Count == 0) return;

			await _procManager.EnsureOllama();

			var genConfig = _config.GetProperty("Tasks").GetProperty("GenerateSoundEffects");
			string outputFolder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, genConfig.GetProperty("OutputFolder").GetString()));
			if (!Directory.Exists(outputFolder)) Directory.CreateDirectory(outputFolder);

			string tempAudioFolder = Path.Combine(AppContext.BaseDirectory, "GenerateSoundEffects");
			if (!Directory.Exists(tempAudioFolder)) Directory.CreateDirectory(tempAudioFolder);

			string venvPath = await EnsureSharedPythonEnvironment();

			var batch = new List<object>();
			var excludedFileNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (Directory.Exists(outputFolder))
			{
				foreach (var file in Directory.GetFiles(outputFolder, "*.ogg", SearchOption.AllDirectories))
				{
					excludedFileNames.Add(Path.GetFileNameWithoutExtension(file));
				}
			}

			foreach (var item in items)
			{
				if (excludedFileNames.Contains(item.FileName)) continue;

				// Ensure parent directory of final path exists
				string? parentDir = Path.GetDirectoryName(item.FinalOggPath);
				if (!string.IsNullOrEmpty(parentDir) && !Directory.Exists(parentDir))
				{
					Directory.CreateDirectory(parentDir);
				}

				if (File.Exists(item.FinalOggPath))
				{
					excludedFileNames.Add(item.FileName);
					continue;
				}

				var prompt = await _ollama.GenerateSoundEffectPrompt(item.FileName, item.Description);
				if (!string.IsNullOrWhiteSpace(prompt))
				{
					string tempWavPath = Path.Combine(tempAudioFolder, $"{item.FileName}.wav");
					batch.Add(new
					{
						prompt = prompt,
						output = tempWavPath,
						final_path = item.FinalOggPath,
						duration = item.Duration
					});
					excludedFileNames.Add(item.FileName);
				}
			}

			if (batch.Count > 0)
			{
				await RunBatchAudioGenerationPython(venvPath, "generate_soundEffect_stableAudio.py", batch, genConfig.GetProperty("model").GetString());
			}
		}

		private async Task CategorizeAndMoveImage(string imgPath, string fileName, AssetCategory category, string originalPath, bool isRefined = false)
		{
			Console.WriteLine($"Categorizing: {Path.GetFileName(imgPath)}");
			string prompt = @"Analyze the provided image, which is intended to be a fantasy RTS game icon.

Characters are moveable units or vehicles that a player could control
Items are equippable gear to make characters stronger
Abilities are actions or spells a character can perform
Buildings are large completed man-made architectural structures, excluding independent components that must be combined to form a full structure, such as: doors, pillars, arches, walls
Environment is naturally occurring terrain
UI are clickable buttons a player would use to issue a command to the game
Resources are gathered components that a player would spend as currency to build or purchase something
Props are a catch-all for anything that doesn't match another category

Return ONLY a valid JSON object with exactly these three fields:
- ""fileName"": a concise 1-3 word snake_case description of what the icon represents. Do NOT include generic words like ""icon"", ""game"", ""asset"".
- ""category"": one exact value from this list only: Characters, Items, Abilities, Buildings, Environment, Resources, UI, Props
- ""quality"": an integer 1, 2, or 3 where:
  3 = perfectly fits a high-quality fantasy RTS icon (sharp, thematic, good composition)
  2 = usable but has minor issues
  1 = does not fit the style or is low quality";

			string resultJson = await _ollama.AnalyzeImage(prompt, imgPath);
			try
			{
				int start = resultJson.IndexOf("{");
				int end = resultJson.LastIndexOf("}");
				if (start >= 0 && end > start)
				{
					var doc = JsonDocument.Parse(resultJson.Substring(start, end - start + 1));
					var root = doc.RootElement;
					string rawFileName = root.GetProperty("fileName").GetString().Trim().Replace(" ", "_").ToLower();
					string detectedFileName = System.Text.RegularExpressions.Regex.Replace(rawFileName, "[^a-z0-9_]", "_");
					detectedFileName = System.Text.RegularExpressions.Regex.Replace(detectedFileName, "_+", "_");
					detectedFileName = detectedFileName.Trim('_');

					string detectedCategoryInput = root.GetProperty("category").GetString().Trim();
					bool isResource = detectedCategoryInput.Equals("Resources", StringComparison.OrdinalIgnoreCase);

					AssetCategory detectedCategory;
					if (isResource)
					{
						detectedCategory = AssetCategory.Items;
					}
					else if (!Enum.TryParse<AssetCategory>(detectedCategoryInput, true, out detectedCategory))
					{
						detectedCategory = AssetCategory.Props;
					}

					int quality = 1;
					if (root.GetProperty("quality").ValueKind == JsonValueKind.Number)
						quality = root.GetProperty("quality").GetInt32();
					else if (int.TryParse(root.GetProperty("quality").GetString(), out int q))
						quality = q;

					if (quality < 1) quality = 1;
					if (quality > 3) quality = 3;

					if (quality == 3 && !string.IsNullOrEmpty(originalPath))
					{
						File.Copy(imgPath, originalPath, true);
						Console.WriteLine($"REPLACED original with high quality version: {originalPath}");
						return;
					}

					string baseTargetDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, _config.GetProperty("Tasks").GetProperty("GenerateIcons").GetProperty("OutputFolder").GetString()));
					string targetDir;
					if (quality == 3)
					{
						string categoryFolderName = isRefined ? "Refined" : (isResource ? "Resources" : detectedCategory.ToString());
						targetDir = Path.Combine(baseTargetDir, categoryFolderName);
					}
					else
						targetDir = Path.Combine(baseTargetDir, "unusable");

					if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

					string finalFileName = string.IsNullOrEmpty(originalPath) ? fileName : detectedFileName;
					string destFile = Path.Combine(targetDir, $"{finalFileName}.png");
					int counter = 1;
					while (File.Exists(destFile))
					{
						destFile = Path.Combine(targetDir, $"{finalFileName}_{counter++}.png");
					}

					File.Copy(imgPath, destFile, true);
					Console.WriteLine($"Saved to: {destFile} (Quality: {quality})");
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Failed to categorize {imgPath}: {ex.Message}");
			}
		}

		private async Task CategorizeAndMove3DModel(string glbPath)
		{
			await _procManager.EnsureOllama();

			string fileName = Path.GetFileNameWithoutExtension(glbPath);
			Console.WriteLine($"Categorizing 3D Model: {fileName}");

			string glTurnaroundPng = await RunGlTurnaround(glbPath);

			if (string.IsNullOrEmpty(glTurnaroundPng) || !File.Exists(glTurnaroundPng))
			{
				Console.WriteLine($"Turnaround rendering failed for {glbPath}");
				return;
			}

			string prompt = @"Analyze the provided image, which is intended to be a multi-angle orthographic 3d model sheet for an RTS game (of any biome, genre, or style).

Characters are moveable units or vehicles that a player could control
Buildings are large completed man-made architectural structures, excluding independent components that must be combined to form a full structure, such as: doors, pillars, arches, walls
Environment is naturally occurring terrain
Projectiles are ranged ammunition, spells, or thrown missiles
Props are a catch-all for anything that doesn't match another category

Return ONLY a valid JSON object with exactly these three fields:
- ""fileName"": a concise 1-3 word snake_case description of what the 3d model should be named. Do NOT include generic words like ""icon"", ""model"", ""game"", ""asset"".
- ""category"": one exact value from this list only: TPose_Auto_Riggable_Humanoid_Characters, Non_Auto_Riggable_Characters, Buildings, Environment, Weapons, Projectiles, Props
- ""quality"": an integer 1, 2, or 3 where:
  3 = publishable model that thematically fits an RTS game and is aesthetically pleasing in both texturing and geometry
  2 = usable but has minor issues
  1 = defective model due to style or low quality (non-manifold, disjoint, self-intersection, erroneous geometry, etc)";

			string resultJson = await _ollama.AnalyzeImage(prompt, glTurnaroundPng);

			try
			{
				int start = resultJson.IndexOf("{");
				int end = resultJson.LastIndexOf("}");
				if (start >= 0 && end > start)
				{
					var doc = JsonDocument.Parse(resultJson.Substring(start, end - start + 1));
					var root = doc.RootElement;
					string detectedFileName = root.GetProperty("fileName").GetString().Trim().Replace(" ", "_").ToLower();
					string detectedCategoryInput = root.GetProperty("category").GetString().Trim();

					var validCategoryFolders = new[] { "TPose_Auto_Riggable_Humanoid_Characters", "Non_Auto_Riggable_Characters", "Buildings", "Environment", "Weapons", "Projectiles", "Props" };
					string detectedCategoryFolder = validCategoryFolders.FirstOrDefault(c => string.Equals(c, detectedCategoryInput, StringComparison.OrdinalIgnoreCase)) ?? "Props";

					bool isAutoRiggableHumanoid = detectedCategoryFolder.Equals("TPose_Auto_Riggable_Humanoid_Characters", StringComparison.OrdinalIgnoreCase);
					bool isNonAutoRiggableCharacter = detectedCategoryFolder.Equals("Non_Auto_Riggable_Characters", StringComparison.OrdinalIgnoreCase);

					int quality = 1;
					if (root.GetProperty("quality").ValueKind == JsonValueKind.Number)
						quality = root.GetProperty("quality").GetInt32();
					else if (int.TryParse(root.GetProperty("quality").GetString(), out int q))
						quality = q;

					if (quality < 1) quality = 1;
					if (quality > 3) quality = 3;

					string baseTargetDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "3DModels_Categorized"));
					string targetDir = Path.Combine(baseTargetDir, quality.ToString(), detectedCategoryFolder);

					if (!Directory.Exists(targetDir)) Directory.CreateDirectory(targetDir);

					string destFile = Path.Combine(targetDir, $"{detectedFileName}.glb");
					int counter = 1;
					while (File.Exists(destFile))
					{
						destFile = Path.Combine(targetDir, $"{detectedFileName}_{counter++}.glb");
					}

					File.Copy(glbPath, destFile, true);
					Console.WriteLine($"Saved to: {destFile} (Quality: {quality})");
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Failed to categorize {glbPath}: {ex.Message}");
			}
		}

		private async Task RunPngQuant(string filePath)
		{
			string pngQuantPath = Path.Combine(AppContext.BaseDirectory, "pngquant.exe");
			if (!File.Exists(pngQuantPath))
			{
				Console.WriteLine($"PngQuant not found at: {pngQuantPath}");
				return;
			}

			Console.WriteLine($"Optimizing: {Path.GetFileName(filePath)}");
			int minQuality = 80;
			int maxQuality = 90;

			while (minQuality > 0)
			{
				int exitCode = await _procManager.RunProcessAsync(pngQuantPath, $"--quality={minQuality}-{maxQuality} --speed 1 --strip --ext .png --force \"{filePath}\"");

				if (exitCode == 0 || exitCode == 98) break;
				if (exitCode == 99)
				{
					minQuality -= 10;
				}
				else break;
			}
		}

		private async Task RunGltfPack(string glbPath)
		{
			string gltfpackPath = Path.Combine(AppContext.BaseDirectory, "gltfpack.exe");
			if (!File.Exists(gltfpackPath))
			{
				Console.WriteLine($"gltfpack not found at: {gltfpackPath}");
				return;
			}

			Console.WriteLine($"Optimizing glTF: {Path.GetFileName(glbPath)}");
			await _procManager.RunProcessAsync(gltfpackPath, $"-tc -noq -i \"{glbPath}\" -o \"{glbPath}\"");
		}

		private async Task<string> RunGlCleanup(string glbPath)
		{
			string hash = BitConverter.ToString(MD5.HashData(File.ReadAllBytes(glbPath))).Replace("-", "").ToLower();
			string cachedGlb = Path.Combine(_cacheDir, $"{hash}_cleaned.glb");

			if (File.Exists(cachedGlb))
			{
				Console.WriteLine($"Using cached cleaned glTF: {Path.GetFileName(glbPath)}");
				return cachedGlb;
			}

			string blenderPath = (_config.TryGetProperty("BlenderPath", out var blenderProp) ? blenderProp.GetString() : null) ?? @"C:\Program Files\Blender Foundation\Blender 5.0\blender.exe";
			string scriptPath = Path.Combine(AppContext.BaseDirectory, "gl_cleanup.py");

			if (!File.Exists(blenderPath))
			{
				Console.WriteLine($"Blender not found at: {blenderPath}");
				Environment.Exit(1);
			}

			if (!File.Exists(scriptPath))
			{
				Console.WriteLine($"Cleanup script not found at: {scriptPath}");
				Environment.Exit(1);
			}

			string tempGlbPath = await DecompressKtxToTemp(glbPath);
			string cleanedTempGlbPath = Path.Combine(Path.GetDirectoryName(tempGlbPath), Path.GetFileNameWithoutExtension(tempGlbPath) + "_cleaned.glb");

			Console.WriteLine($"Cleaning glTF: {Path.GetFileName(glbPath)}");
			int exitCode = await _procManager.RunProcessAsync(blenderPath, $"--background --python \"{scriptPath}\" -- \"{tempGlbPath}\" \"{cleanedTempGlbPath}\"");

			if (File.Exists(tempGlbPath)) File.Delete(tempGlbPath);

			if (exitCode != 0)
			{
				Console.WriteLine($"Blender cleanup failed with exit code: {exitCode}");
				return glbPath;
			}

			if (!File.Exists(cleanedTempGlbPath))
			{
				return glbPath;
			}

			File.Copy(cleanedTempGlbPath, cachedGlb, true);
			return cachedGlb;
		}

		private async Task RunRemoveAnimationsAndBones(string glbPath)
		{
			string blenderPath = (_config.TryGetProperty("BlenderPath", out var blenderProp) ? blenderProp.GetString() : null) ?? @"C:\Program Files\Blender Foundation\Blender 5.0\blender.exe";
			string scriptPath = Path.Combine(AppContext.BaseDirectory, "gl_remove_animations.py");

			if (!File.Exists(blenderPath))
			{
				Console.WriteLine($"Blender not found at: {blenderPath}");
				return;
			}

			if (!File.Exists(scriptPath))
			{
				Console.WriteLine($"Remove animations script not found at: {scriptPath}");
				return;
			}

			Console.WriteLine($"Removing animations and bones from glTF: {Path.GetFileName(glbPath)}");
			int exitCode = await _procManager.RunProcessAsync(blenderPath, $"--background --python \"{scriptPath}\" -- \"{glbPath}\"");

			if (exitCode != 0)
			{
				Console.WriteLine($"Blender animation removal failed with exit code: {exitCode}");
			}
		}

		private async Task<string> DecompressKtxToTemp(string glbPath)
		{
			string cmd = "gltf-transform";
			if (OperatingSystem.IsWindows()) cmd += ".cmd";

			string tempDir = Path.Combine(Path.GetTempPath(), "3d_model_processing_temp");
			if (!Directory.Exists(tempDir)) Directory.CreateDirectory(tempDir);

			string tempGlbPath = Path.Combine(tempDir, Guid.NewGuid().ToString() + "_" + Path.GetFileName(glbPath));
			File.Copy(glbPath, tempGlbPath, true);

			// Try to decompress KTX textures
			try
			{
				int exitCode = await _procManager.RunProcessAsync(cmd, $"ktxdecompress \"{tempGlbPath}\" \"{tempGlbPath}\"");
				if (exitCode != 0)
				{
					Console.WriteLine();
					throw new Exception("gltf-transform ktxdecompress failed.");
				}
			}
			catch (System.ComponentModel.Win32Exception)
			{
				Console.WriteLine("Error: 'gltf-transform' not found. This is required for decompressing KTX textures.");
				Console.WriteLine("Please install it globally via npm: npm install -g @gltf-transform/cli");
				Environment.Exit(1);
			}

			return tempGlbPath;
		}

		private async Task RunGlRender(string glbPath, string outputPath, float rotationAngle = 180f)
		{
			string glbHash = BitConverter.ToString(MD5.HashData(File.ReadAllBytes(glbPath))).Replace("-", "").ToLower();
			string hashInput = $"{glbHash}_{rotationAngle}";
			string cacheKey = BitConverter.ToString(MD5.HashData(Encoding.UTF8.GetBytes(hashInput))).Replace("-", "").ToLower();
			string cachedPng = Path.Combine(_cacheDir, $"{cacheKey}_render.png");

			if (File.Exists(cachedPng))
			{
				Console.WriteLine($"Using cached glTF render: {Path.GetFileName(outputPath)}");
				string outDir = Path.GetDirectoryName(outputPath);
				if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir)) Directory.CreateDirectory(outDir);
				File.Copy(cachedPng, outputPath, true);
				return;
			}

			string blenderPath = (_config.TryGetProperty("BlenderPath", out var blenderProp) ? blenderProp.GetString() : null) ?? @"C:\Program Files\Blender Foundation\Blender 5.0\blender.exe";
			string scriptPath = Path.Combine(AppContext.BaseDirectory, "render_gl_icon.py");

			if (!File.Exists(blenderPath))
			{
				Console.WriteLine($"Blender not found at: {blenderPath}");
				return;
			}

			if (!File.Exists(scriptPath))
			{
				Console.WriteLine($"Render script not found at: {scriptPath}");
				return;
			}

			string tempGlbPath = await DecompressKtxToTemp(glbPath);

			Console.WriteLine($"Rendering glTF: {Path.GetFileName(glbPath)} to {outputPath}");
			int exitCode = await _procManager.RunProcessAsync(blenderPath, $"--background --python \"{scriptPath}\" -- \"{tempGlbPath}\" \"{outputPath}\" {rotationAngle}");

			if (File.Exists(tempGlbPath)) File.Delete(tempGlbPath);

			if (exitCode != 0)
			{
				Console.WriteLine($"Blender render failed with exit code: {exitCode}");
				return;
			}

			if (File.Exists(outputPath))
			{
				File.Copy(outputPath, cachedPng, true);
			}
		}

		private async Task<string> RunGlTurnaround(string glbPath)
		{
			string hash = BitConverter.ToString(MD5.HashData(File.ReadAllBytes(glbPath))).Replace("-", "").ToLower();
			string outputPath = Path.Combine(_cacheDir, $"{hash}_turnaround.png");

			if (File.Exists(outputPath))
			{
				Console.WriteLine($"Using cached turnaround: {outputPath}");
				return outputPath;
			}

			string blenderPath = (_config.TryGetProperty("BlenderPath", out var blenderProp) ? blenderProp.GetString() : null) ?? @"C:\Program Files\Blender Foundation\Blender 5.0\blender.exe";
			string scriptPath = Path.Combine(AppContext.BaseDirectory, "render_gl_to_turnaround_sheets.py");

			if (!File.Exists(blenderPath))
			{
				Console.WriteLine($"Blender not found at: {blenderPath}");
				return null;
			}

			if (!File.Exists(scriptPath))
			{
				Console.WriteLine($"Turnaround script not found at: {scriptPath}");
				return null;
			}

			string tempGlbPath = await DecompressKtxToTemp(glbPath);

			Console.WriteLine($"Generating turnaround for glTF: {Path.GetFileName(glbPath)} to {outputPath}");
			int exitCode = await _procManager.RunProcessAsync(blenderPath, $"--background --python \"{scriptPath}\" -- \"{tempGlbPath}\" \"{outputPath}\"");

			if (File.Exists(tempGlbPath)) File.Delete(tempGlbPath);

			if (exitCode != 0)
			{
				Console.WriteLine($"Blender turnaround failed with exit code: {exitCode}");
				return null;
			}

			return outputPath;
		}

		public static long SeedFromString(string input, long minInclusive = 100000000000000L, long maxExclusive = 9007199254740991L)
		{
			byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
			ulong rawValue = BitConverter.ToUInt64(hash, 0);
			ulong range = (ulong)(maxExclusive - minInclusive);
			return minInclusive + (long)(rawValue % range);
		}

		private async Task RunGenerateTilesheets()
		{
			const int CrosshairThickness = 56;
			const float MaskBlurRadius = 8.0f;
			const int TileSize = 512;

			Console.WriteLine("Starting GenerateTilesheets task...");
			await _procManager.EnsureOllama();

			var genConfig = _config.GetProperty("Tasks").GetProperty("GenerateTilesheets");
			int biomeCount = genConfig.TryGetProperty("BiomeCount", out var biomeProp) ? biomeProp.GetInt32() : 15;
			int promptCount = genConfig.GetProperty("PromptCount").GetInt32();
			string outputFolder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, genConfig.GetProperty("OutputFolder").GetString()));
			if (!Directory.Exists(outputFolder)) Directory.CreateDirectory(outputFolder);

			string tempFolder = Path.Combine(AppContext.BaseDirectory, "TempTilesheets");
			if (!Directory.Exists(tempFolder)) Directory.CreateDirectory(tempFolder);

			Console.WriteLine($"Generating list of {biomeCount} biomes...");
			string biomesPrompt = $@"Timestamp: {DateTime.Now:yyyy-MM-dd}
Pick {biomeCount} diverse and visually distinct biomes or environmental terrain settings for video games.
For each, generate a 1-3 words snake_case format name.
Output ONLY the {biomeCount} snake_case biome names each on a separate line.";

			string rawBiomes = await _ollama.GenerateText(biomesPrompt);
			var biomes = ParseSnakeCaseList(rawBiomes, biomeCount);

			foreach (var biome in biomes)
			{
				string biomeHumanName = biome.Replace("_", " ");
				Console.WriteLine($"\n=== Generating tilesheets for Biome: {biomeHumanName} ({biome}) ===");
				await _procManager.EnsureOllama();

				string biomeOutputFolder = Path.Combine(outputFolder, biomeHumanName);
				if (!Directory.Exists(biomeOutputFolder)) Directory.CreateDirectory(biomeOutputFolder);

				string filenamesPrompt = $@"Timestamp: {DateTime.Now:yyyy-MM-dd}
Pick {promptCount} random environment texture/tilesheet names for an RTS custom arcade map using biome '{biomeHumanName}'.
For each, generate a 2 words snake_case format filename.
Output ONLY the snake_case filenames each on a separate line.";

				string rawFilenames = await _ollama.GenerateText(filenamesPrompt);
				var filenames = ParseSnakeCaseList(rawFilenames, promptCount);

				var batch = new List<(string Name, string FinalPrompt, string FinalPath)>();

				foreach (var rawName in filenames)
				{
					string name = DeduplicateSnakeCaseTerms($"{biome}_{rawName}");
					string finalPath = Path.Combine(biomeOutputFolder, $"{name}.png");
					if (File.Exists(finalPath)) continue;

					string description = rawName.Replace("_", " ");
					Console.WriteLine($"Generating Flux prompt for tilesheet: {name}");
					string promptReq = $@"Generate a flux prompt for creating a top-down, stylized, hand-painted ground texture of: {description} for a {biomeHumanName} biome.
1. Focus ONLY on the flat broad ground surface, DO NOT include details for individual objects or debris. 
2. DO NOT use words like 'realistic', '8k', 'sharp details', 'intricate', or 'high resolution', 'tileable', or 'seamless'.
3. The entire frame should have a uniform, organic distribution
Output in JSON format with only this field: fluxPrompt";

					string promptJson = await _ollama.GenerateText(promptReq);
					string fluxPrompt = GetFluxPromptFromResponse(promptJson) ?? description;
					string finalPrompt = $@"top-down view texture, stylized hand-painted video game texture, uniform texture distribution, clean broad shapes, low detail, smooth gradients, no micro-textures, minimal noise, solid colors, RTS environment asset, " + fluxPrompt;

					batch.Add((name, finalPrompt, finalPath));
				}

				if (batch.Count > 0)
				{
					await _procManager.EnsureComfyUI();

					var generatedImages = new List<(string TempImg, string FinalPath, string FinalPrompt, string Name, long Seed)>();
					foreach (var item in batch)
					{
						long seed = SeedFromString(item.FinalPrompt);
						string tempImg = await _comfy.GenerateImage(item.FinalPrompt, item.Name, tempFolder, TileSize, TileSize, seed);
						if (!string.IsNullOrEmpty(tempImg) && File.Exists(tempImg))
						{
							generatedImages.Add((tempImg, item.FinalPath, item.FinalPrompt, item.Name, seed));
						}
					}

					foreach (var item in generatedImages)
					{
						Console.WriteLine($"Processing seamless tiling pipeline for: {item.Name}");
						await MakeTileSeamless(item.TempImg, item.FinalPath, item.FinalPrompt, item.Name, tempFolder, item.Seed, CrosshairThickness, MaskBlurRadius);
					}
				}
			}
		}

		private async Task<string?> MakeImageSeamless(
			string inputImagePath,
			string finalOutputPath,
			string prompt,
			string baseName,
			string tempFolder,
			SeamlessMode mode = SeamlessMode.Tile2D,
			long? seed = null,
			int seamThickness = 56,
			float maskBlurRadius = 8.0f,
			bool enforceTopBottomFade = false,
			int fadeBandHeight = 24,
			string assetLabel = "image",
			bool removeBackground = false)
		{
			using var srcBitmap = SKBitmap.Decode(inputImagePath);
			if (srcBitmap == null)
			{
				Console.WriteLine($"[ERROR] Failed to load input image: {inputImagePath}");
				return null;
			}

			int width = srcBitmap.Width;
			int height = srcBitmap.Height;

			// Step 1: Image Offset / Toroidal Wrap
			int shiftX = (mode == SeamlessMode.Tile2D || mode == SeamlessMode.Horizontal1D) ? width / 2 : 0;
			int shiftY = (mode == SeamlessMode.Tile2D || mode == SeamlessMode.Vertical1D) ? height / 2 : 0;

			using var shiftedOriginal = ShiftImageToroidal(srcBitmap, shiftX, shiftY);

			// Step 2: Mask & Alpha Channel Generation
			// Create seam mask with Gaussian blur edge softening and Perlin noise border stretching
			// Alpha: Kept/Protected = 255 (Opaque), Inpaint/Seam = 0 (Transparent)
			float[,] mask = GenerateGaussianBlurredSeamMask(width, height, seamThickness, maskBlurRadius, mode, seed);

			// Format output as 32-bit RGBA PNG
			using var rgbaBitmap = CreateRgbaWithMask(shiftedOriginal, mask);
			string stitchedAlphaPath = Path.Combine(tempFolder, $"{baseName}_stitched_alpha.png");
			SaveBitmapToPng(rgbaBitmap, stitchedAlphaPath);

			// Step 3: ComfyUI API Execution
			// Call api with workflow Flux2_Image_To_Image_With_Mask.json & pass the prompt & image & seed
			string? inpaintResultPath = await _comfy.InpaintImage(prompt, stitchedAlphaPath, $"{baseName}_seamless", seed, removeBackground: removeBackground);

			if (string.IsNullOrEmpty(inpaintResultPath) || !File.Exists(inpaintResultPath))
			{
				Console.WriteLine($"[WARNING] Inpainting failed for {baseName}, falling back to original image.");
				string? dir = Path.GetDirectoryName(finalOutputPath);
				if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
				File.Copy(inputImagePath, finalOutputPath, true);
				return finalOutputPath;
			}

			// Step 4: Inverse Unwrap & Final Assembly
			using var inpaintBitmap = SKBitmap.Decode(inpaintResultPath);
			if (inpaintBitmap == null)
			{
				Console.WriteLine($"[WARNING] Failed to decode inpaint result for {baseName}.");
				string? dir = Path.GetDirectoryName(finalOutputPath);
				if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
				File.Copy(inputImagePath, finalOutputPath, true);
				return finalOutputPath;
			}

			// Fallback / Fidelity Compositing: Composite inpaint result over original shifted image using the feathered mask
			using var compositedShifted = CompositeImages(shiftedOriginal, inpaintBitmap, mask, enforceTopBottomFade, fadeBandHeight);

			// Inverse unwrap: Shift back by same toroidal offsets
			using var finalSeamless = ShiftImageToroidal(compositedShifted, shiftX, shiftY);

			SaveBitmapToPng(finalSeamless, finalOutputPath);
			Console.WriteLine($"[+] Seamless {assetLabel} successfully created: {finalOutputPath}");

			// Cleanup temporary files
			try { if (File.Exists(stitchedAlphaPath)) File.Delete(stitchedAlphaPath); } catch { }
			try { if (File.Exists(inputImagePath)) File.Delete(inputImagePath); } catch { }

			return finalOutputPath;
		}

		private Task<string?> MakeTileSeamless(
			string inputImagePath,
			string finalOutputPath,
			string prompt,
			string baseName,
			string tempFolder,
			long? seed = null,
			int crosshairThickness = 56,
			float maskBlurRadius = 8.0f)
		{
			return MakeImageSeamless(
				inputImagePath,
				finalOutputPath,
				prompt,
				baseName,
				tempFolder,
				mode: SeamlessMode.Tile2D,
				seed: seed,
				seamThickness: crosshairThickness,
				maskBlurRadius: maskBlurRadius,
				enforceTopBottomFade: false,
				assetLabel: "tile");
		}

		private Task<string?> MakeRibbonSeamless(
			string inputImagePath,
			string finalOutputPath,
			string prompt,
			string baseName,
			string tempFolder,
			long? seed = null,
			int seamThickness = 56,
			float maskBlurRadius = 8.0f)
		{
			return MakeImageSeamless(
				inputImagePath,
				finalOutputPath,
				prompt,
				baseName,
				tempFolder,
				mode: SeamlessMode.Horizontal1D,
				seed: seed,
				seamThickness: seamThickness,
				maskBlurRadius: maskBlurRadius,
				enforceTopBottomFade: true,
				fadeBandHeight: 24,
				assetLabel: "ribbon texture", removeBackground: true);
		}

		public static SKBitmap ShiftImageToroidal(SKBitmap src, int shiftX, int shiftY)
		{
			int width = src.Width;
			int height = src.Height;
			var result = new SKBitmap(width, height, src.ColorType, src.AlphaType);

			for (int y = 0; y < height; y++)
			{
				int srcY = ((y - shiftY) % height + height) % height;
				for (int x = 0; x < width; x++)
				{
					int srcX = ((x - shiftX) % width + width) % width;
					result.SetPixel(x, y, src.GetPixel(srcX, srcY));
				}
			}

			return result;
		}

		#region Periodic Perlin Noise Generation

		private static readonly (float X, float Y)[] PerlinGradients2D = new (float, float)[]
		{
			(1f, 0f), (-1f, 0f), (0f, 1f), (0f, -1f),
			(0.70710678f, 0.70710678f), (-0.70710678f, 0.70710678f),
			(0.70710678f, -0.70710678f), (-0.70710678f, -0.70710678f)
		};

		private static int Hash2D(int x, int y, int seed)
		{
			unchecked
			{
				uint h = (uint)(seed ^ (x * 374761393) ^ (y * 668265263));
				h = (h ^ (h >> 13)) * 1274126177;
				return (int)(h ^ (h >> 16));
			}
		}

		private static float Grad2D(int x, int y, int seed, float dx, float dy)
		{
			int h = Hash2D(x, y, seed) & 7;
			var g = PerlinGradients2D[h];
			return g.X * dx + g.Y * dy;
		}

		private static float Fade(float t)
		{
			return t * t * t * (t * (t * 6.0f - 15.0f) + 10.0f);
		}

		public static float PeriodicPerlin2D(float x, float y, int width, int height, int periodX, int periodY, int seed)
		{
			float u = (x / width) * periodX;
			float v = (y / height) * periodY;

			int x0 = (int)MathF.Floor(u);
			int y0 = (int)MathF.Floor(v);
			int x1 = x0 + 1;
			int y1 = y0 + 1;

			float tx = u - x0;
			float ty = v - y0;

			int x0Wrap = ((x0 % periodX) + periodX) % periodX;
			int x1Wrap = ((x1 % periodX) + periodX) % periodX;
			int y0Wrap = ((y0 % periodY) + periodY) % periodY;
			int y1Wrap = ((y1 % periodY) + periodY) % periodY;

			float g00 = Grad2D(x0Wrap, y0Wrap, seed, tx, ty);
			float g10 = Grad2D(x1Wrap, y0Wrap, seed, tx - 1.0f, ty);
			float g01 = Grad2D(x0Wrap, y1Wrap, seed, tx, ty - 1.0f);
			float g11 = Grad2D(x1Wrap, y1Wrap, seed, tx - 1.0f, ty - 1.0f);

			float fx = Fade(tx);
			float fy = Fade(ty);

			float xInterp0 = g00 + fx * (g10 - g00);
			float xInterp1 = g01 + fx * (g11 - g01);

			return xInterp0 + fy * (xInterp1 - xInterp0);
		}

		public static float PeriodicPerlinFbm(
			float x, float y,
			int width, int height,
			int basePeriodX = 4, int basePeriodY = 4,
			int octaves = 3, float persistence = 0.5f,
			int seed = 1337)
		{
			float total = 0f;
			float maxAmp = 0f;
			float amp = 1f;
			int periodX = basePeriodX;
			int periodY = basePeriodY;
			int curSeed = seed;

			for (int i = 0; i < octaves; i++)
			{
				float n = PeriodicPerlin2D(x, y, width, height, periodX, periodY, curSeed);
				total += n * amp;
				maxAmp += amp;
				amp *= persistence;
				periodX *= 2;
				periodY *= 2;
				curSeed = unchecked(curSeed * 31 + 1013);
			}

			return total / maxAmp;
		}

		#endregion

		public static float[,] GenerateGaussianBlurredSeamMask(
			int width,
			int height,
			int seamThickness,
			float blurRadius,
			SeamlessMode mode = SeamlessMode.Tile2D,
			long? seed = null,
			float noiseStretch = 0.5f,
			float noiseWarp = 0.35f)
		{
			int cx = width / 2;
			int cy = height / 2;
			float th = seamThickness / 2.0f;
			int seedInt = seed.HasValue ? unchecked((int)seed.Value) : Random.Shared.Next();

			float maxStretch = th * noiseStretch; // Random stretch / thickness modulation
			float maxWarp = th * noiseWarp;       // 2D coordinate displacement / warp

			bool maskVerticalSeam = (mode == SeamlessMode.Tile2D || mode == SeamlessMode.Horizontal1D);
			bool maskHorizontalSeam = (mode == SeamlessMode.Tile2D || mode == SeamlessMode.Vertical1D);

			float[,] mask = new float[width, height];
			for (int y = 0; y < height; y++)
			{
				for (int x = 0; x < width; x++)
				{
					bool inSeam = false;

					if (maskVerticalSeam)
					{
						// Periodic noise for organic domain warping along X and Y
						float warpX = PeriodicPerlinFbm(x, y, width, height, 4, 4, 3, 0.5f, seedInt);
						// Periodic noise for stretching / varying the vertical seam thickness along Y
						float stretchV = PeriodicPerlinFbm(x, y, width, height, 4, 4, 2, 0.5f, unchecked(seedInt + 22222));
						float px = x + warpX * maxWarp;
						float thV = MathF.Max(th * 0.65f, th + stretchV * maxStretch);
						if (MathF.Abs(px - cx) < thV)
						{
							inSeam = true;
						}
					}

					if (maskHorizontalSeam && !inSeam)
					{
						// Periodic noise for organic domain warping along X and Y
						float warpY = PeriodicPerlinFbm(x, y, width, height, 4, 4, 3, 0.5f, unchecked(seedInt + 54321));
						// Periodic noise for stretching / varying the horizontal seam thickness along X
						float stretchH = PeriodicPerlinFbm(x, y, width, height, 4, 4, 2, 0.5f, unchecked(seedInt + 11111));
						float py = y + warpY * maxWarp;
						float thH = MathF.Max(th * 0.65f, th + stretchH * maxStretch);
						if (MathF.Abs(py - cy) < thH)
						{
							inSeam = true;
						}
					}

					// 0 = inpaint/seam, 255 = keep/original
					mask[x, y] = inSeam ? 0f : 255f;
				}
			}

			if (blurRadius <= 0.01f)
			{
				return mask;
			}

			bool wrapX = (mode == SeamlessMode.Tile2D || mode == SeamlessMode.Horizontal1D);
			bool wrapY = (mode == SeamlessMode.Tile2D || mode == SeamlessMode.Vertical1D);

			return ApplyGaussianBlur2D(mask, width, height, blurRadius, wrapX, wrapY);
		}

		public static float[,] GenerateGaussianBlurredCrosshairMask(
			int width,
			int height,
			int crosshairThickness,
			float blurRadius,
			long? seed = null,
			float noiseStretch = 0.5f,
			float noiseWarp = 0.35f) =>
			GenerateGaussianBlurredSeamMask(width, height, crosshairThickness, blurRadius, SeamlessMode.Tile2D, seed, noiseStretch, noiseWarp);

		public static float[,] GenerateGaussianBlurredVerticalSeamMask(
			int width,
			int height,
			int seamThickness,
			float blurRadius,
			long? seed = null,
			float noiseStretch = 0.5f,
			float noiseWarp = 0.35f) =>
			GenerateGaussianBlurredSeamMask(width, height, seamThickness, blurRadius, SeamlessMode.Horizontal1D, seed, noiseStretch, noiseWarp);

		public static float[,] ApplyGaussianBlur2D(
			float[,] matrix,
			int width,
			int height,
			float blurRadius,
			bool wrapX = true,
			bool wrapY = true)
		{
			if (blurRadius <= 0.01f)
			{
				return (float[,])matrix.Clone();
			}

			// Build 1D Gaussian kernel
			int radius = Math.Max(1, (int)Math.Ceiling(3.0f * blurRadius));
			int kernelSize = radius * 2 + 1;
			float[] kernel = new float[kernelSize];
			float sum = 0f;
			float twoSigmaSq = 2.0f * blurRadius * blurRadius;

			for (int i = -radius; i <= radius; i++)
			{
				float weight = MathF.Exp(-(i * i) / twoSigmaSq);
				kernel[i + radius] = weight;
				sum += weight;
			}
			for (int i = 0; i < kernelSize; i++)
			{
				kernel[i] /= sum;
			}

			// Horizontal pass
			float[,] temp = new float[width, height];
			for (int y = 0; y < height; y++)
			{
				for (int x = 0; x < width; x++)
				{
					float acc = 0f;
					for (int k = -radius; k <= radius; k++)
					{
						int sampleX = wrapX ? ((x + k) % width + width) % width : Math.Clamp(x + k, 0, width - 1);
						acc += matrix[sampleX, y] * kernel[k + radius];
					}
					temp[x, y] = acc;
				}
			}

			// Vertical pass
			float[,] blurred = new float[width, height];
			for (int y = 0; y < height; y++)
			{
				for (int x = 0; x < width; x++)
				{
					float acc = 0f;
					for (int k = -radius; k <= radius; k++)
					{
						int sampleY = wrapY ? ((y + k) % height + height) % height : Math.Clamp(y + k, 0, height - 1);
						acc += temp[x, sampleY] * kernel[k + radius];
					}
					blurred[x, y] = Math.Clamp(acc, 0f, 255f);
				}
			}

			return blurred;
		}

		public static SKBitmap CreateRgbaWithMask(SKBitmap shiftedImage, float[,] mask)
		{
			int width = shiftedImage.Width;
			int height = shiftedImage.Height;
			var rgbaBitmap = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);

			for (int y = 0; y < height; y++)
			{
				for (int x = 0; x < width; x++)
				{
					var srcColor = shiftedImage.GetPixel(x, y);
					byte alpha = (byte)Math.Clamp((int)Math.Round(mask[x, y]), 0, 255);
					rgbaBitmap.SetPixel(x, y, new SKColor(srcColor.Red, srcColor.Green, srcColor.Blue, alpha));
				}
			}

			return rgbaBitmap;
		}

		public static SKBitmap CompositeImages(
			SKBitmap shiftedOriginal,
			SKBitmap inpaintResult,
			float[,] mask,
			bool enforceTopBottomFade = false,
			int fadeBandHeight = 24)
		{
			int width = shiftedOriginal.Width;
			int height = shiftedOriginal.Height;
			var composited = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);

			for (int y = 0; y < height; y++)
			{
				// Compute top/bottom boundary fade factor
				// (1.0 in the middle, smoothly tapering to 0.0 at the absolute top and bottom edge rows if within fadeBandHeight)
				float edgeFade = 1.0f;
				if (enforceTopBottomFade && fadeBandHeight > 0)
				{
					if (y < fadeBandHeight)
					{
						float t = (float)y / fadeBandHeight;
						edgeFade = t * t * (3.0f - 2.0f * t); // smoothstep
					}
					else if (y >= height - fadeBandHeight)
					{
						float t = (float)(height - 1 - y) / fadeBandHeight;
						edgeFade = t * t * (3.0f - 2.0f * t); // smoothstep
					}
				}

				for (int x = 0; x < width; x++)
				{
					var origColor = shiftedOriginal.GetPixel(x, y);
					var inpaintColor = inpaintResult.GetPixel(x, y);

					float origWeight = mask[x, y] / 255f;
					float inpaintWeight = 1f - origWeight;

					float blendedR = origColor.Red * origWeight + inpaintColor.Red * inpaintWeight;
					float blendedG = origColor.Green * origWeight + inpaintColor.Green * inpaintWeight;
					float blendedB = origColor.Blue * origWeight + inpaintColor.Blue * inpaintWeight;
					float blendedA = origColor.Alpha * origWeight + inpaintColor.Alpha * inpaintWeight;

					byte r = (byte)Math.Clamp((int)Math.Round(blendedR * edgeFade), 0, 255);
					byte g = (byte)Math.Clamp((int)Math.Round(blendedG * edgeFade), 0, 255);
					byte b = (byte)Math.Clamp((int)Math.Round(blendedB * edgeFade), 0, 255);
					byte a = (byte)Math.Clamp((int)Math.Round(blendedA * edgeFade), 0, 255);

					composited.SetPixel(x, y, new SKColor(r, g, b, a));
				}
			}

			return composited;
		}

		public static SKBitmap CompositeRibbonImages(
			SKBitmap shiftedOriginal,
			SKBitmap inpaintResult,
			float[,] mask,
			bool enforceTopBottomFade = true,
			int fadeBandHeight = 24) =>
			CompositeImages(shiftedOriginal, inpaintResult, mask, enforceTopBottomFade, fadeBandHeight);

		public static void SaveBitmapToPng(SKBitmap bitmap, string outputPath)
		{
			string dir = Path.GetDirectoryName(outputPath);
			if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
				Directory.CreateDirectory(dir);

			using var image = SKImage.FromBitmap(bitmap);
			using var data = image.Encode(SKEncodedImageFormat.Png, 100);
			using var stream = File.OpenWrite(outputPath);
			stream.SetLength(0);
			data.SaveTo(stream);
		}

		private async Task RunGenerateRibbons()
		{
			const int SeamThickness = 56;
			const float MaskBlurRadius = 8.0f;
			const int RibbonWidth = 512;
			const int RibbonHeight = 512;

			Console.WriteLine("Starting GenerateRibbons task...");
			await _procManager.EnsureOllama();

			var genConfig = _config.GetProperty("Tasks").GetProperty("GenerateRibbons");
			int categoryCount = genConfig.TryGetProperty("CategoryCount", out var catProp) ? catProp.GetInt32() : (genConfig.TryGetProperty("BiomeCount", out var bProp) ? bProp.GetInt32() : 15);
			int promptCount = genConfig.GetProperty("PromptCount").GetInt32();
			string outputFolder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, genConfig.GetProperty("OutputFolder").GetString()));
			if (!Directory.Exists(outputFolder)) Directory.CreateDirectory(outputFolder);

			string tempFolder = Path.Combine(AppContext.BaseDirectory, "TempRibbons");
			if (!Directory.Exists(tempFolder)) Directory.CreateDirectory(tempFolder);

			Console.WriteLine($"Generating list of {categoryCount} ribbon particle effect categories...");
			string categoriesPrompt = $@"Timestamp: {DateTime.Now:yyyy-MM-dd}
Pick {categoryCount} diverse and visually distinct particle effect ribbon/trail themes or projectile types for video games.
For each, generate a 1-3 words snake_case format name.
Output ONLY the {categoryCount} snake_case category names each on a separate line.";

			string rawCategories = await _ollama.GenerateText(categoriesPrompt);
			var categories = ParseSnakeCaseList(rawCategories, categoryCount);

			foreach (var category in categories)
			{
				string categoryHumanName = category.Replace("_", " ");
				Console.WriteLine($"\n=== Generating ribbons for Category: {categoryHumanName} ({category}) ===");
				await _procManager.EnsureOllama();

				string categoryOutputFolder = Path.Combine(outputFolder, categoryHumanName);
				if (!Directory.Exists(categoryOutputFolder)) Directory.CreateDirectory(categoryOutputFolder);

				string filenamesPrompt = $@"Timestamp: {DateTime.Now:yyyy-MM-dd}
Pick {promptCount} random projectile ribbon trail or particle beam effect names for a fantasy RTS game using theme '{categoryHumanName}'.
For each, generate a 2 words snake_case format filename.
Output ONLY the snake_case filenames each on a separate line.";

				string rawFilenames = await _ollama.GenerateText(filenamesPrompt);
				var filenames = ParseSnakeCaseList(rawFilenames, promptCount);

				var batch = new List<(string Name, string FinalPrompt, string FinalPath)>();

				foreach (var rawName in filenames)
				{
					string name = DeduplicateSnakeCaseTerms($"{category}_{rawName}");
					string finalPath = Path.Combine(categoryOutputFolder, $"{name}.png");
					if (File.Exists(finalPath)) continue;

					string description = rawName.Replace("_", " ");
					Console.WriteLine($"Generating Flux prompt for ribbon effect: {name}");
					string promptReq = $@"Generate a flux prompt for creating a 2D particle ribbon trail texture of: {description} for a {categoryHumanName} effect.
1. The particle effect must be oriented strictly horizontally across the canvas (flowing from left to right along the center horizontal line).
2. The ribbon effect must occupy the horizontal center of the frame, with the top and bottom borders cleanly fading into pure black or empty transparent background.
3. The texture should have a continuous, organic, horizontal flowing stream or trail.
4. DO NOT include any other objects besides the ribbon trail.
5. Solid pitch black background (#000000), with soft feathered top and bottom edges.
Output in JSON format with only this field: fluxPrompt";

					string promptJson = await _ollama.GenerateText(promptReq);
					string fluxPrompt = GetFluxPromptFromResponse(promptJson) ?? description;
					string finalPrompt = $@"horizontal particle ribbon trail texture, game vfx sprite, horizontal flowing stream, continuous left to right flow, centered horizontally, soft fading top and bottom edges, pitch black background, glowing vfx texture for billboard quad trail renderer, RTS particle effect, " + fluxPrompt;

					batch.Add((name, finalPrompt, finalPath));
				}

				if (batch.Count > 0)
				{
					await _procManager.EnsureComfyUI();

					var generatedImages = new List<(string TempImg, string FinalPath, string FinalPrompt, string Name, long Seed)>();
					foreach (var item in batch)
					{
						long seed = SeedFromString(item.FinalPrompt);
						string tempImg = await _comfy.GenerateImage(item.FinalPrompt, item.Name, tempFolder, RibbonWidth, RibbonHeight, seed, removeBackground: true);
						if (!string.IsNullOrEmpty(tempImg) && File.Exists(tempImg))
						{
							generatedImages.Add((tempImg, item.FinalPath, item.FinalPrompt, item.Name, seed));
						}
					}

					foreach (var item in generatedImages)
					{
						Console.WriteLine($"Processing seamless ribbon pipeline for: {item.Name}");
						await MakeRibbonSeamless(item.TempImg, item.FinalPath, item.FinalPrompt, item.Name, tempFolder, item.Seed, SeamThickness, MaskBlurRadius);
					}
				}
			}
		}

		private async Task RunGenerateDecals()
		{
			Console.WriteLine("Starting GenerateDecals task...");
			await _procManager.EnsureOllama();

			var genConfig = _config.GetProperty("Tasks").GetProperty("GenerateDecals");
			int biomeCount = genConfig.TryGetProperty("BiomeCount", out var biomeProp) ? biomeProp.GetInt32() : 15;
			int promptCount = genConfig.GetProperty("PromptCount").GetInt32();
			string outputFolder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, genConfig.GetProperty("OutputFolder").GetString()));
			if (!Directory.Exists(outputFolder)) Directory.CreateDirectory(outputFolder);

			string tempFolder = Path.Combine(AppContext.BaseDirectory, "TempDecals");
			if (!Directory.Exists(tempFolder)) Directory.CreateDirectory(tempFolder);

			Console.WriteLine($"Generating list of {biomeCount} biomes...");
			string biomesPrompt = $@"Timestamp: {DateTime.Now:yyyy-MM-dd}
Pick {biomeCount} diverse and visually distinct biomes or environmental terrain settings for video games.
For each, generate a 1-3 words snake_case format name.
Output ONLY the {biomeCount} snake_case biome names each on a separate line.";

			string rawBiomes = await _ollama.GenerateText(biomesPrompt);
			var biomes = ParseSnakeCaseList(rawBiomes, biomeCount);

			foreach (var biome in biomes)
			{
				string biomeHumanName = biome.Replace("_", " ");
				Console.WriteLine($"\n=== Generating decals for Biome: {biomeHumanName} ({biome}) ===");
				await _procManager.EnsureOllama();

				string biomeOutputFolder = Path.Combine(outputFolder, biomeHumanName);
				if (!Directory.Exists(biomeOutputFolder)) Directory.CreateDirectory(biomeOutputFolder);

				string filenamesPrompt = $@"Timestamp: {DateTime.Now:yyyy-MM-dd}
Pick {promptCount} random ground decal or impact mark names for a fantasy RTS game using biome '{biomeHumanName}'.
For each, generate a 2 words snake_case format filename.
Output ONLY the snake_case filenames each on a separate line.";

				string rawFilenames = await _ollama.GenerateText(filenamesPrompt);
				var filenames = ParseSnakeCaseList(rawFilenames, promptCount, requireUnderscore: true);

				var batch = new List<(string Name, string FinalPrompt, string FinalPath)>();

				foreach (var rawName in filenames)
				{
					string name = DeduplicateSnakeCaseTerms($"{biome}_{rawName}");
					string finalPath = Path.Combine(biomeOutputFolder, $"{name}.png");
					if (File.Exists(finalPath)) continue;

					string description = rawName.Replace("_", " ");
					Console.WriteLine($"Generating Flux prompt for decal: {name}");
					string promptReq = $@"Generate a detailed flux prompt for creating a top-down game decal asset of: {description} for a {biomeHumanName} biome. It should be centered against a solid uniform background.
Output in JSON format with only this field: fluxPrompt";

					string promptJson = await _ollama.GenerateText(promptReq);
					string fluxPrompt = GetFluxPromptFromResponse(promptJson) ?? description;
					string finalPrompt = "top-down flat decal, centered game icon asset, solid uniform background, high contrast, " + fluxPrompt;

					batch.Add((name, finalPrompt, finalPath));
				}

				if (batch.Count > 0)
				{
					await _procManager.EnsureComfyUI();

					foreach (var item in batch)
					{
						string tempImg = await _comfy.GenerateImage(item.FinalPrompt, item.Name, tempFolder, 256, removeBackground: true);
						if (string.IsNullOrEmpty(tempImg) || !File.Exists(tempImg)) continue;
						File.Move(tempImg, item.FinalPath);
					}
				}
			}
		}

		private async Task RunGenerateSpellSpritesheets()
		{
			Console.WriteLine("Starting GenerateSpellSpritesheets task...");
			await _procManager.EnsureOllama();

			var genConfig = _config.GetProperty("Tasks").GetProperty("GenerateSpellSpritesheets");
			int promptCount = genConfig.GetProperty("PromptCount").GetInt32();
			int rows = genConfig.TryGetProperty("Rows", out var rProp) ? rProp.GetInt32() : 4;
			int cols = genConfig.TryGetProperty("Columns", out var cProp) ? cProp.GetInt32() : 4;

			string outputFolder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, genConfig.GetProperty("OutputFolder").GetString()));
			if (!Directory.Exists(outputFolder)) Directory.CreateDirectory(outputFolder);

			string tempFolder = Path.Combine(AppContext.BaseDirectory, "TempSpritesheets");
			if (!Directory.Exists(tempFolder)) Directory.CreateDirectory(tempFolder);

			string filenamesPrompt = $@"Timestamp: {DateTime.Now:yyyy-MM-dd}
Pick {promptCount} random spell sprite or magic projectile names for a fantasy RTS game.
For each, generate a 2 words snake_case format filename.
Output ONLY the snake_case filenames each on a separate line.";

			string rawFilenames = await _ollama.GenerateText(filenamesPrompt);
			var filenames = ParseSnakeCaseList(rawFilenames, promptCount, requireUnderscore: true);

			var batch = new List<(string Name, string FinalPrompt, string FinalPath)>();

			foreach (var name in filenames)
			{
				string finalPath = Path.Combine(outputFolder, $"{name}_sheet.png");
				// string gifPath = Path.Combine(outputFolder, $"{name}_preview.gif"); // GIF generation commented out
				if (File.Exists(finalPath)) continue;

				string description = name.Replace("_", " ");
				Console.WriteLine($"Generating Flux prompt for spritesheet: {name}");
				int totalFrames = rows * cols;
				string promptReq = $@"Generate a detailed flux prompt for creating a game animation spritesheet of: {description}. It should be a {rows}x{cols} grid of sequential animation frames showing the spell evolving over time, against a solid uniform background.
Output in JSON format with only this field: fluxPrompt";

				string promptJson = await _ollama.GenerateText(promptReq);
				string fluxPrompt = GetFluxPromptFromResponse(promptJson) ?? description;
				string finalPrompt = $"spritesheet grid of {totalFrames} sequential animation frames in {rows}x{cols} grid, spell animation, solid uniform background, " + fluxPrompt;

				batch.Add((name, finalPrompt, finalPath));
			}

			if (batch.Count > 0)
			{
				await _procManager.EnsureComfyUI();
				string venvPath = await EnsureSharedPythonEnvironment();
				string pythonExe = Path.Combine(venvPath, OperatingSystem.IsWindows() ? "Scripts\\python.exe" : "bin/python");
				string scriptPath = Path.Combine(AppContext.BaseDirectory, "process_spritesheet.py");

				foreach (var item in batch)
				{
					string tempImg = await _comfy.GenerateImage(item.FinalPrompt, item.Name, tempFolder, 2048);
					if (string.IsNullOrEmpty(tempImg) || !File.Exists(tempImg)) continue;

					Console.WriteLine($"Processing spritesheet slicing for: {item.Name}");
					// GIF generation commented out: omit --output-gif parameter
					await _procManager.RunProcessAsync(pythonExe, $"\"{scriptPath}\" --input \"{tempImg}\" --output-sheet \"{item.FinalPath}\" --rows {rows} --cols {cols}");
				}
			}
		}

		private async Task RunGenerateSkyboxes()
		{
			Console.WriteLine("Starting GenerateSkyboxes task...");
			await _procManager.EnsureOllama();

			var genConfig = _config.GetProperty("Tasks").GetProperty("GenerateSkyboxes");
			int promptCount = genConfig.GetProperty("PromptCount").GetInt32();
			string outputFolder = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, genConfig.GetProperty("OutputFolder").GetString()));
			if (!Directory.Exists(outputFolder)) Directory.CreateDirectory(outputFolder);

			string tempFolder = Path.Combine(AppContext.BaseDirectory, "TempSkyboxes");
			if (!Directory.Exists(tempFolder)) Directory.CreateDirectory(tempFolder);

			string filenamesPrompt = $@"Timestamp: {DateTime.Now:yyyy-MM-dd}
Pick {promptCount} random skybox panorama/background names for a fantasy RTS game.
For each, generate a 2 words snake_case format filename.
Output ONLY the snake_case filenames each on a separate line.";

			string rawFilenames = await _ollama.GenerateText(filenamesPrompt);
			var filenames = ParseSnakeCaseList(rawFilenames, promptCount, requireUnderscore: true);

			var batch = new List<(string Name, string FinalPrompt, string FinalPath)>();

			foreach (var name in filenames)
			{
				string finalPath = Path.Combine(outputFolder, $"{name}.png");
				if (File.Exists(finalPath)) continue;

				string description = name.Replace("_", " ");
				Console.WriteLine($"Generating Flux prompt for skybox: {name}");
				string promptReq = $@"Generate a highly detailed image generation prompt for an equirectangular 360-degree panoramic skybox background for a top-down fantasy RTS game based on this theme: '{description}'.

CRITICAL COMPOSITION RULES:
1. The image must be a wide-open, expansive sky filled with atmospheric lighting, clouds, and theme-appropriate celestial elements.
2. DO NOT mention any terrain, mountains, trees, structures, ground, or floor elements. 
3. The very bottom edge must be described as a featureless, empty, clean gradient of soft haze or atmospheric mist that fades into a solid color.

Include technical terms for generation: '360 degree equirectangular panorama skybox background, seamless horizontal wrap, perfect spherical projection mapping, no visible poles, high dynamic range, cinematic lighting, 8k resolution, ultra-detailed atmosphere'.

Output in valid JSON format with only this field: fluxPrompt";

				string promptJson = await _ollama.GenerateText(promptReq);
				string fluxPrompt = GetFluxPromptFromResponse(promptJson) ?? description;

				batch.Add((name, fluxPrompt, finalPath));
			}

			if (batch.Count > 0)
			{
				await _procManager.EnsureComfyUI();
				string venvPath = await EnsureSharedPythonEnvironment();
				string pythonExe = Path.Combine(venvPath, OperatingSystem.IsWindows() ? "Scripts\\python.exe" : "bin/python");
				string scriptPath = Path.Combine(AppContext.BaseDirectory, "process_skybox.py");

				foreach (var item in batch)
				{
					string tempImg = await _comfy.GenerateImage(item.FinalPrompt, item.Name, tempFolder, 2048, 1024);
					if (string.IsNullOrEmpty(tempImg) || !File.Exists(tempImg)) continue;

					Console.WriteLine($"Post-processing skybox: {item.Name}");
					int exitCode = await _procManager.RunProcessAsync(pythonExe, $"\"{scriptPath}\" --input \"{tempImg}\" --output \"{item.FinalPath}\"");
					try { File.Delete(tempImg); } catch { }

					if (exitCode != 0)
					{
						throw new Exception($"process_skybox.py failed with exit code {exitCode}");
					}
				}
			}
		}

		private async Task RunGenerateMetadata()
		{
			Console.WriteLine("Starting GenerateMetadata task...");

			// 1. Locate assets directory
			string assetsDir = FindAssetsDirectory();
			string repoRoot = Path.GetDirectoryName(assetsDir);
			Console.WriteLine($"Found Assets folder at: {assetsDir}");
			Console.WriteLine($"Repository root at: {repoRoot}");

			// 2. Gather all files in Assets to select real files
			var allFiles = Directory.Exists(assetsDir)
				? Directory.GetFiles(assetsDir, "*.*", SearchOption.AllDirectories)
				: Array.Empty<string>();

			// Categorize files by relative paths (using forward slashes)
			var characterGlbs = new List<string>();
			var buildingGlbs = new List<string>();
			var propGlbs = new List<string>();
			var environmentGlbs = new List<string>();
			var projectileGlbs = new List<string>();

			var abilityIcons = new List<string>();
			var itemIcons = new List<string>();
			var characterIcons = new List<string>();
			var uiIcons = new List<string>();
			var generalIcons = new List<string>();

			var musicTracks = new List<string>();
			var soundEffects = new List<string>();
			var voiceLines = new List<string>();

			foreach (var file in allFiles)
			{
				string relPath = Path.GetRelativePath(repoRoot, file).Replace('\\', '/');
				string lowerPath = relPath.ToLowerInvariant();

				if (lowerPath.EndsWith(".glb"))
				{
					if (lowerPath.Contains("/3d/characters/")) characterGlbs.Add(relPath);
					else if (lowerPath.Contains("/3d/buildings/")) buildingGlbs.Add(relPath);
					else if (lowerPath.Contains("/3d/props/")) propGlbs.Add(relPath);
					else if (lowerPath.Contains("/3d/environment/")) environmentGlbs.Add(relPath);
					else if (lowerPath.Contains("/3d/projectiles/")) projectileGlbs.Add(relPath);
				}
				else if (lowerPath.EndsWith(".png") || lowerPath.EndsWith(".jpg") || lowerPath.EndsWith(".jpeg"))
				{
					if (lowerPath.Contains("/icons/abilities/")) abilityIcons.Add(relPath);
					else if (lowerPath.Contains("/icons/items/")) itemIcons.Add(relPath);
					else if (lowerPath.Contains("/icons/characters/")) characterIcons.Add(relPath);
					else if (lowerPath.Contains("/icons/ui/")) uiIcons.Add(relPath);
					else if (lowerPath.Contains("/icons/")) generalIcons.Add(relPath);
				}
				else if (lowerPath.EndsWith(".ogg") || lowerPath.EndsWith(".wav") || lowerPath.EndsWith(".mp3"))
				{
					if (lowerPath.Contains("/audio/music/")) musicTracks.Add(relPath);
					else if (lowerPath.Contains("/audio/soundeffects/")) soundEffects.Add(relPath);
					else if (lowerPath.Contains("/audio/voices/")) voiceLines.Add(relPath);
				}
			}

			// Asset selector helpers
			string GetRealAsset(List<string> categoryList, string keyword, string fallbackDefault)
			{
				if (categoryList == null || categoryList.Count == 0) return fallbackDefault;
				var matches = categoryList.Where(p => p.ToLowerInvariant().Contains(keyword.ToLowerInvariant())).ToList();
				if (matches.Count > 0)
				{
					return matches.OrderBy(p => p).First();
				}
				return categoryList.OrderBy(p => p).First();
			}

			List<string> GetRealAssets(List<string> categoryList, string keyword, int count, List<string> fallbackList)
			{
				if (categoryList == null || categoryList.Count == 0) return fallbackList;
				var matches = categoryList.Where(p => p.ToLowerInvariant().Contains(keyword.ToLowerInvariant())).OrderBy(p => p).Take(count).ToList();
				if (matches.Count > 0) return matches;
				return categoryList.Take(count).ToList();
			}

			// 4. Ollama image analysis of character sheets (Image-to-Text Round)
			Console.WriteLine("Ensuring Ollama is running for vision/text analysis...");
			await _procManager.EnsureOllama();

			var characterSummaries = new List<string>();

			for (int i = 0; i < characterGlbs.Count; i++)
			{
				string relGlb = characterGlbs[i];
				string absGlb = Path.Combine(repoRoot, relGlb);
				string fileName = Path.GetFileName(relGlb);

				Console.WriteLine($"[Vision] Rendering turnaround sheet for {fileName} ({i + 1}/{characterGlbs.Count})...");
				string glTurnaroundPng = await RunGlTurnaround(absGlb);
				if (string.IsNullOrEmpty(glTurnaroundPng) || !File.Exists(glTurnaroundPng))
				{
					Console.WriteLine($"[Warning] Failed to render turnaround for {fileName}. Skipping.");
					continue;
				}

				Console.WriteLine($"[Vision] Ollama analyzing turnaround for {fileName}...");

				try
				{
					string analysisRaw = await Analyze3DModelTurnaroundImage(glTurnaroundPng);
					int startIdx = analysisRaw.IndexOf("{");
					int endIdx = analysisRaw.LastIndexOf("}");
					if (startIdx >= 0 && endIdx > startIdx)
					{
						analysisRaw = analysisRaw.Substring(startIdx, endIdx - startIdx + 1);
						characterSummaries.Add(analysisRaw);
						Console.WriteLine($"[Vision] Summary for {fileName}: {analysisRaw}");
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine($"[Warning] Ollama vision failed for {fileName}: {ex.Message}");
				}
			}

			if (characterSummaries.Count == 0)
			{
				Console.WriteLine("[Warning] No character summaries generated.");
				return;
			}

			// 5. Round 1: Faction Assignment Prompt
			string summariesJson = "[" + string.Join(",\n", characterSummaries) + "]";
			string organizePrompt = $@"Based on this list of RTS 3D model carahcter asset descriptions:
{summariesJson}

Please organize these assets into exactly 4 unique factions for a fantasy RTS game. For each faction:
1. Define a creative FactionName, ThemeDescription, and a UniqueMechanic/Trait.
2. Select exactly 3 characters from the list to assign as Tier1 (basic), Tier2 (specialist), and Tier3 (Hero). You must reference their original 'Filename'.
3. Assign each unit a thematic 'UnitName' and 'Role' within the faction.

Ensure the 4 factions are:
- Balanced as a whole but asymmetric.
- Thematically distinct
- Do not repeat units across different factions.

Respond ONLY with a valid JSON object in this format (do not wrap in markdown or prefix text, just the raw JSON):
{{
  ""factions"": [
    {{
      ""FactionName"": ""Alliance of Light"",
      ""ThemeDescription"": ""Defensive, armored holy soldiers."",
      ""UniqueMechanic"": ""Holy shield protection and healing spells."",
      ""Tier1"": {{ ""Filename"": ""silver_armor_knight.glb"", ""UnitName"": ""Vanguard Initiate"", ""Role"": ""frontline tank"" }},
      ""Tier2"": {{ ""Filename"": ""blue_robed_wizard.glb"", ""UnitName"": ""Highland Priest"", ""Role"": ""healing caster"" }},
      ""Tier3"": {{ ""Filename"": ""heavy_paladin.glb"", ""UnitName"": ""Arch-Paladin Uther"", ""Role"": ""legendary paladin hero"" }}
    }}
  ]
}}";

			Console.WriteLine("[LLM] Asking Ollama to define 4 factions based on vision summaries...");
			string factionsResponse = await _ollama.GenerateText(organizePrompt);
			int startF = factionsResponse.IndexOf("{");
			int endF = factionsResponse.LastIndexOf("}");
			if (startF >= 0 && endF > startF)
			{
				factionsResponse = factionsResponse.Substring(startF, endF - startF + 1);
			}

			// 6. Round 2: Detailed Stats & Abilities Generation Prompt
			string detailsPrompt = $@"Based on this list of RTS faction definitions:
{factionsResponse}

Please design the detailed stats, weapons, abilities, items, and upgrades for these factions, matching the theme and balance goals.
Rules:
- Balance overall stats: Factions should be balanced (Total HP and DPS comparable, or trade-offs between cost, speed, range, and stats).
- The Tier 3 unit is a Hero (IsHero = true), starts with a potion_of_healing, and has 2 abilities.
- CustomAbilities: Define 2-3 custom abilities per faction that match their theme. Each must have: AbilityId, Name, Description, AbilityType (target_spell/instant_spell/passive), ManaCost, Cooldown, TargetRange, and optional Damage/Healing values.
- CustomWeapons: Define 1 custom weapon per unit with WeaponId, Name, Damage, Range, AttackCooldown, AttackType (melee/ranged).
- CustomUpgrades: Define 1 custom upgrade per faction affecting its units.

Respond ONLY with a valid JSON object in this format (no other text, just JSON):
{{
  ""abilities"": [
    {{
      ""AbilityId"": ""heal"",
      ""Name"": ""Holy Light"",
      ""Description"": ""Heals a friendly unit."",
      ""AbilityType"": ""target_spell"",
      ""ManaCost"": 50,
      ""Cooldown"": 10,
      ""TargetRange"": 800,
      ""Healing"": 150
    }}
  ],
  ""weapons"": [
    {{
      ""WeaponId"": ""alliance_sword"",
      ""Name"": ""Iron Broadsword"",
      ""Damage"": 12,
      ""Range"": 100,
      ""AttackCooldown"": 1.2,
      ""AttackType"": ""melee""
    }}
  ],
  ""upgrades"": [
    {{
      ""UpgradeId"": ""alliance_melee_upg"",
      ""Name"": ""Iron Weapons & Armor"",
      ""Description"": ""Upgrades Vanguard and Paladin combat stats."",
      ""CostGold"": 150,
      ""CostWood"": 100,
      ""CostStone"": 0,
      ""ResearchTime"": 30,
      ""MaxLevel"": 3,
      ""AffectedUnitIds"": [""Alliance_Vanguard"", ""Alliance_Paladin""],
      ""MaxHpBonus"": 50,
      ""DamageBonus"": 3,
      ""ArmorBonus"": 1,
      ""SpeedBonus"": 0
    }}
  ],
  ""units"": {{
    ""Alliance_Vanguard"": {{
      ""MaxHp"": 220,
      ""Damage"": 12,
      ""Range"": 100,
      ""Armor"": 4,
      ""Speed"": 270,
      ""AttackCooldown"": 1.2,
      ""CostGold"": 120,
      ""CostWood"": 20,
      ""CostStone"": 0,
      ""ProductionTime"": 25,
      ""PopCost"": 2,
      ""AttackType"": ""melee"",
      ""ArmorType"": ""heavy"",
      ""GoldBounty"": 36,
      ""MovementType"": ""ground"",
      ""Abilities"": [],
      ""Weapons"": [""alliance_sword""],
      ""StartingItems"": [],
      ""Upgrades"": [""alliance_melee_upg""],
      ""VoiceKeyword"": ""knight""
    }}
  }}
}}";

			Console.WriteLine("[LLM] Asking Ollama to generate detailed stats, weapons, abilities, upgrades...");
			string detailsResponse = await _ollama.GenerateText(detailsPrompt);

			// Clean up helper to strip markdown backticks and extract the outermost JSON object
			string CleanJson(string raw)
			{
				if (string.IsNullOrEmpty(raw)) return "{}";
				// Strip markdown block markers
				raw = System.Text.RegularExpressions.Regex.Replace(raw, @"```json\s*", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
				raw = System.Text.RegularExpressions.Regex.Replace(raw, @"```\s*", "");
				int start = raw.IndexOf("{");
				int end = raw.LastIndexOf("}");
				if (start >= 0 && end > start)
				{
					return raw.Substring(start, end - start + 1);
				}
				return raw.Trim();
			}

			factionsResponse = CleanJson(factionsResponse);
			detailsResponse = CleanJson(detailsResponse);

			try
			{
				var factionsJson = JsonNode.Parse(factionsResponse);
				var detailsJson = JsonNode.Parse(detailsResponse);

				// Construct metadata.json with the dynamic details
				var customAbilities = new List<JsonObject>();
				var customWeapons = new List<JsonObject>();
				var customItems = new List<JsonObject>();
				var customUpgrades = new List<JsonObject>();
				var units = new Dictionary<string, JsonObject>();

				// Build Custom Abilities with real icons and sounds
				foreach (var abiNode in detailsJson["abilities"].AsArray())
				{
					string id = abiNode["AbilityId"].ToString();
					string name = abiNode["Name"].ToString();
					string desc = abiNode["Description"].ToString();
					string type = abiNode["AbilityType"].ToString();
					double mana = abiNode["ManaCost"]?.GetValue<double>() ?? 0;
					double cd = abiNode["Cooldown"]?.GetValue<double>() ?? 0;
					double range = abiNode["TargetRange"]?.GetValue<double>() ?? 0;

					var icon = GetRealAsset(abilityIcons, id, $"Assets/2d/Icons/Abilities/magic_spell.png");
					var sound = GetRealAsset(soundEffects, id, $"Assets/Audio/SoundEffects/Abilities/spell.ogg");

					var abi = new JsonObject
					{
						["AbilityId"] = id,
						["Name"] = name,
						["Description"] = desc,
						["AbilityType"] = type,
						["ManaCost"] = mana,
						["Cooldown"] = cd,
						["TargetRange"] = range,
						["IconPath"] = icon,
						["CastSound"] = sound
					};
					if (abiNode["Damage"] != null) abi["Damage"] = abiNode["Damage"].GetValue<double>();
					if (abiNode["Healing"] != null) abi["Healing"] = abiNode["Healing"].GetValue<double>();
					customAbilities.Add(abi);
				}

				// Build Custom Weapons with real sounds and projectiles
				foreach (var wpnNode in detailsJson["weapons"].AsArray())
				{
					string id = wpnNode["WeaponId"].ToString();
					string name = wpnNode["Name"].ToString();
					double dmg = wpnNode["Damage"].GetValue<double>();
					double range = wpnNode["Range"].GetValue<double>();
					double cd = wpnNode["AttackCooldown"].GetValue<double>();
					string type = wpnNode["AttackType"].ToString();

					var sound = GetRealAsset(soundEffects, id, $"Assets/Audio/SoundEffects/Combat/hit.ogg");
					var wpn = new JsonObject
					{
						["WeaponId"] = id,
						["Name"] = name,
						["Damage"] = dmg,
						["Range"] = range,
						["AttackCooldown"] = cd,
						["AttackType"] = type,
						["AttackSound"] = sound
					};
					if (type == "ranged")
					{
						wpn["ProjectileSpeed"] = 20.0;
						wpn["ProjectileModelPath"] = GetRealAsset(projectileGlbs.Count > 0 ? projectileGlbs : propGlbs, "arrow", "Assets/3d/Projectiles/arrow.glb");
					}
					customWeapons.Add(wpn);
				}

				// Build Items (default standard potions)
				var potionIcon = GetRealAsset(itemIcons, "potion", "Assets/2d/Icons/Items/potion.png");
				customItems.Add(new JsonObject
				{
					["ItemId"] = "potion_of_healing",
					["Name"] = "Potion of Healing",
					["Description"] = "Restores 250 Health.",
					["ItemClass"] = "consumable",
					["CostGold"] = 150.0,
					["IconPath"] = potionIcon,
					["CanDrop"] = true
				});

				// Build Upgrades
				foreach (var upgNode in detailsJson["upgrades"].AsArray())
				{
					string id = upgNode["UpgradeId"].ToString();
					string name = upgNode["Name"].ToString();
					string desc = upgNode["Description"].ToString();
					double gold = upgNode["CostGold"]?.GetValue<double>() ?? 100;
					double wood = upgNode["CostWood"]?.GetValue<double>() ?? 100;

					var affectedArray = new JsonArray();
					if (upgNode["AffectedUnitIds"] != null)
					{
						foreach (var uId in upgNode["AffectedUnitIds"].AsArray()) affectedArray.Add(JsonValue.Create(uId.ToString()));
					}

					var upg = new JsonObject
					{
						["UpgradeId"] = id,
						["Name"] = name,
						["Description"] = desc,
						["CostGold"] = gold,
						["CostWood"] = wood,
						["CostStone"] = 0.0,
						["ResearchTime"] = 30.0,
						["MaxLevel"] = 3,
						["AffectedUnitIds"] = affectedArray,
						["MaxHpBonus"] = upgNode["MaxHpBonus"]?.GetValue<double>() ?? 0,
						["DamageBonus"] = upgNode["DamageBonus"]?.GetValue<double>() ?? 0,
						["ArmorBonus"] = upgNode["ArmorBonus"]?.GetValue<double>() ?? 0
					};
					customUpgrades.Add(upg);
				}

				// Build Units with Vision-assigned character models
				var factionsList = factionsJson["factions"].AsArray();
				foreach (var factionObj in factionsList)
				{
					string factionName = factionObj["FactionName"].ToString();

					var tiers = new[] {
						(Node: factionObj["Tier1"], Key: "Tier1", IsHero: false),
						(Node: factionObj["Tier2"], Key: "Tier2", IsHero: false),
						(Node: factionObj["Tier3"], Key: "Tier3", IsHero: true)
					};

					foreach (var tier in tiers)
					{
						string originalFile = tier.Node["Filename"].ToString();
						string unitName = tier.Node["UnitName"].ToString();
						string role = tier.Node["Role"].ToString();

						// Look up generated details matching the UnitId / Name
						// We search detailsJson["units"] for a unit matching filename or role
						string unitId = factionName.Replace(" ", "") + "_" + tier.Key;

						// Default stats in case matching fails
						double hp = tier.IsHero ? 800.0 : 180.0;
						double dmg = tier.IsHero ? 30.0 : 12.0;
						double range = 100.0;
						double armor = tier.IsHero ? 5.0 : 2.0;
						double speed = 280.0;
						double cd = 1.4;
						double gold = tier.IsHero ? 400.0 : 120.0;
						double wood = tier.IsHero ? 100.0 : 20.0;
						int pop = tier.IsHero ? 5 : 2;
						string attackType = "melee";
						string armorType = "heavy";
						string voiceKeyword = "knight";
						var abilities = new List<string>();
						var weapons = new List<string>();
						var upgrades = new List<string>();

						// Find closest matching key in detailsJson["units"]
						var unitsObj = detailsJson["units"].AsObject();
						KeyValuePair<string, JsonNode> matchKvp = default;
						foreach (var kvp in unitsObj)
						{
							if (kvp.Key.ToLowerInvariant().Contains(tier.Key.ToLowerInvariant()) ||
								kvp.Value["Name"]?.ToString().ToLowerInvariant().Contains(unitName.ToLowerInvariant()) == true)
							{
								matchKvp = kvp;
								break;
							}
						}

						if (matchKvp.Key != null)
						{
							var uDetail = matchKvp.Value;
							hp = uDetail["MaxHp"]?.GetValue<double>() ?? hp;
							dmg = uDetail["Damage"]?.GetValue<double>() ?? dmg;
							range = uDetail["Range"]?.GetValue<double>() ?? range;
							armor = uDetail["Armor"]?.GetValue<double>() ?? armor;
							speed = uDetail["Speed"]?.GetValue<double>() ?? speed;
							cd = uDetail["AttackCooldown"]?.GetValue<double>() ?? cd;
							gold = uDetail["CostGold"]?.GetValue<double>() ?? gold;
							wood = uDetail["CostWood"]?.GetValue<double>() ?? wood;
							pop = uDetail["PopCost"]?.GetValue<int>() ?? pop;
							attackType = uDetail["AttackType"]?.ToString() ?? attackType;
							armorType = uDetail["ArmorType"]?.ToString() ?? armorType;
							voiceKeyword = uDetail["VoiceKeyword"]?.ToString() ?? voiceKeyword;

							if (uDetail["Abilities"] != null)
							{
								foreach (var a in uDetail["Abilities"].AsArray()) abilities.Add(a.ToString());
							}
							if (uDetail["Weapons"] != null)
							{
								foreach (var w in uDetail["Weapons"].AsArray()) weapons.Add(w.ToString());
							}
							if (uDetail["Upgrades"] != null)
							{
								foreach (var u in uDetail["Upgrades"].AsArray()) upgrades.Add(u.ToString());
							}
						}

						if (weapons.Count == 0) weapons.Add("alliance_sword");

						var model = GetRealAsset(characterGlbs, Path.GetFileNameWithoutExtension(originalFile), "Assets/3d/Characters/silver_armor_knight.glb");
						var portrait = model;

						var relativeVoiceLines = GetRealAssets(voiceLines, voiceKeyword, 3, new List<string> {
							$"Assets/Audio/Voices/{voiceKeyword}_yes.ogg",
							$"Assets/Audio/Voices/{voiceKeyword}_attack.ogg",
							$"Assets/Audio/Voices/{voiceKeyword}_ready.ogg"
						});

						var unitNode = new JsonObject
						{
							["UnitId"] = unitId,
							["Name"] = unitName,
							["Description"] = role,
							["MaxHp"] = hp,
							["Damage"] = dmg,
							["Range"] = range,
							["Armor"] = armor,
							["Speed"] = speed,
							["AttackCooldown"] = cd,
							["ScanRadius"] = Math.Max(range + 200, 800.0),
							["CostGold"] = gold,
							["CostWood"] = wood,
							["CostStone"] = 0.0,
							["ProductionTime"] = tier.IsHero ? 60.0 : 15.0 + pop * 5.0,
							["PopCost"] = pop,
							["AttackType"] = attackType,
							["ArmorType"] = armorType,
							["GoldBounty"] = tier.IsHero ? 200.0 : gold * 0.3,
							["ModelPath"] = model,
							["PortraitModelPath"] = portrait,
							["IsHero"] = tier.IsHero,
							["MovementType"] = "ground",
							["Abilities"] = new JsonArray(abilities.Select(a => JsonValue.Create(a)).ToArray()),
							["Weapons"] = new JsonArray(weapons.Select(w => JsonValue.Create(w)).ToArray()),
							["StartingItems"] = tier.IsHero ? new JsonArray(new[] { JsonValue.Create("potion_of_healing") }) : new JsonArray(),
							["Upgrades"] = new JsonArray(upgrades.Select(u => JsonValue.Create(u)).ToArray()),
							["SoundEvents"] = new JsonArray(relativeVoiceLines.Select(v => JsonValue.Create(v)).ToArray())
						};

						if (tier.IsHero)
						{
							unitNode["XpBounty"] = 500.0;
						}
						else
						{
							unitNode["XpBounty"] = (hp + dmg * 5.0) * 0.1;
						}

						units[unitId] = unitNode;
					}
				}

				// 7. Map Properties
				var loadingMusic = GetRealAsset(musicTracks, "loading", GetRealAsset(musicTracks, "ambient", "Assets/Audio/Music/default_theme.ogg"));
				var minimap = GetRealAsset(uiIcons, "minimap", "Assets/2d/Icons/UI/minimap.png");
				var loadingImage = GetRealAsset(uiIcons, "loading", "Assets/2d/Icons/UI/loading_screen.png");

				// Player Slots
				var playerSlots = new JsonArray();
				int slotId = 0;
				string[] colors = { "Blue", "Red", "Purple", "Green" };
				foreach (var factionObj in factionsList)
				{
					string fName = factionObj["FactionName"].ToString();
					playerSlots.Add(new JsonObject
					{
						["SlotId"] = slotId,
						["Name"] = $"{fName} Commander",
						["Color"] = colors[slotId % colors.Length],
						["Faction"] = fName,
						["Controller"] = slotId == 0 ? "HumanPlayer" : "ComputerAi"
					});
					slotId++;
				}

				var mapProps = new JsonObject
				{
					["MapName"] = "Dunia Realm",
					["MapDescription"] = "A perfectly balanced dynamic RTS battlefield generated via vision analysis of character assets.",
					["SuggestedPlayers"] = "2-4",
					["MinimapImage"] = minimap,
					["FogOfWarType"] = "grey",
					["TerrainBaseHeight"] = 10.0,
					["ShadowIntensity"] = 1.2,
					["MapWidth"] = 128,
					["MapHeight"] = 128,
					["PlayableWidth"] = 120,
					["PlayableHeight"] = 120,
					["LoadingImage"] = loadingImage,
					["LoadingMusic"] = loadingMusic,
					["LoadingTitle"] = "Entering Dunia Realm",
					["LoadingSubtitle"] = "Dynamically Balanced Factions Engage",
					["LoadingBodyText"] = "This map features 4 unique factions designed by vision and LLM analysis of 3D game models.",
					["HowToPlayInstructions"] = new JsonArray(
						JsonValue.Create("Harvest Gold and Wood to build your base."),
						JsonValue.Create("Command your Tier 3 Hero to gain experience."),
						JsonValue.Create("Counter enemy units using asymmetric faction mechanics.")
					),
					["HowToPlayObjective"] = "Destroy all enemy bases.",
					["Version"] = "1.0.0",
					["Changelog"] = new JsonArray(
						new JsonObject { ["Version"] = "1.0.0", ["Date"] = "2026-06-26", ["Details"] = "Dynamic vision-designed map metadata release." }
					),
					["PlayerSlots"] = playerSlots,
					["Teams"] = new JsonArray(
						new JsonObject { ["TeamName"] = "Team 1", ["Slots"] = new JsonArray(JsonValue.Create(0), JsonValue.Create(3)) },
						new JsonObject { ["TeamName"] = "Team 2", ["Slots"] = new JsonArray(JsonValue.Create(1), JsonValue.Create(2)) }
					),
					["CustomAbilities"] = new JsonArray(customAbilities.ToArray()),
					["CustomUpgrades"] = new JsonArray(customUpgrades.ToArray()),
					["CustomItems"] = new JsonArray(customItems.ToArray()),
					["CustomWeapons"] = new JsonArray(customWeapons.ToArray())
				};

				var rootJson = new JsonObject
				{
					["MapProperties"] = mapProps
				};

				foreach (var kvp in units)
				{
					rootJson[kvp.Key] = kvp.Value;
				}

				string outputJsonPath = Path.Combine(repoRoot, "metadata.json");
				var writeOptions = new JsonSerializerOptions { WriteIndented = true };
				string jsonText = rootJson.ToJsonString(writeOptions);
				await File.WriteAllTextAsync(outputJsonPath, jsonText, Encoding.UTF8);

				Console.WriteLine($"Successfully generated vision-designed metadata.json at: {outputJsonPath}");

				PrintBalanceAnalysis(units);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[Error] Dynamic metadata design failed: {ex.Message}");
			}
		}

		private void PrintBalanceAnalysis(Dictionary<string, JsonObject> units)
		{
			Console.WriteLine("\n================ FACTION BALANCE ANALYSIS ================");

			var factions = new Dictionary<string, List<JsonObject>>
			{
				["Alliance of Light"] = new List<JsonObject>(),
				["Iron Horde"] = new List<JsonObject>(),
				["Undead Scourge"] = new List<JsonObject>(),
				["Nature Sentinels"] = new List<JsonObject>()
			};

			foreach (var kvp in units)
			{
				string id = kvp.Key;
				var unit = kvp.Value;
				if (id.StartsWith("Alliance_")) factions["Alliance of Light"].Add(unit);
				else if (id.StartsWith("Horde_")) factions["Iron Horde"].Add(unit);
				else if (id.StartsWith("Undead_")) factions["Undead Scourge"].Add(unit);
				else if (id.StartsWith("Nature_")) factions["Nature Sentinels"].Add(unit);
			}

			foreach (var fac in factions)
			{
				string name = fac.Key;
				var uList = fac.Value;
				double totalHp = uList.Sum(u => u["MaxHp"].GetValue<double>());
				double totalDmg = uList.Sum(u => u["Damage"].GetValue<double>());
				double totalGold = uList.Sum(u => u["CostGold"].GetValue<double>());
				double avgSpeed = uList.Average(u => u["Speed"].GetValue<double>());
				double avgCd = uList.Average(u => u["AttackCooldown"].GetValue<double>());

				Console.WriteLine($"Faction: {name}");
				Console.WriteLine($"  - Total Unit HP: {totalHp}");
				Console.WriteLine($"  - Total Unit DMG: {totalDmg}");
				Console.WriteLine($"  - Total Gold Cost: {totalGold}");
				Console.WriteLine($"  - Avg Movement Speed: {avgSpeed:F1}");
				Console.WriteLine($"  - Avg Attack Cooldown: {avgCd:F2}s");
			}
			Console.WriteLine("==========================================================\n");
		}

		private string FindAssetsDirectory()
		{
			string current = AppContext.BaseDirectory;
			while (!string.IsNullOrEmpty(current))
			{
				string candidate = Path.Combine(current, "Assets");
				if (Directory.Exists(candidate) && Directory.Exists(Path.Combine(candidate, "3d")) && Directory.Exists(Path.Combine(candidate, "2d")))
				{
					return Path.GetFullPath(candidate);
				}
				current = Path.GetDirectoryName(current);
			}
			// Fallback
			return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../Assets"));
		}

		private async Task RunDownloadAnimations()
		{
			Console.WriteLine("Starting DownloadAnimations task...");

			// Read settings from config, default if not specified
			bool setupDownloadAllExceptInPlace = true;
			bool setupDownloadOnlyInPlace = false;
			bool setupDownloadOnlyRoot = true;
			bool setupDownloadOnlyInPlaceMirrored = false;
			bool setupDownloadAllExceptInPlaceMirrored = false;
			bool setupDownloadOnlyRootMirrored = false;

			int itemsPerPage = 96;
			int pageNum = 1;
			int maxPages = 999;
			int debugMaxIndex = 0;
			string downloadDirectory = Path.Combine(_cacheDir, "MixamoDownloads");

			if (_config.TryGetProperty("Tasks", out var tasksProp) && tasksProp.TryGetProperty("DownloadAnimations", out var dlProp))
			{
				if (dlProp.TryGetProperty("Setup_DownloadAllExceptInPlace", out var p1)) setupDownloadAllExceptInPlace = p1.GetBoolean();
				if (dlProp.TryGetProperty("Setup_DownloadOnlyInPlace", out var p2)) setupDownloadOnlyInPlace = p2.GetBoolean();
				if (dlProp.TryGetProperty("Setup_DownloadOnlyRoot", out var p3)) setupDownloadOnlyRoot = p3.GetBoolean();
				if (dlProp.TryGetProperty("Setup_DownloadOnlyInPlace_Mirrored", out var p4)) setupDownloadOnlyInPlaceMirrored = p4.GetBoolean();
				if (dlProp.TryGetProperty("Setup_DownloadAllExceptInPlace_Mirrored", out var p5)) setupDownloadAllExceptInPlaceMirrored = p5.GetBoolean();
				if (dlProp.TryGetProperty("Setup_DownloadOnlyRoot_Mirrored", out var p6)) setupDownloadOnlyRootMirrored = p6.GetBoolean();

				if (dlProp.TryGetProperty("ItemsPerPage", out var ipp)) itemsPerPage = ipp.GetInt32();
				if (dlProp.TryGetProperty("StartPage", out var sp)) pageNum = sp.GetInt32();
				if (dlProp.TryGetProperty("MaxPages", out var mp)) maxPages = mp.GetInt32();
				if (dlProp.TryGetProperty("DebugMaxIndex", out var dmi)) debugMaxIndex = dmi.GetInt32();
				if (dlProp.TryGetProperty("DownloadFolder", out var df)) downloadDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, df.GetString()));
			}

			if (!Directory.Exists(downloadDirectory)) Directory.CreateDirectory(downloadDirectory);

			Console.WriteLine("Initializing browser for downloads...");
			var browserFetcher = new BrowserFetcher();
			await browserFetcher.DownloadAsync();

			var userDataDir = Path.Combine(_cacheDir, "PuppeteerUserData");
			var browser = await Puppeteer.LaunchAsync(new LaunchOptions
			{
				Headless = false,
				UserDataDir = userDataDir,
				DefaultViewport = null,
				Args = new[] { "--start-maximized" }
			});

			var page = await browser.NewPageAsync();

			// Enable download in headless/headful with specific directory
			var client = page.Client;
			await client.SendAsync("Browser.setDownloadBehavior", new
			{
				behavior = "allow",
				downloadPath = downloadDirectory
			});

			string startUrl = $"https://www.mixamo.com/#/?page={pageNum}&type=Motion%2CMotionPack&limit={itemsPerPage}";
			Console.WriteLine($"Navigating to {startUrl}");
			await page.GoToAsync(startUrl);

			Console.WriteLine("\n[IMPORTANT]");
			Console.WriteLine("1. Please log in to your Adobe/Mixamo account in the opened browser window.");
			Console.WriteLine("2. Manually download one sample animation first to initialize settings (browser default download setup).");
			Console.WriteLine("Once you are ready and logged in, press [Enter] in this terminal to start automated downloading...");
			Console.ReadLine();

			int filesProcessed = 0;
			bool stopScript = false;

			while (!stopScript && pageNum <= maxPages)
			{
				Console.WriteLine($"\n--- Processing Page {pageNum} ---");

				// Wait for products list to load
				try
				{
					await page.WaitForSelectorAsync(".product", new WaitForSelectorOptions { Timeout = 15000 });
				}
				catch
				{
					Console.WriteLine("No product elements found on the page. Ending task or trying to reload page.");
					break;
				}

				// Check pagination
				int detectedMaxPage = 1;
				var paginationElement = await page.QuerySelectorAsync(".pagination li:nth-last-child(2) a");
				if (paginationElement != null)
				{
					string paginationText = await page.EvaluateFunctionAsync<string>("el => el.textContent", paginationElement);
					if (int.TryParse(paginationText, out int pm))
					{
						detectedMaxPage = pm;
					}
				}
				Console.WriteLine($"Detected total pages on Mixamo: {detectedMaxPage}");

				int productCount = await page.EvaluateExpressionAsync<int>("document.getElementsByClassName('product').length");
				Console.WriteLine($"Found {productCount} animations on this page.");

				for (int index = 0; index < productCount; index++)
				{
					if (debugMaxIndex > 0 && filesProcessed >= debugMaxIndex)
					{
						Console.WriteLine($"Reached max files limit ({debugMaxIndex}). Stopping.");
						stopScript = true;
						break;
					}

					Console.WriteLine($"Processing item {index + 1}/{productCount} (Total processed: {filesProcessed})");

					// Get animation name
					string animName = await page.EvaluateExpressionAsync<string>(
						"(() => { var parent = document.getElementsByClassName('product')[" + index + "]; var el = parent && parent.getElementsByClassName('text-capitalize'); return el && el.length > 0 ? el[0].textContent : ''; })()"
					);
					animName = animName.Trim();
					if (string.IsNullOrEmpty(animName) || animName == "[]")
					{
						Console.WriteLine("Skipping: Could not retrieve animation name.");
						continue;
					}

					if (animName.ToLower().Contains(" pack"))
					{
						Console.WriteLine($"Skipping Pack: {animName}");
						continue;
					}

					string description = await page.EvaluateExpressionAsync<string>(
						"(() => { var parent = document.getElementsByClassName('product')[" + index + "]; var el = parent && parent.getElementsByClassName('product-metadata'); return el && el.length > 0 ? el[0].textContent : ''; })()"
					);
					description = description.Replace("description", "", StringComparison.InvariantCultureIgnoreCase).Replace(":", "").Trim();
					var cleanAnimName = animName.Replace("on x bot", "", StringComparison.InvariantCultureIgnoreCase).Trim();
					var newFileName = Path.Combine(downloadDirectory, string.Concat((cleanAnimName + " - " + description).Split(Path.GetInvalidFileNameChars())).Trim() + ".fbx");
					if (File.Exists(newFileName))
					{
						Console.WriteLine("Skipping " + cleanAnimName + ", already downloaded");
						continue;
					}

					// Click animation product
					await page.EvaluateExpressionAsync($"document.getElementsByClassName('product')[{index}].click()");
					await Task.Delay(2000);

					bool hasInPlace = await page.EvaluateExpressionAsync<bool>("document.getElementsByName('inplace').length > 0");
					bool hasMirror = await page.EvaluateExpressionAsync<bool>("document.getElementsByName('mirror').length > 0");

					// Check which variants to download based on rule matching
					// Determine states to run downloads
					var downloadVariants = new List<(bool inplace, bool mirror)>();

					if (setupDownloadAllExceptInPlace && !hasInPlace)
						downloadVariants.Add((false, false));
					if (setupDownloadOnlyInPlace && hasInPlace)
						downloadVariants.Add((true, false));
					if (setupDownloadOnlyRoot && hasInPlace)
						downloadVariants.Add((false, false));
					if (setupDownloadOnlyInPlaceMirrored && hasInPlace)
						downloadVariants.Add((true, true));
					if (setupDownloadAllExceptInPlaceMirrored && !hasInPlace)
						downloadVariants.Add((false, true));
					if (setupDownloadOnlyRootMirrored && hasInPlace)
						downloadVariants.Add((false, true));

					if (downloadVariants.Count == 0)
					{
						Console.WriteLine($"No matching download variants for '{animName}' under current setup.");
						filesProcessed++;
						continue;
					}

					foreach (var (reqInPlace, reqMirror) in downloadVariants)
					{
						Console.WriteLine($"Downloading variant of '{animName}' (InPlace: {reqInPlace}, Mirror: {reqMirror})");

						// Set checkboxes
						if (hasInPlace)
						{
							await page.EvaluateFunctionAsync(@"async (desired) => {
                                var el = document.getElementsByName('inplace');
                                if (el && el.length > 0 && el[0].checked !== desired) {
                                    el[0].click();
                                    await new Promise(r => setTimeout(r, 500));
                                }
                            }", reqInPlace);
						}

						if (hasMirror)
						{
							await page.EvaluateFunctionAsync(@"async (desired) => {
                                var el = document.getElementsByName('mirror');
                                if (el && el.length > 0 && el[0].checked !== desired) {
                                    el[0].click();
                                    await new Promise(r => setTimeout(r, 500));
                                }
                            }", reqMirror);
						}

						// Trigger download popup
						await page.EvaluateFunctionAsync(@"() => {
                            var buttons = document.getElementsByClassName('btn-block btn btn-primary');
                            if (buttons && buttons.length > 0) {
                                buttons[0].click();
                            }
                        }");

						await Task.Delay(1500);

						// Without skin
						var skinSelectHandle = await page.EvaluateFunctionHandleAsync(@"() => {
                            var labels = document.getElementsByClassName('control-label');
                            if (labels && labels.length > 1) {
                                var selects = labels[1].parentElement.getElementsByTagName('select');
								if (selects && selects.length > 0) {
									return selects[0];
								}
                            }
                        }") as IElementHandle;

						if (skinSelectHandle != null)
						{
							await skinSelectHandle.FocusAsync();
							await Task.Delay(100);

							// Use Puppeteer's SelectAsync
							try
							{
								await skinSelectHandle.SelectAsync("false");
							}
							catch { }
							await Task.Delay(100);
						}

						var extension = ".fbx";
						var initialFiles = Utils.GetOutputFiles(downloadDirectory, extension);

						// Click confirm button
						await page.EvaluateFunctionAsync(@"() => {
                            var buttons = document.getElementsByClassName('btn btn-primary');
                            if (buttons && buttons.length > 1) {
                                buttons[1].click();
                            }
                        }");

						// Wait for progress bar to start and finish
						bool isDownloading = false;
						for (int i = 0; i < 20; i++)
						{
							isDownloading = await page.EvaluateExpressionAsync<bool>("document.getElementsByClassName('progress-bar').length > 0");
							if (isDownloading) break;
							await Task.Delay(250);
						}

						if (isDownloading)
						{
							Console.Write("Downloading...");
							int elapsedSec = 0;
							while (await page.EvaluateExpressionAsync<bool>("document.getElementsByClassName('progress-bar').length > 0") && elapsedSec < 120)
							{
								await Task.Delay(1000);
								elapsedSec++;
							}
							Console.WriteLine(" Done.");
							await Task.Delay(1000); // safety pad

							string resultPath = await Utils.FindNewOutputFile(downloadDirectory, extension, initialFiles, maxWaitMinutes: 1);
							File.Move(resultPath, Path.Combine(downloadDirectory, newFileName));
						}
						else
						{
							Console.WriteLine("Warning: Did not detect download progress bar. Proceeding.");
							await Task.Delay(3000);
						}
					}

					filesProcessed++;
				}

				if (pageNum >= detectedMaxPage)
				{
					Console.WriteLine("Reached max detected page limit. Stopping.");
					break;
				}

				pageNum++;
				string nextPageUrl = $"https://www.mixamo.com/#/?page={pageNum}&type=Motion%2CMotionPack&limit={itemsPerPage}";
				Console.WriteLine($"Navigating to next page: {nextPageUrl}");
				await page.GoToAsync(nextPageUrl);
				await Task.Delay(3000);
			}

			Console.WriteLine("Finished downloading task.");
			await browser.CloseAsync();
		}

		private async Task RunRig3DModels()
		{
			// Iterate over all glb 3d models in \Assets\3d\Characters folder
			string assetsDir = FindAssetsDirectory();
			string charactersDir = Path.Combine(assetsDir, "3d", "Characters");
			string outputCharactersDir = Path.Combine(assetsDir, "3d", "Characters_Rigged");

			if (!Directory.Exists(charactersDir))
			{
				Console.WriteLine($"Characters directory does not exist: {charactersDir}");
				return;
			}

			var glbFiles = Directory.GetFiles(charactersDir, "*.glb");
			if (glbFiles.Length == 0)
			{
				Console.WriteLine($"No .glb files found in {charactersDir}");
				return;
			}

			Console.WriteLine($"Found {glbFiles.Length} .glb models in {charactersDir}. Starting animation process...");

			await _procManager.EnsureComfyUI();

			foreach (var glbPath in glbFiles)
			{
				string baseName = Path.GetFileNameWithoutExtension(glbPath);
				string destGlbPath = Path.Combine(outputCharactersDir, Path.GetFileName(glbPath));
				if (File.Exists(destGlbPath))
				{
					Console.WriteLine($"Character already rigged, skipping. {baseName}");
					continue;
				}

				Console.WriteLine($"Rigging {baseName}");

				string tempGlbPath = null;
				try
				{
					tempGlbPath = await DecompressKtxToTemp(glbPath);

					await RunRemoveAnimationsAndBones(tempGlbPath);

					var riggedGlbPath = await RigHumanoid3DModel(tempGlbPath);
					await RunGltfPack(riggedGlbPath);
					File.Move(riggedGlbPath, destGlbPath, true);
				}
				finally
				{
					try
					{
						if (!string.IsNullOrWhiteSpace(tempGlbPath))
						{
							File.Delete(tempGlbPath);
						}
					}
					catch (Exception ex)
					{
						Console.WriteLine($"Error cleaning up temp model file: {ex.Message}");
					}
				}
			}
		}

		private async Task<string> RigHumanoid3DModel(string glbPath)
		{
			string baseName = Path.GetFileNameWithoutExtension(glbPath);
			string outputDir = Path.GetDirectoryName(glbPath);
			string destGlbPath = Path.Combine(outputDir, Path.GetFileNameWithoutExtension(glbPath) + "_rigged" + Path.GetExtension(glbPath));
			try
			{
				var overrides = new Dictionary<string, Dictionary<string, object>> {
						{ "1", new Dictionary<string, object> {
							{ "input_model_path", glbPath },
							{ "no_fingers", true },
							{ "use_normals", false },
							{ "weight_postprocess", false },
							{ "animation_file", "" }
						} }
					};

				string riggedGlbPath = await _comfy.RunWorkflow(@"ComfyUI\Workflows\model_animate.json", overrides, baseName + "_rigged", ".glb");
				if (!string.IsNullOrEmpty(riggedGlbPath) && File.Exists(riggedGlbPath))
				{
					File.Move(riggedGlbPath, destGlbPath, true);
					Console.WriteLine($"Successfully rigged model: {baseName}");
					return destGlbPath;
				}
				else
				{
					Console.WriteLine($"Failed to rig model: {baseName}");
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"Error rigging model {baseName}: {ex.Message}");
			}

			return glbPath;
		}

		private async Task RunAddAnimationsTo3DModels()
		{
			Console.WriteLine("Starting AddAnimationsTo3DModels task...");
			await _procManager.EnsureOllama();

			string downloadDirectory = Path.Combine(_cacheDir, "MixamoDownloads");
			if (_config.TryGetProperty("Tasks", out var tasksProp) && tasksProp.TryGetProperty("DownloadAnimations", out var dlProp))
			{
				if (dlProp.TryGetProperty("DownloadFolder", out var df))
				{
					downloadDirectory = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, df.GetString()));
				}
			}

			if (!Directory.Exists(downloadDirectory))
			{
				Console.WriteLine($"Directory does not exist: {downloadDirectory}");
				return;
			}

			var fbxFiles = Directory.GetFiles(downloadDirectory, "*.fbx");
			if (fbxFiles.Length == 0)
			{
				Console.WriteLine($"No .fbx files found in {downloadDirectory}");
				return;
			}

			Console.WriteLine($"Found {fbxFiles.Length} .fbx files to process.");

			AnimationCategory[] validCategories = { AnimationCategory.Idle, AnimationCategory.Walk, AnimationCategory.Attack, AnimationCategory.Death, AnimationCategory.Spell_Cast, AnimationCategory.Labor };
			var categorizedAnimations = new Dictionary<AnimationCategory, List<string>>();
			foreach (var cat in validCategories)
			{
				categorizedAnimations[cat] = new List<string>();
			}

			foreach (var file in fbxFiles)
			{
				string filename = Path.GetFileName(file);
				string nameWithoutExt = Path.GetFileNameWithoutExtension(file);

				string prompt = $@"
Note: Attack is not general combat such as being struck, it is only striking towards an enemy.
'Other' category is for any animation that doesn't belong in a fantasy RTS on a unit's basic animation list
Categorize the animation filename '{nameWithoutExt}' into exactly one of the following categories:

{string.Join("\r\n", validCategories.Select(c => c.ToString()))}
Other

Output ONLY the exact category name";

				string response = await _ollama.GenerateText(prompt);
				string categoryStr = response.Trim().Replace("\"", "").Replace("'", "").Replace(".", "").Trim();

				if (Enum.TryParse<AnimationCategory>(categoryStr, true, out var matchedCategory) && matchedCategory != AnimationCategory.Other)
				{
					categorizedAnimations[matchedCategory].Add(file);
					Console.WriteLine($"File: {filename} -> Category: {matchedCategory}");
				}
				else
				{
					Console.WriteLine($"File: {filename} -> Category: Other (Excluded)");
				}
			}

			// Copy to temp files
			string tempDir = Path.GetTempPath();
			var copiedFiles = new Dictionary<AnimationCategory, List<string>>();
			foreach (var cat in validCategories)
			{
				copiedFiles[cat] = new List<string>();
			}

			foreach (var type in validCategories)
			{
				var filesForType = categorizedAnimations[type];
				for (int i = 0; i < filesForType.Count; i++)
				{
					int index = i + 1;
					string targetSubDir = Path.Combine(tempDir, index.ToString());
					if (!Directory.Exists(targetSubDir))
					{
						Directory.CreateDirectory(targetSubDir);
					}
					string destPath = Path.Combine(targetSubDir, $"{type}.fbx");
					try
					{
						File.Copy(filesForType[i], destPath, true);
						copiedFiles[type].Add(destPath);
						Console.WriteLine($"Copied {Path.GetFileName(filesForType[i])} to: {destPath}");
					}
					catch (Exception ex)
					{
						Console.WriteLine($"Error copying to {destPath}: {ex.Message}");
					}
				}
			}

			// Iterate over all glb 3d models in \Assets\3d\Characters folder
			string assetsDir = FindAssetsDirectory();
			string charactersDir = Path.Combine(assetsDir, "3d", "Characters");
			string outputCharactersDir = Path.Combine(assetsDir, "3d", "Characters_Animated");

			if (!Directory.Exists(charactersDir))
			{
				Console.WriteLine($"Characters directory does not exist: {charactersDir}");
				return;
			}

			var glbFiles = Directory.GetFiles(charactersDir, "*.glb");
			if (glbFiles.Length == 0)
			{
				Console.WriteLine($"No .glb files found in {charactersDir}");
				return;
			}

			Console.WriteLine($"Found {glbFiles.Length} .glb models in {charactersDir}. Starting animation process...");

			await _procManager.EnsureComfyUI();

			foreach (var glbPath in glbFiles)
			{
				string baseName = Path.GetFileNameWithoutExtension(glbPath);
				var chosenAnimations = new List<string>();
				foreach (var type in validCategories)
				{
					var list = copiedFiles[type];
					if (list.Count > 0)
					{
						int randIdx = Random.Shared.Next(0, list.Count);
						chosenAnimations.Add(list[randIdx]);
					}
				}

				if (chosenAnimations.Count == 0)
				{
					Console.WriteLine($"Skipping {baseName} because no animations were classified into any of the valid categories.");
					continue;
				}

				Console.WriteLine($"Animating {baseName} using {chosenAnimations.Count} animations...");
				string animationFileParam = string.Join("\n", chosenAnimations);

				string tempGlbPath = await DecompressKtxToTemp(glbPath);

				await RunRemoveAnimationsAndBones(tempGlbPath);

				try
				{
					var overrides = new Dictionary<string, Dictionary<string, object>> {
						{ "1", new Dictionary<string, object> {
							{ "input_model_path", tempGlbPath },
							{ "no_fingers", true },
							{ "use_normals", false },
							{ "weight_postprocess", false },
							{ "animation_file", animationFileParam }
						} }
					};

					string riggedAnimatedGlbPath = await _comfy.RunWorkflow(@"ComfyUI\Workflows\model_animate.json", overrides, baseName + "_animated", ".glb");
					if (!string.IsNullOrEmpty(riggedAnimatedGlbPath) && File.Exists(riggedAnimatedGlbPath))
					{
						if (!Directory.Exists(outputCharactersDir))
						{
							Directory.CreateDirectory(outputCharactersDir);
						}
						string destGlbPath = Path.Combine(outputCharactersDir, Path.GetFileName(glbPath));
						File.Copy(riggedAnimatedGlbPath, destGlbPath, true);
						await RunGltfPack(destGlbPath);
						Console.WriteLine($"Successfully animated and copied model to: {destGlbPath}");
					}
					else
					{
						Console.WriteLine($"Failed to animate model: {glbPath}");
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine($"Error animating model {baseName}: {ex.Message}");
				}
				finally
				{
					try
					{
						File.Delete(tempGlbPath);
					}
					catch (Exception ex)
					{
						Console.WriteLine($"Error cleaning up temp model file: {ex.Message}");
					}
				}
			}
		}

		public static void ResizeImage(string inputPath, int newWidth, int? newHeight = null, SKSamplingOptions? algorithm = null)
		{
			int targetHeight = newHeight ?? newWidth;

			using (var oldBitmap = SKBitmap.Decode(inputPath))
			{
				if (oldBitmap == null)
				{
					throw new InvalidOperationException("Could not decode the image file.");
				}

				var info = new SKImageInfo(newWidth, targetHeight, oldBitmap.ColorType, oldBitmap.AlphaType);

				using (var newBitmap = oldBitmap.Resize(info, algorithm ?? new SKSamplingOptions(SKCubicResampler.Mitchell)))
				{
					if (newBitmap == null)
					{
						throw new InvalidOperationException("Failed to resize the image.");
					}

					using (var image = SKImage.FromBitmap(newBitmap))
					using (var data = image.Encode(SKEncodedImageFormat.Png, 100))
					using (var stream = File.OpenWrite(inputPath))
					{
						stream.SetLength(0);
						data.SaveTo(stream);
					}
				}
			}
		}

		public static (string BaseName, int Index) SplitFileNameIndex(string fileNameOrPath) => Utils.SplitFileNameIndex(fileNameOrPath);

		private string? FindRealmToolsCliPath()
		{
			string[] candidates = new[]
			{
				Path.Combine(AppContext.BaseDirectory, "Realm.Tools.Cli.exe"),
				Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..\\..\\..\\Realm.Tools.Cli.exe")),
				Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..\\..\\..\\..\\Generation\\Realm.Tools.Cli.exe")),
				Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "Generation\\Realm.Tools.Cli.exe"))
			};

			foreach (var path in candidates)
			{
				if (File.Exists(path)) return path;
			}
			return null;
		}

		private async Task RunFinalAssetCleanup()
		{
			Console.WriteLine("Starting FinalAssetCleanup task...");

			string assetDir = @"D:\Realm_Asset_Generation\Assets";
			if (_config.TryGetProperty("Tasks", out var tasks) && tasks.TryGetProperty("FinalAssetCleanup", out var cleanupConfig))
			{
				if (cleanupConfig.TryGetProperty("Directory", out var dirProp) && !string.IsNullOrWhiteSpace(dirProp.GetString()))
				{
					assetDir = dirProp.GetString()!;
				}
				else if (cleanupConfig.TryGetProperty("OutputFolder", out var outProp) && !string.IsNullOrWhiteSpace(outProp.GetString()))
				{
					assetDir = outProp.GetString()!;
				}
				else if (cleanupConfig.TryGetProperty("AssetDirectory", out var assetDirProp) && !string.IsNullOrWhiteSpace(assetDirProp.GetString()))
				{
					assetDir = assetDirProp.GetString()!;
				}
			}
			else if (_config.TryGetProperty("Tasks", out var tasks2) && tasks2.TryGetProperty("CleanupAssets", out var cleanupConfig2))
			{
				if (cleanupConfig2.TryGetProperty("Directory", out var dirProp) && !string.IsNullOrWhiteSpace(dirProp.GetString()))
				{
					assetDir = dirProp.GetString()!;
				}
			}
			else if (_config.TryGetProperty("FinalAssetCleanupDirectory", out var topProp) && !string.IsNullOrWhiteSpace(topProp.GetString()))
			{
				assetDir = topProp.GetString()!;
			}

			if (!Path.IsPathRooted(assetDir))
			{
				assetDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, assetDir));
			}

			Console.WriteLine($"Target Asset Directory: {assetDir}");
			if (!Directory.Exists(assetDir))
			{
				Console.WriteLine($"Directory does not exist: {assetDir}");
				return;
			}

			var indexer = new AssetIndex();
			indexer.PerformCleanup(assetDir);

			Console.WriteLine($"Steps 1 & 2 Complete! Duplicates deleted: {indexer.DuplicatesDeletedCount}, Files renamed: {indexer.FilesRenamedCount}, Unique files indexed: {indexer.Count}.");

			// Step 3: Optimize all .glb files using Realm.Tools.Cli.exe
			Console.WriteLine("\nStep 3: Optimizing all .glb files using Realm.Tools.Cli...");
			string? cliPath = FindRealmToolsCliPath();
			if (cliPath != null)
			{
				Console.WriteLine($"Using CLI tool at: {cliPath}");
				var glbFiles = Directory.GetFiles(assetDir, "*.glb", SearchOption.AllDirectories);
				Array.Sort(glbFiles, StringComparer.OrdinalIgnoreCase);
				Console.WriteLine($"Found {glbFiles.Length} .glb file(s) to process.");

				int totalGlb = glbFiles.Length;
				int lastReportedGlbPercent = 0;
				int glbOptimized = 0;

				for (int i = 0; i < glbFiles.Length; i++)
				{
					var glbPath = glbFiles[i];
					int exitCode = await _procManager.RunProcessAsync(cliPath, $"glb_optimize -i \"{glbPath}\" --in-place");
					if (exitCode == 0) glbOptimized++;

					if (totalGlb > 0)
					{
						int percent = (int)(((i + 1) * 100L) / totalGlb);
						if (percent / 10 > lastReportedGlbPercent / 10)
						{
							lastReportedGlbPercent = (percent / 10) * 10;
							Console.WriteLine($"Step 3 Progress: {lastReportedGlbPercent}% ({i + 1}/{totalGlb} .glb files processed)");
						}
					}
				}
				Console.WriteLine($"Step 3 Complete: {glbOptimized}/{totalGlb} .glb files processed.");
			}
			else
			{
				Console.WriteLine("[WARNING] Realm.Tools.Cli.exe not found. Skipping GLB optimization.");
			}

			// Step 4: Optimize all Tilesheets\**\*.png to ktx2 using Realm.Tools.Cli.exe
			Console.WriteLine("\nStep 4: Converting Tilesheets PNGs to KTX2 using Realm.Tools.Cli...");
			if (cliPath != null)
			{
				string tilesheetsDir = Path.Combine(assetDir, "2d", "Tilesheets");
				if (!Directory.Exists(tilesheetsDir))
				{
					string altDir = Path.Combine(assetDir, "Tilesheets");
					if (Directory.Exists(altDir)) tilesheetsDir = altDir;
				}

				if (Directory.Exists(tilesheetsDir))
				{
					var pngFiles = Directory.GetFiles(tilesheetsDir, "*.png", SearchOption.AllDirectories);
					Array.Sort(pngFiles, StringComparer.OrdinalIgnoreCase);
					Console.WriteLine($"Found {pngFiles.Length} tilesheet .png file(s) in: {tilesheetsDir}");

					int totalPng = pngFiles.Length;
					int lastReportedPngPercent = 0;
					int pngConverted = 0;

					for (int i = 0; i < pngFiles.Length; i++)
					{
						var pngPath = pngFiles[i];
						int exitCode = await _procManager.RunProcessAsync(cliPath, $"texture_convert -i \"{pngPath}\" -m to_ktx2 --in-place");
						if (exitCode == 0) pngConverted++;

						if (totalPng > 0)
						{
							int percent = (int)(((i + 1) * 100L) / totalPng);
							if (percent / 10 > lastReportedPngPercent / 10)
							{
								lastReportedPngPercent = (percent / 10) * 10;
								Console.WriteLine($"Step 4 Progress: {lastReportedPngPercent}% ({i + 1}/{totalPng} tilesheet files processed)");
							}
						}
					}
					Console.WriteLine($"Step 4 Complete: {pngConverted}/{totalPng} tilesheet textures processed.");
				}
				else
				{
					Console.WriteLine($"[INFO] Tilesheets directory not found at '{tilesheetsDir}'. Skipping Step 4.");
				}
			}
			else
			{
				Console.WriteLine("[WARNING] Realm.Tools.Cli.exe not found. Skipping Tilesheets texture conversion.");
			}

			Console.WriteLine("\nFinalAssetCleanup Task Complete!");
		}
	}

	public class ProcessManager
	{
		private readonly JsonElement _config;
		private Process _ollamaProc;
		private Process _comfyProc;
		private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };

		private readonly string _uvPathComfyUI;
		private readonly string _workspacePathComfyUI;
		private const int ComfyPort = 8188;

		public ProcessManager(JsonElement config)
		{
			_config = config;
			var comfyConfig = _config.GetProperty("ComfyUI");
			string installDir = comfyConfig.GetProperty("InstallDir").GetString();
			_uvPathComfyUI = Path.Combine(installDir, "standalone-env", "uv.exe");
			_workspacePathComfyUI = Path.Combine(installDir, "ComfyUI");
		}

		public async Task EnsureOllama()
		{
			if (await IsOllamaRunning()) return;
			StopComfyUI();

			var startInfo = new ProcessStartInfo
			{
				FileName = "ollama",
				Arguments = "serve",
				UseShellExecute = false,
				CreateNoWindow = true
			};
			_ollamaProc = Process.Start(startInfo);
			for (int i = 0; i < 30; i++)
			{
				if (await IsOllamaRunning()) return;
				await Task.Delay(1000);
			}
		}

		public async Task EnsurePythonEnv(string venvPath, string[] requirements, string pythonVersion = "", string uvPath = "", string requirementsFile = "")
		{
			if (string.IsNullOrWhiteSpace(uvPath))
			{
				uvPath = "uv";
				if (!CanRunCommand(uvPath, "--version"))
				{
					Console.WriteLine("Error: 'uv' was not found on your system PATH.");
					Console.WriteLine("Please install 'uv' to your global path: https://github.com/astral-sh/uv");
					Environment.Exit(1);
				}
			}

			if (!Directory.Exists(venvPath))
			{
				Console.WriteLine($"[INFO] Creating Python venv at {venvPath} {(string.IsNullOrEmpty(pythonVersion) ? "" : "using Python " + pythonVersion)}...");
				string venvArgs = $"venv \"{venvPath}\"";
				if (!string.IsNullOrEmpty(pythonVersion)) venvArgs += $" --python {pythonVersion}";
				await RunProcessAsync(uvPath, venvArgs);
			}

			string pythonExe = Path.Combine(venvPath, OperatingSystem.IsWindows() ? "Scripts\\python.exe" : "bin/python");

			Console.WriteLine("[INFO] Installing/Updating Python requirements...");
			if (string.IsNullOrWhiteSpace(uvPath))
			{
				uvPath = "uv";

				if (!CanRunCommand("uv", "--version"))
				{
					Console.WriteLine("Error: 'uv' was not found on your system PATH.");
					Console.WriteLine("Please install 'uv' to your global path: https://github.com/astral-sh/uv");
					Environment.Exit(1);
				}
			}

			if (!string.IsNullOrEmpty(requirementsFile))
			{
				await RunProcessAsync(uvPath, $"pip install -r \"{requirementsFile}\" --python \"{pythonExe}\"");
			}

			if (requirements != null && requirements.Length > 0)
			{
				string reqArgs = string.Join(" ", requirements.Select(r => $"\"{r}\""));
				await RunProcessAsync(uvPath, $"pip install {reqArgs} --python \"{pythonExe}\"");
			}
		}

		public async Task EnsureComfyUI()
		{
			if (await IsComfyUIRunning()) return;
			StopOllama();

			var comfyConfig = _config.GetProperty("ComfyUI");
			string installDir = comfyConfig.GetProperty("InstallDir").GetString();
			string workspacePath = comfyConfig.TryGetProperty("WorkspacePath", out var wp) ? wp.GetString() : _workspacePathComfyUI;
			string sharedModelPaths = comfyConfig.GetProperty("SharedModelPathsConfig").GetString();
			string inputDir = comfyConfig.GetProperty("InputDirectory").GetString();
			string outputDir = comfyConfig.GetProperty("OutputDirectory").GetString();

			string pythonExe = Path.Combine(workspacePath, OperatingSystem.IsWindows() ? ".venv\\Scripts\\python.exe" : ".venv/bin/python");
			if (!File.Exists(pythonExe))
			{
				pythonExe = Path.Combine(installDir, "python_embeded", "python.exe");
				if (!File.Exists(pythonExe))
				{
					pythonExe = "python";
				}
			}

			string mainPy = Path.Combine(workspacePath, "main.py");

			Console.WriteLine("[INFO] Starting ComfyUI server...");
			var startInfo = new ProcessStartInfo
			{
				FileName = pythonExe,
				Arguments = $"\"{mainPy}\" --feature-flag show_signin_button=true --enable-manager " +
							$"--extra-model-paths-config \"{sharedModelPaths}\" " +
							$"--input-directory \"{inputDir}\" " +
							$"--output-directory \"{outputDir}\"",
				WorkingDirectory = workspacePath,
				UseShellExecute = false,
				CreateNoWindow = true
			};

			_comfyProc = Process.Start(startInfo);

			Console.WriteLine("[INFO] Waiting for ComfyUI server to start listening (up to 90 seconds)...");
			bool started = await PollPort("127.0.0.1", ComfyPort, TimeSpan.FromSeconds(90));
			if (!started)
			{
				throw new Exception("Timeout: ComfyUI server did not start listening on port 8188.");
			}
			Console.WriteLine("[INFO] ComfyUI server is online and ready.");
		}

		public string ResolveComfyUIExecutable(out string baseArguments)
		{
			if (CanRunCommand("comfy", "--version"))
			{
				baseArguments = "";
				return "comfy";
			}
			if (File.Exists(_uvPathComfyUI))
			{
				baseArguments = "tool run comfy-cli";
				return _uvPathComfyUI;
			}
			if (CanRunCommand("uv", "--version"))
			{
				baseArguments = "tool run comfy-cli";
				return "uv";
			}
			throw new Exception("Could not locate comfy-cli, global comfy command, or uv.exe.");
		}

		private bool CanRunCommand(string command, string arguments)
		{
			try
			{
				var psi = new ProcessStartInfo
				{
					FileName = command,
					Arguments = arguments,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					UseShellExecute = false,
					CreateNoWindow = true
				};
				using var process = Process.Start(psi);
				if (process != null)
				{
					process.WaitForExit();
					return true;
				}
				return false;
			}
			catch { return false; }
		}

		public async Task<int> RunProcessAsync(string filename, string arguments, TimeSpan? timeout = null)
		{
			var psi = new ProcessStartInfo
			{
				FileName = filename,
				Arguments = arguments,
				RedirectStandardOutput = true,
				RedirectStandardError = true,
				UseShellExecute = false,
				CreateNoWindow = true,
				EnvironmentVariables = { ["PYTHONUTF8"] = "1", ["PYTHONIOENCODING"] = "utf-8" }
			};

			using var process = new Process { StartInfo = psi };
			process.OutputDataReceived += (s, e) => { if (e.Data != null) Console.WriteLine(e.Data); };
			process.ErrorDataReceived += (s, e) => { if (e.Data != null) Console.Error.WriteLine(e.Data); };

			if (!process.Start()) return -1;

			process.BeginOutputReadLine();
			process.BeginErrorReadLine();

			if (timeout.HasValue)
			{
				using var cts = new CancellationTokenSource(timeout.Value);
				try
				{
					await process.WaitForExitAsync(cts.Token);
				}
				catch (OperationCanceledException)
				{
					try { process.Kill(true); } catch { }
					Console.Error.WriteLine($"[ERROR] Process timed out after {timeout.Value.TotalSeconds}s: {filename} {arguments}");
					return -1;
				}
			}
			else
			{
				await process.WaitForExitAsync();
			}

			return process.ExitCode;
		}

		private async Task<bool> PollPort(string host, int port, TimeSpan timeout)
		{
			var stopwatch = Stopwatch.StartNew();
			while (stopwatch.Elapsed < timeout)
			{
				if (await IsPortOpen(host, port)) return true;
				await Task.Delay(2000);
			}
			return false;
		}

		private async Task<bool> IsPortOpen(string host, int port)
		{
			try
			{
				using var client = new TcpClient();
				using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(1));
				await client.ConnectAsync(host, port, cts.Token);
				return true;
			}
			catch { return false; }
		}

		private async Task<bool> IsOllamaRunning()
		{
			try
			{
				using var response = await _http.GetAsync(_config.GetProperty("OllamaUrl").GetString() + "/api/tags");
				return response.IsSuccessStatusCode;
			}
			catch { return false; }
		}

		private async Task<bool> IsComfyUIRunning()
		{
			try
			{
				using var response = await _http.GetAsync(_config.GetProperty("ComfyUIUrl").GetString());
				return response.IsSuccessStatusCode;
			}
			catch { return false; }
		}

		public void StopOllama()
		{
			if (_ollamaProc != null && !_ollamaProc.HasExited)
			{
				try { _ollamaProc.Kill(true); } catch { }
				_ollamaProc.WaitForExit();
				_ollamaProc = null;
			}
			foreach (var p in Process.GetProcessesByName("ollama"))
			{
				try { p.Kill(true); } catch { }
			}
		}

		public void StopComfyUI()
		{
			if (_comfyProc != null && !_comfyProc.HasExited)
			{
				try { _comfyProc.Kill(true); } catch { }
				_comfyProc.WaitForExit();
				_comfyProc = null;
			}
			foreach (var p in Process.GetProcessesByName("python"))
			{
				try
				{
					// Check if it's likely a ComfyUI process by checking command line or just kill it if we are in isolation
					// For safety, we can check if it has 'comfy' in the path or arguments if we had access to that.
					// Given the context of this app, killing all python processes is often what's expected.
					p.Kill(true);
				}
				catch { }
			}
		}

		public void StopAll() { StopOllama(); StopComfyUI(); }
	}

	public class OllamaClient
	{
		private readonly JsonElement _config;
		private readonly string _cacheDir;
		private readonly string _url;
		private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };

		public OllamaClient(JsonElement config, string cacheDir)
		{
			_config = config;
			_cacheDir = cacheDir;
			_url = config.GetProperty("OllamaUrl").GetString();
		}

		public async Task<string> GenerateText(string prompt, bool think = false, string modelOverride = null)
		{
			return await OllamaPromptAsync(prompt, think, modelOverride);
		}

		public async Task<string> AnalyzeImage(string prompt, string imagePath, bool think = false)
		{
			string model = _config.GetProperty("OllamaModel").GetString();
			byte[] imageBytes = await File.ReadAllBytesAsync(imagePath);
			string base64Image = Convert.ToBase64String(imageBytes);
			return await OllamaPromptAsync(prompt, think, model, new[] { base64Image }, imagePath);
		}

		public static string SanitizeClassification(string input)
		{
			if (string.IsNullOrEmpty(input)) return string.Empty;

			string lower = input.ToLowerInvariant();
			var sb = new StringBuilder();
			foreach (char c in lower)
			{
				if (c >= 'a' && c <= 'z')
				{
					sb.Append(c);
				}
				else
				{
					sb.Append('_');
				}
			}

			string temp = System.Text.RegularExpressions.Regex.Replace(sb.ToString(), "_+", "_");
			return temp.Trim('_');
		}

		public static readonly HashSet<string> FantasyClassifications = (new[]
		{
            // Creatures
            "dragon", "griffin", "manticore", "chimera", "basilisk", "hydra", "wyvern", "phoenix", "pegasus", "unicorn",
			"gargoyle", "golem", "earth_elemental", "fire_elemental", "water_elemental", "air_elemental", "treant",
			"minotaur", "centaur", "harpy", "siren", "cerberus", "naga", "imp", "demon", "devil",

            // Buildings
            "barracks", "keep", "tower", "archery_range", "stable", "blacksmith", "marketplace", "town_hall", "lumber_mill",
			"quarry", "farm", "watchtower", "temple", "shrine", "wizard_tower", "fortress", "castle", "shipyard", "dock",
			"warehouse", "armory", "granary", "tavern", "laboratory", "stone_wall", "city_gate",

            // Units
            "footman", "archer", "knight", "pikeman", "crossbowman", "lancer", "scout", "skirmisher", "mage", "cleric",
			"druid", "necromancer", "warlock", "assassin", "thief", "sapper", "catapult", "ballista", "trebuchet",
			"battering_ram", "airship", "galleon", "shield_bearer", "gladiator", "vanguard", "halberdier",
			"paladin", "archmage", "beastmaster", "demon_hunter", "death_knight", "shadow_hunter", "warden", "blademaster",
			"ranger", "high_priest", "lich", "druid_of_the_claw", "spellbreaker", "blood_mage", "chieftain", "pit_lord",
			"crypt_lord", "firelord", "alchemist", "tinker", "sea_witch", "dark_ranger", "inquisitor", "berserker",
			"crusader", "warchief",

            // Props
            "barrel", "crate", "chest", "anvil", "cart", "wagon", "lantern", "torch", "banner", "flag",
			"cage", "throne", "sarcophagus", "pillar", "statue", "fountain", "barricade", "signpost", "bookshelf",
			"catapult_wreckage", "target_dummy", "well", "campfire", "wooden_table", "wooden_chair", "treasure_pile",

            // Environment
            "mountain", "hill", "river", "lake", "swamp", "forest", "cave", "desert", "canyon", "waterfall",
			"stone_bridge", "dirt_path", "cliff", "oasis", "volcano", "crystal_formation", "lava_pool", "ancient_ruins",
			"geyser", "glacier", "coral_reef", "valley", "boulder", "pine_tree", "oak_tree", "stone_arch",

            // Weapons
            "sword", "greatsword", "dagger", "rapier", "mace", "warhammer", "halberd", "spear", "pike", "battleaxe",
			"handaxe", "shortbow", "longbow", "crossbow", "magic_wand", "staff", "scepter", "flail", "scythe",
			"morningstar", "glaive", "throwing_knife", "javelin", "blowgun", "sling", "katana",

            // Armor
            "helmet", "breastplate", "gauntlets", "greaves", "pauldrons", "boots", "shield", "buckler", "tower_shield",
			"chainmail", "leather_armor", "platemail", "robe", "scale_mail", "ring_mail", "bracers", "belt", "cape",
			"cloak", "crown", "visored_helmet", "mail_coif", "targe", "kite_shield", "round_shield", "amulet_of_protection",

            // Items
            "health_potion", "mana_potion", "scroll_of_town_portal", "spellbook", "amulet", "ring", "necklace", "gem",
			"gold_coin", "key", "treasure_map", "war_horn", "hourglass", "compass", "telescope", "quill", "inkwell",
			"golden_chalice", "goblet", "magic_mirror", "pipe", "lute", "harp", "tome_of_knowledge", "parchment", "rune",

            // Fauna
            "wolf", "bear", "deer", "boar", "eagle", "hawk", "owl", "snake", "spider", "rat",
			"bat", "frog", "toad", "fox", "rabbit", "squirrel", "badger", "beaver", "raccoon", "wolf_pup",
			"wild_boar", "stag", "crow", "raven", "salmon", "lizard",

            // Magic Spells
            "fireball", "lightning_bolt", "frostbolt", "meteor_strike", "chain_lightning", "blizzard", "healing_wave", "resurrection",
			"teleport", "haste", "invisibility", "shield_ward", "summon_familiar", "mana_drain", "curse", "mind_control",
			"time_stop", "acid_rain", "earthquake", "wind_gust", "poison_cloud", "bloodlust", "divine_shield", "shadow_step",
			"fire_shield",

            // Building Materials & Resources
            "lumber", "stone", "gold_ore", "iron_ore", "coal", "clay", "obsidian", "mana_crystals", "mithril", "adamantite",
			"leather_hide", "food_rations", "wood_planks", "bricks", "sulfur",

            // catch-all
            "other"
		}).Select(SanitizeClassification).Distinct().ToHashSet();

		public async Task<string> ClassifyImage(string imagePath, bool think = false)
		{
			string model = _config.GetProperty("OllamaModel").GetString();
			string termsList = string.Join(", ", FantasyClassifications);
			string prompt = $"Analyze the provided image and select the one single term from the list below that most closely matches the image. " +
							$"Return ONLY the exact raw matching term from this list:\n\n" +
							$"{termsList}";

			byte[] imageBytes = await File.ReadAllBytesAsync(imagePath);
			string base64Image = Convert.ToBase64String(imageBytes);
			string response = await OllamaPromptAsync(prompt, think, model, new[] { base64Image }, imagePath);

			string sanitizedResponse = SanitizeClassification(response);

			if (FantasyClassifications.Contains(sanitizedResponse))
			{
				return sanitizedResponse;
			}

			return "other";
		}


		public async Task<string> OllamaPromptAsync(string prompt, bool think = false, string model = null, string[] images = null, string imagePathForHash = null)
		{
			model ??= _config.GetProperty("OllamaModel").GetString();
			string hashInput = model + prompt;
			if (images != null && imagePathForHash != null)
			{
				byte[] imgBytes = await File.ReadAllBytesAsync(imagePathForHash);
				hashInput += BitConverter.ToString(MD5.HashData(imgBytes)).Replace("-", "").ToLower();
			}

			string cacheKey = ComputeHash(hashInput);
			string cacheFile = Path.Combine(_cacheDir, $"{cacheKey}.bin");

			if (File.Exists(cacheFile)) return await File.ReadAllTextAsync(cacheFile);

			var requestBody = new
			{
				model = model,
				think = think,
				prompt = prompt,
				stream = false,
				images = images,
				options = new { temperature = 1.0, top_p = 0.95, top_k = 64 }
			};

			var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
			using var response = await _http.PostAsync($"{_url}/api/generate", content);
			response.EnsureSuccessStatusCode();

			var jsonResponse = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
			string result = jsonResponse.RootElement.GetProperty("response").GetString();

			await File.WriteAllTextAsync(cacheFile, result);
			return result;
		}

		private string ComputeHash(string input)
		{
			byte[] hashBytes = MD5.HashData(Encoding.UTF8.GetBytes(input));
			return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
		}

		public async Task<string> GenerateSoundEffectPrompt(string fileName, string description)
		{
			Console.WriteLine($"Generating sound effect prompt for: {fileName}");
			string promptReq = $@"Output ONLY the raw prompt. Generate a single short, vivid, AI generation prompt for a high-quality video game sound effect with filename: '{fileName}' described by: {description}";

			string prompt = await GenerateText(promptReq);
			prompt = prompt.Trim(' ', '"', '.', '\n', '\r');
			if (string.IsNullOrWhiteSpace(prompt))
			{
				return "";
			}
			return $"Video Game Sound Effect, {fileName.Replace("_", " ")} - {prompt}";
		}
	}

	public class ComfyUIClient
	{
		private readonly JsonElement _config;
		private readonly string _cacheDir;
		private readonly ProcessManager _procManager;
		private readonly HttpClient _http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };

		public ComfyUIClient(JsonElement config, string cacheDir, ProcessManager procManager)
		{
			_config = config;
			_cacheDir = cacheDir;
			_procManager = procManager;
		}

		public async Task<string> GenerateImage(string fluxPrompt, string fileName, string outputFolder, int? imageSizeX = null, int? imageSizeY = null, long? seed = null, bool removeBackground = false)
		{
			var comfyConfig = _config.GetProperty("ComfyUI");

			var overrides = new Dictionary<string, Dictionary<string, object>>
			{
				{
					"4", new Dictionary<string, object>
					{
						{ "text", fluxPrompt }
					}
				},
				{
					"1", new Dictionary<string, object>
					{
						{ "unet_name", comfyConfig.GetProperty("diffusion_unet_model_name").GetString() }
					}
				},
				{
					"3", new Dictionary<string, object>
					{
						{ "vae_name", comfyConfig.GetProperty("vae_model_name").GetString() }
					}
				},
				{
					"2", new Dictionary<string, object>
					{
						{ "clip_name", comfyConfig.GetProperty("clip_model_name").GetString() }
					}
				},
				{
					"5", new Dictionary<string, object>
					{
						{ "width", imageSizeX ?? 512 },
						{ "height", imageSizeY ?? imageSizeX ?? 512 }
					}
				},
				{
					"54", new Dictionary<string, object>
					{
						{ "value", removeBackground }
					}
				}
			};

			return await RunWorkflow(@"ComfyUI\Workflows\Flux2_Text_To_Image.json", overrides, fileName, ".png", seed);
		}

		public async Task<string> InpaintImage(string fluxPrompt, string inputImagePath, string fileName, long? seed = null, bool removeBackground = false)
		{
			var comfyConfig = _config.GetProperty("ComfyUI");

			var overrides = new Dictionary<string, Dictionary<string, object>>
			{
				{
					"80", new Dictionary<string, object>
					{
						{ "text", fluxPrompt }
					}
				},
				{
					"76", new Dictionary<string, object>
					{
						{ "unet_name", comfyConfig.GetProperty("diffusion_unet_model_name").GetString() }
					}
				},
				{
					"75", new Dictionary<string, object>
					{
						{ "vae_name", comfyConfig.GetProperty("vae_model_name").GetString() }
					}
				},
				{
					"77", new Dictionary<string, object>
					{
						{ "clip_name", comfyConfig.GetProperty("clip_model_name").GetString() }
					}
				},
				{
					"82", new Dictionary<string, object>
					{
						{ "image", inputImagePath }
					}
				},
				{
					"92", new Dictionary<string, object>
					{
						{ "value", removeBackground }
					}
				}
			};

			return await RunWorkflow(@"ComfyUI\Workflows\Flux2_Image_To_Image_With_Mask.json", overrides, fileName, ".png", seed);
		}

		public async Task<string> RemoveBackground(string inputImagePath, string fileName)
		{
			var comfyConfig = _config.GetProperty("ComfyUI");

			var overrides = new Dictionary<string, Dictionary<string, object>>
			{
				{
					"61", new Dictionary<string, object>
					{
						{ "image", inputImagePath }
					}
				}
			};

			return await RunWorkflow(@"ComfyUI\Workflows\Remove_Background.json", overrides, fileName, ".png");
		}

		private async Task<string> ConvertUiToApiWorkflow(JsonNode uiWorkflow)
		{
			var apiWorkflow = new JsonObject();
			var nodes = uiWorkflow["nodes"]?.AsArray();
			var linksArray = uiWorkflow["links"]?.AsArray();

			if (nodes == null) return uiWorkflow.ToJsonString();

			var links = new Dictionary<int, JsonArray>();
			if (linksArray != null)
			{
				foreach (var linkObj in linksArray)
				{
					var link = linkObj as JsonArray;
					if (link != null && link.Count >= 4)
					{
						int linkId = link[0].GetValue<int>();
						int fromNode = link[1].GetValue<int>();
						int fromSocket = link[2].GetValue<int>();
						links[linkId] = new JsonArray { fromNode.ToString(), fromSocket };
					}
				}
			}

			string objectInfoJson = null;
			try
			{
				string url = _config.GetProperty("ComfyUIUrl").GetString().TrimEnd('/') + "/object_info";
				objectInfoJson = await _http.GetStringAsync(url);
			}
			catch (Exception ex)
			{
				Console.WriteLine("Warning: Failed to fetch /object_info: " + ex.Message);
			}

			var objectInfo = objectInfoJson != null ? JsonNode.Parse(objectInfoJson) : null;

			foreach (var node in nodes)
			{
				if (node == null) continue;
				string id = node["id"]?.ToString();
				string type = node["type"]?.ToString();
				if (id == null || type == null) continue;

				if (objectInfo != null && objectInfo[type] == null) continue;

				var apiNode = new JsonObject();
				apiNode["class_type"] = type;
				var inputs = new JsonObject();
				apiNode["inputs"] = inputs;

				var expectedInputs = new List<string>();
				if (objectInfo != null && objectInfo[type] != null)
				{
					var nodeInfo = objectInfo[type];
					var required = nodeInfo["input"]?["required"]?.AsObject();
					if (required != null) foreach (var kvp in required) expectedInputs.Add(kvp.Key);
					var optional = nodeInfo["input"]?["optional"]?.AsObject();
					if (optional != null) foreach (var kvp in optional) expectedInputs.Add(kvp.Key);
				}

				var connectedInputs = new HashSet<string>();
				var uiInputs = node["inputs"]?.AsArray();
				if (uiInputs != null)
				{
					foreach (var uiInput in uiInputs)
					{
						string name = uiInput["name"]?.ToString();
						var linkNode = uiInput["link"];
						if (name != null && linkNode != null && linkNode.GetValueKind() == JsonValueKind.Number)
						{
							int linkId = linkNode.GetValue<int>();
							if (links.ContainsKey(linkId))
							{
								inputs[name] = JsonNode.Parse(links[linkId].ToJsonString());
								connectedInputs.Add(name);
							}
						}
					}
				}

				var widgetsValues = node["widgets_values"]?.AsArray();
				if (widgetsValues != null && expectedInputs.Count > 0)
				{
					int widgetIndex = 0;
					foreach (string inputName in expectedInputs)
					{
						if (!connectedInputs.Contains(inputName))
						{
							if (widgetIndex < widgetsValues.Count)
							{
								var val = widgetsValues[widgetIndex];
								inputs[inputName] = val != null ? JsonNode.Parse(val.ToJsonString()) : null;
								widgetIndex++;

								if (inputName == "seed" || inputName == "noise_seed")
								{
									if (widgetIndex < widgetsValues.Count && widgetsValues[widgetIndex]?.ToString() is string s &&
										(s == "randomize" || s == "fixed" || s == "increment" || s == "decrement" || s == "control_after_generate"))
									{
										widgetIndex++;
									}
								}
							}
						}
					}
				}

				apiWorkflow[id] = apiNode;
			}

			return apiWorkflow.ToJsonString();
		}

		private string GetWorkflowHash(string workflowJsonFilename, Dictionary<string, Dictionary<string, object>> nodeOverrides, string fileName)
		{
			var shortWorkflowName = Path.GetFileName(workflowJsonFilename);
			return Utils.ComputeHash(shortWorkflowName + (nodeOverrides != null ? JsonSerializer.Serialize(nodeOverrides) : "") + fileName);
		}

		public string GetWorkflowCacheFilePath(string workflowJsonFilename, Dictionary<string, Dictionary<string, object>> nodeOverrides, string fileName, string extension)
		{
			string cacheKey = GetWorkflowHash(workflowJsonFilename, nodeOverrides, fileName);
			return Path.Combine(_cacheDir, $"{cacheKey}{extension}");
		}

		public async Task<string> RunWorkflow(string workflowJsonFilename, Dictionary<string, Dictionary<string, object>> nodeOverrides, string fileName, string extension, long? seed = null)
		{
			string cacheFile = GetWorkflowCacheFilePath(workflowJsonFilename, nodeOverrides, fileName, extension);
			if (File.Exists(cacheFile)) return cacheFile;

			string workflowJson = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, workflowJsonFilename));
			var root = JsonNode.Parse(workflowJson);
			string apiWorkflowStr = await ConvertUiToApiWorkflow(root);
			var apiRoot = JsonNode.Parse(apiWorkflowStr);

			if (apiRoot is JsonObject seedRoot)
			{
				foreach (var nodeKvp in seedRoot)
				{
					var nodeObj = nodeKvp.Value?.AsObject();
					var inputs = nodeObj?["inputs"]?.AsObject();
					if (inputs != null && inputs.ContainsKey("seed"))
					{
						inputs["seed"] = seed ?? Random.Shared.NextInt64(100000000000000, 9007199254740991);
					}
				}
			}

			if (nodeOverrides != null)
			{
				foreach (var kvp in nodeOverrides)
				{
					string nodeId = kvp.Key;
					if (apiRoot[nodeId] != null && apiRoot[nodeId]["inputs"] != null)
					{
						var inputs = apiRoot[nodeId]["inputs"].AsObject();
						foreach (var over in kvp.Value)
						{
							inputs[over.Key] = JsonValue.Create(over.Value);
						}
					}
				}
			}

			var comfyConfig = _config.GetProperty("ComfyUI");
			string inputDir = comfyConfig.GetProperty("InputDirectory").GetString();
			if (!Directory.Exists(inputDir)) Directory.CreateDirectory(inputDir);

			if (apiRoot is JsonObject apiRootObj)
			{
				foreach (var nodeKvp in apiRootObj)
				{
					var nodeObj = nodeKvp.Value?.AsObject();
					if (nodeObj == null) continue;

					string classType = nodeObj["class_type"]?.ToString();
					var inputs = nodeObj["inputs"]?.AsObject();
					if (inputs == null) continue;

					var inputKeys = inputs.Select(k => k.Key).ToList();
					foreach (var key in inputKeys)
					{
						var nodeVal = inputs[key];
						if (nodeVal != null && nodeVal.GetValueKind() == JsonValueKind.String)
						{
							string strVal = nodeVal.GetValue<string>();
							if (!string.IsNullOrEmpty(strVal) && File.Exists(strVal))
							{
								string fileNameOnly = Path.GetFileName(strVal);
								string destFile = Path.Combine(inputDir, fileNameOnly);
								try
								{
									if (!File.Exists(destFile) || new FileInfo(strVal).LastWriteTime != new FileInfo(destFile).LastWriteTime)
									{
										File.Copy(strVal, destFile, true);
									}
								}
								catch (Exception ex)
								{
									Console.WriteLine($"[WARNING] Could not copy input file '{strVal}' to '{destFile}': {ex.Message}");
								}

								if (classType == "LoadImage" || classType == "LoadImageMask" || key == "image")
								{
									inputs[key] = fileNameOnly;
								}
								else
								{
									inputs[key] = destFile;
								}
							}
						}
					}
				}
			}

			string outputDir = comfyConfig.GetProperty("OutputDirectory").GetString();
			string url = _config.GetProperty("ComfyUIUrl").GetString().TrimEnd('/') + "/prompt";

			var initialFiles = Utils.GetOutputFiles(outputDir, extension);

			Console.WriteLine($"[INFO] Submitting workflow to ComfyUI API for {fileName}...");

			var requestBody = new
			{
				client_id = Guid.NewGuid().ToString(),
				prompt = apiRoot
			};

			var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
			var response = await _http.PostAsync(url, content);

			if (!response.IsSuccessStatusCode)
			{
				string error = await response.Content.ReadAsStringAsync();
				Console.WriteLine($"[ERROR] ComfyUI API returned {response.StatusCode}: {error}");
				return null;
			}

			string resultPath = await Utils.FindNewOutputFile(outputDir, extension, initialFiles);
			if (resultPath != null)
			{
				File.Copy(resultPath, cacheFile, true);
				return cacheFile;
			}

			return null;
		}
	}

	public static class Utils
	{
		public static HashSet<string> GetOutputFiles(string directory, string extension)
		{
			var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
			if (Directory.Exists(directory))
			{
				foreach (var file in Directory.GetFiles(directory, "*" + extension))
				{
					files.Add(Path.GetFileName(file));
				}
			}
			return files;
		}

		public static async Task<string> FindNewOutputFile(string directory, string extension, HashSet<string> initialFiles, int maxWaitMinutes = 5)
		{
			const int delayMs = 500;

			try
			{
				int maxWaitMs = (int)TimeSpan.FromMinutes(maxWaitMinutes).TotalMilliseconds;
				for (int i = 0; i < maxWaitMs / delayMs; i++) // Poll for up to 5 minutes
				{
					var currentFiles = GetOutputFiles(directory, extension);
					var newFiles = currentFiles.Except(initialFiles).ToList();

					if (newFiles.Count > 0)
					{
						var newest = newFiles
							.Select(f => new FileInfo(Path.Combine(directory, f)))
							.OrderByDescending(f => f.LastWriteTime)
							.FirstOrDefault();

						if (newest != null && newest.Exists)
						{
							return newest.FullName;
						}
					}

					await Task.Delay(delayMs);
				}

				return null;
			}
			finally
			{
				await Task.Delay(delayMs); // extra delay after file found so it can have time to finish saving
			}
		}

		public static string ComputeHash(string input)
		{
			byte[] hashBytes = MD5.HashData(Encoding.UTF8.GetBytes(input));
			return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
		}

		public static (string BaseName, int Index) SplitFileNameIndex(string fileNameOrPath)
		{
			if (string.IsNullOrWhiteSpace(fileNameOrPath))
				return (string.Empty, 0);

			string fileNameWithoutExt = Path.GetFileNameWithoutExtension(fileNameOrPath);
			int lastUnderscore = fileNameWithoutExt.LastIndexOf('_');
			if (lastUnderscore > 0 && lastUnderscore < fileNameWithoutExt.Length - 1)
			{
				string suffix = fileNameWithoutExt.Substring(lastUnderscore + 1);
				if (int.TryParse(suffix, out int parsedIndex) && parsedIndex >= 0)
				{
					string baseName = fileNameWithoutExt.Substring(0, lastUnderscore);
					return (baseName, parsedIndex);
				}
			}

			return (fileNameWithoutExt, 0);
		}

		public static string ComputeBlake3Hash(string filePath)
		{
			using var hasher = Hasher.New();
			using var stream = File.OpenRead(filePath);
			byte[] buffer = new byte[65536];
			int bytesRead;
			while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
			{
				hasher.Update(buffer.AsSpan(0, bytesRead));
			}
			return hasher.Finalize().ToString();
		}

		public static string? ExtractRealmMetadata(string filePath) => RealmMetadataHelper.ExtractMetadata(filePath);
		public static bool AddRealmMetadata(string filePath, string realmMetadataJson) => RealmMetadataHelper.AddMetadata(filePath, realmMetadataJson);
	}

	public class AssetIndexEntry
	{
		public string FilePath { get; set; } = string.Empty;
		public string FileName { get; set; } = string.Empty;
		public string BaseName { get; set; } = string.Empty;
		public int Index { get; set; }
		public string Extension { get; set; } = string.Empty;
		public string Blake3Hash { get; set; } = string.Empty;
		public long FileSizeBytes { get; set; }
		public DateTime LastModifiedUtc { get; set; }
		public string? RealmMetadataJson { get; set; }
	}

	public class AssetIndex
	{
		private readonly Dictionary<string, AssetIndexEntry> _entriesByHash = new();
		private readonly Dictionary<string, AssetIndexEntry> _entriesByFileName = new(StringComparer.OrdinalIgnoreCase);
		private readonly Dictionary<string, int> _maxIndexByPrefix = new(StringComparer.OrdinalIgnoreCase);
		private readonly List<AssetIndexEntry> _allEntries = new();

		public IReadOnlyList<AssetIndexEntry> Entries => _allEntries;
		public int Count => _allEntries.Count;
		public int DuplicatesDeletedCount { get; private set; }
		public int FilesRenamedCount { get; private set; }

		public void Clear()
		{
			_entriesByHash.Clear();
			_entriesByFileName.Clear();
			_maxIndexByPrefix.Clear();
			_allEntries.Clear();
			DuplicatesDeletedCount = 0;
			FilesRenamedCount = 0;
		}

		public bool TryGetByHash(string hash, out AssetIndexEntry? entry) => _entriesByHash.TryGetValue(hash, out entry);
		public bool TryGetByFileName(string fileName, out AssetIndexEntry? entry) => _entriesByFileName.TryGetValue(fileName, out entry);
		public AssetIndexEntry? GetByHash(string hash) => _entriesByHash.TryGetValue(hash, out var entry) ? entry : null;
		public AssetIndexEntry? GetByFileName(string fileName) => _entriesByFileName.TryGetValue(fileName, out var entry) ? entry : null;

		public int GetMaxIndex(string basePrefix)
		{
			return _maxIndexByPrefix.TryGetValue(basePrefix, out int val) ? val : 0;
		}

		public string GenerateNextFileName(string fileNameOrPath)
		{
			var (baseName, _) = Utils.SplitFileNameIndex(fileNameOrPath);
			string ext = Path.GetExtension(fileNameOrPath);
			int nextIndex = GetMaxIndex(baseName) + 1;
			return $"{baseName}_{nextIndex}{ext}";
		}

		public AssetIndexEntry? AddOrUpdateFile(string filePath, bool deleteIfDuplicate = false)
		{
			if (!File.Exists(filePath)) return null;

			string hash;
			try
			{
				hash = Utils.ComputeBlake3Hash(filePath);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[ERROR] Failed to compute BLAKE3 hash for '{filePath}': {ex.Message}");
				return null;
			}

			if (_entriesByHash.TryGetValue(hash, out var existing))
			{
				if (deleteIfDuplicate)
				{
					try
					{
						File.Delete(filePath);
						DuplicatesDeletedCount++;
						Console.WriteLine($"[Duplicate Deleted] Deleted '{filePath}' (Duplicate of '{existing.FilePath}')");
					}
					catch (Exception ex)
					{
						Console.WriteLine($"[ERROR] Could not delete duplicate '{filePath}': {ex.Message}");
					}
					return null;
				}
				return existing;
			}

			string? metadata = null;
			try
			{
				metadata = RealmMetadataHelper.ExtractMetadata(filePath);
			}
			catch { }

			var (baseName, index) = Utils.SplitFileNameIndex(filePath);
			if (!string.IsNullOrEmpty(baseName))
			{
				if (!_maxIndexByPrefix.TryGetValue(baseName, out int currentMax) || index > currentMax)
				{
					_maxIndexByPrefix[baseName] = index;
				}
			}

			var fileInfo = new FileInfo(filePath);
			string fileName = Path.GetFileName(filePath);
			var entry = new AssetIndexEntry
			{
				FilePath = filePath,
				FileName = fileName,
				BaseName = baseName,
				Index = index,
				Extension = Path.GetExtension(filePath),
				Blake3Hash = hash,
				FileSizeBytes = fileInfo.Exists ? fileInfo.Length : 0,
				LastModifiedUtc = fileInfo.Exists ? fileInfo.LastWriteTimeUtc : DateTime.UtcNow,
				RealmMetadataJson = metadata
			};

			_entriesByHash[hash] = entry;
			_allEntries.Add(entry);

			if (!_entriesByFileName.ContainsKey(fileName))
			{
				_entriesByFileName[fileName] = entry;
			}

			return entry;
		}

		public bool RemoveFile(string filePath)
		{
			string fileName = Path.GetFileName(filePath);
			var entry = _allEntries.FirstOrDefault(e => e.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));
			if (entry == null) return false;

			_allEntries.Remove(entry);
			if (!string.IsNullOrEmpty(entry.Blake3Hash)) _entriesByHash.Remove(entry.Blake3Hash);
			if (_entriesByFileName.TryGetValue(fileName, out var existing) && existing == entry)
			{
				_entriesByFileName.Remove(fileName);
			}
			return true;
		}

		public bool RenameConflict(string filePath, out string newFilePath)
		{
			newFilePath = filePath;
			if (!File.Exists(filePath)) return false;

			string fileName = Path.GetFileName(filePath);
			if (!_entriesByFileName.TryGetValue(fileName, out var existing) || existing.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}

			var (baseName, _) = Utils.SplitFileNameIndex(fileName);
			string extension = Path.GetExtension(filePath);
			string dir = Path.GetDirectoryName(filePath) ?? "";

			int currentMax = GetMaxIndex(baseName);
			int newIndex = currentMax + 1;
			_maxIndexByPrefix[baseName] = newIndex;

			string newFileName = $"{baseName}_{newIndex}{extension}";
			newFilePath = Path.Combine(dir, newFileName);

			while (_entriesByFileName.ContainsKey(newFileName) || File.Exists(newFilePath))
			{
				newIndex++;
				_maxIndexByPrefix[baseName] = newIndex;
				newFileName = $"{baseName}_{newIndex}{extension}";
				newFilePath = Path.Combine(dir, newFileName);
			}

			try
			{
				File.Move(filePath, newFilePath);
				FilesRenamedCount++;
				Console.WriteLine($"[Renamed Conflict] Renamed '{filePath}' -> '{newFilePath}' (Conflict with '{existing.FilePath}')");

				var entry = _allEntries.FirstOrDefault(e => e.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));
				if (entry != null)
				{
					entry.FilePath = newFilePath;
					entry.FileName = newFileName;
					entry.Index = newIndex;
					_entriesByFileName[newFileName] = entry;
				}
				return true;
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[ERROR] Failed to rename '{filePath}' to '{newFilePath}': {ex.Message}");
				return false;
			}
		}

		public void UpdateMetadata(string filePath, string realmMetadataJson)
		{
			if (!File.Exists(filePath)) return;
			bool success = RealmMetadataHelper.AddMetadata(filePath, realmMetadataJson);
			if (success)
			{
				var entry = _allEntries.FirstOrDefault(e => e.FilePath.Equals(filePath, StringComparison.OrdinalIgnoreCase));
				if (entry != null)
				{
					entry.RealmMetadataJson = realmMetadataJson;
					try
					{
						string newHash = Utils.ComputeBlake3Hash(filePath);
						if (!string.IsNullOrEmpty(entry.Blake3Hash)) _entriesByHash.Remove(entry.Blake3Hash);
						entry.Blake3Hash = newHash;
						_entriesByHash[newHash] = entry;
					}
					catch { }
				}
			}
		}

		public void EnumerateDirectory(string directoryPath, bool deleteDuplicates = false, bool renameConflicts = false, Action<int, int, int>? progressCallback = null)
		{
			if (!Directory.Exists(directoryPath))
			{
				Console.WriteLine($"Directory does not exist: {directoryPath}");
				return;
			}

			Clear();
			var allFiles = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);
			Array.Sort(allFiles, StringComparer.OrdinalIgnoreCase);

			Console.WriteLine($"Found {allFiles.Length} files to process.");
			Console.WriteLine("Step 1: Enumerating files, computing BLAKE3 hashes, extracting Realm metadata, and calculating max indices...");

			int totalFiles = allFiles.Length;
			int lastReportedPercent = 0;

			for (int i = 0; i < allFiles.Length; i++)
			{
				var filePath = allFiles[i];
				AddOrUpdateFile(filePath, deleteIfDuplicate: deleteDuplicates);

				if (totalFiles > 0)
				{
					int percent = (int)(((i + 1) * 100L) / totalFiles);
					if (percent / 10 > lastReportedPercent / 10)
					{
						lastReportedPercent = (percent / 10) * 10;
						Console.WriteLine($"Step 1 Progress: {lastReportedPercent}% ({i + 1}/{totalFiles} files processed)");
						progressCallback?.Invoke(lastReportedPercent, i + 1, totalFiles);
					}
				}
			}

			Console.WriteLine($"Step 1 Complete: {DuplicatesDeletedCount} duplicates deleted, {_allEntries.Count} unique files remaining.");

			if (renameConflicts)
			{
				Console.WriteLine("Step 2: Checking case-insensitive filename collisions and renaming conflicts...");
				var seenFileNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
				var snapshot = _allEntries.ToList();

				foreach (var entry in snapshot)
				{
					if (!File.Exists(entry.FilePath)) continue;

					if (!seenFileNames.TryGetValue(entry.FileName, out string? existingPath))
					{
						seenFileNames[entry.FileName] = entry.FilePath;
					}
					else
					{
						var (baseName, _) = Utils.SplitFileNameIndex(entry.FileName);
						string extension = entry.Extension;
						string dir = Path.GetDirectoryName(entry.FilePath) ?? directoryPath;

						int currentMax = GetMaxIndex(baseName);
						int newIndex = currentMax + 1;
						_maxIndexByPrefix[baseName] = newIndex;

						string newFileName = $"{baseName}_{newIndex}{extension}";
						string newFilePath = Path.Combine(dir, newFileName);

						while (seenFileNames.ContainsKey(newFileName) || File.Exists(newFilePath))
						{
							newIndex++;
							_maxIndexByPrefix[baseName] = newIndex;
							newFileName = $"{baseName}_{newIndex}{extension}";
							newFilePath = Path.Combine(dir, newFileName);
						}

						try
						{
							File.Move(entry.FilePath, newFilePath);
							FilesRenamedCount++;
							seenFileNames[newFileName] = newFilePath;
							Console.WriteLine($"[Renamed Conflict] Renamed '{entry.FilePath}' -> '{newFilePath}' (Conflict with '{existingPath}')");

							entry.FilePath = newFilePath;
							entry.FileName = newFileName;
							entry.Index = newIndex;
							_entriesByFileName[newFileName] = entry;
						}
						catch (Exception ex)
						{
							Console.WriteLine($"[ERROR] Failed to rename '{entry.FilePath}' to '{newFilePath}': {ex.Message}");
						}
					}
				}
			}
		}

		public void PerformCleanup(string directoryPath, Action<int, int, int>? progressCallback = null)
		{
			EnumerateDirectory(directoryPath, deleteDuplicates: true, renameConflicts: true, progressCallback: progressCallback);
		}
	}

	public static class RealmMetadataHelper
	{
		private static readonly uint[] CrcTable = InitializeCrcTable();
		private static readonly uint[] OggCrcTable = InitializeOggCrcTable();

		private static uint[] InitializeCrcTable()
		{
			uint[] table = new uint[256];
			for (uint i = 0; i < 256; i++)
			{
				uint c = i;
				for (int k = 0; k < 8; k++)
				{
					if ((c & 1) != 0) c = 0xEDB88320 ^ (c >> 1);
					else c >>= 1;
				}
				table[i] = c;
			}
			return table;
		}

		private static uint[] InitializeOggCrcTable()
		{
			uint[] table = new uint[256];
			for (uint i = 0; i < 256; i++)
			{
				uint r = i << 24;
				for (int j = 0; j < 8; j++)
				{
					if ((r & 0x80000000) != 0) r = (r << 1) ^ 0x04C11DB7;
					else r <<= 1;
				}
				table[i] = r;
			}
			return table;
		}

		public static uint CalculatePngCrc(ReadOnlySpan<byte> typeBytes, ReadOnlySpan<byte> dataBytes)
		{
			uint crc = 0xFFFFFFFF;
			foreach (byte b in typeBytes) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
			foreach (byte b in dataBytes) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
			return crc ^ 0xFFFFFFFF;
		}

		public static uint CalculateOggCrc(ReadOnlySpan<byte> data)
		{
			uint crc = 0;
			foreach (byte b in data) crc = (crc << 8) ^ OggCrcTable[((crc >> 24) ^ b) & 0xFF];
			return crc;
		}

		public static string? ExtractMetadata(string filePath)
		{
			if (!File.Exists(filePath)) return null;
			string ext = Path.GetExtension(filePath).ToLowerInvariant();
			return ext switch
			{
				".glb" => ExtractMetadataFromGlb(filePath),
				".png" => ExtractMetadataFromPng(filePath),
				".ktx2" or ".ktx" => ExtractMetadataFromKtx2(filePath),
				".ranim" => ExtractMetadataFromRanim(filePath),
				".ogg" => ExtractMetadataFromOgg(filePath),
				_ => null
			};
		}

		public static bool AddMetadata(string filePath, string realmMetadataJson)
		{
			if (!File.Exists(filePath)) return false;
			string ext = Path.GetExtension(filePath).ToLowerInvariant();
			try
			{
				switch (ext)
				{
					case ".glb":
						AddMetadataToGlb(filePath, realmMetadataJson);
						return true;
					case ".png":
						AddMetadataToPng(filePath, realmMetadataJson);
						return true;
					case ".ktx2" or ".ktx":
						AddMetadataToKtx2(filePath, realmMetadataJson);
						return true;
					case ".ranim":
						AddMetadataToRanim(filePath, realmMetadataJson);
						return true;
					case ".ogg":
						AddMetadataToOgg(filePath, realmMetadataJson);
						return true;
					default:
						return false;
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[ERROR] Failed to add metadata to '{filePath}': {ex.Message}");
				return false;
			}
		}

		#region GLB Metadata
		public static string? ExtractMetadataFromGlb(string filePath)
		{
			if (!File.Exists(filePath)) return null;
			byte[] bytes = File.ReadAllBytes(filePath);
			return ExtractMetadataFromGlbBytes(bytes);
		}

		public static string? ExtractMetadataFromGlbBytes(ReadOnlySpan<byte> bytes)
		{
			if (bytes.Length < 20) return null;
			uint magic = BitConverter.ToUInt32(bytes.Slice(0, 4));
			if (magic != 0x46546C67) return null; // "glTF"

			uint chunk0Length = BitConverter.ToUInt32(bytes.Slice(12, 4));
			uint chunk0Type = BitConverter.ToUInt32(bytes.Slice(16, 4));
			if (chunk0Type != 0x4E4F534A) return null; // "JSON"
			if (bytes.Length < 20 + chunk0Length) return null;

			string jsonText = Encoding.UTF8.GetString(bytes.Slice(20, (int)chunk0Length));
			try
			{
				using var doc = JsonDocument.Parse(jsonText);
				var root = doc.RootElement;
				if (root.TryGetProperty("extras", out var extras) && extras.ValueKind == JsonValueKind.Object)
				{
					if (extras.TryGetProperty("Realm", out var realmProp) || extras.TryGetProperty("realm", out realmProp))
					{
						return realmProp.ValueKind == JsonValueKind.String ? realmProp.GetString() : realmProp.GetRawText();
					}
				}
				if (root.TryGetProperty("asset", out var asset) && asset.TryGetProperty("extras", out extras) && extras.ValueKind == JsonValueKind.Object)
				{
					if (extras.TryGetProperty("Realm", out var realmProp) || extras.TryGetProperty("realm", out realmProp))
					{
						return realmProp.ValueKind == JsonValueKind.String ? realmProp.GetString() : realmProp.GetRawText();
					}
				}
			}
			catch { }
			return null;
		}

		public static void AddMetadataToGlb(string filePath, string realmMetadataJson)
		{
			byte[] bytes = File.ReadAllBytes(filePath);
			byte[] updated = AddMetadataToGlbBytes(bytes, realmMetadataJson);
			File.WriteAllBytes(filePath, updated);
		}

		public static byte[] AddMetadataToGlbBytes(byte[] bytes, string realmMetadataJson)
		{
			if (bytes.Length < 20) throw new InvalidOperationException("Invalid GLB file.");
			uint magic = BitConverter.ToUInt32(bytes, 0);
			if (magic != 0x46546C67) throw new InvalidOperationException("Invalid GLB magic header.");

			uint version = BitConverter.ToUInt32(bytes, 4);
			uint chunk0Length = BitConverter.ToUInt32(bytes, 12);
			uint chunk0Type = BitConverter.ToUInt32(bytes, 16);
			if (chunk0Type != 0x4E4F534A) throw new InvalidOperationException("First GLB chunk is not JSON.");

			string jsonText = Encoding.UTF8.GetString(bytes, 20, (int)chunk0Length);
			var rootNode = JsonNode.Parse(jsonText) ?? new JsonObject();
			if (rootNode["extras"] == null || rootNode["extras"] is not JsonObject)
			{
				rootNode["extras"] = new JsonObject();
			}

			try
			{
				var realmNode = JsonNode.Parse(realmMetadataJson);
				rootNode["extras"]!["Realm"] = realmNode;
			}
			catch
			{
				rootNode["extras"]!["Realm"] = JsonValue.Create(realmMetadataJson);
			}

			byte[] newJsonBytes = Encoding.UTF8.GetBytes(rootNode.ToJsonString());
			int padLength = (4 - (newJsonBytes.Length % 4)) % 4;
			byte[] paddedJson = new byte[newJsonBytes.Length + padLength];
			Buffer.BlockCopy(newJsonBytes, 0, paddedJson, 0, newJsonBytes.Length);
			for (int i = 0; i < padLength; i++) paddedJson[newJsonBytes.Length + i] = 0x20; // ASCII space

			int chunk1Offset = 20 + (int)chunk0Length;
			int chunk1RemainingLength = bytes.Length - chunk1Offset;

			uint newTotalLength = 12 + 8 + (uint)paddedJson.Length + (uint)chunk1RemainingLength;
			using var ms = new MemoryStream();
			using var writer = new BinaryWriter(ms);
			writer.Write(magic);
			writer.Write(version);
			writer.Write(newTotalLength);
			writer.Write((uint)paddedJson.Length);
			writer.Write(chunk0Type);
			writer.Write(paddedJson);
			if (chunk1RemainingLength > 0)
			{
				writer.Write(bytes, chunk1Offset, chunk1RemainingLength);
			}
			return ms.ToArray();
		}
		#endregion

		#region PNG Metadata
		public static string? ExtractMetadataFromPng(string filePath)
		{
			if (!File.Exists(filePath)) return null;
			byte[] bytes = File.ReadAllBytes(filePath);
			return ExtractMetadataFromPngBytes(bytes);
		}

		public static string? ExtractMetadataFromPngBytes(ReadOnlySpan<byte> bytes)
		{
			if (bytes.Length < 8) return null;
			ReadOnlySpan<byte> pngSig = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
			if (!bytes.Slice(0, 8).SequenceEqual(pngSig)) return null;

			int offset = 8;
			while (offset + 8 <= bytes.Length)
			{
				int length = (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
				string chunkType = Encoding.ASCII.GetString(bytes.Slice(offset + 4, 4));
				int dataOffset = offset + 8;
				if (dataOffset + length + 4 > bytes.Length) break;

				if (chunkType == "tEXt" && length > 0)
				{
					var dataSpan = bytes.Slice(dataOffset, length);
					int nullIdx = dataSpan.IndexOf((byte)0);
					if (nullIdx > 0)
					{
						string keyword = Encoding.ASCII.GetString(dataSpan.Slice(0, nullIdx));
						if (keyword.Equals("Realm", StringComparison.OrdinalIgnoreCase))
						{
							return Encoding.UTF8.GetString(dataSpan.Slice(nullIdx + 1));
						}
					}
				}
				else if (chunkType == "iTXt" && length > 5)
				{
					var dataSpan = bytes.Slice(dataOffset, length);
					int nullIdx = dataSpan.IndexOf((byte)0);
					if (nullIdx > 0)
					{
						string keyword = Encoding.ASCII.GetString(dataSpan.Slice(0, nullIdx));
						if (keyword.Equals("Realm", StringComparison.OrdinalIgnoreCase))
						{
							int cur = nullIdx + 1;
							if (cur + 2 <= dataSpan.Length)
							{
								byte compFlag = dataSpan[cur];
								cur += 2;
								while (cur < dataSpan.Length && dataSpan[cur] != 0) cur++;
								cur++;
								while (cur < dataSpan.Length && dataSpan[cur] != 0) cur++;
								cur++;

								if (cur <= dataSpan.Length)
								{
									var textBytes = dataSpan.Slice(cur);
									if (compFlag == 0)
									{
										return Encoding.UTF8.GetString(textBytes);
									}
									else
									{
										try
										{
											using var compMs = new MemoryStream(textBytes.ToArray());
											using var zlib = new ZLibStream(compMs, CompressionMode.Decompress);
											using var outMs = new MemoryStream();
											zlib.CopyTo(outMs);
											return Encoding.UTF8.GetString(outMs.ToArray());
										}
										catch { }
									}
								}
							}
						}
					}
				}
				else if (chunkType == "IEND")
				{
					break;
				}

				offset += 8 + length + 4;
			}
			return null;
		}

		public static void AddMetadataToPng(string filePath, string realmMetadataJson)
		{
			byte[] bytes = File.ReadAllBytes(filePath);
			byte[] updated = AddMetadataToPngBytes(bytes, realmMetadataJson);
			File.WriteAllBytes(filePath, updated);
		}

		public static byte[] AddMetadataToPngBytes(byte[] bytes, string realmMetadataJson)
		{
			if (bytes.Length < 8) throw new InvalidOperationException("Invalid PNG file.");
			byte[] pngSig = new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
			for (int i = 0; i < 8; i++)
				if (bytes[i] != pngSig[i]) throw new InvalidOperationException("Invalid PNG signature.");

			byte[] keyBytes = Encoding.ASCII.GetBytes("Realm\0");
			byte[] jsonBytes = Encoding.UTF8.GetBytes(realmMetadataJson);
			byte[] textChunkData = new byte[keyBytes.Length + jsonBytes.Length];
			Buffer.BlockCopy(keyBytes, 0, textChunkData, 0, keyBytes.Length);
			Buffer.BlockCopy(jsonBytes, 0, textChunkData, keyBytes.Length, jsonBytes.Length);

			var chunks = new List<(string Type, byte[] Data)>();
			int offset = 8;
			bool inserted = false;

			while (offset + 8 <= bytes.Length)
			{
				int length = (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
				string chunkType = Encoding.ASCII.GetString(bytes, offset + 4, 4);
				int dataOffset = offset + 8;
				if (dataOffset + length + 4 > bytes.Length) break;

				byte[] chunkData = new byte[length];
				Buffer.BlockCopy(bytes, dataOffset, chunkData, 0, length);

				bool isOldRealm = false;
				if (chunkType == "tEXt" || chunkType == "iTXt")
				{
					int nullIdx = Array.IndexOf(chunkData, (byte)0);
					if (nullIdx > 0 && Encoding.ASCII.GetString(chunkData, 0, nullIdx).Equals("Realm", StringComparison.OrdinalIgnoreCase))
					{
						isOldRealm = true;
					}
				}

				if (!isOldRealm)
				{
					if (!inserted && (chunkType == "IDAT" || chunkType == "IEND"))
					{
						chunks.Add(("tEXt", textChunkData));
						inserted = true;
					}
					chunks.Add((chunkType, chunkData));
				}

				offset += 8 + length + 4;
			}

			if (!inserted)
			{
				chunks.Add(("tEXt", textChunkData));
			}

			using var ms = new MemoryStream();
			ms.Write(pngSig, 0, 8);
			foreach (var chunk in chunks)
			{
				byte[] chunkTypeBytes = Encoding.ASCII.GetBytes(chunk.Type);
				uint crc = CalculatePngCrc(chunkTypeBytes, chunk.Data);

				byte[] lenBytes = new byte[4] {
					(byte)((chunk.Data.Length >> 24) & 0xFF),
					(byte)((chunk.Data.Length >> 16) & 0xFF),
					(byte)((chunk.Data.Length >> 8) & 0xFF),
					(byte)(chunk.Data.Length & 0xFF)
				};
				byte[] crcBytes = new byte[4] {
					(byte)((crc >> 24) & 0xFF),
					(byte)((crc >> 16) & 0xFF),
					(byte)((crc >> 8) & 0xFF),
					(byte)(crc & 0xFF)
				};

				ms.Write(lenBytes, 0, 4);
				ms.Write(chunkTypeBytes, 0, 4);
				ms.Write(chunk.Data, 0, chunk.Data.Length);
				ms.Write(crcBytes, 0, 4);
			}
			return ms.ToArray();
		}
		#endregion

		#region KTX2 Metadata
		public static string? ExtractMetadataFromKtx2(string filePath)
		{
			if (!File.Exists(filePath)) return null;
			byte[] bytes = File.ReadAllBytes(filePath);
			return ExtractMetadataFromKtx2Bytes(bytes);
		}

		public static string? ExtractMetadataFromKtx2Bytes(ReadOnlySpan<byte> bytes)
		{
			if (bytes.Length < 80) return null;
			ReadOnlySpan<byte> ktx2Sig = new byte[] { 0xAB, 0x4B, 0x54, 0x58, 0x20, 0x32, 0x30, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A };
			if (!bytes.Slice(0, 12).SequenceEqual(ktx2Sig)) return null;

			uint kvdByteOffset = BitConverter.ToUInt32(bytes.Slice(60, 4));
			uint kvdByteLength = BitConverter.ToUInt32(bytes.Slice(64, 4));

			if (kvdByteLength == 0 || kvdByteOffset == 0 || kvdByteOffset + kvdByteLength > bytes.Length)
				return null;

			var kvdSpan = bytes.Slice((int)kvdByteOffset, (int)kvdByteLength);
			int cur = 0;
			while (cur + 4 <= kvdSpan.Length)
			{
				uint keyAndValueByteLength = BitConverter.ToUInt32(kvdSpan.Slice(cur, 4));
				cur += 4;
				if (cur + keyAndValueByteLength > kvdSpan.Length) break;

				var entry = kvdSpan.Slice(cur, (int)keyAndValueByteLength);
				int nullIdx = entry.IndexOf((byte)0);
				if (nullIdx > 0)
				{
					string key = Encoding.UTF8.GetString(entry.Slice(0, nullIdx));
					if (key.Equals("Realm", StringComparison.OrdinalIgnoreCase))
					{
						return Encoding.UTF8.GetString(entry.Slice(nullIdx + 1));
					}
				}

				cur += (int)keyAndValueByteLength;
				int padding = (4 - (cur % 4)) % 4;
				cur += padding;
			}
			return null;
		}

		public static void AddMetadataToKtx2(string filePath, string realmMetadataJson)
		{
			byte[] bytes = File.ReadAllBytes(filePath);
			byte[] updated = AddMetadataToKtx2Bytes(bytes, realmMetadataJson);
			File.WriteAllBytes(filePath, updated);
		}

		public static byte[] AddMetadataToKtx2Bytes(byte[] bytes, string realmMetadataJson)
		{
			if (bytes.Length < 80) throw new InvalidOperationException("Invalid KTX2 file.");
			byte[] ktx2Sig = new byte[] { 0xAB, 0x4B, 0x54, 0x58, 0x20, 0x32, 0x30, 0xBB, 0x0D, 0x0A, 0x1A, 0x0A };
			for (int i = 0; i < 12; i++)
				if (bytes[i] != ktx2Sig[i]) throw new InvalidOperationException("Invalid KTX2 signature.");

			uint kvdByteOffset = BitConverter.ToUInt32(bytes, 60);
			uint kvdByteLength = BitConverter.ToUInt32(bytes, 64);
			ulong sgdByteOffset = BitConverter.ToUInt64(bytes, 68);
			uint levelCount = BitConverter.ToUInt32(bytes, 32);
			if (levelCount == 0) levelCount = 1;

			var existingEntries = new List<byte[]>();
			if (kvdByteLength > 0 && kvdByteOffset > 0 && kvdByteOffset + kvdByteLength <= (uint)bytes.Length)
			{
				int cur = (int)kvdByteOffset;
				int end = (int)(kvdByteOffset + kvdByteLength);
				while (cur + 4 <= end)
				{
					uint keyAndValueByteLength = BitConverter.ToUInt32(bytes, cur);
					if (cur + 4 + keyAndValueByteLength > end) break;
					byte[] entry = new byte[keyAndValueByteLength];
					Buffer.BlockCopy(bytes, cur + 4, entry, 0, (int)keyAndValueByteLength);

					int nullIdx = Array.IndexOf(entry, (byte)0);
					bool isRealm = false;
					if (nullIdx > 0)
					{
						string key = Encoding.UTF8.GetString(entry, 0, nullIdx);
						if (key.Equals("Realm", StringComparison.OrdinalIgnoreCase))
							isRealm = true;
					}
					if (!isRealm)
					{
						existingEntries.Add(entry);
					}

					cur += 4 + (int)keyAndValueByteLength;
					int padding = (4 - (cur % 4)) % 4;
					cur += padding;
				}
			}

			byte[] keyBytes = Encoding.UTF8.GetBytes("Realm\0");
			byte[] valBytes = Encoding.UTF8.GetBytes(realmMetadataJson);
			byte[] realmEntry = new byte[keyBytes.Length + valBytes.Length];
			Buffer.BlockCopy(keyBytes, 0, realmEntry, 0, keyBytes.Length);
			Buffer.BlockCopy(valBytes, 0, realmEntry, keyBytes.Length, valBytes.Length);
			existingEntries.Add(realmEntry);

			using var kvdMs = new MemoryStream();
			foreach (var entry in existingEntries)
			{
				uint len = (uint)entry.Length;
				kvdMs.Write(BitConverter.GetBytes(len), 0, 4);
				kvdMs.Write(entry, 0, entry.Length);
				int pad = (4 - (int)(kvdMs.Position % 4)) % 4;
				for (int p = 0; p < pad; p++) kvdMs.WriteByte(0);
			}
			byte[] newKvdBytes = kvdMs.ToArray();

			if (kvdByteOffset == 0 || kvdByteLength == 0)
			{
				uint targetOffset = (uint)bytes.Length;
				byte[] resultNoPrev = new byte[bytes.Length + newKvdBytes.Length];
				Buffer.BlockCopy(bytes, 0, resultNoPrev, 0, bytes.Length);
				Buffer.BlockCopy(newKvdBytes, 0, resultNoPrev, (int)targetOffset, newKvdBytes.Length);

				Buffer.BlockCopy(BitConverter.GetBytes(targetOffset), 0, resultNoPrev, 60, 4);
				Buffer.BlockCopy(BitConverter.GetBytes((uint)newKvdBytes.Length), 0, resultNoPrev, 64, 4);
				return resultNoPrev;
			}

			int delta = newKvdBytes.Length - (int)kvdByteLength;
			byte[] result = new byte[bytes.Length + delta];

			Buffer.BlockCopy(bytes, 0, result, 0, (int)kvdByteOffset);
			Buffer.BlockCopy(newKvdBytes, 0, result, (int)kvdByteOffset, newKvdBytes.Length);
			int afterKvdOffset = (int)(kvdByteOffset + kvdByteLength);
			if (afterKvdOffset < bytes.Length && kvdByteOffset > 0)
			{
				Buffer.BlockCopy(bytes, afterKvdOffset, result, (int)kvdByteOffset + newKvdBytes.Length, bytes.Length - afterKvdOffset);
			}

			Buffer.BlockCopy(BitConverter.GetBytes(kvdByteOffset), 0, result, 60, 4);
			Buffer.BlockCopy(BitConverter.GetBytes((uint)newKvdBytes.Length), 0, result, 64, 4);

			if (delta != 0)
			{
				if (sgdByteOffset > kvdByteOffset)
				{
					ulong newSgd = sgdByteOffset + (ulong)delta;
					Buffer.BlockCopy(BitConverter.GetBytes(newSgd), 0, result, 68, 8);
				}

				for (int l = 0; l < (int)levelCount; l++)
				{
					int lvlEntryOffset = 80 + (l * 24);
					if (lvlEntryOffset + 24 <= result.Length)
					{
						ulong lvlByteOffset = BitConverter.ToUInt64(result, lvlEntryOffset);
						if (lvlByteOffset > kvdByteOffset)
						{
							lvlByteOffset += (ulong)delta;
							Buffer.BlockCopy(BitConverter.GetBytes(lvlByteOffset), 0, result, lvlEntryOffset, 8);
						}
					}
				}
			}

			return result;
		}
		#endregion

		#region RANIM Metadata
		public static string? ExtractMetadataFromRanim(string filePath)
		{
			if (!File.Exists(filePath)) return null;
			byte[] bytes = File.ReadAllBytes(filePath);
			return ExtractMetadataFromRanimBytes(bytes);
		}

		public static string? ExtractMetadataFromRanimBytes(ReadOnlySpan<byte> bytes)
		{
			if (bytes.Length == 0) return null;

			if (bytes[0] == (byte)'{' || bytes[0] == (byte)'[')
			{
				try
				{
					string jsonText = Encoding.UTF8.GetString(bytes);
					using var doc = JsonDocument.Parse(jsonText);
					if (doc.RootElement.TryGetProperty("Realm", out var realmProp) || doc.RootElement.TryGetProperty("realm", out realmProp))
					{
						return realmProp.ValueKind == JsonValueKind.String ? realmProp.GetString() : realmProp.GetRawText();
					}
					return jsonText;
				}
				catch { }
			}

			if (bytes.Length >= 8)
			{
				if (bytes[bytes.Length - 4] == (byte)'R' &&
					bytes[bytes.Length - 3] == (byte)'M' &&
					bytes[bytes.Length - 2] == (byte)'E' &&
					bytes[bytes.Length - 1] == (byte)'T')
				{
					uint metaLen = BitConverter.ToUInt32(bytes.Slice(bytes.Length - 8, 4));
					if (metaLen > 0 && bytes.Length >= 8 + metaLen)
					{
						int metaStart = bytes.Length - 8 - (int)metaLen;
						return Encoding.UTF8.GetString(bytes.Slice(metaStart, (int)metaLen));
					}
				}
			}

			return null;
		}

		public static void AddMetadataToRanim(string filePath, string realmMetadataJson)
		{
			byte[] bytes = File.ReadAllBytes(filePath);
			byte[] updated = AddMetadataToRanimBytes(bytes, realmMetadataJson);
			File.WriteAllBytes(filePath, updated);
		}

		public static byte[] AddMetadataToRanimBytes(byte[] bytes, string realmMetadataJson)
		{
			if (bytes.Length > 0 && (bytes[0] == (byte)'{' || bytes[0] == (byte)'['))
			{
				try
				{
					var node = JsonNode.Parse(Encoding.UTF8.GetString(bytes)) ?? new JsonObject();
					try { node["Realm"] = JsonNode.Parse(realmMetadataJson); }
					catch { node["Realm"] = JsonValue.Create(realmMetadataJson); }
					return Encoding.UTF8.GetBytes(node.ToJsonString());
				}
				catch { }
			}

			int baseLength = bytes.Length;
			if (bytes.Length >= 8 &&
				bytes[bytes.Length - 4] == (byte)'R' &&
				bytes[bytes.Length - 3] == (byte)'M' &&
				bytes[bytes.Length - 2] == (byte)'E' &&
				bytes[bytes.Length - 1] == (byte)'T')
			{
				uint oldLen = BitConverter.ToUInt32(bytes, bytes.Length - 8);
				if (baseLength >= 8 + (int)oldLen)
				{
					baseLength = baseLength - 8 - (int)oldLen;
				}
			}

			byte[] jsonBytes = Encoding.UTF8.GetBytes(realmMetadataJson);
			byte[] result = new byte[baseLength + jsonBytes.Length + 4 + 4];
			Buffer.BlockCopy(bytes, 0, result, 0, baseLength);
			Buffer.BlockCopy(jsonBytes, 0, result, baseLength, jsonBytes.Length);
			Buffer.BlockCopy(BitConverter.GetBytes((uint)jsonBytes.Length), 0, result, baseLength + jsonBytes.Length, 4);
			result[result.Length - 4] = (byte)'R';
			result[result.Length - 3] = (byte)'M';
			result[result.Length - 2] = (byte)'E';
			result[result.Length - 1] = (byte)'T';
			return result;
		}
		#endregion

		#region OGG Metadata
		public static string? ExtractMetadataFromOgg(string filePath)
		{
			if (!File.Exists(filePath)) return null;
			byte[] bytes = File.ReadAllBytes(filePath);
			return ExtractMetadataFromOggBytes(bytes);
		}

		public static string? ExtractMetadataFromOggBytes(ReadOnlySpan<byte> bytes)
		{
			if (bytes.Length < 4) return null;

			byte[] vorbisCommentTag = new byte[] { 0x03, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s' };
			byte[] opusTags = Encoding.ASCII.GetBytes("OpusTags");

			int tagIdx = bytes.IndexOf(vorbisCommentTag);
			int headerLen = 7;
			if (tagIdx < 0)
			{
				tagIdx = bytes.IndexOf(opusTags);
				headerLen = 8;
			}

			if (tagIdx >= 0)
			{
				int cur = tagIdx + headerLen;
				if (cur + 4 <= bytes.Length)
				{
					uint vendorLen = BitConverter.ToUInt32(bytes.Slice(cur, 4));
					cur += 4 + (int)vendorLen;
					if (cur + 4 <= bytes.Length)
					{
						uint commentCount = BitConverter.ToUInt32(bytes.Slice(cur, 4));
						cur += 4;
						for (uint i = 0; i < commentCount && cur + 4 <= bytes.Length; i++)
						{
							uint cLen = BitConverter.ToUInt32(bytes.Slice(cur, 4));
							cur += 4;
							if (cur + cLen > bytes.Length) break;
							string comment = Encoding.UTF8.GetString(bytes.Slice(cur, (int)cLen));
							cur += (int)cLen;

							if (comment.StartsWith("REALM=", StringComparison.OrdinalIgnoreCase))
							{
								return comment.Substring(6);
							}
						}
					}
				}
			}

			byte[] realmTag = Encoding.UTF8.GetBytes("REALM=");
			int maxSearch = Math.Min(bytes.Length, 65536);
			int rIdx = bytes.Slice(0, maxSearch).IndexOf(realmTag);
			if (rIdx >= 0)
			{
				int start = rIdx + 6;
				int end = start;
				while (end < maxSearch && bytes[end] != 0 && bytes[end] != '\r' && bytes[end] != '\n') end++;
				string val = Encoding.UTF8.GetString(bytes.Slice(start, end - start));
				if (val.TrimStart().StartsWith("{") || val.TrimStart().StartsWith("["))
					return val;
			}

			return null;
		}

		public static void AddMetadataToOgg(string filePath, string realmMetadataJson)
		{
			byte[] bytes = File.ReadAllBytes(filePath);
			byte[] updated = AddMetadataToOggBytes(bytes, realmMetadataJson);
			File.WriteAllBytes(filePath, updated);
		}

		public static byte[] AddMetadataToOggBytes(byte[] bytes, string realmMetadataJson)
		{
			byte[] vorbisCommentTag = new byte[] { 0x03, (byte)'v', (byte)'o', (byte)'r', (byte)'b', (byte)'i', (byte)'s' };
			byte[] opusTags = Encoding.ASCII.GetBytes("OpusTags");

			int tagIdx = bytes.AsSpan().IndexOf(vorbisCommentTag);
			string headerType = "vorbis";
			int headerLen = 7;

			if (tagIdx < 0)
			{
				tagIdx = bytes.AsSpan().IndexOf(opusTags);
				headerType = "opus";
				headerLen = 8;
			}

			if (tagIdx < 0)
			{
				return bytes;
			}

			int pageStart = tagIdx;
			while (pageStart >= 0)
			{
				if (pageStart + 4 <= bytes.Length &&
					bytes[pageStart] == 0x4F && bytes[pageStart + 1] == 0x67 &&
					bytes[pageStart + 2] == 0x67 && bytes[pageStart + 3] == 0x53)
				{
					break;
				}
				pageStart--;
			}
			if (pageStart < 0) return bytes;

			int numSegments = bytes[pageStart + 26];
			int pageHeaderLen = 27 + numSegments;
			int payloadLen = 0;
			for (int s = 0; s < numSegments; s++) payloadLen += bytes[pageStart + 27 + s];
			int pageEnd = pageStart + pageHeaderLen + payloadLen;

			int cur = tagIdx + headerLen;
			uint vendorLen = BitConverter.ToUInt32(bytes, cur);
			string vendorString = Encoding.UTF8.GetString(bytes, cur + 4, (int)vendorLen);
			cur += 4 + (int)vendorLen;

			uint commentCount = BitConverter.ToUInt32(bytes, cur);
			cur += 4;

			var comments = new List<string>();
			for (uint i = 0; i < commentCount && cur + 4 <= bytes.Length; i++)
			{
				uint cLen = BitConverter.ToUInt32(bytes, cur);
				cur += 4;
				if (cur + cLen > bytes.Length) break;
				string comment = Encoding.UTF8.GetString(bytes, cur, (int)cLen);
				cur += (int)cLen;

				if (!comment.StartsWith("REALM=", StringComparison.OrdinalIgnoreCase))
				{
					comments.Add(comment);
				}
			}

			comments.Add("REALM=" + realmMetadataJson);

			using var packetMs = new MemoryStream();
			if (headerType == "vorbis")
				packetMs.Write(vorbisCommentTag, 0, 7);
			else
				packetMs.Write(opusTags, 0, 8);

			byte[] vendorBytes = Encoding.UTF8.GetBytes(vendorString);
			packetMs.Write(BitConverter.GetBytes((uint)vendorBytes.Length), 0, 4);
			packetMs.Write(vendorBytes, 0, vendorBytes.Length);

			packetMs.Write(BitConverter.GetBytes((uint)comments.Count), 0, 4);
			foreach (var c in comments)
			{
				byte[] cBytes = Encoding.UTF8.GetBytes(c);
				packetMs.Write(BitConverter.GetBytes((uint)cBytes.Length), 0, 4);
				packetMs.Write(cBytes, 0, cBytes.Length);
			}
			packetMs.WriteByte(1);

			byte[] newPacketData = packetMs.ToArray();

			var segments = new List<byte>();
			int rem = newPacketData.Length;
			while (rem >= 255)
			{
				segments.Add(255);
				rem -= 255;
			}
			segments.Add((byte)rem);

			if (segments.Count > 255)
			{
				return bytes;
			}

			using var newPageMs = new MemoryStream();
			newPageMs.Write(bytes, pageStart, 26);
			newPageMs.WriteByte((byte)segments.Count);
			foreach (var seg in segments) newPageMs.WriteByte(seg);
			newPageMs.Write(newPacketData, 0, newPacketData.Length);

			byte[] newPageBytes = newPageMs.ToArray();

			newPageBytes[22] = 0;
			newPageBytes[23] = 0;
			newPageBytes[24] = 0;
			newPageBytes[25] = 0;

			uint pageCrc = CalculateOggCrc(newPageBytes);
			Buffer.BlockCopy(BitConverter.GetBytes(pageCrc), 0, newPageBytes, 22, 4);

			using var finalMs = new MemoryStream();
			finalMs.Write(bytes, 0, pageStart);
			finalMs.Write(newPageBytes, 0, newPageBytes.Length);
			if (pageEnd < bytes.Length)
			{
				finalMs.Write(bytes, pageEnd, bytes.Length - pageEnd);
			}
			return finalMs.ToArray();
		}
		#endregion
	}
}
