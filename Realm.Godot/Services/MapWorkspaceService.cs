using Godot;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Realm.Godot.Services.ModelOptimization;
using Realm.Godot.Utils;
using Realm.Godot.Animation;
using Realm.Shared.Metadata;
using Realm.Ecs.Services;

public static partial class MapWorkspaceService
{
	private static string _cachedRepoRoot;
	private static bool _repoRootResolved;

	private static string GetRepoRoot()
	{
		if (_repoRootResolved) return _cachedRepoRoot;
		_repoRootResolved = true;
		string baseDir = PathUtils.GetProjectRoot();
		var current = new DirectoryInfo(baseDir);
		while (current != null)
		{
			if (File.Exists(Path.Combine(current.FullName, "Realm.sln")) || Directory.Exists(Path.Combine(current.FullName, "Realm.MapAPI")))
			{
				_cachedRepoRoot = current.FullName.Replace("\\", "/");
				return _cachedRepoRoot;
			}
			current = current.Parent;
		}
		GD.PushWarning("[MapWorkspaceService] Could not locate the Realm repository (Realm.sln or Realm.MapAPI not found above res://). Template files and the MapAPI DLL will not be available.");
		return null;
	}

	private static string FindRootFile(string relativePath)
	{
		string found = PathUtils.FindPath(relativePath);
		if (File.Exists(found) || Directory.Exists(found))
		{
			return found;
		}

		string repoRoot = GetRepoRoot();
		if (repoRoot == null) return null;
		return Path.Combine(repoRoot, relativePath).Replace("\\", "/");
	}

	private static string GetSchemaSourcePath()
	{
		return FindRootFile("Realm.MapEditorExtension/map_schema.json");
	}

	private static string GetTemplatePath(string fileName)
	{
		return FindRootFile("MapTemplate/" + fileName);
	}

	private const string MapApiDllRelativePath = "lib/Realm.MapAPI.dll";

	private static string FindBuiltApiDll()
	{
		string repoRoot = GetRepoRoot();
		if (repoRoot == null) return null;
		string binDir = Path.Combine(repoRoot, "Realm.MapAPI", "bin");
		if (!Directory.Exists(binDir)) return null;
		return Directory.GetFiles(binDir, "Realm.MapAPI.dll", SearchOption.AllDirectories)
			.OrderByDescending(f => File.GetLastWriteTimeUtc(f))
			.FirstOrDefault();
	}

	public static void SetupWorkspace(string directory, string mapName)
	{
		if (string.IsNullOrEmpty(directory)) return;
		Directory.CreateDirectory(directory);

		GenerateVSCodeConfig(directory);
		EnsureWitFile(directory);
		EnsureCsproj(directory, mapName);
		EnsureMapScript(directory, mapName);
		EnsureWasmEntryPoint(directory);
		EnsureMetadataJson(directory);
		EnsureSolutionFile(directory, mapName);
		NormalizeMetadataTextureEntries(directory);
		EnsureGlbAssetsOptimized(directory);
		EnsurePngAssetsConverted(directory);
	}

	public static void CleanWorkspaceBinaries(string directory)
	{
		if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return;

		foreach (var folder in new[] { "bin", "obj" })
		{
			string targetDir = Path.Combine(directory, folder);
			if (Directory.Exists(targetDir))
			{
				try
				{
					foreach (var file in Directory.GetFiles(targetDir, "*", SearchOption.AllDirectories))
					{
						var attrs = File.GetAttributes(file);
						if ((attrs & FileAttributes.ReadOnly) != 0)
						{
							File.SetAttributes(file, attrs & ~FileAttributes.ReadOnly);
						}
					}
					Directory.Delete(targetDir, true);
				}
				catch (Exception ex)
				{
					GD.PrintErr($"[MapWorkspaceService] Failed to clean build folder {folder}: {ex.Message}");
				}
			}
		}
	}

	public static void GenerateVSCodeConfig(string directory)
	{
		string vscodeDir = Path.Combine(directory, ".vscode");
		Directory.CreateDirectory(vscodeDir);

		string sourceSchema = GetSchemaSourcePath();
		string targetSchema = Path.Combine(vscodeDir, "map_schema.json");
		if (File.Exists(sourceSchema))
		{
			File.Copy(sourceSchema, targetSchema, true);
		}

		string templateVsCodeDir = GetTemplatePath(".vscode");
		if (Directory.Exists(templateVsCodeDir))
		{
			foreach (var file in Directory.GetFiles(templateVsCodeDir))
			{
				string dest = Path.Combine(vscodeDir, Path.GetFileName(file));
				if (!File.Exists(dest))
				{
					File.Copy(file, dest, true);
				}
			}
		}

		string agentsTemplate = GetTemplatePath("AGENTS.md");
		string agentsTarget = Path.Combine(directory, "AGENTS.md");
		if (File.Exists(agentsTemplate) && !File.Exists(agentsTarget))
		{
			File.Copy(agentsTemplate, agentsTarget, true);
		}
	}

	private static string GetWitPath()
	{
		return FindRootFile("Realm.MapAPI/wit/game.g.wit");
	}

	public static void EnsureWitFile(string directory)
	{
		string witDir = Path.Combine(directory, "wit");
		Directory.CreateDirectory(witDir);
		string witPath = Path.Combine(witDir, "game.g.wit");
		string sourceWit = GetWitPath();
		if (File.Exists(sourceWit))
		{
			File.Copy(sourceWit, witPath, true);
		}
	}

	public static void EnsureCsproj(string directory, string mapName)
	{
		string csprojPath = Path.Combine(directory, $"{mapName}.csproj");
		var existingCsprojs = Directory.GetFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly);
		if (existingCsprojs.Length > 0)
		{
			csprojPath = existingCsprojs[0];
		}

		string templatePath = GetTemplatePath("MapScript.csproj");

		if (!File.Exists(csprojPath))
		{
			if (!File.Exists(templatePath))
			{
				GD.PushWarning($"[MapWorkspaceService] Could not generate {Path.GetFileName(csprojPath)}: map script template not found at {templatePath ?? "n/a"}");
				return;
			}
			string csprojContent = File.ReadAllText(templatePath);
			csprojContent = NormalizeMapApiReference(csprojContent);
			File.WriteAllText(csprojPath, csprojContent);
		}
		else
		{
			string csprojContent = File.ReadAllText(csprojPath);
			string normalized = NormalizeMapApiReference(csprojContent);
			if (normalized != csprojContent)
			{
				File.WriteAllText(csprojPath, normalized);
				GD.Print($"[MapWorkspaceService] Repaired MapAPI reference in {Path.GetFileName(csprojPath)} to use the portable relative DLL path.");
			}
		}

