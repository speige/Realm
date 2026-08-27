using Godot;
using LiteDB;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using JsonSerializer = System.Text.Json.JsonSerializer;

public class IndexedAsset
{
	public int Id { get; set; }
	public string FilePath { get; set; } = string.Empty;
	public string FileName { get; set; } = string.Empty;
	public string Extension { get; set; } = string.Empty;
	public string DirectoryPath { get; set; } = string.Empty;
	public long FileSizeBytes { get; set; }
	public DateTime LastModifiedUtc { get; set; }
	public List<string> Tags { get; set; } = new();
	public string MetadataJson { get; set; } = string.Empty;
}

public class IndexedFolder
{
	public int Id { get; set; }
	public string DirectoryPath { get; set; } = string.Empty;
	public DateTime LastScannedUtc { get; set; }
}

public class AssetMetadataModel
{
	public List<string> Tags { get; set; } = new();
}

public class AssetIndexService : IDisposable
{
	private static AssetIndexService? _instance;
	public static AssetIndexService Instance => _instance ??= ServiceLocator.TryGet<AssetIndexService>() ?? new AssetIndexService();

	public event Action<string, bool>? DirectoryIndexingStateChanged;
	public event Action<string>? DirectoryScanCompleted;

	private readonly LiteDatabase _database;
	private readonly ILiteCollection<IndexedAsset> _assetCollection;
	private readonly ILiteCollection<IndexedFolder> _folderCollection;
	private readonly object _syncLock = new();
	private readonly HashSet<string> _indexingDirectories = new(StringComparer.OrdinalIgnoreCase);

	public AssetIndexService()
	{
		string userDirectory = ProjectSettings.GlobalizePath("user://");
		if (!Directory.Exists(userDirectory))
		{
			Directory.CreateDirectory(userDirectory);
		}

		string cacheFilePath = Path.Combine(userDirectory, "asset_index.cache");
		var connectionString = new ConnectionString
		{
			Filename = cacheFilePath,
			Connection = ConnectionType.Shared
		};

		_database = new LiteDatabase(connectionString);
		_assetCollection = _database.GetCollection<IndexedAsset>("assets");
		_folderCollection = _database.GetCollection<IndexedFolder>("folders");

		_assetCollection.EnsureIndex(x => x.FilePath, true);
		_assetCollection.EnsureIndex(x => x.DirectoryPath);
		_assetCollection.EnsureIndex(x => x.Extension);
		_assetCollection.EnsureIndex(x => x.Tags);
		_assetCollection.EnsureIndex(x => x.FileName);

		_folderCollection.EnsureIndex(x => x.DirectoryPath, true);

		InitializeDefaultDirectories();
	}

	private void InitializeDefaultDirectories()
	{
		lock (_syncLock)
		{
			if (_folderCollection.Count() == 0)
			{
				string tempWorkspaceAssets = ProjectSettings.GlobalizePath(MapEditorHUD.TempWorkspaceGodotPath ?? "user://temp_map_workspace");
				if (Directory.Exists(tempWorkspaceAssets))
				{
					AddDirectory(tempWorkspaceAssets);
				}

				string templatePath = PathUtils.FindPath("MapTemplate/Assets");
				if (!string.IsNullOrEmpty(templatePath) && Directory.Exists(templatePath))
				{
					AddDirectory(templatePath);
				}
			}
		}
	}

	public bool IsDirectoryIndexing(string directoryPath)
	{
		if (string.IsNullOrWhiteSpace(directoryPath)) return false;
		lock (_syncLock)
		{
			return _indexingDirectories.Contains(NormalizePath(directoryPath));
		}
	}

	public IReadOnlyList<string> GetIndexedDirectories()
	{
		lock (_syncLock)
		{
			return _folderCollection.FindAll()
				.Select(f => f.DirectoryPath)
				.OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
				.ToList();
		}
	}

