using Godot;
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Realm.Godot.Services.ModelOptimization;
using Realm.Godot.Utils;
using Realm.Godot.Animation;
using Realm.Ecs.Services;

public static class MapWorkspaceService
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

	private static string NormalizeMapApiReference(string csprojContent)
	{
		csprojContent = System.Text.RegularExpressions.Regex.Replace(csprojContent,
			@"<ProjectReference\s+Include=""[^""]*Realm\.MapAPI\.csproj""[^>]*/>",
			$"<Reference Include=\"Realm.MapAPI\">\n      <HintPath>{MapApiDllRelativePath}</HintPath>\n    </Reference>",
			System.Text.RegularExpressions.RegexOptions.Singleline);

		csprojContent = System.Text.RegularExpressions.Regex.Replace(csprojContent,
			@"<Reference\s+Include=""Realm\.MapAPI""\s*>.*?</Reference>",
			$"<Reference Include=\"Realm.MapAPI\">\n      <HintPath>{MapApiDllRelativePath}</HintPath>\n    </Reference>",
			System.Text.RegularExpressions.RegexOptions.Singleline);

		csprojContent = System.Text.RegularExpressions.Regex.Replace(csprojContent,
			@"<HintPath>[^<]*Realm\.MapAPI\.dll</HintPath>",
			$"<HintPath>{MapApiDllRelativePath}</HintPath>",
			System.Text.RegularExpressions.RegexOptions.Singleline);

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
				var processInfo = new System.Diagnostics.ProcessStartInfo("dotnet", "new sln -n temp_map_workspace")
				{
					WorkingDirectory = directory,
					CreateNoWindow = true,
					UseShellExecute = false
				};
				using (var process = System.Diagnostics.Process.Start(processInfo))
				{
					process?.WaitForExit();
				}

				var addProcessInfo = new System.Diagnostics.ProcessStartInfo("dotnet", $"sln add {mapName}.csproj")
				{
					WorkingDirectory = directory,
					CreateNoWindow = true,
					UseShellExecute = false
				};
				using (var addProcess = System.Diagnostics.Process.Start(addProcessInfo))
				{
					addProcess?.WaitForExit();
				}
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

		try
		{
			string[] glbFiles = Directory.GetFiles(workspacePath, "*.glb", SearchOption.AllDirectories);
			if (glbFiles.Length == 0) return;

			bool anyReimported = false;
			ModelOptimizerService optimizer = null;
			ModelOptimizerService.OptimizationOptions options = default;

			foreach (string glbPath in glbFiles)
			{
				string normalized = glbPath.Replace("\\", "/");
				if (normalized.Contains("/bin/") || normalized.Contains("/obj/") || normalized.Contains("/.git/") || normalized.Contains("/.godot/"))
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

				if (_optimizedFlagCache.TryGetValue(glbPath, out var cached) && cached.LastWriteTime == lastWrite)
				{
					if (cached.HasFlag)
					{
						continue;
					}
				}

				if (ModelOptimizerService.HasDecimationCompletedFlag(glbPath))
				{
					_optimizedFlagCache[glbPath] = (lastWrite, true);
					continue;
				}

				if (optimizer == null)
				{
					optimizer = ServiceLocator.TryGet<ModelOptimizerService>()
						?? new ModelOptimizerService(ServiceLocator.TryGet<WorldAccessor>());

					options = new ModelOptimizerService.OptimizationOptions
					{
						AllowedPixelError = 1.5f,
						CreaseAngleDegrees = 45.0f,
						MaxTextureResolution = 1024,
						ForceReDecimate = true
					};
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

				string fileName = Path.GetFileName(glbPath);
				GD.Print($"[MapWorkspaceService] GLB asset '{fileName}' is missing realm_decimate_completed extras. Re-importing and optimizing into workspace...");

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
							UpdateMetadataAnimationHash(workspacePath, animFileName, MapAssetManager.ComputeBlake3(File.ReadAllBytes(animFilePath)));
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

				var optResult = optimizer.OptimizeGlb(glbBytes, options);
				if (optResult.Success && optResult.OptimizedGlbBytes != null && optResult.OptimizedGlbBytes.Length > 0)
				{
					try
					{
						var attrs = File.GetAttributes(glbPath);
						if ((attrs & FileAttributes.ReadOnly) != 0)
						{
							File.SetAttributes(glbPath, attrs & ~FileAttributes.ReadOnly);
						}
						File.WriteAllBytes(glbPath, optResult.OptimizedGlbBytes);
						anyReimported = true;

						DateTime newLastWrite = File.GetLastWriteTimeUtc(glbPath);
						_optimizedFlagCache[glbPath] = (newLastWrite, true);

						string newHash = MapAssetManager.ComputeBlake3(optResult.OptimizedGlbBytes);
						UpdateMetadataGlbHash(workspacePath, fileName, newHash);

						GD.Print($"[MapWorkspaceService] Successfully re-imported and optimized {fileName} ({optResult.OriginalTriangleCount} -> {optResult.OptimizedTriangleCount} tris).");
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

		try
		{
			string[] glbFiles = Directory.GetFiles(workspacePath, "*.glb", SearchOption.AllDirectories);
			if (glbFiles.Length == 0) return;

			var unoptimizedList = new List<string>();
			foreach (string glbPath in glbFiles)
			{
				string normalized = glbPath.Replace("\\", "/");
				if (normalized.Contains("/bin/") || normalized.Contains("/obj/") || normalized.Contains("/.git/") || normalized.Contains("/.godot/"))
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

				if (ModelOptimizerService.HasDecimationCompletedFlag(glbPath))
				{
					_optimizedFlagCache[glbPath] = (lastWrite, true);
					continue;
				}

				unoptimizedList.Add(glbPath);
			}

			if (unoptimizedList.Count == 0) return;

			bool anyReimported = false;
			ModelOptimizerService optimizer = ServiceLocator.TryGet<ModelOptimizerService>()
				?? new ModelOptimizerService(ServiceLocator.TryGet<WorldAccessor>());

			var options = new ModelOptimizerService.OptimizationOptions
			{
				AllowedPixelError = 1.5f,
				CreaseAngleDegrees = 45.0f,
				MaxTextureResolution = 1024,
				ForceReDecimate = true
			};

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

				GD.Print($"[MapWorkspaceService] GLB asset '{fileName}' ({i + 1}/{unoptimizedList.Count}) is missing realm_decimate_completed extras. Re-importing and optimizing into workspace...");

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
							UpdateMetadataAnimationHash(workspacePath, animFileName, MapAssetManager.ComputeBlake3(File.ReadAllBytes(animFilePath)));
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

				ModelOptimizerService.OptimizationResult optResult = default;
				try
				{
					optResult = optimizer.OptimizeGlb(glbBytes, options);
				}
				catch (Exception ex)
				{
					GD.PrintErr($"[MapWorkspaceService] Exception optimizing {fileName}: {ex.Message}");
					_optimizedFlagCache[glbPath] = (lastWrite, true);
					continue;
				}

				if (optResult.Success && optResult.OptimizedGlbBytes != null && optResult.OptimizedGlbBytes.Length > 0)
				{
					try
					{
						var attrs = File.GetAttributes(glbPath);
						if ((attrs & FileAttributes.ReadOnly) != 0)
						{
							File.SetAttributes(glbPath, attrs & ~FileAttributes.ReadOnly);
						}
						File.WriteAllBytes(glbPath, optResult.OptimizedGlbBytes);
						anyReimported = true;

						DateTime newLastWrite = File.GetLastWriteTimeUtc(glbPath);
						_optimizedFlagCache[glbPath] = (newLastWrite, true);

						string newHash = MapAssetManager.ComputeBlake3(optResult.OptimizedGlbBytes);
						UpdateMetadataGlbHash(workspacePath, fileName, newHash);

						GD.Print($"[MapWorkspaceService] Successfully re-imported and optimized {fileName} ({optResult.OriginalTriangleCount} -> {optResult.OptimizedTriangleCount} tris).");
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

			bool modified = false;

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
