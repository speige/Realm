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
		return File.Exists(clangExecutablePath) || File.Exists(clangUnixPath);
	}

	private static string NormalizeDirectoryPath(string path)
	{
		return Path.GetFullPath(path);
	}
}