	public void AddDirectory(string directoryPath)
	{
		if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
		{
			return;
		}

		string normalizedPath = NormalizePath(directoryPath);
		lock (_syncLock)
		{
			var existingFolder = _folderCollection.FindOne(x => x.DirectoryPath == normalizedPath);
			if (existingFolder == null)
			{
				existingFolder = new IndexedFolder
				{
					DirectoryPath = normalizedPath,
					LastScannedUtc = DateTime.MinValue
				};
				_folderCollection.Insert(existingFolder);
			}

			if (_indexingDirectories.Contains(normalizedPath))
			{
				return;
			}
			_indexingDirectories.Add(normalizedPath);
		}

		DirectoryIndexingStateChanged?.Invoke(normalizedPath, true);

		Task.Run(() =>
		{
			try
			{
				ScanDirectoryInternal(normalizedPath);
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[AssetIndexService] Scan error on {normalizedPath}: {ex.Message}");
			}
			finally
			{
				lock (_syncLock)
				{
					_indexingDirectories.Remove(normalizedPath);
				}
				DirectoryIndexingStateChanged?.Invoke(normalizedPath, false);
				DirectoryScanCompleted?.Invoke(normalizedPath);
			}
		});
	}

	public void RemoveDirectory(string directoryPath)
	{
		if (string.IsNullOrWhiteSpace(directoryPath))
		{
			return;
		}

		string normalizedPath = NormalizePath(directoryPath);
		lock (_syncLock)
		{
			_indexingDirectories.Remove(normalizedPath);
			_folderCollection.DeleteMany(x => x.DirectoryPath == normalizedPath);
			_assetCollection.DeleteMany(x => x.DirectoryPath == normalizedPath);
		}

		DirectoryIndexingStateChanged?.Invoke(normalizedPath, false);
	}

	public void RescanDirectory(string directoryPath)
	{
		if (string.IsNullOrWhiteSpace(directoryPath) || !Directory.Exists(directoryPath))
		{
			return;
		}

		string normalizedPath = NormalizePath(directoryPath);
		lock (_syncLock)
		{
			if (_indexingDirectories.Contains(normalizedPath))
			{
				return;
			}
			_indexingDirectories.Add(normalizedPath);
		}

		DirectoryIndexingStateChanged?.Invoke(normalizedPath, true);

		Task.Run(() =>
		{
			try
			{
				ScanDirectoryInternal(normalizedPath);
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[AssetIndexService] Rescan error on {normalizedPath}: {ex.Message}");
			}
			finally
			{
				lock (_syncLock)
				{
					_indexingDirectories.Remove(normalizedPath);
				}
				DirectoryIndexingStateChanged?.Invoke(normalizedPath, false);
				DirectoryScanCompleted?.Invoke(normalizedPath);
			}
		});
	}

	public void RescanAllDirectories()
	{
		List<string> dirsToScan;
		lock (_syncLock)
		{
			dirsToScan = _folderCollection.FindAll()
				.Select(f => f.DirectoryPath)
				.Where(p => Directory.Exists(p) && !_indexingDirectories.Contains(p))
				.ToList();

			foreach (var dir in dirsToScan)
			{
				_indexingDirectories.Add(dir);
			}
		}

		foreach (var dir in dirsToScan)
		{
			DirectoryIndexingStateChanged?.Invoke(dir, true);
		}

		Task.Run(() =>
		{
			foreach (var dir in dirsToScan)
			{
				try
				{
					ScanDirectoryInternal(dir);
				}
				catch (Exception ex)
				{
					GD.PrintErr($"[AssetIndexService] RescanAll error on {dir}: {ex.Message}");
				}
				finally
				{
					lock (_syncLock)
					{
						_indexingDirectories.Remove(dir);
					}
					DirectoryIndexingStateChanged?.Invoke(dir, false);
					DirectoryScanCompleted?.Invoke(dir);
				}
			}
		});
	}

