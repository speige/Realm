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
		return FindRootFile("Realm.MapAPI/MapTemplate/" + fileName);
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

		string settingsJson = @"{
	""editor.formatOnSave"": true,
	""dotnet.preferCSharpExtension"": true,
	""dotnet.server.useOmnisharp"": false,
	""dotnet.projects.enableAutomaticRestore"": true,
	""json.schemas"": [
        {
			""fileMatch"": [
				""/metadata.json""
            ],
			""url"": ""./.vscode/map_schema.json""
        }
    ]
}";
		File.WriteAllText(Path.Combine(vscodeDir, "settings.json"), settingsJson);

		string launchJson = @"{
	""version"": ""0.2.0"",
	""configurations"": [
        {
			""name"": ""Attach to Realm Game Host"",
			""type"": ""coreclr"",
			""request"": ""attach"",
			""processName"": ""Realm.Godot""
        }
    ]
}";
		File.WriteAllText(Path.Combine(vscodeDir, "launch.json"), launchJson);

		string tasksJson = @"{
	""version"": ""2.0.0"",
	""tasks"": [
		{
			""label"": ""Re-generate Map API"",
			""type"": ""shell"",
			""command"": ""powershell"",
			""args"": [
				""-Command"",
				""dotnet build '${workspaceFolder}/../../../Realm.MapAPI/Realm.MapAPI.csproj'; Copy-Item '${workspaceFolder}/../../../Realm.MapAPI/bin/Debug/net10.0/Realm.MapAPI.*' '${workspaceFolder}/lib/' -Force""
			],
			""problemMatcher"": [],
			""group"": {
				""kind"": ""build"",
				""isDefault"": true
			},
			""detail"": ""Compiles Realm.MapAPI and copies DLL, XML, and PDB to the local map lib folder.""
		}
	]
}";
		File.WriteAllText(Path.Combine(vscodeDir, "tasks.json"), tasksJson);

		string agentsMd = @"# Realm Custom Map Agents Guide

Realm is an RTS Game using Godot with C# and the Arch ECS framework.

## Map Scripting (MapScript.cs)
- Implements `IMapScript`.
- `Initialize(IGameAPI api)` is called when the map starts.
- `Update(IGameAPI api, float delta)` is called every simulation tick (30Hz).
- Use `api` to spawn units, send chat messages, define zones, set time of day, etc.

## Unit Configuration (metadata.json)
- Define custom units and properties here.
- Examples of properties: `MaxHp`, `Damage`, `Range`, `Armor`, `Speed`, `CostGold`, `PopCost`, `BuildOptions`, etc.

