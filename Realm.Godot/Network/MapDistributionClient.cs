using System;
using System.IO;
using System.Net.Http;
using System.Text;
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
            var manifest = JsonSerializer.Deserialize<MapManifest>(manifestJson);
            if (manifest == null || manifest.Files == null)
            {
                MapAssetManager.LogErr("[MapDistributionClient] Manifest parsing failed.");
                return false;
            }


            string localManifestDir = MapAssetManager.GlobalArchiveDirectory;
            if (!Directory.Exists(localManifestDir))
            {
                Directory.CreateDirectory(localManifestDir);
            }
            string localManifestPath = Path.Combine(localManifestDir, $"{mapName}_manifest.json");
            File.WriteAllText(localManifestPath, manifestJson);
            MapAssetManager.Log($"[MapDistributionClient] Saved manifest locally to {localManifestPath}");


            var missingHashes = MapAssetManager.GetMissingHashes(manifest.Files.Values);
            MapAssetManager.Log($"[MapDistributionClient] Missing {missingHashes.Count} of {manifest.Files.Count} hashes.");

            if (missingHashes.Count > 0)
            {

                string deltaUrl = $"http://{hostIp}:{port}/map/delta";
                MapAssetManager.Log($"[MapDistributionClient] Requesting delta archive from {deltaUrl}");

                var requestMessage = new HttpRequestMessage(HttpMethod.Post, deltaUrl);
                requestMessage.Content = new StringContent(JsonSerializer.Serialize(missingHashes), Encoding.UTF8, "application/json");
                using (var deltaResponse = await _httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead))
                {
                    if (!deltaResponse.IsSuccessStatusCode)
                    {
                        MapAssetManager.LogErr($"[MapDistributionClient] Delta request failed with status: {deltaResponse.StatusCode}");
                        return false;
                    }

                    long? totalBytes = deltaResponse.Content.Headers.ContentLength;
                    MapAssetManager.Log($"[MapDistributionClient] Expected delta size: {totalBytes ?? -1} bytes");

                    string tempDeltaPath = Path.Combine(MapAssetManager.GlobalArchiveDirectory, Guid.NewGuid().ToString() + "_download_delta.7z");
                    
                    using (var responseStream = await deltaResponse.Content.ReadAsStreamAsync())
                    using (var fs = new FileStream(tempDeltaPath, FileMode.Create, System.IO.FileAccess.Write, FileShare.None, 8192, true))
                    {
                        byte[] buffer = new byte[8192];
                        long totalRead = 0;
                        int bytesRead;

                        while ((bytesRead = await responseStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fs.WriteAsync(buffer, 0, bytesRead);
                            totalRead += bytesRead;

                            if (totalBytes.HasValue && totalBytes.Value > 0)
                            {
                                float progress = (float)totalRead / totalBytes.Value;
                                progressCallback?.Invoke(progress);
                                DownloadProgressChanged?.Invoke(progress);
                            }
                        }
                    }

                    MapAssetManager.Log("[MapDistributionClient] Delta download complete. Ingesting delta files into global archive...");
                    

                    MapAssetManager.IngestDeltaArchive(tempDeltaPath);


                    try { File.Delete(tempDeltaPath); } catch { }
                }
            }
            else
            {

                progressCallback?.Invoke(1.0f);
                DownloadProgressChanged?.Invoke(1.0f);
            }

            MapAssetManager.Log("[MapDistributionClient] Map distribution client delta handshake completed successfully.");
            return true;
        }
        catch (Exception ex)
        {
            MapAssetManager.LogErr($"[MapDistributionClient] Handshake exception: {ex.Message}");
            return false;
        }
    }
}

