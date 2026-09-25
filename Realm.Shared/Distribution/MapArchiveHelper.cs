using System;
using System.IO;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Writers.SevenZip;
using SharpCompress.Common;
using SharpCompress.Readers;

using SharpCompress.Compressors.LZMA;

namespace Realm.Shared.Distribution;

public static class MapArchiveHelper
{
    public static void Create7zArchive(string sourceDirectory, string destination7zPath, Action<float, string>? progressCallback = null, int compressionLevel = 1)
    {
        if (string.IsNullOrWhiteSpace(sourceDirectory) || !Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException($"Source directory '{sourceDirectory}' does not exist.");
        }

        string? destinationDir = Path.GetDirectoryName(destination7zPath);
        if (!string.IsNullOrEmpty(destinationDir) && !Directory.Exists(destinationDir))
        {
            Directory.CreateDirectory(destinationDir);
        }

        if (File.Exists(destination7zPath))
        {
            File.Delete(destination7zPath);
        }

        int dictSize;
        int fastBytes;
        if (compressionLevel <= 1)
        {
            dictSize = 64 * 1024;
            fastBytes = 16;
        }
        else if (compressionLevel <= 3)
        {
            dictSize = 1024 * 1024;
            fastBytes = 32;
        }
        else if (compressionLevel <= 5)
        {
            dictSize = 16 * 1024 * 1024;
            fastBytes = 32;
        }
        else
        {
            dictSize = 32 * 1024 * 1024;
            fastBytes = 64;
        }

        using var outputStream = new FileStream(destination7zPath, FileMode.Create, FileAccess.Write, FileShare.None, 65536);
        var options = new SevenZipWriterOptions(CompressionType.LZMA2)
        {
            CompressionLevel = compressionLevel,
            LzmaProperties = new LzmaEncoderProperties(true, dictSize, fastBytes),
            BufferSize = 65536
        };
        using var writer = new SevenZipWriter(outputStream, options);
        var allFiles = Directory.GetFiles(sourceDirectory, "*.*", SearchOption.AllDirectories);
        var filesToArchive = new List<string>(allFiles.Length);
        foreach (var file in allFiles)
        {
            string relativePath = Path.GetRelativePath(sourceDirectory, file).Replace('\\', '/');
            if (relativePath.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) ||
                relativePath.StartsWith(".backups/", StringComparison.OrdinalIgnoreCase) ||
                relativePath.StartsWith("obj/", StringComparison.OrdinalIgnoreCase) ||
                relativePath.Contains("/obj/", StringComparison.OrdinalIgnoreCase) ||
                relativePath.StartsWith(".godot/", StringComparison.OrdinalIgnoreCase) ||
                relativePath.StartsWith(".sidecarcache/", StringComparison.OrdinalIgnoreCase) ||
                relativePath.StartsWith(".vscode/", StringComparison.OrdinalIgnoreCase) ||
                relativePath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
                relativePath.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) ||
                relativePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
                relativePath.EndsWith(".rar", StringComparison.OrdinalIgnoreCase) ||
                relativePath.EndsWith(".tar", StringComparison.OrdinalIgnoreCase) ||
                relativePath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase) ||
                relativePath.EndsWith(".bak", StringComparison.OrdinalIgnoreCase) ||
                relativePath.EndsWith(".backup", StringComparison.OrdinalIgnoreCase) ||
                relativePath.EndsWith(".rkey", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(Path.GetFileName(relativePath), "authorship_key.pem", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var fileInfo = new FileInfo(file);
            if (!fileInfo.Exists || fileInfo.Length == 0)
            {
                continue;
            }

            filesToArchive.Add(file);
        }

        int total = filesToArchive.Count;
        for (int i = 0; i < total; i++)
        {
            string file = filesToArchive[i];
            string relativePath = Path.GetRelativePath(sourceDirectory, file).Replace('\\', '/');
            progressCallback?.Invoke((float)(i + 1) / Math.Max(1, total), relativePath);
            using var fileStream = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
            writer.Write(relativePath, fileStream, File.GetLastWriteTimeUtc(file));
        }
    }

    public static (string? ManifestJson, string RootPrefix) ReadManifestFromArchive(string archiveFilePath)
    {
        if (string.IsNullOrWhiteSpace(archiveFilePath) || !File.Exists(archiveFilePath))
        {
            return (null, string.Empty);
        }

        if (archiveFilePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            return ReadManifestFromZip(archiveFilePath);
        }

        return ReadManifestFrom7z(archiveFilePath);
    }

