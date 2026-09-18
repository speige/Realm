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
	public string? MapName { get; set; }
	public string? MapVersion { get; set; }
}

public class IndexedMapPackage
{
	public int Id { get; set; }
	public string MapName { get; set; } = string.Empty;
	public string MapVersion { get; set; } = string.Empty;
	public string ManifestPath { get; set; } = string.Empty;
	public DateTime DownloadedUtc { get; set; }
	public List<string> AssetHashes { get; set; } = new();
	public bool IsP2P { get; set; }
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
	private readonly ILiteCollection<IndexedMapPackage> _mapPackageCollection;
	private readonly object _syncLock = new();
	private readonly HashSet<string> _indexingDirectories = new(StringComparer.OrdinalIgnoreCase);

	private LiteDatabase? _p2pDatabase;
	private readonly object _p2pSyncLock = new();

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
		_mapPackageCollection = _database.GetCollection<IndexedMapPackage>("map_packages");

		_assetCollection.EnsureIndex(x => x.FilePath, true);
		_assetCollection.EnsureIndex(x => x.DirectoryPath);
		_assetCollection.EnsureIndex(x => x.Extension);
		_assetCollection.EnsureIndex(x => x.Tags);
		_assetCollection.EnsureIndex(x => x.FileName);
		_assetCollection.EnsureIndex(x => x.MapName);
		_assetCollection.EnsureIndex(x => x.MapVersion);

		_folderCollection.EnsureIndex(x => x.DirectoryPath, true);

		_mapPackageCollection.EnsureIndex(x => x.MapName);
		_mapPackageCollection.EnsureIndex(x => x.MapVersion);