	private void ScanDirectoryInternal(string normalizedDirectoryPath)
	{
		if (!Directory.Exists(normalizedDirectoryPath))
		{
			return;
		}

		var discoveredFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
		var files = Directory.EnumerateFiles(normalizedDirectoryPath, "*.*", SearchOption.AllDirectories);

		foreach (string filePath in files)
		{
			string normalizedFilePath = NormalizePath(filePath);
			string extension = Path.GetExtension(normalizedFilePath).ToLowerInvariant();

			if (extension == ".cache" || extension == ".log" || extension == ".tmp" || extension == ".uid")
			{
				continue;
			}

			if (normalizedFilePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
				(File.Exists(normalizedFilePath.Substring(0, normalizedFilePath.Length - 5))))
			{
				continue;
			}

			discoveredFiles.Add(normalizedFilePath);

			try
			{
				var fileInfo = new FileInfo(normalizedFilePath);

				lock (_syncLock)
				{
					var existingAsset = _assetCollection.FindOne(x => x.FilePath == normalizedFilePath);

					if (existingAsset != null &&
						existingAsset.FileSizeBytes == fileInfo.Length &&
						existingAsset.LastModifiedUtc == fileInfo.LastWriteTimeUtc)
					{
						continue;
					}

					var tags = LoadTagsForFile(normalizedFilePath, normalizedDirectoryPath);

					var asset = existingAsset ?? new IndexedAsset();
					asset.FilePath = normalizedFilePath;
					asset.FileName = Path.GetFileName(normalizedFilePath);
					asset.Extension = extension;
					asset.DirectoryPath = normalizedDirectoryPath;
					asset.FileSizeBytes = fileInfo.Length;
					asset.LastModifiedUtc = fileInfo.LastWriteTimeUtc;
					asset.Tags = tags;
					asset.MetadataJson = JsonSerializer.Serialize(new AssetMetadataModel { Tags = tags });

					_assetCollection.Upsert(asset);
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[AssetIndexService] Scan error on {normalizedFilePath}: {ex.Message}");
			}
		}

		lock (_syncLock)
		{
			_assetCollection.DeleteMany(x => x.DirectoryPath == normalizedDirectoryPath && !discoveredFiles.Contains(x.FilePath));

			var folderRecord = _folderCollection.FindOne(x => x.DirectoryPath == normalizedDirectoryPath);
			if (folderRecord != null)
			{
				folderRecord.LastScannedUtc = DateTime.UtcNow;
				_folderCollection.Update(folderRecord);
			}
		}
	}

	private List<string> LoadTagsForFile(string filePath, string rootDirectory)
	{
		var tagSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		string sidecarJsonWithExt = filePath + ".json";
		string sidecarJsonNoExt = Path.Combine(Path.GetDirectoryName(filePath)!, Path.GetFileNameWithoutExtension(filePath) + ".json");

		string? foundMetadataPath = null;
		if (File.Exists(sidecarJsonWithExt))
		{
			foundMetadataPath = sidecarJsonWithExt;
		}
		else if (File.Exists(sidecarJsonNoExt) && !string.Equals(sidecarJsonNoExt, filePath, StringComparison.OrdinalIgnoreCase))
		{
			foundMetadataPath = sidecarJsonNoExt;
		}

		if (foundMetadataPath != null)
		{
			try
			{
				string jsonContent = File.ReadAllText(foundMetadataPath);
				var rootNode = JsonNode.Parse(jsonContent);
				if (rootNode is JsonObject jsonObject)
				{
					if (jsonObject["tags"] is JsonArray tagsArray)
					{
						foreach (var item in tagsArray)
						{
							if (item != null)
							{
								string tagStr = item.ToString().Trim();
								if (!string.IsNullOrEmpty(tagStr))
								{
									tagSet.Add(tagStr);
								}
							}
						}
					}
				}
			}
			catch { }
		}

		if (tagSet.Count == 0)
		{
			string nameWithoutExt = Path.GetFileNameWithoutExtension(filePath);
			var nameTokens = nameWithoutExt.Split(new[] { '_', '-', ' ', '.', '@' }, StringSplitOptions.RemoveEmptyEntries);
			foreach (var token in nameTokens)
			{
				if (token.Length > 1 && !char.IsDigit(token[0]))
				{
					tagSet.Add(token.ToLowerInvariant());
				}
			}

			string relativeDir = Path.GetRelativePath(rootDirectory, Path.GetDirectoryName(filePath) ?? rootDirectory);
			if (!string.IsNullOrEmpty(relativeDir) && relativeDir != ".")
			{
				var dirTokens = relativeDir.Split(new[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries);
				foreach (var dir in dirTokens)
				{
					if (!string.IsNullOrWhiteSpace(dir))
					{
						tagSet.Add(dir.ToLowerInvariant());
					}
				}
			}

			string ext = Path.GetExtension(filePath).TrimStart('.').ToLowerInvariant();
			if (!string.IsNullOrEmpty(ext))
			{
				tagSet.Add(ext);
			}
		}

		return tagSet.OrderBy(t => t, StringComparer.OrdinalIgnoreCase).ToList();
	}

	public void UpdateAssetTags(string filePath, List<string> newTags)
	{
		if (string.IsNullOrWhiteSpace(filePath))
		{
			return;
		}

		string normalizedFilePath = NormalizePath(filePath);
		lock (_syncLock)
		{
			var asset = _assetCollection.FindOne(x => x.FilePath == normalizedFilePath);
			if (asset == null)
			{
				return;
			}

			asset.Tags = newTags.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			asset.MetadataJson = JsonSerializer.Serialize(new AssetMetadataModel { Tags = asset.Tags });
			_assetCollection.Update(asset);

			try
			{
				string sidecarPath = normalizedFilePath + ".json";
				var root = new JsonObject
				{
					["tags"] = new JsonArray(asset.Tags.Select(t => (JsonNode)JsonValue.Create(t)!).ToArray())
				};
				File.WriteAllText(sidecarPath, root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[AssetIndexService] UpdateAssetTags error writing sidecar: {ex.Message}");
			}
		}
	}

	public List<IndexedAsset> SearchAssets(string? searchTerm, IReadOnlyCollection<string>? allowedExtensions = null, string? directoryFilter = null)
	{
		lock (_syncLock)
		{
			var query = _assetCollection.Query();

			if (!string.IsNullOrWhiteSpace(directoryFilter))
			{
				string normalizedDir = NormalizePath(directoryFilter);
				query = query.Where(x => x.DirectoryPath == normalizedDir);
			}

			if (allowedExtensions != null && allowedExtensions.Count > 0)
			{
				var normalizedExtensions = allowedExtensions
					.Select(e => e.Trim().ToLowerInvariant())
					.Select(e => e.StartsWith(".") ? e : "." + e)
					.ToHashSet(StringComparer.OrdinalIgnoreCase);

				query = query.Where(x => normalizedExtensions.Contains(x.Extension));
			}

			var candidateList = query.ToList();

			if (string.IsNullOrWhiteSpace(searchTerm))
			{
				return candidateList.OrderBy(x => x.FileName, StringComparer.OrdinalIgnoreCase).ToList();
			}

			string queryTerm = searchTerm.Trim().ToLowerInvariant();
			return candidateList
				.Where(asset =>
					(asset.Tags != null && asset.Tags.Any(t => t.IndexOf(queryTerm, StringComparison.OrdinalIgnoreCase) >= 0)) ||
					asset.FileName.IndexOf(queryTerm, StringComparison.OrdinalIgnoreCase) >= 0 ||
					asset.FilePath.IndexOf(queryTerm, StringComparison.OrdinalIgnoreCase) >= 0)
				.OrderBy(x => x.FileName, StringComparer.OrdinalIgnoreCase)
				.ToList();
		}
	}

	private static string NormalizePath(string path)
	{
		return Path.GetFullPath(path).Replace('\\', '/');
	}

	public void Dispose()
	{
		_database.Dispose();
	}
}
