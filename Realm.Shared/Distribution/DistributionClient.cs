using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Realm.Shared.Metadata;

namespace Realm.Shared.Distribution;

public class DistributionClient
{
    private readonly HttpClient _httpClient;
    private readonly string _registryServerUrl;
    private readonly TokenBucketThrottle? _throttle;

    public DistributionClient(string? registryServerUrl = null, HttpClient? httpClient = null, TokenBucketThrottle? throttle = null)
    {
        _registryServerUrl = (string.IsNullOrWhiteSpace(registryServerUrl) ? ServersConfigHelper.GetDefaultServerUrl() : registryServerUrl).TrimEnd('/');
        _httpClient = httpClient ?? new HttpClient();
        _throttle = throttle;
    }

    public async Task<List<SeederNodeDto>> GetActiveSeedersAsync(CancellationToken cancellationToken = default)
    {
        string url = $"{_registryServerUrl}/api/seeders";
        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new List<SeederNodeDto>();
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            var seeders = JsonSerializer.Deserialize<List<SeederNodeDto>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return seeders ?? new List<SeederNodeDto>();
        }
        catch
        {
            return new List<SeederNodeDto>();
        }
    }

    public async Task<MapPublishResponseDto> PublishManifestAsync(
        MapManifest manifest,
        string? adminBypassToken = null,
        CancellationToken cancellationToken = default)
    {
        string url = $"{_registryServerUrl}/api/manifests";
        var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(manifest.ToJson(), Encoding.UTF8, "application/json")
        };

        if (!string.IsNullOrWhiteSpace(adminBypassToken))
        {
            request.Headers.Add("X-Admin-Bypass", adminBypassToken);
        }

        try
        {
            var response = await _httpClient.SendAsync(request, cancellationToken);
            string responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                return new MapPublishResponseDto
                {
                    Success = false,
                    Status = "Failed",
                    Message = $"Publish failed with HTTP {(int)response.StatusCode}: {responseJson}"
                };
            }

            var publishResponse = JsonSerializer.Deserialize<MapPublishResponseDto>(responseJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return publishResponse ?? new MapPublishResponseDto { Success = true, Status = "Published" };
        }
        catch (Exception exception)
        {
            return new MapPublishResponseDto
            {
                Success = false,
                Status = "Error",
                Message = exception.Message
            };
        }
    }

    public async Task<AssetUploadResponseDto> UploadAssetAsync(
        string targetServerBaseUrl,
        byte[] assetBytes,
        string extensionOrPath,
        string? metadataHeadersJson = null,
        string? authorPublicKey = null,
        string? authorSignature = null,
        CancellationToken cancellationToken = default)
    {
        string extension = Path.GetExtension(extensionOrPath).ToLowerInvariant();
        string canonicalBlake3 = RealmMetadataHelper.ComputeBlake3(assetBytes, extension);
        string normalizedHash = ContentAddressableStorage.NormalizeBlake3Hash(canonicalBlake3);

        string url = $"{targetServerBaseUrl.TrimEnd('/')}/api/assets/{normalizedHash}";

        using var content = new MultipartFormDataContent();
        var byteContent = new ByteArrayContent(assetBytes);
        byteContent.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        content.Add(byteContent, "file", $"{normalizedHash}{extension}");

        if (!string.IsNullOrWhiteSpace(metadataHeadersJson))
        {
            content.Add(new StringContent(metadataHeadersJson, Encoding.UTF8), "metadata");
        }

        if (!string.IsNullOrWhiteSpace(authorPublicKey))
        {
            content.Add(new StringContent(authorPublicKey, Encoding.UTF8), "authorPublicKey");
        }

        if (!string.IsNullOrWhiteSpace(authorSignature))
        {
            content.Add(new StringContent(authorSignature, Encoding.UTF8), "authorSignature");
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = content;

        if (!string.IsNullOrEmpty(extension))
        {
            request.Headers.TryAddWithoutValidation("X-File-Extension", extension);
        }

        if (!string.IsNullOrWhiteSpace(metadataHeadersJson))
        {
            request.Headers.TryAddWithoutValidation("X-Asset-Metadata", Convert.ToBase64String(Encoding.UTF8.GetBytes(metadataHeadersJson)));
        }

        if (!string.IsNullOrWhiteSpace(authorPublicKey))
        {
            request.Headers.TryAddWithoutValidation("X-Author-Public-Key", authorPublicKey);
        }

        if (!string.IsNullOrWhiteSpace(authorSignature))
        {
            request.Headers.TryAddWithoutValidation("X-Author-Signature", authorSignature);
        }

        if (_throttle != null)
        {
            await _throttle.ConsumeAsync(assetBytes.Length, cancellationToken);
        }

        var response = await _httpClient.SendAsync(request, cancellationToken);
        string responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            return new AssetUploadResponseDto
            {
                Success = false,
                Message = $"Upload rejected: HTTP {(int)response.StatusCode} - {responseJson}",
                Blake3Hash = normalizedHash
            };
        }

        try
        {
            var result = JsonSerializer.Deserialize<AssetUploadResponseDto>(responseJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return result ?? new AssetUploadResponseDto { Success = true, Blake3Hash = normalizedHash };
        }
        catch
        {
            return new AssetUploadResponseDto { Success = true, Blake3Hash = normalizedHash };
        }
    }

    public async Task<bool> DownloadMissingAssetsMultiThreadedAsync(
        MapManifest manifest,
        ContentAddressableStorage targetStorage,
        List<SeederNodeDto>? availableSeeders = null,
        string? fallbackHostUrl = null,
        Action<float>? progressCallback = null,
        int maximumConcurrency = 4,
        CancellationToken cancellationToken = default)
    {
        var missingHashes = new List<(string VirtualPath, string AssetKey, string NormalizedHash)>();

        foreach (var filePair in manifest.Files)
        {
            string assetKey = filePair.Value;
            string normalizedHash = ContentAddressableStorage.NormalizeBlake3Hash(assetKey);

            if (!targetStorage.HasAsset(normalizedHash))
            {
                missingHashes.Add((filePair.Key, assetKey, normalizedHash));
            }
        }

        if (missingHashes.Count == 0)
        {
            progressCallback?.Invoke(1.0f);
            return true;
        }

        var seeders = availableSeeders ?? await GetActiveSeedersAsync(cancellationToken);
        int totalMissing = missingHashes.Count;
        int completedCount = 0;

        using var semaphore = new SemaphoreSlim(maximumConcurrency);
        var downloadTasks = missingHashes.Select(async item =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                bool downloaded = await DownloadSingleAssetWithRetriesAsync(
                    item.NormalizedHash,
                    item.AssetKey,
                    targetStorage,
                    seeders,
                    fallbackHostUrl,
                    cancellationToken);

                if (downloaded)
                {
                    int currentCompleted = Interlocked.Increment(ref completedCount);
                    float progress = (float)currentCompleted / totalMissing;
                    progressCallback?.Invoke(progress);
                }

                return downloaded;
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        var results = await Task.WhenAll(downloadTasks);
        return results.All(success => success);
    }

    private int _roundRobinCounter = 0;
    private static readonly ConcurrentDictionary<string, SeederCircuitState> SeederCircuitBreakers = new(StringComparer.OrdinalIgnoreCase);

    public static SeederCircuitState GetCircuitState(string baseUrl)
    {
        return SeederCircuitBreakers.GetOrAdd(baseUrl.TrimEnd('/'), _ => new SeederCircuitState());
    }

    public static void ResetAllCircuitBreakers()
    {
        foreach (var pair in SeederCircuitBreakers)
        {
            pair.Value.Reset();
        }
    }

    private async Task<bool> DownloadSingleAssetWithRetriesAsync(
        string normalizedHash,
        string assetKey,
        ContentAddressableStorage targetStorage,
        List<SeederNodeDto> seeders,
        string? fallbackHostUrl,
        CancellationToken cancellationToken)
    {
        var candidateSeeders = seeders
            .Where(s => DistributionSharding.SeederAcceptsHash(s.SeederId, s.CapacityPercentage, normalizedHash))
            .ToList();

        var prioritizedUrls = new List<string>();

        if (candidateSeeders.Count > 0)
        {
            int startIndex = Interlocked.Increment(ref _roundRobinCounter) % candidateSeeders.Count;
            for (int i = 0; i < candidateSeeders.Count; i++)
            {
                int index = (startIndex + i) % candidateSeeders.Count;
                var seeder = candidateSeeders[index];
                prioritizedUrls.Add($"http://{seeder.IP}:{seeder.Port}");
            }
        }

        foreach (var seeder in seeders)
        {
            string url = $"http://{seeder.IP}:{seeder.Port}";
            if (!prioritizedUrls.Contains(url))
            {
                prioritizedUrls.Add(url);
            }
        }

        if (!string.IsNullOrEmpty(fallbackHostUrl) && !prioritizedUrls.Contains(fallbackHostUrl))
        {
            prioritizedUrls.Add(fallbackHostUrl.TrimEnd('/'));
        }

        if (!prioritizedUrls.Contains(_registryServerUrl))
        {
            prioritizedUrls.Add(_registryServerUrl);
        }

        const int maxCycles = 3;
        for (int cycle = 0; cycle < maxCycles; cycle++)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

            if (cycle > 0)
            {
                int baseDelayMs = 150 * (int)Math.Pow(2, cycle - 1);
                int jitterMs = Random.Shared.Next(15, 75);
                await Task.Delay(baseDelayMs + jitterMs, cancellationToken);
            }

            DateTime nowUtc = DateTime.UtcNow;

            var availableUrls = prioritizedUrls
                .Where(url => !GetCircuitState(url).IsOpen(nowUtc))
                .ToList();

            if (availableUrls.Count == 0)
            {
                availableUrls = prioritizedUrls;
            }

            foreach (string baseUrl in availableUrls)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return false;
                }

                var circuit = GetCircuitState(baseUrl);
                try
                {
                    string assetUrl = $"{baseUrl}/api/assets/{normalizedHash}";
                    var response = await _httpClient.GetAsync(assetUrl, cancellationToken);
                    if (response.IsSuccessStatusCode)
                    {
                        byte[] downloadedBytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
                        if (_throttle != null)
                        {
                            await _throttle.ConsumeAsync(downloadedBytes.Length, cancellationToken);
                        }

                        string extension = Path.GetExtension(assetKey).ToLowerInvariant();
                        string computedBlake3 = RealmMetadataHelper.ComputeBlake3(downloadedBytes, extension);
                        string computedNormalized = ContentAddressableStorage.NormalizeBlake3Hash(computedBlake3);

                        if (!string.Equals(computedNormalized, normalizedHash, StringComparison.OrdinalIgnoreCase))
                        {
                            circuit.RecordFailure(DateTime.UtcNow);
                            continue;
                        }

                        string? metadataHeader = null;
                        if (response.Headers.TryGetValues("X-Asset-Metadata", out var metaValues))
                        {
                            string? rawHeader = metaValues.FirstOrDefault();
                            if (!string.IsNullOrWhiteSpace(rawHeader))
                            {
                                try
                                {
                                    byte[] metaBytes = Convert.FromBase64String(rawHeader);
                                    metadataHeader = Encoding.UTF8.GetString(metaBytes);
                                }
                                catch
                                {
                                    metadataHeader = rawHeader;
                                }
                            }
                        }

                        var storeResult = targetStorage.StoreAsset(downloadedBytes, extension, metadataHeader);
                        if (storeResult.Success)
                        {
                            circuit.RecordSuccess();
                            return true;
                        }
                    }
                    else if ((int)response.StatusCode >= 500 || response.StatusCode == System.Net.HttpStatusCode.RequestTimeout)
                    {
                        circuit.RecordFailure(DateTime.UtcNow);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return false;
                }
                catch
                {
                    circuit.RecordFailure(DateTime.UtcNow);
                }
            }
        }

        return false;
    }

    public async Task<PublishMapInitiateResponse> InitiatePublishAsync(PublishMapInitiateRequest request, CancellationToken cancellationToken = default)
    {
        string url = $"{_registryServerUrl}/api/publish_map/initiate";
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync(url, content, cancellationToken);
            string responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

            var result = JsonSerializer.Deserialize<PublishMapInitiateResponse>(responseJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return result ?? new PublishMapInitiateResponse
            {
                Success = response.IsSuccessStatusCode,
                Message = responseJson
            };
        }
        catch (Exception ex)
        {
            return new PublishMapInitiateResponse
            {
                Success = false,
                Status = "Error",
                Message = ex.Message
            };
        }
    }

    public async Task<PublishMapFinalizeResponse> FinalizePublishAsync(PublishMapFinalizeRequest request, CancellationToken cancellationToken = default)
    {
        string url = $"{_registryServerUrl}/api/publish_map/finalize";
        var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.PostAsync(url, content, cancellationToken);
            string responseJson = await response.Content.ReadAsStringAsync(cancellationToken);

            var result = JsonSerializer.Deserialize<PublishMapFinalizeResponse>(responseJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return result ?? new PublishMapFinalizeResponse
            {
                Success = response.IsSuccessStatusCode,
                Message = responseJson
            };
        }
        catch (Exception ex)
        {
            return new PublishMapFinalizeResponse
            {
                Success = false,
                Status = "Error",
                Message = ex.Message
            };
        }
    }

    public async Task<bool> UploadMissingAssetAsync(
        string hash,
        byte[] fileBytes,
        string fileName,
        string currentUsername,
        string authorPublicKey,
        string signature,
        string mapTitle,
        string mapVersion,
        string? sessionId = null,
        CancellationToken cancellationToken = default)
    {
        string url = $"{_registryServerUrl}/api/publish_map/upload_asset";
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent(hash), "Hash");
        form.Add(new StringContent(signature), "Signature");
        form.Add(new StringContent(currentUsername), "AuthorUsername");
        form.Add(new StringContent(authorPublicKey), "PublicKey");
        form.Add(new StringContent(mapTitle), "MapTitle");
        form.Add(new StringContent(mapVersion), "MapVersion");
        if (!string.IsNullOrEmpty(sessionId))
        {
            form.Add(new StringContent(sessionId), "SessionId");
        }

        var fileContent = new ByteArrayContent(fileBytes);
        form.Add(fileContent, "File", fileName);

        if (_throttle != null)
        {
            await _throttle.ConsumeAsync(fileBytes.Length, cancellationToken);
        }

        try
        {
            var response = await _httpClient.PostAsync(url, form, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<List<ClusterEventDto>> GetClusterEventsAsync(DateTime? sinceUtc = null, int limit = 100, CancellationToken cancellationToken = default)
    {
        string url = $"{_registryServerUrl}/api/cluster/events?limit={limit}";
        if (sinceUtc.HasValue)
        {
            url += $"&sinceUtc={Uri.EscapeDataString(sinceUtc.Value.ToString("o"))}";
        }

        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new List<ClusterEventDto>();
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            var list = JsonSerializer.Deserialize<List<ClusterEventDto>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return list ?? new List<ClusterEventDto>();
        }
        catch
        {
            return new List<ClusterEventDto>();
        }
    }

    public async Task<bool> PostClusterEventAsync(ClusterEventDto evt, CancellationToken cancellationToken = default)
    {
        string url = $"{_registryServerUrl}/api/cluster/event";
        try
        {
            var content = new StringContent(JsonSerializer.Serialize(evt), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(url, content, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<RemoveManifestResponseDto> RemoveManifestVersionAsync(string mapTitle, string mapVersion, string adminPrivateKeyBase64, CancellationToken cancellationToken = default)
    {
        string url = $"{_registryServerUrl}/api/admin/remove_manifest";
        string adminPublicKey = AuthorSignatureHelper.GetPublicKey(adminPrivateKeyBase64);
        string payload = $"remove_manifest:{mapTitle.Trim().ToLowerInvariant()}:{mapVersion.Trim().ToLowerInvariant()}";
        string signature = AuthorSignatureHelper.SignMessage(adminPrivateKeyBase64, payload);
        string bypassToken = AdminBypassAuth.CreateBypassToken(adminPrivateKeyBase64, mapTitle.Trim(), mapVersion.Trim());

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("X-Admin-Bypass", bypassToken);
        request.Headers.Add("X-Admin-PublicKey", adminPublicKey);
        request.Headers.Add("X-Cluster-Signature", signature);

        var body = new AdminRemoveManifestRequest
        {
            MapTitle = mapTitle.Trim(),
            MapVersion = mapVersion.Trim(),
            AdminPublicKey = adminPublicKey,
            Signature = signature
        };
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.SendAsync(request, cancellationToken);
            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<RemoveManifestResponseDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return result ?? new RemoveManifestResponseDto
            {
                Success = response.IsSuccessStatusCode,
                MapTitle = mapTitle,
                MapVersion = mapVersion,
                Message = json
            };
        }
        catch (Exception ex)
        {
            return new RemoveManifestResponseDto
            {
                Success = false,
                MapTitle = mapTitle,
                MapVersion = mapVersion,
                Message = ex.Message
            };
        }
    }

    public async Task<RemoveManifestResponseDto> RemoveAllManifestVersionsAsync(string mapTitle, string adminPrivateKeyBase64, CancellationToken cancellationToken = default)
    {
        string url = $"{_registryServerUrl}/api/admin/remove_manifest";
        string adminPublicKey = AuthorSignatureHelper.GetPublicKey(adminPrivateKeyBase64);
        string payload = $"remove_manifest:{mapTitle.Trim().ToLowerInvariant()}:all";
        string signature = AuthorSignatureHelper.SignMessage(adminPrivateKeyBase64, payload);
        string bypassToken = AdminBypassAuth.CreateBypassToken(adminPrivateKeyBase64, mapTitle.Trim(), "all");

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("X-Admin-Bypass", bypassToken);
        request.Headers.Add("X-Admin-PublicKey", adminPublicKey);
        request.Headers.Add("X-Cluster-Signature", signature);

        var body = new AdminRemoveManifestRequest
        {
            MapTitle = mapTitle.Trim(),
            MapVersion = null,
            AdminPublicKey = adminPublicKey,
            Signature = signature
        };
        request.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");

        try
        {
            var response = await _httpClient.SendAsync(request, cancellationToken);
            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<RemoveManifestResponseDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return result ?? new RemoveManifestResponseDto
            {
                Success = response.IsSuccessStatusCode,
                MapTitle = mapTitle,
                AllVersionsRemoved = true,
                Message = json
            };
        }
        catch (Exception ex)
        {
            return new RemoveManifestResponseDto
            {
                Success = false,
                MapTitle = mapTitle,
                AllVersionsRemoved = true,
                Message = ex.Message
            };
        }
    }

    public async Task<RemoveManifestResponseDto> RemoveManifestAsync(string mapTitle, string? mapVersion, string adminPrivateKeyBase64, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(mapVersion))
        {
            return await RemoveAllManifestVersionsAsync(mapTitle, adminPrivateKeyBase64, cancellationToken);
        }
        return await RemoveManifestVersionAsync(mapTitle, mapVersion, adminPrivateKeyBase64, cancellationToken);
    }

    public async Task<CasPruneResponseDto> PruneServerCasAsync(string adminPrivateKeyBase64, CancellationToken cancellationToken = default)
    {
        string url = $"{_registryServerUrl}/api/admin/prune_cas";
        string bypassToken = AdminBypassAuth.CreateBypassToken(adminPrivateKeyBase64, "admin", "prune_cas");

        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Headers.Add("X-Admin-Bypass", bypassToken);

        try
        {
            var response = await _httpClient.SendAsync(request, cancellationToken);
            string json = await response.Content.ReadAsStringAsync(cancellationToken);

            var result = JsonSerializer.Deserialize<CasPruneResponseDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return result ?? new CasPruneResponseDto
            {
                Success = response.IsSuccessStatusCode,
                Message = json
            };
        }
        catch (Exception ex)
        {
            return new CasPruneResponseDto
            {
                Success = false,
                Message = ex.Message
            };
        }
    }

    public async Task<ClusterStateDigestDto?> GetStateDigestAsync(CancellationToken cancellationToken = default)
    {
        string url = $"{_registryServerUrl}/api/cluster/state_digest";
        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<ClusterStateDigestDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }

    public async Task<ClusterSnapshotDto?> GetSnapshotAsync(CancellationToken cancellationToken = default)
    {
        string url = $"{_registryServerUrl}/api/cluster/snapshot";
        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<ClusterSnapshotDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch
        {
            return null;
        }
    }

    public async Task<ApplySnapshotResponseDto> ApplySnapshotAsync(ClusterSnapshotDto snapshot, string? adminPrivateKeyBase64 = null, CancellationToken cancellationToken = default)
    {
        string url = $"{_registryServerUrl}/api/cluster/snapshot";
        using var request = new HttpRequestMessage(HttpMethod.Post, url);
        request.Content = new StringContent(JsonSerializer.Serialize(snapshot), Encoding.UTF8, "application/json");

        if (!string.IsNullOrWhiteSpace(adminPrivateKeyBase64))
        {
            string bypassToken = AdminBypassAuth.CreateBypassToken(adminPrivateKeyBase64, "cluster", "snapshot");
            request.Headers.Add("X-Admin-Bypass", bypassToken);
        }

        try
        {
            var response = await _httpClient.SendAsync(request, cancellationToken);
            string json = await response.Content.ReadAsStringAsync(cancellationToken);

            var result = JsonSerializer.Deserialize<ApplySnapshotResponseDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return result ?? new ApplySnapshotResponseDto
            {
                Success = response.IsSuccessStatusCode,
                Message = json
            };
        }
        catch (Exception ex)
        {
            return new ApplySnapshotResponseDto
            {
                Success = false,
                Message = ex.Message
            };
        }
    }

    public async Task<MapManifest?> GetManifestAsync(string mapId, CancellationToken cancellationToken = default)
    {
        string url = $"{_registryServerUrl}/api/manifests/{Uri.EscapeDataString(mapId)}";
        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            return MapManifest.LoadFromJson(json);
        }
        catch
        {
            return null;
        }
    }

    public async Task<List<DiscoveryMapDto>> GetDiscoveryMapsAsync(CancellationToken cancellationToken = default)
    {
        string url = $"{_registryServerUrl}/api/discovery/maps";
        try
        {
            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new List<DiscoveryMapDto>();
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            var maps = JsonSerializer.Deserialize<List<DiscoveryMapDto>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return maps ?? new List<DiscoveryMapDto>();
        }
        catch
        {
            return new List<DiscoveryMapDto>();
        }
    }
}
