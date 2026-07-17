using Godot;
using System.IO;

public static class MapWorkspaceService
{
	private static string GetSchemaSourcePath()
	{
		return ProjectSettings.GlobalizePath("res://..").Replace("\\", "/") + "/Realm.MapEditorExtension/map_schema.json";
	}

	private static string GetApiProjPath()
	{
		return ProjectSettings.GlobalizePath("res://..").Replace("\\", "/") + "/Realm.MapAPI/Realm.MapAPI.csproj";
	}

	public static void SetupWorkspace(string directory, string mapName)
	{
		if (string.IsNullOrEmpty(directory)) return;
		Directory.CreateDirectory(directory);

		GenerateVSCodeConfig(directory);
		EnsureCsproj(directory, mapName);
		EnsureMapScript(directory, mapName);
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
	""editor.scrollbar.vertical"": ""visible"",
	""editor.scrollbar.horizontal"": ""visible"",
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

	public static void EnsureCsproj(string directory, string mapName)
	{
		string csprojPath = Path.Combine(directory, $"{mapName}.csproj");
		if (!File.Exists(csprojPath))
		{
			string apiProjPath = GetApiProjPath();
			string csprojContent = $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
  </PropertyGroup>
  <ItemGroup>
	<ProjectReference Include=""{apiProjPath}"" />
  </ItemGroup>
</Project>";
			File.WriteAllText(csprojPath, csprojContent);
		}
	}

	public static void EnsureMapScript(string directory, string mapName)
	{
		string scriptPath = Path.Combine(directory, "MapScript.cs");
		if (!File.Exists(scriptPath) || new FileInfo(scriptPath).Length == 0)
		{
			string scriptContent = $@"namespace Realm.Maps;

using Realm.MapAPI;

public class {mapName} : IMapScript
{{
    public void Initialize(IGameAPI api)
    {{
    }}

    public void Update(IGameAPI api, float delta)
    {{
    }}
}}";
			File.WriteAllText(scriptPath, scriptContent);
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
