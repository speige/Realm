using System;
using System.IO;
using System.Linq;

public static class WasiSdkResolver
{
	/// <summary>
	/// Dynamically resolves the WASI SDK installation path for any user and environment.
	/// </summary>
	/// <returns>The resolved absolute path to the active WASI SDK directory.</returns>
	public static string ResolveWasiSdkPath()
	{
		// 1. Check environment variable WASI_SDK_PATH if explicitly set and valid
		string? envPath = Environment.GetEnvironmentVariable("WASI_SDK_PATH");
		if (!string.IsNullOrEmpty(envPath) && Directory.Exists(envPath))
		{
			if (File.Exists(Path.Combine(envPath, "bin", "clang.exe")) || File.Exists(Path.Combine(envPath, "bin", "clang")))
			{
				return envPath;
			}
		}

		// 2. Scan user profile directory for .wasi-sdk
		string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
		string wasiBaseDir = Path.Combine(userProfile, ".wasi-sdk");
		if (Directory.Exists(wasiBaseDir))
		{
			var candidateDirs = Directory.GetDirectories(wasiBaseDir, "wasi-sdk*");
			if (candidateDirs.Length > 0)
			{
				// Prefer directories containing valid clang compiler and share/wasi-sysroot
				var validSdk = candidateDirs.FirstOrDefault(d =>
					(File.Exists(Path.Combine(d, "bin", "clang.exe")) || File.Exists(Path.Combine(d, "bin", "clang"))) &&
					Directory.Exists(Path.Combine(d, "share", "wasi-sysroot"))
				);
				if (validSdk != null)
				{
					return validSdk;
				}

				var fallbackSdk = candidateDirs.FirstOrDefault(d =>
					File.Exists(Path.Combine(d, "bin", "clang.exe")) || File.Exists(Path.Combine(d, "bin", "clang"))
				);
				if (fallbackSdk != null)
				{
					return fallbackSdk;
				}

				return candidateDirs[0];
			}
		}

		// 3. Fallback to the dotnet packs WASI SDK path (any installed SDK version)
		string programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
		string packsRoot = Path.Combine(programFiles, "dotnet", "packs", "Microsoft.NET.Runtime.WebAssembly.Wasi.Sdk");
		if (Directory.Exists(packsRoot))
		{
			var sdkVersions = Directory.GetDirectories(packsRoot)
				.OrderByDescending(d => TryParseVersion(Path.GetFileName(d)));
			foreach (var sdkVersionDir in sdkVersions)
			{
				string wasiSdk = Path.Combine(sdkVersionDir, "wasi-sdk");
				if (Directory.Exists(wasiSdk))
				{
					return wasiSdk;
				}
			}
		}
		return Path.Combine(packsRoot, "10.0.10", "wasi-sdk");
	}

	private static Version TryParseVersion(string dirName)
	{
		// Version folders can include prerelease suffixes such as "10.0.0-preview.1".
		string numericPart = dirName.Split('-')[0];
		return Version.TryParse(numericPart, out var version) ? version : new Version(0, 0, 0);
	}
}
