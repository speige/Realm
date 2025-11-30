using System;
using System.Diagnostics;
using System.IO;

namespace Realm.Shared;

public static class NativeToolRunner
{
	private static string? _cachedGltfPackPath;

	public static string? FindGltfPackPath()
	{
		if (!string.IsNullOrEmpty(_cachedGltfPackPath) && File.Exists(_cachedGltfPackPath))
		{
			return _cachedGltfPackPath;
		}

		bool isWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;
		string exeName = isWindows ? "gltfpack.exe" : "gltfpack";

		string baseDir = AppContext.BaseDirectory;
		string cwd = Directory.GetCurrentDirectory();

		string[] candidatePaths = new string[]
		{
			Path.Combine(baseDir, exeName),
			Path.Combine(baseDir, "ThirdPartyBinaries", exeName),
			Path.Combine(cwd, exeName),
			Path.Combine(cwd, "ThirdPartyBinaries", exeName),
			Path.Combine(cwd, "..", "ThirdPartyBinaries", exeName),
			Path.Combine(cwd, "..", "..", "ThirdPartyBinaries", exeName)
		};

		foreach (var path in candidatePaths)
		{
			if (File.Exists(path))
			{
				_cachedGltfPackPath = Path.GetFullPath(path);
				return _cachedGltfPackPath;
			}
		}

		var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
		var pathSeparator = isWindows ? ';' : ':';
		foreach (var dir in pathEnv.Split(pathSeparator, StringSplitOptions.RemoveEmptyEntries))
		{
			var candidate = Path.Combine(dir.Trim(), exeName);
			if (File.Exists(candidate))
			{
				_cachedGltfPackPath = Path.GetFullPath(candidate);
				return _cachedGltfPackPath;
			}
		}

		try
		{
			string tempDir = Path.Combine(Path.GetTempPath(), "realm_tools_bin");
			string tempExe = Path.Combine(tempDir, exeName);
			if (File.Exists(tempExe) && new FileInfo(tempExe).Length > 0)
			{
				_cachedGltfPackPath = tempExe;
				return _cachedGltfPackPath;
			}

			var asm = typeof(NativeToolRunner).Assembly;
			Stream? stream = asm.GetManifestResourceStream("Realm.Shared.ThirdPartyBinaries.gltfpack.exe");
			if (stream == null)
			{
				foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
				{
					stream = a.GetManifestResourceStream("Realm.Tools.Cli.ThirdPartyBinaries.gltfpack.exe");
					if (stream != null) break;
					var names = a.GetManifestResourceNames();
					foreach (var name in names)
					{
						if (name.EndsWith("gltfpack.exe", StringComparison.OrdinalIgnoreCase))
						{
							stream = a.GetManifestResourceStream(name);
							break;
						}
					}
					if (stream != null) break;
				}
			}

			if (stream != null)
			{
				Directory.CreateDirectory(tempDir);
				using (var fileStream = File.Create(tempExe))
				{
					stream.CopyTo(fileStream);
				}
				_cachedGltfPackPath = tempExe;
				return _cachedGltfPackPath;
			}
		}
		catch
		{
		}

		return null;
	}

	private static string? _cachedFfmpegPath;