		InitializeDefaultDirectories();
	}

	private LiteDatabase GetP2PDatabase()
	{
		lock (_p2pSyncLock)
		{
			if (_p2pDatabase != null) return _p2pDatabase;
			string p2pDir = MapAssetManager.P2PArchiveDirectory;
			if (!Directory.Exists(p2pDir)) Directory.CreateDirectory(p2pDir);
			string cacheFilePath = Path.Combine(p2pDir, "p2p_asset_index.cache");
			var connectionString = new ConnectionString
			{
				Filename = cacheFilePath,
				Connection = ConnectionType.Shared
			};
			_p2pDatabase = new LiteDatabase(connectionString);
			var p2pAssetCol = _p2pDatabase.GetCollection<IndexedAsset>("assets");
			var p2pMapCol = _p2pDatabase.GetCollection<IndexedMapPackage>("map_packages");
			p2pAssetCol.EnsureIndex(x => x.FilePath, true);
			p2pAssetCol.EnsureIndex(x => x.MapName);
			p2pAssetCol.EnsureIndex(x => x.MapVersion);
			p2pMapCol.EnsureIndex(x => x.MapName);
			p2pMapCol.EnsureIndex(x => x.MapVersion);
			return _p2pDatabase;
		}
	}

	public void ClearP2PIndex()
	{
		lock (_p2pSyncLock)
		{
			if (_p2pDatabase != null)
			{
				_p2pDatabase.Dispose();
				_p2pDatabase = null;
			}
		}
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
				.Select(f => (BsonValue)f.Id)
				.ToArray();
			if (forbiddenFolders.Length > 0)
			{
				_folderCollection.DeleteMany(Query.In("_id", forbiddenFolders));
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
				.Select(a => (BsonValue)a.Id)
				.ToArray();
			if (orphanedAssets.Length > 0)
			{
				_assetCollection.DeleteMany(Query.In("_id", orphanedAssets));
			}

			if (Directory.Exists(MapAssetManager.GlobalArchiveDirectory))
			{
				var manifestFiles = Directory.GetFiles(MapAssetManager.GlobalArchiveDirectory, "*manifest*.json");
				foreach (var file in manifestFiles)
				{
					if (Path.GetFileName(file) == "pck_cache.json" || Path.GetFileName(file) == "servers.json") continue;
					try
					{
						var manifest = MapManifest.LoadFromFile(file);
						if (manifest != null && manifest.Files != null && manifest.Files.Count > 0)
						{
							RegisterManifest(manifest, file, isP2P: false);
						}
					}
					catch { }
				}
			}

			_database.Checkpoint();
		}
	}

	public void RegisterManifest(Realm.Shared.Distribution.MapManifest manifest, string manifestPath, bool isP2P = false)
	{
		if (manifest == null) return;
		string mapName = !string.IsNullOrWhiteSpace(manifest.MapName) ? manifest.MapName.Trim() : Path.GetFileNameWithoutExtension(manifestPath).Replace("_manifest", "");
		string mapVersion = !string.IsNullOrWhiteSpace(manifest.Version) ? manifest.Version.Trim() : "1.0.0";

		if (isP2P)
		{
			lock (_p2pSyncLock)
			{
				var p2pDb = GetP2PDatabase();
				var p2pMapCol = p2pDb.GetCollection<IndexedMapPackage>("map_packages");
				var p2pAssetCol = p2pDb.GetCollection<IndexedAsset>("assets");

				var package = p2pMapCol.FindOne(p => p.MapName == mapName && p.MapVersion == mapVersion) ?? new IndexedMapPackage();
				package.MapName = mapName;
				package.MapVersion = mapVersion;
				package.ManifestPath = manifestPath;
				package.DownloadedUtc = DateTime.UtcNow;
				package.IsP2P = true;
				package.AssetHashes = manifest.Files != null ? manifest.Files.Values.ToList() : new List<string>();
				p2pMapCol.Upsert(package);

				if (manifest.Files != null)
				{
					foreach (var kvp in manifest.Files)
					{
						string virtualPath = kvp.Key;
						string hash = kvp.Value;
						string norm = Realm.Shared.Distribution.ContentAddressableStorage.NormalizeBlake3Hash(hash);
						string? casPath = MapAssetManager.P2PStorage.FindAssetFilePath(norm);
						if (casPath != null && File.Exists(casPath))
						{
							string normPath = NormalizePath(casPath);
							var asset = p2pAssetCol.FindOne(x => x.FilePath == normPath) ?? new IndexedAsset();
							var fi = new FileInfo(normPath);
							asset.FilePath = normPath;
							asset.FileName = Path.GetFileName(virtualPath);
							asset.Extension = Path.GetExtension(normPath).ToLowerInvariant();
							asset.DirectoryPath = MapAssetManager.P2PArchiveDirectory;
							asset.FileSizeBytes = fi.Length;
							asset.LastModifiedUtc = fi.LastWriteTimeUtc;
							asset.MapName = mapName;
							asset.MapVersion = mapVersion;
							p2pAssetCol.Upsert(asset);
						}
					}
				}

				p2pDb.Checkpoint();
			}
			return;
		}

		lock (_syncLock)
		{
			var package = _mapPackageCollection.FindOne(p => p.MapName == mapName && p.MapVersion == mapVersion) ?? new IndexedMapPackage();
			package.MapName = mapName;
			package.MapVersion = mapVersion;
			package.ManifestPath = manifestPath;
			package.DownloadedUtc = DateTime.UtcNow;
			package.IsP2P = false;
			package.AssetHashes = manifest.Files != null ? manifest.Files.Values.ToList() : new List<string>();
			_mapPackageCollection.Upsert(package);

			if (manifest.Files != null)
			{
				foreach (var kvp in manifest.Files)
				{
					string virtualPath = kvp.Key;
					string hash = kvp.Value;
					string norm = Realm.Shared.Distribution.ContentAddressableStorage.NormalizeBlake3Hash(hash);
					string? casPath = MapAssetManager.Storage.FindAssetFilePath(norm);
					if (casPath != null && File.Exists(casPath))
					{
						string normPath = NormalizePath(casPath);
						var asset = _assetCollection.FindOne(x => x.FilePath == normPath) ?? new IndexedAsset();
						var fi = new FileInfo(normPath);
						asset.FilePath = normPath;
						asset.FileName = Path.GetFileName(virtualPath);
						asset.Extension = Path.GetExtension(normPath).ToLowerInvariant();
						asset.DirectoryPath = GlobalCasAssetsDirectory;
						asset.FileSizeBytes = fi.Length;
						asset.LastModifiedUtc = fi.LastWriteTimeUtc;
						asset.MapName = mapName;
						asset.MapVersion = mapVersion;

						string? metaJson = MapAssetManager.Storage.GetAssetMetadata(norm);
						var tags = ExtractTagsFromMetadataJson(metaJson);
						if (tags.Count > 0)
						{
							asset.Tags = tags;
							asset.MetadataJson = JsonSerializer.Serialize(new AssetMetadataModel { Tags = tags });
						}
						asset.HasRealmMetadata = Realm.Shared.Metadata.RealmMetadataHelper.HasRealmMetadata(normPath);
						asset.AssetType = Realm.Shared.Metadata.RealmMetadataHelper.ExtractAssetType(normPath);

						_assetCollection.Upsert(asset);
					}
				}
			}

			_database.Checkpoint();
		}
	}

	public IReadOnlyList<IndexedMapPackage> GetDownloadedMapPackages()
	{
		lock (_syncLock)
		{
			return _mapPackageCollection.FindAll()
				.Where(p => !p.IsP2P)
				.OrderBy(p => p.MapName, StringComparer.OrdinalIgnoreCase)
				.ThenByDescending(p => p.MapVersion, StringComparer.OrdinalIgnoreCase)
				.ToList();
		}
	}

	public string? GetLatestVersionForMap(string mapName)
	{
		if (string.IsNullOrWhiteSpace(mapName)) return null;
		lock (_syncLock)
		{
			var packages = _mapPackageCollection.Find(p => p.MapName == mapName && !p.IsP2P).ToList();
			if (packages.Count == 0) return null;
			packages.Sort((a, b) => CompareVersions(b.MapVersion, a.MapVersion));
			return packages[0].MapVersion;
		}
	}

	public static int CompareVersions(string v1, string v2)
	{
		if (System.Version.TryParse(v1, out var ver1) && System.Version.TryParse(v2, out var ver2))
		{
			return ver1.CompareTo(ver2);
		}
		return string.Compare(v1, v2, StringComparison.OrdinalIgnoreCase);
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

			_folderCollection.DeleteMany(f => f.DirectoryPath == normalizedPath);
			var leftoverFolders = _folderCollection.FindAll()
				.Where(f => string.Equals(f.DirectoryPath, normalizedPath, StringComparison.OrdinalIgnoreCase))
				.Select(f => (BsonValue)f.Id)
				.ToArray();
			if (leftoverFolders.Length > 0)
			{
				_folderCollection.DeleteMany(Query.In("_id", leftoverFolders));
			}

			_assetCollection.DeleteMany(Query.EQ("DirectoryPath", normalizedPath));
			_assetCollection.DeleteMany(Query.StartsWith("FilePath", normalizedPath + "/"));
			_assetCollection.DeleteMany(Query.StartsWith("FilePath", normalizedPath + "\\"));

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
				.Select(a => (BsonValue)a.Id)
				.ToArray();
			if (orphanedAssets.Length > 0)
			{
				_assetCollection.DeleteMany(Query.In("_id", orphanedAssets));
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

		Dictionary<string, IndexedAsset> existingAssets;
		lock (_syncLock)
		{
			existingAssets = _assetCollection.Find(Query.EQ("DirectoryPath", normalizedDirectoryPath))
				.ToDictionary(x => x.FilePath, StringComparer.OrdinalIgnoreCase);
		}

		var batchToUpsert = new List<IndexedAsset>();

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

				if (existingAssets.TryGetValue(normalizedFilePath, out var existingAsset) &&
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
				asset.MapName = existingAsset?.MapName ?? asset.MapName;
				asset.MapVersion = existingAsset?.MapVersion ?? asset.MapVersion;

				batchToUpsert.Add(asset);

				if (batchToUpsert.Count >= 250)
				{
					lock (_syncLock)
					{
						_assetCollection.Upsert(batchToUpsert);
					}
					batchToUpsert.Clear();
				}
			}
			catch (Exception ex)
			{
				GD.PrintErr($"[AssetIndexService] Scan error on {normalizedFilePath}: {ex.Message}");
			}
		}

		if (batchToUpsert.Count > 0)
		{
			lock (_syncLock)
			{
				_assetCollection.Upsert(batchToUpsert);
			}
			batchToUpsert.Clear();
		}

		lock (_syncLock)
		{
			var idsToDelete = existingAssets.Values
				.Where(x => !discoveredFiles.Contains(x.FilePath))
				.Select(x => (BsonValue)x.Id)
				.ToArray();
			if (idsToDelete.Length > 0)
			{
				_assetCollection.DeleteMany(Query.In("_id", idsToDelete));
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

	public List<IndexedAsset> SearchAssets(
		string? searchTerm,
		IReadOnlyCollection<string>? allowedExtensions = null,
		string? directoryFilter = null,
		bool requireRealmMetadata = false,
		string? requiredAssetType = null,
		string? mapNameFilter = null,
		string? mapVersionFilter = null)
	{
		lock (_syncLock)
		{
			var query = _assetCollection.Query();

			if (!string.IsNullOrWhiteSpace(mapNameFilter))
			{
				string targetVersion = mapVersionFilter ?? "latest";
				if (string.Equals(targetVersion, "latest", StringComparison.OrdinalIgnoreCase))
				{
					string? latest = GetLatestVersionForMap(mapNameFilter);
					targetVersion = latest ?? "";
				}

				if (!string.IsNullOrEmpty(targetVersion))
				{
					query = query.Where(x => x.MapName == mapNameFilter && x.MapVersion == targetVersion);
				}
				else
				{
					query = query.Where(x => x.MapName == mapNameFilter);
				}
			}
			else if (!string.IsNullOrWhiteSpace(directoryFilter))
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
					asset.FilePath.IndexOf(queryTerm, StringComparison.OrdinalIgnoreCase) >= 0 ||
					(!string.IsNullOrEmpty(asset.MapName) && asset.MapName.IndexOf(queryTerm, StringComparison.OrdinalIgnoreCase) >= 0))
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
		lock (_p2pSyncLock)
		{
			_p2pDatabase?.Dispose();
			_p2pDatabase = null;
		}
		_database.Dispose();
	}
}