		EnsureTrimmerRoot(csprojPath);

		EnsureApiLib(directory);

		string targetsTemplate = GetTemplatePath("Directory.Build.targets");
		string targetsPath = Path.Combine(directory, "Directory.Build.targets");
		if (File.Exists(targetsTemplate))
		{
			try
			{
				File.Copy(targetsTemplate, targetsPath, true);
			}
			catch
			{
			}
		}
	}

	private static void EnsureTrimmerRoot(string csprojPath)
	{
		if (string.IsNullOrEmpty(csprojPath) || !File.Exists(csprojPath)) return;

		string content = File.ReadAllText(csprojPath);
		if (content.Contains("<TrimmerRootAssembly", StringComparison.OrdinalIgnoreCase)) return;

		string root = "  <ItemGroup>\n    <TrimmerRootAssembly Include=\"$(AssemblyName)\" />\n  </ItemGroup>\n";
		int projectEnd = content.LastIndexOf("</Project>", StringComparison.OrdinalIgnoreCase);
		content = projectEnd >= 0 ? content.Insert(projectEnd, root) : content + root;

		File.WriteAllText(csprojPath, content);
		GD.Print($"[MapWorkspaceService] Added TrimmerRootAssembly to {Path.GetFileName(csprojPath)}");
	}

	[GeneratedRegex(@"<ProjectReference\s+Include=""[^""]*Realm\.MapAPI\.csproj""[^>]*/>", RegexOptions.Singleline)]
	private static partial Regex ProjectReferenceMapApiRegex();

	[GeneratedRegex(@"<Reference\s+Include=""Realm\.MapAPI""\s*>.*?</Reference>", RegexOptions.Singleline)]
	private static partial Regex ReferenceMapApiRegex();

	[GeneratedRegex(@"<HintPath>[^<]*Realm\.MapAPI\.dll</HintPath>", RegexOptions.Singleline)]
	private static partial Regex HintPathMapApiRegex();

	private static string NormalizeMapApiReference(string csprojContent)
	{
		csprojContent = ProjectReferenceMapApiRegex().Replace(csprojContent,
			$"<Reference Include=\"Realm.MapAPI\">\n      <HintPath>{MapApiDllRelativePath}</HintPath>\n    </Reference>");

		csprojContent = ReferenceMapApiRegex().Replace(csprojContent,
			$"<Reference Include=\"Realm.MapAPI\">\n      <HintPath>{MapApiDllRelativePath}</HintPath>\n    </Reference>");

		csprojContent = HintPathMapApiRegex().Replace(csprojContent,
			$"<HintPath>{MapApiDllRelativePath}</HintPath>");

		if (!csprojContent.Contains("TrimmerRootAssembly"))
		{
			csprojContent = csprojContent.Replace("</Project>",
				"  <ItemGroup>\n    <TrimmerRootAssembly Include=\"$(MSBuildProjectName)\" />\n  </ItemGroup>\n</Project>");
		}

		return csprojContent;
	}

	public static void EnsureApiLib(string directory)
	{
		string libDir = Path.Combine(directory, "lib");
		Directory.CreateDirectory(libDir);

		var candidates = new System.Collections.Generic.List<string>();
		string builtDll = FindBuiltApiDll();
		if (builtDll != null) candidates.Add(builtDll);
		string templateDll = GetTemplatePath("lib/Realm.MapAPI.dll");
		if (templateDll != null && File.Exists(templateDll)) candidates.Add(templateDll);

		if (candidates.Count == 0)
		{
			GD.PushWarning("[MapWorkspaceService] Realm.MapAPI.dll not found (neither the Realm.MapAPI build output nor MapTemplate/lib is available). Map scripts will not compile until a valid DLL is provided.");
			return;
		}

		string sourceDll = candidates.OrderByDescending(f => File.GetLastWriteTimeUtc(f)).First();
		string dllName = Path.GetFileName(sourceDll);
		foreach (var fileName in new[] { dllName, Path.ChangeExtension(dllName, ".pdb"), Path.ChangeExtension(dllName, ".xml") })
		{
			string source = Path.Combine(Path.GetDirectoryName(sourceDll), fileName);
			if (File.Exists(source))
			{
				try
				{
					File.Copy(source, Path.Combine(libDir, fileName), true);
				}
				catch (IOException)
				{
				}
			}
		}
	}

	public static void EnsureMapScript(string directory, string mapName)
	{
		string scriptPath = Path.Combine(directory, "MapScript.cs");
		if (!File.Exists(scriptPath) || new FileInfo(scriptPath).Length == 0)
		{
			string template = GetTemplatePath("MapScript.cs");
			if (File.Exists(template))
			{
				File.WriteAllText(scriptPath, File.ReadAllText(template).Replace("class MapScript", $"class {mapName}"));
			}
		}
	}

	public static void EnsureWasmEntryPoint(string directory)
	{
		string entryPointPath = Path.Combine(directory, "WasmEntryPoint.cs");
		string template = GetTemplatePath("WasmEntryPoint.cs");
		if (File.Exists(template))
		{
			if (!File.Exists(entryPointPath) || new FileInfo(entryPointPath).Length == 0)
			{
				File.Copy(template, entryPointPath);
			}
			else
			{
				string existing = File.ReadAllText(entryPointPath);
				if (existing.Contains("private static IWasmModule? _mapScript;"))
				{
					File.Copy(template, entryPointPath, true);
				}
			}
		}
	}

	public static void EnsureMetadataJson(string directory)
	{
		string templateMeta = GetTemplatePath("metadata.json");
		string metadataPath = Path.Combine(directory, "metadata.json");
		if (!File.Exists(metadataPath) || new FileInfo(metadataPath).Length == 0)
		{
			if (File.Exists(templateMeta))
			{
				File.Copy(templateMeta, metadataPath, true);
			}
			else
			{
				MapJsonFormatter.SaveFormattedJson(metadataPath, "{}");
			}
		}

		if (File.Exists(templateMeta))
		{
			string templateAssetsDir = Path.Combine(Path.GetDirectoryName(templateMeta), "Assets");
			if (Directory.Exists(templateAssetsDir))
			{
				Realm.Godot.Animation.RealmDefaultAnimations.EnsureDefaultTemplateAnimations(templateAssetsDir);
				string destAssetsDir = Path.Combine(directory, "Assets");
				foreach (var assetFile in Directory.GetFiles(templateAssetsDir, "*", SearchOption.AllDirectories))
				{
					string relPath = Path.GetRelativePath(templateAssetsDir, assetFile);
					string destFile = Path.Combine(destAssetsDir, relPath);
					Directory.CreateDirectory(Path.GetDirectoryName(destFile));
					if (!File.Exists(destFile))
					{
						File.Copy(assetFile, destFile, true);
					}
				}
			}
		}

		Realm.Godot.Animation.RealmDefaultAnimations.EnsureDefaultTemplateAnimations(Path.Combine(directory, "Assets"));
	}

	public static void EnsureSolutionFile(string directory, string mapName)
	{
		string slnPath = Path.Combine(directory, "temp_map_workspace.slnx");
		if (!File.Exists(slnPath))
		{
			try
			{
				Realm.Shared.NativeToolRunner.RunTool("dotnet", "new sln -n temp_map_workspace", timeoutMs: 15000, workingDir: directory);
				Realm.Shared.NativeToolRunner.RunTool("dotnet", $"sln add {mapName}.csproj", timeoutMs: 15000, workingDir: directory);
			}
			catch
			{
			}
		}
	}

	private static readonly ConcurrentDictionary<string, (DateTime LastWriteTime, bool HasFlag)> _optimizedFlagCache = new(StringComparer.OrdinalIgnoreCase);

	public static void EnsureGlbAssetsOptimized(string workspacePath)
	{
		NormalizeMetadataTextureEntries(workspacePath);
		if (string.IsNullOrEmpty(workspacePath) || !Directory.Exists(workspacePath)) return;

		string assetsDir = Path.Combine(workspacePath, "Assets");
		if (!Directory.Exists(assetsDir)) return;

		try
		{
			string[] glbFiles = Directory.GetFiles(assetsDir, "*.glb", SearchOption.AllDirectories);
			if (glbFiles.Length == 0) return;

			bool anyReimported = false;
			var optimizer = new Realm.Shared.GlbOptimizer();
			var options = new Realm.Shared.OptimizationOptions
			{
				SimplificationRatio = 0.5f,
				AllowedPixelError = 1.5f,
				MaxTextureResolution = 1024,
				ForceReDecimate = false,
				CompressTextures = true,
				GenerateLods = true
			};

			foreach (string glbPath in glbFiles)
			{
				string normalized = glbPath.Replace("\\", "/");
				if (normalized.Contains("/bin/") || normalized.Contains("/obj/") || normalized.Contains("/.git/") || normalized.Contains("/.godot/") || normalized.Contains("/vscode_embedded/") || normalized.Contains("/wasi_sdk_embedded/"))
				{
					continue;
				}

				DateTime lastWrite;
				try
				{
					lastWrite = File.GetLastWriteTimeUtc(glbPath);
				}
				catch
				{
					continue;
				}

				if (_optimizedFlagCache.TryGetValue(glbPath, out var cached) && cached.LastWriteTime == lastWrite && cached.HasFlag)
				{
					continue;
				}

				byte[] glbBytes = null;
				try
				{
					glbBytes = File.ReadAllBytes(glbPath);
				}
				catch (Exception ex)
				{
					GD.PrintErr($"[MapWorkspaceService] Failed to read GLB {glbPath}: {ex.Message}");
					continue;
				}

				if (glbBytes == null || glbBytes.Length == 0) continue;

				if (optimizer.IsOptimized(glbBytes))
				{
					_optimizedFlagCache[glbPath] = (lastWrite, true);
					continue;
				}

				string fileName = Path.GetFileName(glbPath);
				GD.Print($"[MapWorkspaceService] GLB asset '{fileName}' is missing realm_optimize_completed extras. Optimizing into workspace...");

				try
				{
					string animsDir = Path.Combine(workspacePath, "Assets", "animations");
					Directory.CreateDirectory(animsDir);
					var extractedAnims = MixamoAnimationImporter.ExtractAnimationsFromGlb(glbPath);
					foreach (var (animName, animData) in extractedAnims)
					{
						string animFileName = $"{animName.ToLowerInvariant()}.ranim";
						string animFilePath = Path.Combine(animsDir, animFileName);
						if (!File.Exists(animFilePath))
						{
							RealmAnimationSerializer.SaveToFile(animFilePath, animData);
							UpdateMetadataAnimationHash(workspacePath, animFileName, RealmMetadataHelper.ComputeBlake3(File.ReadAllBytes(animFilePath), ".ranim"));
						}
					}

					if (extractedAnims.Count > 0)
					{
						MixamoAnimationImporter.StripAnimationsFromGlb(glbPath, glbPath);
						glbBytes = File.ReadAllBytes(glbPath);
					}
				}
				catch (Exception ex)
				{
					GD.PrintErr($"[MapWorkspaceService] Animation extraction error for {fileName}: {ex.Message}");
				}

				var optResult = optimizer.Optimize(glbBytes, options);
				if (optResult.Success && optResult.OutputGlbBytes != null && optResult.OutputGlbBytes.Length > 0)
				{
					try
					{
						var attrs = File.GetAttributes(glbPath);
						if ((attrs & FileAttributes.ReadOnly) != 0)
						{
							File.SetAttributes(glbPath, attrs & ~FileAttributes.ReadOnly);
						}
						File.WriteAllBytes(glbPath, optResult.OutputGlbBytes);
						anyReimported = true;

						DateTime newLastWrite = File.GetLastWriteTimeUtc(glbPath);
						_optimizedFlagCache[glbPath] = (newLastWrite, true);

						string newHash = RealmMetadataHelper.ComputeBlake3(optResult.OutputGlbBytes, ".glb");
						UpdateMetadataGlbHash(workspacePath, fileName, newHash);

						GD.Print($"[MapWorkspaceService] Successfully optimized {fileName} ({optResult.OriginalSize} -> {optResult.OptimizedSize} bytes).");
					}
					catch (Exception ex)
					{
						GD.PrintErr($"[MapWorkspaceService] Failed to write optimized GLB {glbPath}: {ex.Message}");
					}
				}
				else
				{
					_optimizedFlagCache[glbPath] = (lastWrite, true);
					GD.PrintErr($"[MapWorkspaceService] Optimization failed for {fileName}: {optResult.ErrorMessage}");
				}
			}

			if (anyReimported)
			{
				ModelCache.Clear();
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapWorkspaceService] EnsureGlbAssetsOptimized error: {ex.Message}");
		}
	}

	public static async Task EnsureGlbAssetsOptimizedCooperativeAsync(
		string workspacePath,
		Func<int, int, string, Task>? onModelProgress = null)
	{
		NormalizeMetadataTextureEntries(workspacePath);
		if (string.IsNullOrEmpty(workspacePath) || !Directory.Exists(workspacePath)) return;

		string assetsDir = Path.Combine(workspacePath, "Assets");
		if (!Directory.Exists(assetsDir)) return;

		try
		{
			string[] glbFiles = Directory.GetFiles(assetsDir, "*.glb", SearchOption.AllDirectories);
			if (glbFiles.Length == 0) return;

			var optimizer = new Realm.Shared.GlbOptimizer();
			var options = new Realm.Shared.OptimizationOptions
			{
				SimplificationRatio = 0.5f,
				AllowedPixelError = 1.5f,
				MaxTextureResolution = 1024,
				ForceReDecimate = false,
				CompressTextures = true,
				GenerateLods = true
			};

			var unoptimizedList = new List<string>();
			foreach (string glbPath in glbFiles)
			{
				string normalized = glbPath.Replace("\\", "/");
				if (normalized.Contains("/bin/") || normalized.Contains("/obj/") || normalized.Contains("/.git/") || normalized.Contains("/.godot/") || normalized.Contains("/vscode_embedded/") || normalized.Contains("/wasi_sdk_embedded/"))
				{
					continue;
				}

				DateTime lastWrite;
				try
				{
					lastWrite = File.GetLastWriteTimeUtc(glbPath);
				}
				catch
				{
					continue;
				}

				if (_optimizedFlagCache.TryGetValue(glbPath, out var cached) && cached.LastWriteTime == lastWrite && cached.HasFlag)
				{
					continue;
				}

				byte[] bytes = null;
				try
				{
					bytes = File.ReadAllBytes(glbPath);
				}
				catch
				{
					continue;
				}

				if (bytes == null || bytes.Length == 0) continue;

				if (optimizer.IsOptimized(bytes))
				{
					_optimizedFlagCache[glbPath] = (lastWrite, true);
					continue;
				}

				unoptimizedList.Add(glbPath);
			}

			if (unoptimizedList.Count == 0) return;

			bool anyReimported = false;
			for (int i = 0; i < unoptimizedList.Count; i++)
			{
				string glbPath = unoptimizedList[i];
				string fileName = Path.GetFileName(glbPath);

				if (onModelProgress != null)
				{
					try
					{
						await onModelProgress(i + 1, unoptimizedList.Count, fileName);
					}
					catch (Exception ex)
					{
						GD.PrintErr($"[MapWorkspaceService] Progress callback error: {ex.Message}");
					}
				}

				DateTime lastWrite;
				try
				{
					lastWrite = File.GetLastWriteTimeUtc(glbPath);
				}
				catch
				{
					continue;
				}

				byte[] glbBytes = null;
				try
				{
					glbBytes = File.ReadAllBytes(glbPath);
				}
				catch (Exception ex)
				{
					GD.PrintErr($"[MapWorkspaceService] Failed to read GLB {glbPath}: {ex.Message}");
					continue;
				}

				if (glbBytes == null || glbBytes.Length == 0) continue;

				GD.Print($"[MapWorkspaceService] GLB asset '{fileName}' ({i + 1}/{unoptimizedList.Count}) is missing realm_optimize_completed extras. Optimizing into workspace...");

				try
				{
					string animsDir = Path.Combine(workspacePath, "Assets", "animations");
					Directory.CreateDirectory(animsDir);
					var extractedAnims = MixamoAnimationImporter.ExtractAnimationsFromGlb(glbPath);
					foreach (var (animName, animData) in extractedAnims)
					{
						string animFileName = $"{animName.ToLowerInvariant()}.ranim";
						string animFilePath = Path.Combine(animsDir, animFileName);
						if (!File.Exists(animFilePath))
						{
							RealmAnimationSerializer.SaveToFile(animFilePath, animData);
							UpdateMetadataAnimationHash(workspacePath, animFileName, RealmMetadataHelper.ComputeBlake3(File.ReadAllBytes(animFilePath), ".ranim"));
						}
					}

					if (extractedAnims.Count > 0)
					{
						MixamoAnimationImporter.StripAnimationsFromGlb(glbPath, glbPath);
						glbBytes = File.ReadAllBytes(glbPath);
					}
				}
				catch (Exception ex)
				{
					GD.PrintErr($"[MapWorkspaceService] Animation extraction error for {fileName}: {ex.Message}");
				}

				Realm.Shared.OptimizationResult optResult = default;
				try
				{
					optResult = optimizer.Optimize(glbBytes, options);
				}
				catch (Exception ex)
				{
					GD.PrintErr($"[MapWorkspaceService] Exception optimizing {fileName}: {ex.Message}");
					_optimizedFlagCache[glbPath] = (lastWrite, true);
					continue;
				}

				if (optResult.Success && optResult.OutputGlbBytes != null && optResult.OutputGlbBytes.Length > 0)
				{
					try
					{
						var attrs = File.GetAttributes(glbPath);
						if ((attrs & FileAttributes.ReadOnly) != 0)
						{
							File.SetAttributes(glbPath, attrs & ~FileAttributes.ReadOnly);
						}
						File.WriteAllBytes(glbPath, optResult.OutputGlbBytes);
						anyReimported = true;

						DateTime newLastWrite = File.GetLastWriteTimeUtc(glbPath);
						_optimizedFlagCache[glbPath] = (newLastWrite, true);

						string newHash = RealmMetadataHelper.ComputeBlake3(optResult.OutputGlbBytes, ".glb");
						UpdateMetadataGlbHash(workspacePath, fileName, newHash);

						GD.Print($"[MapWorkspaceService] Successfully optimized {fileName} ({optResult.OriginalSize} -> {optResult.OptimizedSize} bytes).");
					}
					catch (Exception ex)
					{
						GD.PrintErr($"[MapWorkspaceService] Failed to write optimized GLB {glbPath}: {ex.Message}");
					}
				}
				else
				{
					_optimizedFlagCache[glbPath] = (lastWrite, true);
					GD.PrintErr($"[MapWorkspaceService] Optimization failed for {fileName}: {optResult.ErrorMessage}");
				}
			}

			if (anyReimported)
			{
				ModelCache.Clear();
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapWorkspaceService] EnsureGlbAssetsOptimizedCooperativeAsync error: {ex.Message}");
		}
	}

	private static (string AssetType, int Columns, int Rows) DetectPngAssetInfo(string workspacePath, string pngPath, JsonObject? metadataRoot)
	{
		string fileName = Path.GetFileName(pngPath);
		string cleanName = Path.GetFileNameWithoutExtension(pngPath);
		string normalized = pngPath.Replace("\\", "/").ToLowerInvariant();

		string? metaJson = RealmMetadataHelper.ExtractMetadata(pngPath);
		if (!string.IsNullOrEmpty(metaJson))
		{
			try
			{
				var node = JsonNode.Parse(metaJson);
				string? type = node?["type"]?.GetValue<string>();
				int cols = node?["columns"]?.GetValue<int>() ?? 4;
				int rows = node?["rows"]?.GetValue<int>() ?? 4;
				if (!string.IsNullOrEmpty(type))
				{
					return (type, cols, rows);
				}
			}
			catch { }
		}

		if (metadataRoot != null)
		{
			JsonObject? assetsObj = null;
			if (metadataRoot["Assets"] is JsonObject a1) assetsObj = a1;
			else if (metadataRoot["MapProperties"] is JsonObject mp && mp["Assets"] is JsonObject a2) assetsObj = a2;

			if (assetsObj != null)
			{
				if (assetsObj["decals"] is JsonObject decals && (decals.ContainsKey(fileName) || decals.ContainsKey($"{cleanName}.rtex")))
					return ("decal", 4, 4);

				if (assetsObj["icons"] is JsonObject icons && (icons.ContainsKey(fileName) || icons.ContainsKey($"{cleanName}.rtex")))
					return ("icon", 4, 4);

				if (assetsObj["skyboxes"] is JsonObject skyboxes && (skyboxes.ContainsKey(fileName) || skyboxes.ContainsKey($"{cleanName}.rtex")))
					return ("skybox", 4, 4);

				if (assetsObj["textures"] is JsonObject textures && (textures.ContainsKey(fileName) || textures.ContainsKey($"{cleanName}.rtex")))
					return ("terrain_texture", 4, 4);

				if (assetsObj["vfx_spritesheets"] is JsonObject spritesheets)
				{
					JsonNode? entry = null;
					if (spritesheets.TryGetPropertyValue(fileName, out var e1)) entry = e1;
					else if (spritesheets.TryGetPropertyValue($"{cleanName}.rtex", out var e2)) entry = e2;

					if (entry != null)
					{
						int cols = 4;
						int rows = 4;
						if (entry is JsonObject sObj)
						{
							if (sObj.TryGetPropertyValue("columns", out var cNode) && int.TryParse(cNode?.ToString(), out int c)) cols = c;
							if (sObj.TryGetPropertyValue("rows", out var rNode) && int.TryParse(rNode?.ToString(), out int r)) rows = r;
						}
						return ("vfx_spritesheet", cols, rows);
					}
				}

				if (assetsObj["ribbon_textures"] is JsonObject ribbons && (ribbons.ContainsKey(fileName) || ribbons.ContainsKey($"{cleanName}.rtex")))
					return ("ribbon_texture", 4, 4);

				if (assetsObj["noise_textures"] is JsonObject noise && (noise.ContainsKey(fileName) || noise.ContainsKey($"{cleanName}.rtex")))
					return ("noise_texture", 4, 4);
			}
		}

		if (normalized.Contains("/assets/decals/") || normalized.Contains("/decals/"))
			return ("decal", 4, 4);

		if (normalized.Contains("/assets/icons/") || normalized.Contains("/icons/"))
			return ("icon", 4, 4);

		if (normalized.Contains("/assets/skyboxes/") || normalized.Contains("/skyboxes/"))
			return ("skybox", 4, 4);

		if (normalized.Contains("/assets/vfx/") || normalized.Contains("/vfx/") || normalized.Contains("/spritesheets/"))
			return ("vfx_spritesheet", 4, 4);

		if (normalized.Contains("/textures/ribbons/") || normalized.Contains("/ribbons/"))
			return ("ribbon_texture", 4, 4);

		if (normalized.Contains("/textures/noise/") || normalized.Contains("/noise/"))
			return ("noise_texture", 4, 4);

		if (normalized.Contains("/assets/textures/") || normalized.Contains("/tilesheets/"))
			return ("terrain_texture", 4, 4);

		return ("terrain_texture", 4, 4);
	}

	public static void UpdateMetadataConvertedTexture(
		string workspacePath,
		string pngFileName,
		string rtexFileName,
		string assetType,
		string newHash,
		int columns = 4,
		int rows = 4)
	{
		try
		{
			string metadataPath = Path.Combine(workspacePath, "metadata.json");
			if (!File.Exists(metadataPath)) return;

			string jsonText = File.ReadAllText(metadataPath);
			var jsonNode = JsonNode.Parse(jsonText);
			if (jsonNode is not JsonObject root) return;

			bool modified = false;
			JsonObject? assetsObj = null;
			if (root["Assets"] is JsonObject a1) assetsObj = a1;
			else if (root["MapProperties"] is JsonObject mp && mp["Assets"] is JsonObject a2) assetsObj = a2;

			if (assetsObj == null)
			{
				assetsObj = new JsonObject();
				root["Assets"] = assetsObj;
				modified = true;
			}

			string categoryKey = assetType switch
			{
				"decal" or "decals" => "decals",
				"icon" or "icons" => "icons",
				"skybox" or "skyboxes" => "skyboxes",
				"vfx_spritesheet" or "spritesheet" or "spritesheets" => "vfx_spritesheets",
				"ribbon_texture" or "ribbon" or "ribbons" => "ribbon_textures",
				"noise_texture" or "noise" => "noise_textures",
				_ => "textures"
			};

			if (!assetsObj.ContainsKey(categoryKey) || assetsObj[categoryKey] is not JsonObject)
			{
				assetsObj[categoryKey] = new JsonObject();
				modified = true;
			}

			if (assetsObj[categoryKey] is JsonObject targetCatObj)
			{
				int existingSwatchIndex = -1;
				JsonObject? preservedProps = null;
				if (targetCatObj.ContainsKey(pngFileName))
				{
					if (targetCatObj[pngFileName] is JsonObject oldEntry)
					{
						if (oldEntry.TryGetPropertyValue("swatchIndex", out var sIdx) && int.TryParse(sIdx?.ToString(), out int parsed))
						{
							existingSwatchIndex = parsed;
						}
						preservedProps = oldEntry.DeepClone() as JsonObject;
					}
					targetCatObj.Remove(pngFileName);
					modified = true;
				}

				if (categoryKey == "vfx_spritesheets")
				{
					targetCatObj[rtexFileName] = new JsonObject
					{
						["columns"] = columns,
						["rows"] = rows,
						["hash"] = newHash
					};
					modified = true;
				}
				else if (categoryKey == "textures")
				{
					var texEntry = preservedProps ?? (targetCatObj.ContainsKey(rtexFileName) && targetCatObj[rtexFileName] is JsonObject exObj ? (exObj.DeepClone() as JsonObject) : new JsonObject());
					texEntry["hash"] = newHash;
					if (existingSwatchIndex >= 0)
					{
						texEntry["swatchIndex"] = existingSwatchIndex;
					}
					targetCatObj[rtexFileName] = texEntry;
					modified = true;
				}
				else
				{
					targetCatObj[rtexFileName] = newHash;
					modified = true;
				}
			}

			void ReplacePngReferences(JsonNode? node)
			{
				if (node is JsonObject obj)
				{
					var propList = obj.ToList();
					foreach (var prop in propList)
					{
						if (prop.Value is JsonValue val && val.TryGetValue<string>(out string strVal))
						{
							if (strVal.EndsWith(pngFileName, StringComparison.OrdinalIgnoreCase))
							{
								string updated = strVal.Substring(0, strVal.Length - pngFileName.Length) + rtexFileName;
								obj[prop.Key] = updated;
								modified = true;
							}
						}
						else if (prop.Value is JsonObject or JsonArray)
						{
							ReplacePngReferences(prop.Value);
						}
					}
				}
				else if (node is JsonArray arr)
				{
					for (int i = 0; i < arr.Count; i++)
					{
						if (arr[i] is JsonValue val && val.TryGetValue<string>(out string strVal))
						{
							if (strVal.EndsWith(pngFileName, StringComparison.OrdinalIgnoreCase))
							{
								string updated = strVal.Substring(0, strVal.Length - pngFileName.Length) + rtexFileName;
								arr[i] = updated;
								modified = true;
							}
						}
						else if (arr[i] is JsonObject or JsonArray)
						{
							ReplacePngReferences(arr[i]);
						}
					}
				}
			}

			ReplacePngReferences(root);

			if (modified)
			{
				NormalizeTextureEntries(root);
				MapJsonFormatter.SaveFormattedJson(metadataPath, root);
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapWorkspaceService] Failed to update metadata.json for converted texture {pngFileName}: {ex.Message}");
		}
	}

	public static void EnsurePngAssetsConverted(string workspacePath)
	{
		if (string.IsNullOrEmpty(workspacePath) || !Directory.Exists(workspacePath)) return;

		string assetsDir = Path.Combine(workspacePath, "Assets");
		if (!Directory.Exists(assetsDir)) return;

		try
		{
			string[] pngFiles = Directory.GetFiles(assetsDir, "*.png", SearchOption.AllDirectories);
			if (pngFiles.Length == 0) return;

			JsonObject? metadataRoot = null;
			string metadataPath = Path.Combine(workspacePath, "metadata.json");
			if (File.Exists(metadataPath))
			{
				try
				{
					metadataRoot = JsonNode.Parse(File.ReadAllText(metadataPath)) as JsonObject;
				}
				catch { }
			}

			foreach (string pngPath in pngFiles)
			{
				string normalized = pngPath.Replace("\\", "/");
				if (normalized.Contains("/bin/") || normalized.Contains("/obj/") || normalized.Contains("/.git/") || normalized.Contains("/.godot/") || normalized.Contains("/vscode_embedded/") || normalized.Contains("/wasi_sdk_embedded/"))
				{
					continue;
				}

				string pngFileName = Path.GetFileName(pngPath);
				string rtexFileName = Path.ChangeExtension(pngFileName, ".rtex");
				string rtexPath = Path.ChangeExtension(pngPath, ".rtex");

				var (assetType, columns, rows) = DetectPngAssetInfo(workspacePath, pngPath, metadataRoot);

				GD.Print($"[MapWorkspaceService] Converting PNG asset '{pngFileName}' (type: {assetType}) to .rtex...");

				var convResult = Realm.Shared.Textures.TextureConverter.ConvertTextureFile(pngPath, rtexPath, assetType, columns, rows);
				if (convResult.Success && File.Exists(rtexPath))
				{
					byte[] rtexBytes = File.ReadAllBytes(rtexPath);
					string newHash = RealmMetadataHelper.ComputeBlake3(rtexBytes, ".rtex");

					UpdateMetadataConvertedTexture(workspacePath, pngFileName, rtexFileName, assetType, newHash, columns, rows);

					try
					{
						var attrs = File.GetAttributes(pngPath);
						if ((attrs & FileAttributes.ReadOnly) != 0)
						{
							File.SetAttributes(pngPath, attrs & ~FileAttributes.ReadOnly);
						}
						File.Delete(pngPath);
						GD.Print($"[MapWorkspaceService] Successfully converted '{pngFileName}' to '{rtexFileName}'.");
					}
					catch (Exception ex)
					{
						GD.PrintErr($"[MapWorkspaceService] Failed to delete PNG {pngPath}: {ex.Message}");
					}
				}
				else
				{
					GD.PrintErr($"[MapWorkspaceService] Failed to convert PNG '{pngFileName}': {convResult.ErrorMessage}");
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapWorkspaceService] EnsurePngAssetsConverted error: {ex.Message}");
		}
	}

	public static async Task EnsurePngAssetsConvertedCooperativeAsync(
		string workspacePath,
		Func<int, int, string, Task>? onTextureProgress = null)
	{
		if (string.IsNullOrEmpty(workspacePath) || !Directory.Exists(workspacePath)) return;

		string assetsDir = Path.Combine(workspacePath, "Assets");
		if (!Directory.Exists(assetsDir)) return;

		try
		{
			string[] pngFiles = Directory.GetFiles(assetsDir, "*.png", SearchOption.AllDirectories);
			if (pngFiles.Length == 0) return;

			var validPngs = new List<string>();
			foreach (string pngPath in pngFiles)
			{
				string normalized = pngPath.Replace("\\", "/");
				if (normalized.Contains("/bin/") || normalized.Contains("/obj/") || normalized.Contains("/.git/") || normalized.Contains("/.godot/") || normalized.Contains("/vscode_embedded/") || normalized.Contains("/wasi_sdk_embedded/"))
				{
					continue;
				}
				validPngs.Add(pngPath);
			}

			if (validPngs.Count == 0) return;

			JsonObject? metadataRoot = null;
			string metadataPath = Path.Combine(workspacePath, "metadata.json");
			if (File.Exists(metadataPath))
			{
				try
				{
					metadataRoot = JsonNode.Parse(File.ReadAllText(metadataPath)) as JsonObject;
				}
				catch { }
			}

			for (int i = 0; i < validPngs.Count; i++)
			{
				string pngPath = validPngs[i];
				string pngFileName = Path.GetFileName(pngPath);
				string rtexFileName = Path.ChangeExtension(pngFileName, ".rtex");
				string rtexPath = Path.ChangeExtension(pngPath, ".rtex");

				if (onTextureProgress != null)
				{
					try
					{
						await onTextureProgress(i + 1, validPngs.Count, pngFileName);
					}
					catch (Exception ex)
					{
						GD.PrintErr($"[MapWorkspaceService] Progress callback error: {ex.Message}");
					}
				}

				var (assetType, columns, rows) = DetectPngAssetInfo(workspacePath, pngPath, metadataRoot);

				GD.Print($"[MapWorkspaceService] Converting PNG asset '{pngFileName}' ({i + 1}/{validPngs.Count}) (type: {assetType}) to .rtex...");

				var convResult = Realm.Shared.Textures.TextureConverter.ConvertTextureFile(pngPath, rtexPath, assetType, columns, rows);
				if (convResult.Success && File.Exists(rtexPath))
				{
					byte[] rtexBytes = File.ReadAllBytes(rtexPath);
					string newHash = RealmMetadataHelper.ComputeBlake3(rtexBytes, ".rtex");

					UpdateMetadataConvertedTexture(workspacePath, pngFileName, rtexFileName, assetType, newHash, columns, rows);

					try
					{
						var attrs = File.GetAttributes(pngPath);
						if ((attrs & FileAttributes.ReadOnly) != 0)
						{
							File.SetAttributes(pngPath, attrs & ~FileAttributes.ReadOnly);
						}
						File.Delete(pngPath);
						GD.Print($"[MapWorkspaceService] Successfully converted '{pngFileName}' to '{rtexFileName}'.");
					}
					catch (Exception ex)
					{
						GD.PrintErr($"[MapWorkspaceService] Failed to delete PNG {pngPath}: {ex.Message}");
					}
				}
				else
				{
					GD.PrintErr($"[MapWorkspaceService] Failed to convert PNG '{pngFileName}': {convResult.ErrorMessage}");
				}
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapWorkspaceService] EnsurePngAssetsConvertedCooperativeAsync error: {ex.Message}");
		}
	}

	public static bool NormalizeTextureEntries(JsonObject root, string? wsPath = null)
	{
		if (root == null) return false;
		bool modified = false;
		string effectiveWsPath = !string.IsNullOrEmpty(wsPath)
			? wsPath
			: (GameHost.Instance != null && !string.IsNullOrEmpty(GameHost.Instance.CurrentMapDirectory)
				? GameHost.Instance.CurrentMapDirectory
				: ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath ?? "user://temp_map_workspace"));

		void CleanTexturesObject(JsonObject texturesObj)
		{
			var entriesToConvert = new List<(string Key, string Hash)>();
			foreach (var kvp in texturesObj)
			{
				if (kvp.Value is JsonValue val && val.TryGetValue<string>(out string hashStr))
				{
					entriesToConvert.Add((kvp.Key, hashStr));
				}
			}

			foreach (var (k, h) in entriesToConvert)
			{
				var o = new JsonObject
				{
					["hash"] = h
				};
				texturesObj[k] = o;
				modified = true;
			}

			var usedIndices = new HashSet<int>();
			foreach (var kvp in texturesObj)
			{
				if (kvp.Value is JsonObject texObj)
				{
					if (texObj.ContainsKey("swatch_index"))
					{
						if (!texObj.ContainsKey("swatchIndex"))
						{
							texObj["swatchIndex"] = texObj["swatch_index"]?.DeepClone();
						}
						texObj.Remove("swatch_index");
						modified = true;
					}
					if (texObj.ContainsKey("SwatchIndex"))
					{
						if (!texObj.ContainsKey("swatchIndex"))
						{
							texObj["swatchIndex"] = texObj["SwatchIndex"]?.DeepClone();
						}
						texObj.Remove("SwatchIndex");
						modified = true;
					}

					if (texObj.TryGetPropertyValue("swatchIndex", out var sIdxNode) && sIdxNode != null && int.TryParse(sIdxNode.ToString(), out int parsedIdx) && parsedIdx >= 0)
					{
						usedIndices.Add(parsedIdx);
					}

					if (texObj.ContainsKey("Tile_Mode"))
					{
						if (!texObj.ContainsKey("tile_mode"))
						{
							texObj["tile_mode"] = texObj["Tile_Mode"]?.GetValue<string>();
						}
						texObj.Remove("Tile_Mode");
						modified = true;
					}
					if (texObj.ContainsKey("UV_Scale"))
					{
						if (!texObj.ContainsKey("uv_scale"))
						{
							texObj["uv_scale"] = texObj["UV_Scale"]?.DeepClone();
						}
						texObj.Remove("UV_Scale");
						modified = true;
					}
					if (texObj.ContainsKey("Stochastic_Tile_Size"))
					{
						if (!texObj.ContainsKey("stochastic_tile_size"))
						{
							texObj["stochastic_tile_size"] = texObj["Stochastic_Tile_Size"]?.DeepClone();
						}
						texObj.Remove("Stochastic_Tile_Size");
						modified = true;
					}
					if (texObj.ContainsKey("Scale_Factor"))
					{
						if (!texObj.ContainsKey("scale_factor"))
						{
							texObj["scale_factor"] = texObj["Scale_Factor"]?.DeepClone();
						}
						texObj.Remove("Scale_Factor");
						modified = true;
					}
					if (texObj.ContainsKey("ScaleFactor"))
					{
						if (!texObj.ContainsKey("scale_factor"))
						{
							texObj["scale_factor"] = texObj["ScaleFactor"]?.DeepClone();
						}
						texObj.Remove("ScaleFactor");
						modified = true;
					}
					if (!texObj.ContainsKey("scale_factor"))
					{
						string rtexPath = Path.Combine(effectiveWsPath, "Assets", "textures", kvp.Key);
						if (!File.Exists(rtexPath)) rtexPath = Path.Combine(effectiveWsPath, kvp.Key);
						if (File.Exists(rtexPath))
						{
							try
							{
								string? rtexMeta = Realm.Shared.Metadata.RealmMetadataHelper.ExtractMetadata(rtexPath);
								if (!string.IsNullOrEmpty(rtexMeta))
								{
									var rNode = JsonNode.Parse(rtexMeta);
									if (rNode is JsonObject rObj && (rObj.TryGetPropertyValue("scale_factor", out var sfVal) || rObj.TryGetPropertyValue("Scale_Factor", out sfVal) || rObj.TryGetPropertyValue("scaleFactor", out sfVal)) && float.TryParse(sfVal?.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out float parsedRtexScale))
									{
										texObj["scale_factor"] = parsedRtexScale;
										modified = true;
									}
								}
							}
							catch { }
						}
					}
					if (texObj.ContainsKey("Brightness"))
					{
						if (!texObj.ContainsKey("brightness"))
						{
							texObj["brightness"] = texObj["Brightness"]?.DeepClone();
						}
						texObj.Remove("Brightness");
						modified = true;
					}
					if (texObj.ContainsKey("Tint"))
					{
						if (!texObj.ContainsKey("tint"))
						{
							texObj["tint"] = texObj["Tint"]?.GetValue<string>();
						}
						texObj.Remove("Tint");
						modified = true;
					}
					if (texObj.ContainsKey("Variants"))
					{
						if (!texObj.ContainsKey("variants"))
						{
							texObj["variants"] = texObj["Variants"]?.DeepClone();
						}
						texObj.Remove("Variants");
						modified = true;
					}
					if (texObj.ContainsKey("Cross_Fade"))
					{
						if (!texObj.ContainsKey("cross_fade"))
						{
							texObj["cross_fade"] = texObj["Cross_Fade"]?.DeepClone();
						}
						texObj.Remove("Cross_Fade");
						modified = true;
					}
					if (texObj.ContainsKey("Grid_Cross_Fade"))
					{
						if (!texObj.ContainsKey("cross_fade"))
						{
							texObj["cross_fade"] = texObj["Grid_Cross_Fade"]?.DeepClone();
						}
						texObj.Remove("Grid_Cross_Fade");
						modified = true;
					}
				}
			}

			int nextAvailable = 0;
			foreach (var kvp in texturesObj)
			{
				if (kvp.Value is JsonObject texObj)
				{
					bool hasValidIdx = texObj.TryGetPropertyValue("swatchIndex", out var sIdxNode) && sIdxNode != null && int.TryParse(sIdxNode.ToString(), out int parsedIdx) && parsedIdx >= 0;
					if (!hasValidIdx)
					{
						while (usedIndices.Contains(nextAvailable))
						{
							nextAvailable++;
						}
						texObj["swatchIndex"] = nextAvailable;
						usedIndices.Add(nextAvailable);
						modified = true;
					}
				}
			}
		}

		if (root["Assets"] is JsonObject assets && assets["textures"] is JsonObject tex1)
		{
			CleanTexturesObject(tex1);
		}
		if (root["MapProperties"] is JsonObject mp && mp["Assets"] is JsonObject mpAssets && mpAssets["textures"] is JsonObject tex2)
		{
			CleanTexturesObject(tex2);
		}
		if (root["textures"] is JsonObject tex3)
		{
			CleanTexturesObject(tex3);
		}

		return modified;
	}

	public static void NormalizeMetadataTextureEntries(string workspacePath)
	{
		if (string.IsNullOrEmpty(workspacePath) || !Directory.Exists(workspacePath)) return;

		try
		{
			string metadataPath = Path.Combine(workspacePath, "metadata.json");
			if (!File.Exists(metadataPath)) return;

			string jsonText = File.ReadAllText(metadataPath);
			var jsonNode = JsonNode.Parse(jsonText);
			if (jsonNode is not JsonObject root) return;

			bool modified = NormalizeTextureEntries(root);

			if (modified)
			{
				MapJsonFormatter.SaveFormattedJson(metadataPath, root);
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapWorkspaceService] NormalizeMetadataTextureEntries error: {ex.Message}");
		}
	}

	public static void UpdateMetadataGlbHash(string workspacePath, string fileName, string newHash)
	{
		try
		{
			string metadataPath = Path.Combine(workspacePath, "metadata.json");
			if (!File.Exists(metadataPath)) return;

			string jsonText = File.ReadAllText(metadataPath);
			var jsonNode = JsonNode.Parse(jsonText);
			if (jsonNode is not JsonObject root) return;

			bool modified = false;
			if (root["Assets"] is JsonObject assetsObj && assetsObj["glb"] is JsonObject glbObj)
			{
				foreach (var subCatKvp in glbObj)
				{
					if (subCatKvp.Value is JsonObject subCatObj)
					{
						if (subCatObj.ContainsKey(fileName))
						{
							if (subCatObj[fileName] is JsonObject entryObj && entryObj.ContainsKey("hash"))
							{
								entryObj["hash"] = newHash;
							}
							else
							{
								subCatObj[fileName] = newHash;
							}
							modified = true;
						}
					}
				}
			}

			if (modified)
			{
				MapJsonFormatter.SaveFormattedJson(metadataPath, root);
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapWorkspaceService] Failed to update metadata.json for {fileName}: {ex.Message}");
		}
	}

	public static void UpdateMetadataAnimationHash(string workspacePath, string animFileName, string newHash)
	{
		try
		{
			string metadataPath = Path.Combine(workspacePath, "metadata.json");
			if (!File.Exists(metadataPath)) return;

			string jsonText = File.ReadAllText(metadataPath);
			var jsonNode = JsonNode.Parse(jsonText);
			if (jsonNode is not JsonObject root) return;

			bool modified = false;
			if (root["Assets"] is JsonObject assetsObj && assetsObj["animations"] is JsonObject animsObj)
			{
				if (animsObj.ContainsKey(animFileName))
				{
					animsObj[animFileName] = newHash;
					modified = true;
				}
			}

			if (modified)
			{
				MapJsonFormatter.SaveFormattedJson(metadataPath, root);
			}
		}
		catch (Exception ex)
		{
			GD.PrintErr($"[MapWorkspaceService] Failed to update metadata.json for {animFileName}: {ex.Message}");
		}
	}
}
