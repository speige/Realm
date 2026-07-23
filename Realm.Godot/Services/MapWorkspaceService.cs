using Godot;
using System;
using System.IO;
using System.Linq;

public static class MapWorkspaceService
{
	private static string _cachedRepoRoot;

	private static string GetRepoRoot()
	{
		if (_cachedRepoRoot != null) return _cachedRepoRoot;
		string baseDir = ProjectSettings.GlobalizePath("res://");
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
		_cachedRepoRoot = ProjectSettings.GlobalizePath("res://..").Replace("\\", "/");
		return _cachedRepoRoot;
	}

	private static string FindRootFile(string relativePath)
	{
		string repoRoot = GetRepoRoot();
		string candidate = Path.Combine(repoRoot, relativePath).Replace("\\", "/");
		if (File.Exists(candidate) || Directory.Exists(candidate))
		{
			return candidate;
		}
		return candidate;
	}

	private static string GetSchemaSourcePath()
	{
		return FindRootFile("Realm.MapEditorExtension/map_schema.json");
	}

	private static string GetApiProjPath()
	{
		return FindRootFile("Realm.MapAPI/Realm.MapAPI.csproj");
	}

	private static string GetTemplatePath(string fileName)
	{
		return FindRootFile("MapTemplate/" + fileName);
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
		var existingCsprojs = Directory.GetFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly);
		if (existingCsprojs.Length > 0)
		{
			return;
		}

		string csprojPath = Path.Combine(directory, $"{mapName}.csproj");
		string apiProjPath = GetApiProjPath();
		string templatePath = GetTemplatePath("MapScript.csproj");

		if (File.Exists(templatePath))
		{
			string csprojContent = File.ReadAllText(templatePath);
			// Replace DLL reference with local project reference when generating workspace in editor
			csprojContent = System.Text.RegularExpressions.Regex.Replace(csprojContent,
				@"<ItemGroup>\s*<Reference Include=""Realm\.MapAPI"">.*?</Reference>\s*</ItemGroup>",
				$"<ItemGroup>\n    <ProjectReference Include=\"{apiProjPath}\" />\n  </ItemGroup>",
				System.Text.RegularExpressions.RegexOptions.Singleline);
			File.WriteAllText(csprojPath, csprojContent);
		}

		string targetsTemplate = GetTemplatePath("Directory.Build.targets");
		string targetsPath = Path.Combine(directory, "Directory.Build.targets");
		if (!File.Exists(targetsPath) && File.Exists(targetsTemplate))
		{
			File.Copy(targetsTemplate, targetsPath, true);
		}
	}

	public static void EnsureMapScript(string directory, string mapName)
	{
		string scriptPath = Path.Combine(directory, "MapScript.cs");
		if (!File.Exists(scriptPath) || new FileInfo(scriptPath).Length == 0)
		{
			string template = File.ReadAllText(GetTemplatePath("MapScript.cs"));
			File.WriteAllText(scriptPath, template.Replace("class MapScript", $"class {mapName}"));
		}
	}

	public static void EnsureWasmEntryPoint(string directory)
	{
		string entryPointPath = Path.Combine(directory, "WasmEntryPoint.cs");
		if (!File.Exists(entryPointPath) || new FileInfo(entryPointPath).Length == 0)
		{
			File.Copy(GetTemplatePath("WasmEntryPoint.cs"), entryPointPath);
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
				File.WriteAllText(metadataPath, "{}");
			}
		}

		// Also copy any template asset files (such as .ktx2 textures in Assets/textures) if not already present
		string templateAssetsDir = Path.Combine(Path.GetDirectoryName(templateMeta), "Assets");
		if (Directory.Exists(templateAssetsDir))
		{
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

	public static string BuildPayload(string workspaceDir = null)
	{
		if (string.IsNullOrEmpty(workspaceDir))
		{
			workspaceDir = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath);
		}
		string metaPath = Path.Combine(workspaceDir, "metadata.json").Replace("\\", "/");
		string scriptPath = Path.Combine(workspaceDir, "MapScript.cs").Replace("\\", "/");
		if (!metaPath.StartsWith("/")) metaPath = "/" + metaPath;
		if (!scriptPath.StartsWith("/")) scriptPath = "/" + scriptPath;

		return System.Text.Json.JsonSerializer.Serialize(new[]
		{
			new[] { "openFile", metaPath },
			new[] { "openFile", scriptPath }
		});
	}

	public static void EnsureSolutionFile(string directory, string mapName)
	{
		string slnPath = Path.Combine(directory, "temp_map_workspace.sln");
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
}
