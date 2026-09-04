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

    public DistributionClient(string registryServerUrl = "http://127.0.0.1:5000", HttpClient? httpClient = null, TokenBucketThrottle? throttle = null)
    {
        _registryServerUrl = registryServerUrl.TrimEnd('/');
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

        foreach (string baseUrl in prioritizedUrls)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return false;
            }

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
                        continue;
                    }

                    string? metadataHeader = null;
                    if (response.Headers.TryGetValues("X-Asset-Metadata", out var metaValues))
                    {
                        metadataHeader = metaValues.FirstOrDefault();
                    }

                    var storeResult = targetStorage.StoreAsset(downloadedBytes, extension, metadataHeader);
                    if (storeResult.Success)
                    {
                        return true;
                    }
                }
            }
            catch
            {
            }
        }

        return false;
    }
}
