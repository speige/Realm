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
				string versionedPath = Path.Combine(appDataDir, "wasi_sdk", "wasi-sdk-30");
				if (IsValidWasiSdkDirectory(versionedPath))
				{
					return NormalizeDirectoryPath(versionedPath);
				}

				string wasiSdkParentDir = Path.Combine(appDataDir, "wasi_sdk");
				if (Directory.Exists(wasiSdkParentDir))
				{
					foreach (string candidate in Directory.GetDirectories(wasiSdkParentDir, "wasi-sdk-*"))
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
			string appDataFallback = Path.Combine(appData, "Godot", "app_userdata", "Realm.Godot", "wasi_sdk", "wasi-sdk-30");
			if (IsValidWasiSdkDirectory(appDataFallback))
			{
				return NormalizeDirectoryPath(appDataFallback);
			}

			string fallbackSdkParent = Path.Combine(appData, "Godot", "app_userdata", "Realm.Godot", "wasi_sdk");
			if (Directory.Exists(fallbackSdkParent))
			{
				foreach (string candidate in Directory.GetDirectories(fallbackSdkParent, "wasi-sdk-*"))
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

	private static string NormalizeDirectoryPath(string path)
	{
		return Path.GetFullPath(path);
	}
}