## Debugging
- Use the 'Attach to Realm Game Host' launch configuration in VS Code to attach the .NET debugger to the game and hit breakpoints in your `MapScript.cs`.
- Hot reloading is supported via the temp workspace sync.
";
		File.WriteAllText(Path.Combine(directory, "AGENTS.md"), agentsMd);
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
		string apiProjPath = GetApiProjPath();

		var existingCsprojs = Directory.GetFiles(directory, "*.csproj", SearchOption.TopDirectoryOnly);
		if (existingCsprojs.Length > 0 && !File.Exists(csprojPath))
		{
			csprojPath = existingCsprojs.FirstOrDefault(f => Path.GetFileName(f).Equals("CustomMap.csproj", StringComparison.OrdinalIgnoreCase)) ?? existingCsprojs[0];
		}

		bool needsWrite = true;
		if (File.Exists(csprojPath))
		{
			string content = File.ReadAllText(csprojPath);
			bool modified = false;

			if (content.Contains("map-world"))
			{
				content = content.Replace("map-world", "game-client");
				modified = true;
			}

			if (content.Contains("<Wit Include=\"wit\\game.g.wit\"") || content.Contains("<Wit Include=\"wit/game.g.wit\""))
			{
				content = System.Text.RegularExpressions.Regex.Replace(content, @"\s*<Wit Include=""wit[/\\]game\.g\.wit"".*?/>", "");
				modified = true;
			}

			if (!content.Contains("0436"))
			{
				if (content.Contains("</PropertyGroup>"))
				{
					content = content.Replace("</PropertyGroup>", "    <NoWarn>$(NoWarn);0436;1591;IL2026;IL2072;IL2062;CS0419;NU1603</NoWarn>\n  </PropertyGroup>");
					modified = true;
				}
			}

			if (content.Contains("wasi-wasm") && content.Contains("BytecodeAlliance.Componentize.DotNet.Wasm.SDK") && content.Contains("WasmComponentWorld"))
			{
				needsWrite = false;
				if (content.Contains("<ProjectReference Include="))
				{
					var match = System.Text.RegularExpressions.Regex.Match(content, @"<ProjectReference Include=""([^""]+)""");
					if (match.Success)
					{
						string oldRef = match.Groups[1].Value;
						string resolvedOld = Path.GetFullPath(Path.Combine(directory, oldRef));
						if (!File.Exists(resolvedOld) && File.Exists(apiProjPath))
						{
							content = content.Replace(oldRef, apiProjPath);
							modified = true;
						}
					}
				}
			}

			if (modified)
			{
				File.WriteAllText(csprojPath, content);
			}
		}

		if (needsWrite)
		{
			string csprojContent = $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RuntimeIdentifier>wasi-wasm</RuntimeIdentifier>
    <WasmGenerateAppBundle>false</WasmGenerateAppBundle>
    <AllowUnsafeBlocks>true</AllowUnsafeBlocks>
    <NativeLib>Shared</NativeLib>
    <WasmComponentWorld>game-client</WasmComponentWorld>
    <RootAllApplicationAssemblies>true</RootAllApplicationAssemblies>
    <NoWarn>$(NoWarn);0436;1591;IL2026;IL2072;IL2062;CS0419;NU1603</NoWarn>
  </PropertyGroup>

  <ItemGroup>
    <Compile Remove=""runner\**"" />
    <None Remove=""runner\**"" />
  </ItemGroup>

  <ItemGroup>
    <RdXmlFile Include=""RuntimeDirectives.xml"" />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include=""{apiProjPath}"" />
  </ItemGroup>

  <ItemGroup>
    <Wit Remove=""wit\game.g.wit"" />
    <Wit Remove=""wit/game.g.wit"" />
  </ItemGroup>


  <ItemGroup>
    <DirectPInvoke Include=""custom:game/game-api"" />
    <LinkerArg Include=""-Wl,--allow-undefined"" />
  </ItemGroup>

  <!-- Force WASI Preview 1 target and strip all component-type flags right before linking -->
  <Target Name=""OverrideIlcLlvmTarget"" BeforeTargets=""LinkNative;LinkNativeLlvm"">
    <PropertyGroup>
      <IlcLlvmTarget>wasm32-unknown-wasi</IlcLlvmTarget>
    </PropertyGroup>
    <ItemGroup>
      <WasmComponentTypeWit Remove=""@(WasmComponentTypeWit)"" />
      <CustomLinkerArg Remove=""@(CustomLinkerArg)"" Condition=""$([System.String]::Copy('%(Identity)').Contains('--component-type'))"" />
      <LinkerArg Remove=""@(LinkerArg)"" Condition=""$([System.String]::Copy('%(Identity)').Contains('--component-type'))"" />
    </ItemGroup>
  </Target>

  <ItemGroup>
    <PackageReference Include=""BytecodeAlliance.Componentize.DotNet.Wasm.SDK"" Version=""0.8.0-preview00011"" />
    <PackageReference Include=""BytecodeAlliance.Componentize.DotNet.WitBindgen"" Version=""0.8.0-preview00011"" />
    <PackageReference Include=""runtime.win-x64.Microsoft.DotNet.ILCompiler.LLVM"" Version=""10.0.0-rc.1.26357.1"" />
  </ItemGroup>
</Project>";
			File.WriteAllText(csprojPath, csprojContent);

		string rdXmlPath = Path.Combine(directory, "RuntimeDirectives.xml");
		string targetAssemblyName = Path.GetFileNameWithoutExtension(csprojPath);
		string rdXmlContent = $@"<Directives xmlns=""http://schemas.microsoft.com/netfx/2013/01/metadata"">
  <Application>
    <Assembly Name=""{targetAssemblyName}"" Dynamic=""Required All"" />
  </Application>
</Directives>";
		File.WriteAllText(rdXmlPath, rdXmlContent);
		}

		string targetsPath = Path.Combine(directory, "Directory.Build.targets");
		if (!File.Exists(targetsPath))
		{
			string targetsContent = @"<Project>
  <!-- Override Mono WASM SDK target to allow Native AOT build -->
  <Target Name=""PrepareInputsForWasmBuild"" />
  <!-- Override componentize-dotnet target to avoid circular dependency in .NET 10 -->
  <Target Name=""PublishAfterBuild"" />
</Project>";
			File.WriteAllText(targetsPath, targetsContent);
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
		string metadataPath = Path.Combine(directory, "metadata.json");
		if (!File.Exists(metadataPath) || new FileInfo(metadataPath).Length == 0)
		{
			File.WriteAllText(metadataPath, "{}");
		}
	}

	public static string BuildPayload()
	{
		return System.Text.Json.JsonSerializer.Serialize(new[]
		{
			new[] { "openFile", "metadata.json" },
			new[] { "openFile", "MapScript.cs" }
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
