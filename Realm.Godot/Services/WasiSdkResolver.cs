using System;
using System.IO;

public static class WasiSdkResolver
{
	public static string ResolveWasiSdkPath()
	{
		string environmentPath = Environment.GetEnvironmentVariable("WASI_SDK_PATH");
		if (!string.IsNullOrWhiteSpace(environmentPath) && IsValidWasiSdkDirectory(environmentPath))
		{
			return NormalizeDirectoryPath(environmentPath);
		}

		try
		{
			string appDataDir = Godot.OS.GetUserDataDir();
			if (!string.IsNullOrWhiteSpace(appDataDir))
			{
				string versionedPath = Path.Combine(appDataDir, "wasi_sdk", "wasi-sdk-34");
				if (IsValidWasiSdkDirectory(versionedPath))
				{
					return NormalizeDirectoryPath(versionedPath);
				}

				string wasiSdkParentDir = Path.Combine(appDataDir, "wasi_sdk");
				if (Directory.Exists(wasiSdkParentDir))
				{
					foreach (string candidate in System.Linq.Enumerable.OrderByDescending(Directory.GetDirectories(wasiSdkParentDir, "wasi-sdk-*"), d => d))
					{
						if (IsValidWasiSdkDirectory(candidate))
						{
							return NormalizeDirectoryPath(candidate);
						}
					}
				}
			}
		}
		catch
		{
		}

		try
		{
			string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
			string appDataFallback = Path.Combine(appData, "Godot", "app_userdata", "Realm", "wasi_sdk", "wasi-sdk-34");
			if (IsValidWasiSdkDirectory(appDataFallback))
			{
				return NormalizeDirectoryPath(appDataFallback);
			}

			string fallbackSdkParent = Path.Combine(appData, "Godot", "app_userdata", "Realm", "wasi_sdk");
			if (Directory.Exists(fallbackSdkParent))
			{
				foreach (string candidate in System.Linq.Enumerable.OrderByDescending(Directory.GetDirectories(fallbackSdkParent, "wasi-sdk-*"), d => d))
				{
					if (IsValidWasiSdkDirectory(candidate))
					{
						return NormalizeDirectoryPath(candidate);
					}
				}
			}
		}
		catch
		{
		}

		try
		{
			string foundPath = PathUtils.FindPath("wasi_sdk_embedded");
			if (!string.IsNullOrWhiteSpace(foundPath) && IsValidWasiSdkDirectory(foundPath))
			{
				return NormalizeDirectoryPath(foundPath);
			}
		}
		catch
		{
		}

		try
		{
			string projectRootPath = PathUtils.GetProjectRoot();
			if (!string.IsNullOrWhiteSpace(projectRootPath))
			{
				string candidatePath = Path.Combine(projectRootPath, "wasi_sdk_embedded");
				if (IsValidWasiSdkDirectory(candidatePath))
				{
					return NormalizeDirectoryPath(candidatePath);
				}
			}
		}
		catch
		{
		}

		string baseDirectoryPath = AppDomain.CurrentDomain.BaseDirectory;
		DirectoryInfo currentDirectoryInfo = new DirectoryInfo(baseDirectoryPath);
		while (currentDirectoryInfo != null)
		{
			string candidatePath = Path.Combine(currentDirectoryInfo.FullName, "wasi_sdk_embedded");
			if (IsValidWasiSdkDirectory(candidatePath))
			{
				return NormalizeDirectoryPath(candidatePath);
			}

			if (IsValidWasiSdkDirectory(currentDirectoryInfo.FullName))
			{
				return NormalizeDirectoryPath(currentDirectoryInfo.FullName);
			}

			currentDirectoryInfo = currentDirectoryInfo.Parent;
		}

		string fallbackDirectoryPath = Path.GetFullPath(Path.Combine(baseDirectoryPath, "..", "wasi_sdk_embedded"));
		return NormalizeDirectoryPath(fallbackDirectoryPath);
	}

	private static bool IsValidWasiSdkDirectory(string directoryPath)
	{
		if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
		{
			return false;
		}

		string clangExecutablePath = Path.Combine(directoryPath, "bin", "clang.exe");
		string clangUnixPath = Path.Combine(directoryPath, "bin", "clang");
		return (File.Exists(clangExecutablePath) && new FileInfo(clangExecutablePath).Length > 0)
			|| (File.Exists(clangUnixPath) && new FileInfo(clangUnixPath).Length > 0);
	}

	public static string GetDefaultIlcLlvmTarget(string? wasiSdkPath = null)
	{
		string path = !string.IsNullOrWhiteSpace(wasiSdkPath) ? wasiSdkPath : ResolveWasiSdkPath();
		if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path))
		{
			string libDir = Path.Combine(path, "share", "wasi-sysroot", "lib");
			if (Directory.Exists(libDir))
			{
				if (Directory.Exists(Path.Combine(libDir, "wasm32-wasip1")))
				{
					return "wasm32-unknown-wasip1";
				}
				if (Directory.Exists(Path.Combine(libDir, "wasm32-wasi")))
				{
					return "wasm32-unknown-wasi";
				}
			}
		}
		return "wasm32-unknown-wasip1";
	}

	private static string NormalizeDirectoryPath(string path)
	{
		return Path.GetFullPath(path);
	}
}