	public static string? FindFfmpegPath()
	{
		if (!string.IsNullOrEmpty(_cachedFfmpegPath) && File.Exists(_cachedFfmpegPath))
		{
			return _cachedFfmpegPath;
		}

		bool isWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;
		string exeName = isWindows ? "ffmpeg.exe" : "ffmpeg";

		string baseDir = AppContext.BaseDirectory;
		string cwd = Directory.GetCurrentDirectory();

		string[] candidatePaths = new string[]
		{
			Path.Combine(baseDir, exeName),
			Path.Combine(baseDir, "ThirdPartyBinaries", exeName),
			Path.Combine(cwd, exeName),
			Path.Combine(cwd, "ThirdPartyBinaries", exeName),
			Path.Combine(cwd, "..", "ThirdPartyBinaries", exeName),
			Path.Combine(cwd, "..", "..", "ThirdPartyBinaries", exeName)
		};

		foreach (var path in candidatePaths)
		{
			if (File.Exists(path))
			{
				_cachedFfmpegPath = Path.GetFullPath(path);
				return _cachedFfmpegPath;
			}
		}

		var pathEnv = Environment.GetEnvironmentVariable("PATH") ?? "";
		var pathSeparator = isWindows ? ';' : ':';
		foreach (var dir in pathEnv.Split(pathSeparator, StringSplitOptions.RemoveEmptyEntries))
		{
			var candidate = Path.Combine(dir.Trim(), exeName);
			if (File.Exists(candidate))
			{
				_cachedFfmpegPath = Path.GetFullPath(candidate);
				return _cachedFfmpegPath;
			}
		}

		return null;
	}

	private static bool ExtractEmbeddedResource(string resourceSuffix, string destinationPath)
	{
		if (File.Exists(destinationPath) && new FileInfo(destinationPath).Length > 0)
		{
			return true;
		}

		Stream? stream = null;
		foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
		{
			var names = asm.GetManifestResourceNames();
			foreach (var name in names)
			{
				if (name.EndsWith(resourceSuffix, StringComparison.OrdinalIgnoreCase))
				{
					stream = asm.GetManifestResourceStream(name);
					break;
				}
			}
			if (stream != null) break;
		}

		if (stream != null)
		{
			string? dir = Path.GetDirectoryName(destinationPath);
			if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
			using (var fileStream = File.Create(destinationPath))
			{
				stream.CopyTo(fileStream);
			}
			return true;
		}

		return false;
	}

	public static (int ExitCode, string Stdout, string Stderr) RunTool(string toolFileName, string arguments, int timeoutMs = 60000, string? workingDir = null)
	{
		string effectiveWorkingDir = !string.IsNullOrEmpty(workingDir) && Directory.Exists(workingDir)
			? workingDir
			: Directory.GetCurrentDirectory();
		var psi = new ProcessStartInfo
		{
			FileName = toolFileName,
			Arguments = arguments,
			WorkingDirectory = effectiveWorkingDir,
			CreateNoWindow = true,
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true
		};

		using var proc = Process.Start(psi);
		if (proc == null)
		{
			return (-1, string.Empty, $"Failed to start process: {toolFileName}");
		}

		string stdout = proc.StandardOutput.ReadToEnd();
		string stderr = proc.StandardError.ReadToEnd();
		proc.WaitForExit(timeoutMs);

		return (proc.ExitCode, stdout, stderr);
	}

