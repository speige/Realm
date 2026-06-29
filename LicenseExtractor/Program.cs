using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Collections.Generic;

namespace LicenseExtractor
{
    public static class Program
    {
        public static void Main(string[] args)
        {
            if (args.Length < 2)
            {
                return;
            }

            string sourceDirectory = Path.GetFullPath(args[0]);
            string destinationDirectory = Path.GetFullPath(args[1]);

            var searcher = new LicenseSearcher(sourceDirectory, destinationDirectory);
            searcher.Extract();
        }
    }

    public class LicenseSearcher
    {
        private readonly string _sourceDirectory;
        private readonly string _destinationDirectory;
        private readonly Regex _licenseRegex;
        private readonly HashSet<string> _excludedDirectories;
        private readonly HashSet<string> _excludedExtensions;

        public LicenseSearcher(string sourceDirectory, string destinationDirectory)
        {
            _sourceDirectory = sourceDirectory;
            _destinationDirectory = destinationDirectory;
            _licenseRegex = new Regex(@"^(?i)[\w\-.]*(license|licence|copyright)[\w\-.]*$", RegexOptions.Compiled);
            _excludedDirectories = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".git",
                ".vs",
                "obj",
                "bin",
                ".godot",
                "node_modules",
                "vscode_embedded"
            };
            _excludedExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".cs",
                ".csproj",
                ".sln",
                ".gd",
                ".tscn",
                ".import",
                ".json",
                ".png",
                ".jpg",
                ".jpeg",
                ".gif",
                ".svg",
                ".ico",
                ".dll",
                ".pdb",
                ".exe",
                ".xml",
                ".user",
                ".suo",
                ".userprefs",
                ".yml",
                ".yaml",
                ".md5",
                ".sha1"
            };
        }

        public void Extract()
        {
            if (!Directory.Exists(_sourceDirectory))
            {
                return;
            }

            if (Directory.Exists(_destinationDirectory))
            {
                Directory.Delete(_destinationDirectory, true);
            }
            Directory.CreateDirectory(_destinationDirectory);

            SearchDirectory(_sourceDirectory);
        }

        private void SearchDirectory(string currentDirectory)
        {
            if (currentDirectory.Equals(_destinationDirectory, StringComparison.OrdinalIgnoreCase) ||
                currentDirectory.StartsWith(_destinationDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string dirName = Path.GetFileName(currentDirectory);
            if (_excludedDirectories.Contains(dirName))
            {
                return;
            }

            foreach (string filePath in Directory.GetFiles(currentDirectory))
            {
                string filename = Path.GetFileName(filePath);
                string extension = Path.GetExtension(filePath);

                if (_excludedExtensions.Contains(extension))
                {
                    continue;
                }

                if (_licenseRegex.IsMatch(filename))
                {
                    CopyFile(filePath);
                }
            }

            foreach (string subDirectory in Directory.GetDirectories(currentDirectory))
            {
                SearchDirectory(subDirectory);
            }
        }

        private void CopyFile(string sourceFilePath)
        {
            string relativePath = Path.GetRelativePath(_sourceDirectory, sourceFilePath);
            string destFilePath = Path.Combine(_destinationDirectory, relativePath);
            string? destFileDir = Path.GetDirectoryName(destFilePath);

            if (!string.IsNullOrEmpty(destFileDir))
            {
                Directory.CreateDirectory(destFileDir);
            }

            File.Copy(sourceFilePath, destFilePath, true);
        }
    }
}
