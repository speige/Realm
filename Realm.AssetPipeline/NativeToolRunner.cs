using System;
using System.Diagnostics;
using System.IO;

namespace Realm.AssetPipeline;

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
			Stream? stream = asm.GetManifestResourceStream("Realm.AssetPipeline.ThirdPartyBinaries.gltfpack.exe");
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

		string tempDir = Path.Combine(Path.GetTempPath(), $"realm_pipeline_{Guid.NewGuid():N}");
		Directory.CreateDirectory(tempDir);
		string tempInput = Path.Combine(tempDir, "input.glb");
		string tempOutput = Path.Combine(tempDir, "output.glb");

		try
		{
			File.WriteAllBytes(tempInput, inputBytes);

			string textureArgs = compressTextures
				? $"-tc -tl {Math.Max(128, maxTextureResolution)}"
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
