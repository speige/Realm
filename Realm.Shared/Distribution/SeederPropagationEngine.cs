using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Realm.Shared.Metadata;

namespace Realm.Shared.Distribution;

public class SeederPropagationEngine
{
    private readonly string _seederId;
    private readonly int _capacityPercentage;
    private readonly ContentAddressableStorage _storage;
    private readonly string _registryServerUrl;
    private readonly HttpClient _httpClient;
    private readonly TokenBucketThrottle? _throttle;
    private CancellationTokenSource? _cancellationTokenSource;

    public SeederPropagationEngine(
        string seederId,
        int capacityPercentage,
        ContentAddressableStorage storage,
        string registryServerUrl = "http://127.0.0.1:5000",
        HttpClient? httpClient = null,
        TokenBucketThrottle? throttle = null)
    {
        _seederId = seederId;
        _capacityPercentage = capacityPercentage;
        _storage = storage;
        _registryServerUrl = registryServerUrl.TrimEnd('/');
        _httpClient = httpClient ?? new HttpClient();
        _throttle = throttle;
    }

    public async Task<int> RunPropagationCycleAsync(CancellationToken cancellationToken = default)
    {
        if (!_storage.CheckFreeDiskSpaceAcceptingUploads())
        {
            return 0;
        }

        var seeders = await FetchOtherSeedersAsync(cancellationToken);
        if (seeders.Count == 0)
        {
            return 0;
        }

        int propagatedCount = 0;

        foreach (var peerSeeder in seeders)
        {
            if (cancellationToken.IsCancellationRequested || !_storage.CheckFreeDiskSpaceAcceptingUploads())
            {
                break;
            }

            if (string.Equals(peerSeeder.SeederId, _seederId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var peerHashes = await FetchPeerCatalogAsync(peerSeeder.IP, peerSeeder.Port, cancellationToken);
            if (peerHashes.Count == 0)
            {
                continue;
            }

            var wantedHashes = peerHashes
                .Where(hash => DistributionSharding.SeederAcceptsHash(_seederId, _capacityPercentage, hash))
                .Where(hash => !_storage.HasAsset(hash))
                .ToList();

            foreach (string hash in wantedHashes)
            {
                if (cancellationToken.IsCancellationRequested || !_storage.CheckFreeDiskSpaceAcceptingUploads())
                {
                    break;
                }

                bool downloaded = await DownloadAssetFromPeerAsync(peerSeeder.IP, peerSeeder.Port, hash, cancellationToken);
                if (downloaded)
                {
                    propagatedCount++;
                }
            }
        }

        return propagatedCount;
    }

    public void StartBackgroundWorker(TimeSpan interval, CancellationToken cancellationToken = default)
    {
        _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _cancellationTokenSource.Token;

        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(interval, token);
                    await RunPropagationCycleAsync(token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch
                {
                }
            }
        }, token);
    }

    public void Stop()
    {
        _cancellationTokenSource?.Cancel();
    }

    private async Task<List<SeederNodeDto>> FetchOtherSeedersAsync(CancellationToken cancellationToken)
    {
        try
        {
            string url = $"{_registryServerUrl}/api/seeders";
            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new List<SeederNodeDto>();
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            var list = JsonSerializer.Deserialize<List<SeederNodeDto>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return list?.Where(s => !string.Equals(s.SeederId, _seederId, StringComparison.OrdinalIgnoreCase)).ToList() ?? new List<SeederNodeDto>();
        }
        catch
        {
            return new List<SeederNodeDto>();
        }
    }

    private async Task<List<string>> FetchPeerCatalogAsync(string ip, int port, CancellationToken cancellationToken)
    {
        try
        {
            string url = $"http://{ip}:{port}/api/seeders/catalog";
            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new List<string>();
            }

            string json = await response.Content.ReadAsStringAsync(cancellationToken);
            var catalog = JsonSerializer.Deserialize<SeederCatalogResponseDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return catalog?.AssetHashes ?? new List<string>();
        }
        catch
        {
            return new List<string>();
        }
    }

    private async Task<bool> DownloadAssetFromPeerAsync(string ip, int port, string hash, CancellationToken cancellationToken)
    {
        try
        {
            string url = $"http://{ip}:{port}/api/assets/{hash}";
            var response = await _httpClient.GetAsync(url, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return false;
            }

            byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
            if (_throttle != null)
            {
                await _throttle.ConsumeAsync(bytes.Length, cancellationToken);
            }

            string extension = string.Empty;
            if (response.Content.Headers.ContentDisposition?.FileName != null)
            {
                extension = System.IO.Path.GetExtension(response.Content.Headers.ContentDisposition.FileName);
            }

            string canonicalBlake3 = RealmMetadataHelper.ComputeBlake3(bytes, extension);
            string normalizedHash = ContentAddressableStorage.NormalizeBlake3Hash(canonicalBlake3);

            if (!string.Equals(normalizedHash, hash, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string? metadataHeader = null;
            if (response.Headers.TryGetValues("X-Asset-Metadata", out var metaValues))
            {
                metadataHeader = metaValues.FirstOrDefault();
            }

            var result = _storage.StoreAsset(bytes, extension, metadataHeader);
            return result.Success;
        }
        catch
        {
            return false;
        }
    }
}
