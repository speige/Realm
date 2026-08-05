using Godot;
using System;
using System.IO;

public static class PathUtils
{
	private static string _cachedProjectRoot;

	public static bool IsDevelopmentBuild
	{
		get
		{
#if DEBUG
			return true;
#else
			string baseDir = AppDomain.CurrentDomain.BaseDirectory.Replace("\\", "/").TrimEnd('/');
			if (baseDir.Contains("data_Realm.Godot_windows_x86_64", StringComparison.OrdinalIgnoreCase))
			{
				return false;
			}

			string exeDir = OS.GetExecutablePath().GetBaseDir().Replace("\\", "/").TrimEnd('/');
			if (Directory.Exists(exeDir))
			{
				string[] dataDirs = Directory.GetDirectories(exeDir, "data_*");
				if (dataDirs.Length > 0 && baseDir.Equals(dataDirs[0].Replace("\\", "/").TrimEnd('/'), StringComparison.OrdinalIgnoreCase))
				{
					return false;
				}
			}

			return true;
#endif
		}
	}

	public static string GetProjectRoot()
	{
		if (!string.IsNullOrEmpty(_cachedProjectRoot))
		{
			return _cachedProjectRoot;
		}

		string resPath = ProjectSettings.GlobalizePath("res://");
		if (!string.IsNullOrWhiteSpace(resPath) && resPath != "." && resPath != "./" && Directory.Exists(resPath))
		{
			_cachedProjectRoot = resPath.Replace("\\", "/").TrimEnd('/');
			return _cachedProjectRoot;
		}

		string baseDir = AppDomain.CurrentDomain.BaseDirectory.Replace("\\", "/").TrimEnd('/');
		if (!string.IsNullOrWhiteSpace(baseDir) && Directory.Exists(baseDir))
		{
			_cachedProjectRoot = baseDir;
			return _cachedProjectRoot;
		}

		string exeDir = OS.GetExecutablePath().GetBaseDir().Replace("\\", "/").TrimEnd('/');
		if (!string.IsNullOrWhiteSpace(exeDir) && Directory.Exists(exeDir))
		{
			string[] dataDirs = Directory.GetDirectories(exeDir, "data_*");
			if (dataDirs.Length > 0)
			{
				_cachedProjectRoot = dataDirs[0].Replace("\\", "/").TrimEnd('/');
				return _cachedProjectRoot;
			}

			_cachedProjectRoot = exeDir;
			return _cachedProjectRoot;
		}

		_cachedProjectRoot = ".";
		return _cachedProjectRoot;
	}

	public static string FindPath(string relativePath)
	{
		if (string.IsNullOrEmpty(relativePath))
		{
			return GetProjectRoot();
		}

		string normalizedRelative = relativePath.Replace("\\", "/").TrimStart('/');

		string primaryPath = Path.Combine(GetProjectRoot(), normalizedRelative).Replace("\\", "/");
		if (File.Exists(primaryPath) || Directory.Exists(primaryPath))
		{
			return primaryPath;
		}

		string baseDir = AppDomain.CurrentDomain.BaseDirectory.Replace("\\", "/").TrimEnd('/');
		if (!string.IsNullOrWhiteSpace(baseDir))
		{
			string directPath = Path.Combine(baseDir, normalizedRelative).Replace("\\", "/");
			if (File.Exists(directPath) || Directory.Exists(directPath))
			{
				return directPath;
			}
		}

		string exeDir = OS.GetExecutablePath().GetBaseDir().Replace("\\", "/").TrimEnd('/');
		if (!string.IsNullOrWhiteSpace(exeDir))
		{
			string exeDirectPath = Path.Combine(exeDir, normalizedRelative).Replace("\\", "/");
			if (File.Exists(exeDirectPath) || Directory.Exists(exeDirectPath))
			{
				return exeDirectPath;
			}

			string[] dataDirs = Directory.GetDirectories(exeDir, "data_*");
			foreach (var dataDir in dataDirs)
			{
				string normalizedDataDir = dataDir.Replace("\\", "/").TrimEnd('/');
				string dataDirPath = Path.Combine(normalizedDataDir, normalizedRelative).Replace("\\", "/");
				if (File.Exists(dataDirPath) || Directory.Exists(dataDirPath))
				{
					return dataDirPath;
				}
			}
		}

		string globalizedRes = ProjectSettings.GlobalizePath("res://" + normalizedRelative).Replace("\\", "/");
		if (!string.IsNullOrWhiteSpace(globalizedRes) && (File.Exists(globalizedRes) || Directory.Exists(globalizedRes)))
		{
			return globalizedRes;
		}

		return primaryPath;
	}

	public static string GlobalizePath(string path)
	{
		if (string.IsNullOrEmpty(path))
		{
			return GetProjectRoot();
		}

		if (path.StartsWith("res://"))
		{
			string subPath = path.Substring(6);
			return FindPath(subPath);
		}

		return ProjectSettings.GlobalizePath(path);
	}
}