    private static (string? ManifestJson, string RootPrefix) ReadManifestFromZip(string zipFilePath)
    {
        using var zip = System.IO.Compression.ZipFile.OpenRead(zipFilePath);
        var manifestEntries = zip.Entries.Where(e =>
        {
            string norm = e.FullName.Replace('\\', '/');
            if (norm.Contains("/.backups/", StringComparison.OrdinalIgnoreCase) ||
                norm.StartsWith(".backups/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            return norm.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase) &&
                   (norm.Equals("manifest.json", StringComparison.OrdinalIgnoreCase) || norm.EndsWith("/manifest.json", StringComparison.OrdinalIgnoreCase));
        }).ToList();

        if (manifestEntries.Count == 0)
        {
            return (null, string.Empty);
        }

        if (manifestEntries.Count > 1)
        {
            throw new InvalidOperationException($"Multiple manifest.json files found in archive ({manifestEntries.Count} found). Import aborted.");
        }

        var manifestEntry = manifestEntries[0];
        string full = manifestEntry.FullName.Replace('\\', '/');
        string rootPrefix = full.Length > "manifest.json".Length
            ? full.Substring(0, full.Length - "manifest.json".Length)
            : string.Empty;

        using var stream = manifestEntry.Open();
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        return (reader.ReadToEnd(), rootPrefix);
    }

    private static (string? ManifestJson, string RootPrefix) ReadManifestFrom7z(string archiveFilePath)
    {
        using var fileStream = new FileStream(archiveFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
        using var archive = SevenZipArchive.OpenArchive(fileStream, new ReaderOptions());
        var manifestEntries = archive.Entries.Where(e =>
        {
            if (e.IsDirectory || string.IsNullOrWhiteSpace(e.Key))
            {
                return false;
            }
            string norm = e.Key.Replace('\\', '/');
            if (norm.Contains("/.backups/", StringComparison.OrdinalIgnoreCase) ||
                norm.StartsWith(".backups/", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            return norm.EndsWith("manifest.json", StringComparison.OrdinalIgnoreCase) &&
                   (norm.Equals("manifest.json", StringComparison.OrdinalIgnoreCase) || norm.EndsWith("/manifest.json", StringComparison.OrdinalIgnoreCase));
        }).ToList();

        if (manifestEntries.Count == 0)
        {
            return (null, string.Empty);
        }

        if (manifestEntries.Count > 1)
        {
            throw new InvalidOperationException($"Multiple manifest.json files found in archive ({manifestEntries.Count} found). Import aborted.");
        }

        var manifestEntry = manifestEntries[0];
        string full = manifestEntry.Key!.Replace('\\', '/');
        string rootPrefix = full.Length > "manifest.json".Length
            ? full.Substring(0, full.Length - "manifest.json".Length)
            : string.Empty;

        using var stream = manifestEntry.OpenEntryStream();
        using var reader = new StreamReader(stream, System.Text.Encoding.UTF8);
        return (reader.ReadToEnd(), rootPrefix);
    }

    public static void ProcessArchiveCandidates(
        string archiveFilePath,
        MapManifest manifest,
        string rootPrefix,
        Action<string, Func<Stream>> candidateHandler)
    {
        if (archiveFilePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            using var zip = System.IO.Compression.ZipFile.OpenRead(archiveFilePath);
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\"))
                {
                    continue;
                }

                string norm = entry.FullName.Replace('\\', '/');
                if (!string.IsNullOrEmpty(rootPrefix) && norm.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    norm = norm.Substring(rootPrefix.Length);
                }
                norm = norm.TrimStart('/');

                if (manifest.IsCandidateFile(norm))
                {
                    candidateHandler(norm, () => entry.Open());
                }
            }
        }
        else
        {
            using var fileStream = new FileStream(archiveFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, FileOptions.SequentialScan);
            using var archive = SevenZipArchive.OpenArchive(fileStream, new ReaderOptions());
            foreach (var entry in archive.Entries)
            {
                if (entry.IsDirectory || string.IsNullOrWhiteSpace(entry.Key))
                {
                    continue;
                }

                string norm = entry.Key.Replace('\\', '/');
                if (!string.IsNullOrEmpty(rootPrefix) && norm.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                {
                    norm = norm.Substring(rootPrefix.Length);
                }
                norm = norm.TrimStart('/');

                if (manifest.IsCandidateFile(norm))
                {
                    candidateHandler(norm, () => entry.OpenEntryStream());
                }
            }
        }
    }

    public static void Extract7zArchive(string archiveFilePath, string targetDirectory)
    {
        if (string.IsNullOrWhiteSpace(archiveFilePath) || !File.Exists(archiveFilePath))
        {
            throw new FileNotFoundException($"Archive file '{archiveFilePath}' not found.");
        }

        if (!Directory.Exists(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        if (TryExtractNative(archiveFilePath, targetDirectory))
        {
            return;
        }

        using var fileStream = new FileStream(archiveFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
        using var archive = SevenZipArchive.OpenArchive(fileStream, new ReaderOptions());
        foreach (var entry in archive.Entries)
        {
            if (entry.IsDirectory)
            {
                continue;
            }

            string entryKey = entry.Key ?? string.Empty;
            if (string.IsNullOrWhiteSpace(entryKey))
            {
                continue;
            }

            string destinationPath = Path.Combine(targetDirectory, entryKey.Replace('/', Path.DirectorySeparatorChar));
            string? destinationDir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destinationDir) && !Directory.Exists(destinationDir))
            {
                Directory.CreateDirectory(destinationDir);
            }

            using var entryStream = entry.OpenEntryStream();
            using var outStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024);
            entryStream.CopyTo(outStream, 1024 * 1024);
        }
    }

    public static void ExtractZipArchive(string zipFilePath, string targetDirectory)
    {
        if (string.IsNullOrWhiteSpace(zipFilePath) || !File.Exists(zipFilePath))
        {
            throw new FileNotFoundException($"Archive file '{zipFilePath}' not found.");
        }

        if (!Directory.Exists(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        System.IO.Compression.ZipFile.ExtractToDirectory(zipFilePath, targetDirectory, overwriteFiles: true);
    }

    public static void ExtractArchive(string archiveFilePath, string targetDirectory)
    {
        if (string.IsNullOrWhiteSpace(archiveFilePath) || !File.Exists(archiveFilePath))
        {
            throw new FileNotFoundException($"Archive file '{archiveFilePath}' not found.");
        }

        if (TryExtractNative(archiveFilePath, targetDirectory))
        {
            return;
        }

        if (archiveFilePath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                ExtractZipArchive(archiveFilePath, targetDirectory);
                return;
            }
            catch
            {
                Extract7zArchive(archiveFilePath, targetDirectory);
                return;
            }
        }

        try
        {
            Extract7zArchive(archiveFilePath, targetDirectory);
        }
        catch
        {
            ExtractZipArchive(archiveFilePath, targetDirectory);
        }
    }

    public static bool TryExtractNative(string archiveFilePath, string targetDirectory)
    {
        try
        {
            string fullArchivePath = Path.GetFullPath(archiveFilePath);
            string fullTargetPath = Path.GetFullPath(targetDirectory);

            if (!Directory.Exists(fullTargetPath))
            {
                Directory.CreateDirectory(fullTargetPath);
            }

            string? sevenZipPath = FindSevenZipExecutable();
            if (sevenZipPath != null)
            {
                if (RunProcess(sevenZipPath, $"x -y \"-o{fullTargetPath}\" \"{fullArchivePath}\""))
                {
                    if (Directory.GetFileSystemEntries(fullTargetPath).Length > 0)
                    {
                        return true;
                    }
                }
            }

            string? tarPath = FindTarExecutable();
            if (tarPath != null)
            {
                if (RunProcess(tarPath, $"-xf \"{fullArchivePath}\" -C \"{fullTargetPath}\""))
                {
                    if (Directory.GetFileSystemEntries(fullTargetPath).Length > 0)
                    {
                        return true;
                    }
                }
            }

            return false;
        }
        catch
        {
            return false;
        }
    }

    private static string? FindSevenZipExecutable()
    {
        string[] candidates = OperatingSystem.IsWindows()
            ? new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "7-Zip", "7z.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "7-Zip", "7z.exe"),
                @"C:\Program Files\7-Zip\7z.exe",
                @"C:\Program Files (x86)\7-Zip\7z.exe",
                "7z.exe",
                "7z"
            }
            : new[]
            {
                "/usr/bin/7z",
                "/usr/local/bin/7z",
                "/usr/bin/7za",
                "/usr/local/bin/7za",
                "7z",
                "7za"
            };

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return FindInPath(OperatingSystem.IsWindows() ? "7z.exe" : "7z") ?? FindInPath(OperatingSystem.IsWindows() ? "7za.exe" : "7za");
    }

    private static string? FindTarExecutable()
    {
        string[] candidates = OperatingSystem.IsWindows()
            ? new[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "tar.exe"),
                @"C:\Windows\System32\tar.exe",
                "tar.exe",
                "tar"
            }
            : new[]
            {
                "/usr/bin/tar",
                "/bin/tar",
                "/usr/local/bin/tar",
                "tar"
            };

        foreach (string candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return FindInPath(OperatingSystem.IsWindows() ? "tar.exe" : "tar");
    }

    private static string? FindInPath(string filename)
    {
        string? pathEnv = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(pathEnv))
        {
            return null;
        }

        char separator = OperatingSystem.IsWindows() ? ';' : ':';
        string[] paths = pathEnv.Split(separator, StringSplitOptions.RemoveEmptyEntries);
        foreach (string path in paths)
        {
            try
            {
                string fullPath = Path.Combine(path.Trim(), filename);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }
            catch
            {
            }
        }

        return null;
    }

    private static bool RunProcess(string exePath, string arguments)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = System.Diagnostics.Process.Start(psi);
            if (process == null)
            {
                return false;
            }

            process.WaitForExit();
            return process.ExitCode == 0;
        }
        catch
        {
            return false;
        }
    }
}
