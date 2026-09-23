using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

public class MapDistributionClient
{
    private readonly System.Net.Http.HttpClient _httpClient = new System.Net.Http.HttpClient();

    public event Action<float>? DownloadProgressChanged;

    public async Task<bool> DownloadMapAsync(string hostIp, int port, string mapName, Action<float>? progressCallback = null)
    {
        string manifestUrl = $"http://{hostIp}:{port}/map/manifest";
        MapAssetManager.Log($"[MapDistributionClient] Requesting map manifest from {manifestUrl}");

        try
        {
            var manifestResponse = await _httpClient.GetAsync(manifestUrl);
            if (!manifestResponse.IsSuccessStatusCode)
            {
                MapAssetManager.LogErr($"[MapDistributionClient] Failed to get manifest: {manifestResponse.StatusCode}");
                return false;
            }

            string manifestJson = await manifestResponse.Content.ReadAsStringAsync();
            var manifest = MapManifest.LoadFromJson(manifestJson) ?? JsonSerializer.Deserialize<MapManifest>(manifestJson);
            if (manifest == null || manifest.Files == null)
            {
                MapAssetManager.LogErr("[MapDistributionClient] Manifest parsing failed.");
                return false;
            }

            string version = !string.IsNullOrWhiteSpace(manifest.Version) ? manifest.Version.Trim() : "1.0.0";
            string manifestMapName = !string.IsNullOrWhiteSpace(manifest.MapName) ? manifest.MapName.Trim() : mapName;
            string manifestBlake3 = MapAssetManager.ComputeManifestBlake3(manifest);
            string localMapDir = MapAssetManager.GetMapDirectory(manifestMapName, version, manifestBlake3, false);
            if (!Directory.Exists(localMapDir))
            {
                Directory.CreateDirectory(localMapDir);
            }
            string localManifestPath = Path.Combine(localMapDir, "manifest.json");
            File.WriteAllText(localManifestPath, manifest.ToJson());
            MapAssetManager.Log($"[MapDistributionClient] Saved manifest locally to {localManifestPath}");

            var missingHashes = MapAssetManager.GetMissingHashes(manifest.Files.Values);
            MapAssetManager.Log($"[MapDistributionClient] Missing {missingHashes.Count} of {manifest.Files.Count} hashes.");

            if (missingHashes.Count > 0)
            {
                string hostBaseUrl = $"http://{hostIp}:{port}";
                try
                {
                    var distClient = new Realm.Shared.Distribution.DistributionClient(hostBaseUrl);
                    var seeders = new List<Realm.Shared.Distribution.SeederNodeDto>();

                    await distClient.DownloadMissingAssetsMultiThreadedAsync(
                        manifest,
                        MapAssetManager.Storage,
                        seeders,
                        fallbackHostUrl: hostBaseUrl,
                        progressCallback: p =>
                        {
                            progressCallback?.Invoke(p);
                            DownloadProgressChanged?.Invoke(p);
                        },
                        maximumConcurrency: 6);
                }
                catch (Exception casEx)
                {
                    MapAssetManager.Log($"[MapDistributionClient] CAS download exception: {casEx.Message}. Will attempt fallback.");
                }

                var remainingMissing = MapAssetManager.GetMissingHashes(manifest.Files.Values);
                if (remainingMissing.Count > 0 && LobbyManager.Instance != null && !string.IsNullOrEmpty(LobbyManager.Instance.RegistryServerUrl))
                {
                    try
                    {
                        string registryUrl = LobbyManager.Instance.RegistryServerUrl.TrimEnd('/');
                        var regDistClient = new Realm.Shared.Distribution.DistributionClient(registryUrl, _httpClient);
                        var seeders = await regDistClient.GetActiveSeedersAsync();
                        await regDistClient.DownloadMissingAssetsMultiThreadedAsync(
                            manifest,
                            MapAssetManager.Storage,
                            seeders,
                            fallbackHostUrl: registryUrl,
                            progressCallback: p =>
                            {
                                progressCallback?.Invoke(p);
                                DownloadProgressChanged?.Invoke(p);
                            },
                            maximumConcurrency: 6);
                    }
                    catch (Exception regEx)
                    {
                        MapAssetManager.Log($"[MapDistributionClient] Registry CAS fallback error: {regEx.Message}");
                    }
                    remainingMissing = MapAssetManager.GetMissingHashes(manifest.Files.Values);
                }

                if (remainingMissing.Count > 0)
                {
                    MapAssetManager.LogErr($"[MapDistributionClient] Failed to download {remainingMissing.Count} missing assets from host or registry.");
                    return false;
                }
            }
            else
            {
                progressCallback?.Invoke(1.0f);
                DownloadProgressChanged?.Invoke(1.0f);
            }

            MapAssetManager.ExtractManifestFiles(manifest, localMapDir, isP2P: false);
            AssetIndexService.Instance.RegisterManifest(manifest, localManifestPath, isP2P: false);
            MapAssetManager.Log("[MapDistributionClient] Map distribution client handshake completed successfully.");
            return true;
        }
        catch (Exception ex)
        {
            MapAssetManager.LogErr($"[MapDistributionClient] Handshake exception: {ex.Message}");
            return false;
        }
    }

    public async Task<bool> DownloadMapPackageFromRegistryAsync(
        string mapId,
        string registryServerUrl,
        Action<float>? progressCallback = null,
        System.Threading.CancellationToken cancellationToken = default)
    {
        try
        {
            string baseUrl = registryServerUrl.TrimEnd('/');
            var distClient = new Realm.Shared.Distribution.DistributionClient(baseUrl, _httpClient);
            var manifest = await distClient.GetManifestAsync(mapId, cancellationToken);
            if (manifest == null || manifest.Files == null || manifest.Files.Count == 0)
            {
                MapAssetManager.LogErr($"[MapDistributionClient] Failed to load manifest for map '{mapId}' from {baseUrl}");
                return false;
            }

            string version = !string.IsNullOrWhiteSpace(manifest.Version) ? manifest.Version.Trim() : "1.0.0";
            string manifestBlake3 = MapAssetManager.ComputeManifestBlake3(manifest);
            string localMapDir = MapAssetManager.GetMapDirectory(manifest.MapName, version, manifestBlake3, false);
            if (!Directory.Exists(localMapDir))
            {
                Directory.CreateDirectory(localMapDir);
            }

            string localManifestPath = Path.Combine(localMapDir, "manifest.json");
            File.WriteAllText(localManifestPath, manifest.ToJson());

            var seeders = await distClient.GetActiveSeedersAsync(cancellationToken);
            bool success = await distClient.DownloadMissingAssetsMultiThreadedAsync(
                manifest,
                MapAssetManager.Storage,
                seeders,
                fallbackHostUrl: baseUrl,
                progressCallback: progressCallback,
                maximumConcurrency: 6,
                cancellationToken: cancellationToken);

            if (success)
            {
                MapAssetManager.ExtractManifestFiles(manifest, localMapDir, isP2P: false);
                AssetIndexService.Instance.RegisterManifest(manifest, localManifestPath, isP2P: false);
                MapAssetManager.Log($"[MapDistributionClient] Map '{mapId}' successfully downloaded from registry and indexed.");
            }

            return success;
        }
        catch (Exception ex)
        {
            MapAssetManager.LogErr($"[MapDistributionClient] Exception downloading map '{mapId}' from registry: {ex.Message}");
            return false;
        }
    }
}
