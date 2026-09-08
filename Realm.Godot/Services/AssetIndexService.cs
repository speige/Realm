using Godot;
using LiteDB;
using SharpCompress.Archives;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Realm.Shared.Metadata;
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
	public bool HasRealmMetadata { get; set; }
	public string? AssetType { get; set; }
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

	public static string GlobalCasAssetsDirectory => NormalizePath(MapAssetManager.Storage.AssetsDirectory);

	private void InitializeDefaultDirectories()
	{
		lock (_syncLock)
		{
			string legacyArchive = NormalizePath(MapAssetManager.GlobalArchiveFile);
			var forbiddenFolders = _folderCollection.FindAll()
				.Where(f => IsForbiddenPath(f.DirectoryPath) ||
							string.Equals(f.DirectoryPath, legacyArchive, StringComparison.OrdinalIgnoreCase) ||
							f.DirectoryPath.EndsWith(".7z", StringComparison.OrdinalIgnoreCase))
				.ToList();
			foreach (var f in forbiddenFolders)
			{
				_folderCollection.Delete(f.Id);
			}

			string casAssetsDirectory = GlobalCasAssetsDirectory;
			if (!Directory.Exists(casAssetsDirectory))
			{
				Directory.CreateDirectory(casAssetsDirectory);
			}

			var existingCasFolder = _folderCollection.FindOne(x => x.DirectoryPath == casAssetsDirectory);
			if (existingCasFolder == null)
			{
				_folderCollection.Insert(new IndexedFolder
				{
					DirectoryPath = casAssetsDirectory,
					LastScannedUtc = DateTime.MinValue
				});
			}

			var validFolders = _folderCollection.FindAll()
				.Select(f => f.DirectoryPath)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);

			var orphanedAssets = _assetCollection.FindAll()
				.Where(a => !validFolders.Contains(a.DirectoryPath) ||
							IsForbiddenPath(a.DirectoryPath) ||
							IsForbiddenPath(a.FilePath) ||
							a.DirectoryPath.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) ||
							a.FilePath.Contains("/extracted/", StringComparison.OrdinalIgnoreCase) ||
							a.FilePath.Contains("\\extracted\\", StringComparison.OrdinalIgnoreCase))
				.ToList();
			foreach (var orphan in orphanedAssets)
			{
				_assetCollection.Delete(orphan.Id);
			}

			_database.Checkpoint();
		}
	}

	private static bool IsForbiddenPath(string path)
	{
		if (string.IsNullOrWhiteSpace(path)) return true;
		string norm = path.Replace('\\', '/').ToLowerInvariant();
		return norm.Contains(MapWorkspaceService.DefaultWorkspaceFolder) || norm.Contains("maptemplate");
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
		if (string.IsNullOrWhiteSpace(directoryPath))
		{
			return;
		}

		string normalizedPath = NormalizePath(directoryPath);
		if (IsForbiddenPath(normalizedPath) || !Directory.Exists(normalizedPath))
		{
			return;
		}

		if (normalizedPath.EndsWith(".7z", StringComparison.OrdinalIgnoreCase) ||
			normalizedPath.EndsWith(".zip", StringComparison.OrdinalIgnoreCase) ||
			normalizedPath.EndsWith(".rar", StringComparison.OrdinalIgnoreCase) ||
			normalizedPath.EndsWith(".tar", StringComparison.OrdinalIgnoreCase) ||
			normalizedPath.EndsWith(".gz", StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

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
				_database.Checkpoint();
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
		if (string.Equals(normalizedPath, GlobalCasAssetsDirectory, StringComparison.OrdinalIgnoreCase))
		{
			return;
		}

		lock (_syncLock)
		{
			_indexingDirectories.Remove(normalizedPath);

			var foldersToDelete = _folderCollection.FindAll()
				.Where(f => string.Equals(f.DirectoryPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
				.ToList();
			foreach (var f in foldersToDelete)
			{
				_folderCollection.Delete(f.Id);
			}

			var assetsToDelete = _assetCollection.FindAll()
				.Where(a => string.Equals(a.DirectoryPath, normalizedPath, StringComparison.OrdinalIgnoreCase) ||
							a.FilePath.StartsWith(normalizedPath + "/", StringComparison.OrdinalIgnoreCase))
				.ToList();
			foreach (var a in assetsToDelete)
			{
				_assetCollection.Delete(a.Id);
			}

			_database.Checkpoint();
		}

		DirectoryIndexingStateChanged?.Invoke(normalizedPath, false);
	}

	public void RescanDirectory(string directoryPath)
	{
		if (string.IsNullOrWhiteSpace(directoryPath))
		{
			return;
		}

		string normalizedPath = NormalizePath(directoryPath);
		if (IsForbiddenPath(normalizedPath) || !Directory.Exists(normalizedPath))
		{
			return;
		}

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
			var validFolders = _folderCollection.FindAll()
				.Select(f => f.DirectoryPath)
				.ToHashSet(StringComparer.OrdinalIgnoreCase);

			var orphanedAssets = _assetCollection.FindAll()
				.Where(a => !validFolders.Contains(a.DirectoryPath) || IsForbiddenPath(a.DirectoryPath))
				.ToList();
			foreach (var orphan in orphanedAssets)
			{
				_assetCollection.Delete(orphan.Id);
			}

			dirsToScan = _folderCollection.FindAll()
				.Select(f => f.DirectoryPath)
				.Where(p => Directory.Exists(p) && !_indexingDirectories.Contains(p))
				.ToList();

			foreach (var dir in dirsToScan)
			{
				_indexingDirectories.Add(dir);
			}

			_database.Checkpoint();
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

		bool isCasDirectory = string.Equals(normalizedDirectoryPath, GlobalCasAssetsDirectory, StringComparison.OrdinalIgnoreCase);
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

					List<string> tags;
					string? assetType = null;
					string fileName = Path.GetFileName(normalizedFilePath);
					bool hasRealmMetadata = false;

					if (isCasDirectory)
					{
						string blake3Hash = Realm.Shared.Distribution.ContentAddressableStorage.NormalizeBlake3Hash(fileName);
						string? metaJson = MapAssetManager.Storage.GetAssetMetadata(blake3Hash);
						tags = ExtractTagsFromMetadataJson(metaJson);
						if (tags.Count == 0)
						{
							tags = LoadTagsForFile(normalizedFilePath, normalizedDirectoryPath);
						}

						if (!string.IsNullOrWhiteSpace(metaJson))
						{
							hasRealmMetadata = true;
							try
							{
								var node = JsonNode.Parse(metaJson);
								if (node is JsonObject obj)
								{
									string? typeVal = obj["asset_type"]?.ToString()
										?? obj["AssetType"]?.ToString()
										?? obj["type"]?.ToString()
										?? obj["default_asset_type"]?.ToString();
									if (!string.IsNullOrEmpty(typeVal) && Realm.Shared.Metadata.RealmMetadataHelper.IsValidAssetTypeForExtension(normalizedFilePath, typeVal, out string canonical, out _))
									{
										assetType = canonical;
									}

									string? friendlyName = obj["asset_name"]?.ToString()
										?? obj["name"]?.ToString()
										?? obj["original_filename"]?.ToString()
										?? obj["FileName"]?.ToString();
									if (!string.IsNullOrWhiteSpace(friendlyName))
									{
										string fName = Path.GetFileName(friendlyName.Trim());
										fileName = fName.EndsWith(extension, StringComparison.OrdinalIgnoreCase)
											? fName
											: $"{fName}{extension}";
									}
								}
							}
							catch { }
						}
						else
						{
							hasRealmMetadata = Realm.Shared.Metadata.RealmMetadataHelper.HasRealmMetadata(normalizedFilePath);
							assetType = Realm.Shared.Metadata.RealmMetadataHelper.ExtractAssetType(normalizedFilePath);
						}
					}
					else
					{
						tags = LoadTagsForFile(normalizedFilePath, normalizedDirectoryPath);
						hasRealmMetadata = Realm.Shared.Metadata.RealmMetadataHelper.HasRealmMetadata(normalizedFilePath);
						assetType = Realm.Shared.Metadata.RealmMetadataHelper.ExtractAssetType(normalizedFilePath);
					}

					var asset = existingAsset ?? new IndexedAsset();
					asset.FilePath = normalizedFilePath;
					asset.FileName = fileName;
					asset.Extension = extension;
					asset.DirectoryPath = normalizedDirectoryPath;
					asset.FileSizeBytes = fileInfo.Length;
					asset.LastModifiedUtc = fileInfo.LastWriteTimeUtc;
					asset.Tags = tags;
					asset.MetadataJson = JsonSerializer.Serialize(new AssetMetadataModel { Tags = tags });
					asset.HasRealmMetadata = hasRealmMetadata;
					asset.AssetType = assetType;

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
			var toDelete = _assetCollection.FindAll()
				.Where(x => string.Equals(x.DirectoryPath, normalizedDirectoryPath, StringComparison.OrdinalIgnoreCase) && !discoveredFiles.Contains(x.FilePath))
				.ToList();
			foreach (var d in toDelete)
			{
				_assetCollection.Delete(d.Id);
			}

			var folderRecord = _folderCollection.FindOne(x => x.DirectoryPath == normalizedDirectoryPath);
			if (folderRecord != null)
			{
				folderRecord.LastScannedUtc = DateTime.UtcNow;
				_folderCollection.Update(folderRecord);
			}

			_database.Checkpoint();
		}
	}

	private static List<string> ExtractTagsFromMetadataJson(string? metaJson)
	{
		var list = new List<string>();
		if (string.IsNullOrWhiteSpace(metaJson)) return list;
		try
		{
			var node = JsonNode.Parse(metaJson);
			if (node is JsonObject obj && obj["tags"] is JsonArray arr)
			{
				foreach (var item in arr)
				{
					string? t = item?.ToString()?.Trim();
					if (!string.IsNullOrEmpty(t) && !list.Contains(t, StringComparer.OrdinalIgnoreCase))
					{
						list.Add(t);
					}
				}
			}
		}
		catch { }
		return list;
	}

	private List<string> LoadTagsForFile(string filePath, string rootDirectory)
	{
		var tagSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

		var embeddedTags = RealmMetadataHelper.ExtractTags(filePath);
		foreach (var tag in embeddedTags)
		{
			tagSet.Add(tag);
		}

		if (tagSet.Count == 0)
		{
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

			asset.Tags = newTags.Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim().ToLowerInvariant()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
			asset.MetadataJson = JsonSerializer.Serialize(new AssetMetadataModel { Tags = asset.Tags });
			_assetCollection.Update(asset);

			RealmMetadataHelper.SetTags(normalizedFilePath, asset.Tags);

			bool isCasFile = normalizedFilePath.StartsWith(GlobalCasAssetsDirectory + "/", StringComparison.OrdinalIgnoreCase);
			if (isCasFile)
			{
				string hash = Realm.Shared.Distribution.ContentAddressableStorage.NormalizeBlake3Hash(Path.GetFileName(normalizedFilePath));
				string? updatedMeta = RealmMetadataHelper.ExtractMetadata(normalizedFilePath);
				if (!string.IsNullOrWhiteSpace(updatedMeta))
				{
					MapAssetManager.Storage.UpdateSidecarCache(hash, updatedMeta);
				}
			}
			else
			{
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

			_database.Checkpoint();
		}
	}

	public void UpdateAssetType(string filePath, string newAssetType)
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

			asset.AssetType = newAssetType;
			asset.HasRealmMetadata = true;
			_assetCollection.Update(asset);

			RealmMetadataHelper.SetAssetType(normalizedFilePath, newAssetType);

			bool isCasFile = normalizedFilePath.StartsWith(GlobalCasAssetsDirectory + "/", StringComparison.OrdinalIgnoreCase);
			if (isCasFile)
			{
				string hash = Realm.Shared.Distribution.ContentAddressableStorage.NormalizeBlake3Hash(Path.GetFileName(normalizedFilePath));
				string? updatedMeta = RealmMetadataHelper.ExtractMetadata(normalizedFilePath);
				if (!string.IsNullOrWhiteSpace(updatedMeta))
				{
					MapAssetManager.Storage.UpdateSidecarCache(hash, updatedMeta);
				}
			}

			_database.Checkpoint();
		}
	}

	public IndexedAsset? GetAssetByPath(string filePath)
	{
		if (string.IsNullOrWhiteSpace(filePath))
		{
			return null;
		}

		string normalizedFilePath = NormalizePath(filePath);
		lock (_syncLock)
		{
			return _assetCollection.FindOne(x => x.FilePath == normalizedFilePath);
		}
	}

	public List<IndexedAsset> SearchAssets(string? searchTerm, IReadOnlyCollection<string>? allowedExtensions = null, string? directoryFilter = null, bool requireRealmMetadata = false, string? requiredAssetType = null)
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

			if (requireRealmMetadata)
			{
				candidateList = candidateList.Where(a => a.HasRealmMetadata || Realm.Shared.Metadata.RealmMetadataHelper.HasRealmMetadata(a.FilePath)).ToList();
			}

			if (!string.IsNullOrWhiteSpace(requiredAssetType))
			{
				candidateList = candidateList.Where(a =>
				{
					string? type = a.AssetType;
					if (string.IsNullOrEmpty(type))
					{
						type = Realm.Shared.Metadata.RealmMetadataHelper.ExtractAssetType(a.FilePath);
						if (!string.IsNullOrEmpty(type))
						{
							a.AssetType = type;
						}
					}
					return string.Equals(type, requiredAssetType, StringComparison.OrdinalIgnoreCase);
				}).ToList();
			}

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
		return Path.GetFullPath(path).Replace('\\', '/').TrimEnd('/');
	}

	public void Dispose()
	{
		_database.Dispose();
	}
}
