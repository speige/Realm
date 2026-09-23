using System;
using System.IO;
using SharpCompress.Archives.SevenZip;
using SharpCompress.Writers.SevenZip;
using SharpCompress.Common;
using SharpCompress.Readers;

namespace Realm.Shared.Distribution;

public static class MapArchiveHelper
{
    public static void Create7zArchive(string sourceDirectory, string destination7zPath, Action<float, string>? progressCallback = null)
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

        using var outputStream = File.Create(destination7zPath);
        using var writer = new SevenZipWriter(outputStream, new SevenZipWriterOptions(CompressionType.LZMA2));
        var allFiles = Directory.GetFiles(sourceDirectory, "*.*", SearchOption.AllDirectories);
        var filesToArchive = new List<string>();
        foreach (var file in allFiles)
        {
            string relativePath = Path.GetRelativePath(sourceDirectory, file).Replace('\\', '/');
            if (relativePath.StartsWith(".git/", StringComparison.OrdinalIgnoreCase) ||
                relativePath.StartsWith(".backups/", StringComparison.OrdinalIgnoreCase) ||
                relativePath.StartsWith("obj/", StringComparison.OrdinalIgnoreCase) ||
                relativePath.Contains("/obj/", StringComparison.OrdinalIgnoreCase) ||
                relativePath.StartsWith(".godot/", StringComparison.OrdinalIgnoreCase) ||
                relativePath.StartsWith(".sidecarcache/", StringComparison.OrdinalIgnoreCase) ||
                relativePath.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
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
            using var fileStream = File.OpenRead(file);
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
        using var archive = SevenZipArchive.OpenArchive(archiveFilePath, new ReaderOptions());
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
        Action<string, Stream> candidateHandler)
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
                    using var stream = entry.Open();
                    candidateHandler(norm, stream);
                }
            }
        }
        else
        {
            using var archive = SevenZipArchive.OpenArchive(archiveFilePath, new ReaderOptions());
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
                    using var stream = entry.OpenEntryStream();
                    candidateHandler(norm, stream);
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

        using var archive = SevenZipArchive.OpenArchive(archiveFilePath, new ReaderOptions());
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
            using var outStream = File.Create(destinationPath);
            entryStream.CopyTo(outStream);
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

        using var zip = System.IO.Compression.ZipFile.OpenRead(zipFilePath);
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\"))
            {
                continue;
            }

            string destinationPath = Path.Combine(targetDirectory, entry.FullName.Replace('/', Path.DirectorySeparatorChar));
            string? destinationDir = Path.GetDirectoryName(destinationPath);
            if (!string.IsNullOrEmpty(destinationDir) && !Directory.Exists(destinationDir))
            {
                Directory.CreateDirectory(destinationDir);
            }

            using var entryStream = entry.Open();
            using var outStream = File.Create(destinationPath);
            entryStream.CopyTo(outStream);
        }
    }

    public static void ExtractArchive(string archiveFilePath, string targetDirectory)
    {
        if (string.IsNullOrWhiteSpace(archiveFilePath) || !File.Exists(archiveFilePath))
        {
            throw new FileNotFoundException($"Archive file '{archiveFilePath}' not found.");
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
}