	public static (bool Success, byte[] OutputBytes, string ErrorMessage) DecompressKhrTextureBasisuIfNeeded(byte[] glbBytes)
	{
		if (glbBytes == null || glbBytes.Length == 0) return (true, glbBytes ?? Array.Empty<byte>(), string.Empty);

		var (json, _, _) = GlbManifestUtils.ParseGlb(glbBytes);
		if (json is not System.Text.Json.Nodes.JsonObject root) return (true, glbBytes, string.Empty);

		bool hasBasisu = false;
		if (root.TryGetPropertyValue("extensionsUsed", out var extUsed) && extUsed is System.Text.Json.Nodes.JsonArray usedArray)
		{
			foreach (var node in usedArray)
			{
				if (node?.GetValue<string>() == "KHR_texture_basisu")
				{
					hasBasisu = true;
					break;
				}
			}
		}

		if (!hasBasisu && root.TryGetPropertyValue("extensionsRequired", out var extReq) && extReq is System.Text.Json.Nodes.JsonArray reqArray)
		{
			foreach (var node in reqArray)
			{
				if (node?.GetValue<string>() == "KHR_texture_basisu")
				{
					hasBasisu = true;
					break;
				}
			}
		}

		if (!hasBasisu)
		{
			return (true, glbBytes, string.Empty);
		}

		string tempDir = Path.Combine(Path.GetTempPath(), $"realm_decomp_{Guid.NewGuid():N}");
		Directory.CreateDirectory(tempDir);
		string tempIn = Path.Combine(tempDir, "input.glb");
		string tempOut = Path.Combine(tempDir, "output.glb");

		try
		{
			File.WriteAllBytes(tempIn, glbBytes);
			bool isWindows = Environment.OSVersion.Platform == PlatformID.Win32NT;
			string exeName = isWindows ? "cmd.exe" : "npx";
			string args = isWindows
				? $"/c npx --yes @gltf-transform/cli ktxdecompress \"{tempIn}\" \"{tempOut}\""
				: $"--yes @gltf-transform/cli ktxdecompress \"{tempIn}\" \"{tempOut}\"";

			var runResult = RunTool(exeName, args, timeoutMs: 60000, workingDir: tempDir);
			if (runResult.ExitCode == 0 && File.Exists(tempOut))
			{
				byte[] decompressed = File.ReadAllBytes(tempOut);
				return (true, decompressed, string.Empty);
			}

			return (false, glbBytes, $"ktxdecompress failed (exit code {runResult.ExitCode}): {runResult.Stderr}\n{runResult.Stdout}");
		}
		catch (Exception ex)
		{
			return (false, glbBytes, $"Exception during ktxdecompress: {ex.Message}");
		}
		finally
		{
			try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, true); } catch { }
		}
	}

	public static (bool Success, byte[]? OutputBytes, string ErrorMessage) RunGltfPack(
		byte[] inputBytes,
		float simplificationRatio = 0.5f,
		int maxTextureResolution = 1024,
		bool compressTextures = true)
	{
		string? tool = FindGltfPackPath();
		if (string.IsNullOrEmpty(tool))
		{
			return (false, null, "gltfpack binary not found on system or candidate paths.");
		}

		byte[] effectiveInput = inputBytes;
		var (decompOk, decompBytes, _) = DecompressKhrTextureBasisuIfNeeded(inputBytes);
		if (decompOk && decompBytes != null && decompBytes.Length > 0)
		{
			effectiveInput = decompBytes;
		}

		string tempDir = Path.Combine(Path.GetTempPath(), $"realm_pipeline_{Guid.NewGuid():N}");
		Directory.CreateDirectory(tempDir);
		string tempInput = Path.Combine(tempDir, "input.glb");
		string tempOutput = Path.Combine(tempDir, "output.glb");

		try
		{
			File.WriteAllBytes(tempInput, effectiveInput);

			string textureArgs = compressTextures
				? $"-tw -tl {Math.Max(128, maxTextureResolution)}"
				: "";

			string ratioArg = simplificationRatio is > 0f and < 1.0f
				? $"-si {simplificationRatio:F2}"
				: "";

			string args = $"-i \"{tempInput}\" -o \"{tempOutput}\" {textureArgs} {ratioArg} -kn -km -ke -noq";

			var psi = new ProcessStartInfo
			{
				FileName = tool,
				Arguments = args,
				CreateNoWindow = true,
				UseShellExecute = false,
				RedirectStandardOutput = true,
				RedirectStandardError = true
			};

			using var proc = Process.Start(psi);
			if (proc == null)
			{
				return (false, null, "Failed to start gltfpack process.");
			}

			string stdout = proc.StandardOutput.ReadToEnd();
			string stderr = proc.StandardError.ReadToEnd();
			proc.WaitForExit(60000);

			if (proc.ExitCode == 0 && File.Exists(tempOutput))
			{
				byte[] output = File.ReadAllBytes(tempOutput);
				return (true, output, string.Empty);
			}

			return (false, null, $"gltfpack exited with code {proc.ExitCode}: {stderr}\n{stdout}");
		}
		catch (Exception ex)
		{
			return (false, null, $"Exception during gltfpack execution: {ex.Message}");
		}
		finally
		{
			try
			{
				if (Directory.Exists(tempDir))
				{
					Directory.Delete(tempDir, true);
				}
			}
			catch
			{
			}
		}
	}
}
