using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace Realm.Shared.Distribution;

public class DistributionServer
{
    private readonly ContentAddressableStorage _storage;
    private readonly string _seederId;
    private readonly int _capacityPercentage;
    private readonly TokenBucketThrottle? _throttle;
    private readonly string? _adminPublicKeyBase64;
    private readonly Func<string, string, bool>? _greenlightChecker;
    private HttpListener? _listener;
    private bool _isRunning;
    private CancellationTokenSource? _cancellationTokenSource;

    public string SeederId => _seederId;
    public int CapacityPercentage => _capacityPercentage;
    public ContentAddressableStorage Storage => _storage;
    public int BoundPort { get; private set; }

    public DistributionServer(
        ContentAddressableStorage storage,
        string seederId,
        int capacityPercentage = 100,
        TokenBucketThrottle? throttle = null,
        string? adminPublicKeyBase64 = null,
        Func<string, string, bool>? greenlightChecker = null)
    {
        _storage = storage;
        _seederId = seederId;
        _capacityPercentage = capacityPercentage;
        _throttle = throttle;
        _adminPublicKeyBase64 = adminPublicKeyBase64;
        _greenlightChecker = greenlightChecker;
    }

    public void Start(int port)
    {
        _cancellationTokenSource = new CancellationTokenSource();
        _listener = new HttpListener();
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Prefixes.Add($"http://localhost:{port}/");

        try
        {
            _listener.Start();
            BoundPort = port;
            _isRunning = true;
            Task.Run(() => ListenLoopAsync(_listener, _cancellationTokenSource.Token));
        }
        catch (HttpListenerException)
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();
            BoundPort = port;
            _isRunning = true;
            Task.Run(() => ListenLoopAsync(_listener, _cancellationTokenSource.Token));
        }
    }

    public void Stop()
    {
        _isRunning = false;
        _cancellationTokenSource?.Cancel();

        try
        {
            if (_listener != null && _listener.IsListening)
            {
                _listener.Stop();
                _listener.Close();
            }
        }
        catch
        {
        }
    }

    private async Task ListenLoopAsync(HttpListener listener, CancellationToken cancellationToken)
    {
        while (_isRunning && !cancellationToken.IsCancellationRequested)
        {
            try
            {
                var context = await listener.GetContextAsync();
                _ = Task.Run(() => HandleRequestAsync(context), cancellationToken);
            }
            catch
            {
                if (!_isRunning)
                {
                    break;
                }
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        string path = request.Url?.LocalPath.TrimEnd('/') ?? string.Empty;
        string method = request.HttpMethod.ToUpperInvariant();

        try
        {
            if (path.StartsWith("/api/assets/", StringComparison.OrdinalIgnoreCase))
            {
                string hash = path.Substring("/api/assets/".Length);
                await HandleAssetEndpointAsync(context, method, hash);
                return;
            }

            if (path.Equals("/api/manifests", StringComparison.OrdinalIgnoreCase) || path.StartsWith("/api/manifests/", StringComparison.OrdinalIgnoreCase))
            {
                await HandleManifestEndpointAsync(context, method, path);
                return;
            }

            if (path.Equals("/api/seeders/catalog", StringComparison.OrdinalIgnoreCase) && method == "GET")
            {
                await HandleCatalogEndpointAsync(response);
                return;
            }

            if (path.Equals("/api/seeders/bloom_headers", StringComparison.OrdinalIgnoreCase) && method == "GET")
            {
                await HandleBloomHeadersEndpointAsync(response);
                return;
            }

            if (path.Equals("/api/seeders/sync_headers", StringComparison.OrdinalIgnoreCase) && method == "POST")
            {
                await HandleHeaderSyncEndpointAsync(request, response);
                return;
            }

            response.StatusCode = (int)HttpStatusCode.NotFound;
            response.Close();
        }
        catch (Exception exception)
        {
            try
            {
                response.StatusCode = (int)HttpStatusCode.InternalServerError;
                byte[] errorBytes = Encoding.UTF8.GetBytes(exception.Message);
                await response.OutputStream.WriteAsync(errorBytes, 0, errorBytes.Length);
                response.Close();
            }
            catch
            {
            }
        }
    }

    private async Task HandleAssetEndpointAsync(HttpListenerContext context, string method, string hash)
    {
        var request = context.Request;
        var response = context.Response;
        string normalizedHash = ContentAddressableStorage.NormalizeBlake3Hash(hash);

        if (method == "HEAD")
        {
            if (_storage.HasAsset(normalizedHash))
            {
                response.StatusCode = (int)HttpStatusCode.OK;
            }
            else
            {
                response.StatusCode = (int)HttpStatusCode.NotFound;
            }
            response.Close();
            return;
        }

        if (method == "GET")
        {
            string? filePath = _storage.FindAssetFilePath(normalizedHash);
            if (filePath == null || !File.Exists(filePath))
            {
                response.StatusCode = (int)HttpStatusCode.NotFound;
                response.Close();
                return;
            }

            byte[] fileBytes = _storage.GetAssetBytes(normalizedHash) ?? Array.Empty<byte>();
            string? metadataJson = _storage.GetAssetMetadata(normalizedHash);

            if (_throttle != null)
            {
                await _throttle.ConsumeAsync(fileBytes.Length);
            }

            response.ContentType = "application/octet-stream";
            response.ContentLength64 = fileBytes.Length;
            response.AddHeader("Content-Disposition", $"attachment; filename=\"{Path.GetFileName(filePath)}\"");

            if (!string.IsNullOrWhiteSpace(metadataJson))
            {
                response.AddHeader("X-Asset-Metadata", Convert.ToBase64String(Encoding.UTF8.GetBytes(metadataJson)));
            }

            response.StatusCode = (int)HttpStatusCode.OK;
            await response.OutputStream.WriteAsync(fileBytes, 0, fileBytes.Length);
            response.Close();
            return;
        }

        if (method == "POST")
        {
            if (!DistributionSharding.SeederAcceptsHash(_seederId, _capacityPercentage, normalizedHash))
            {
                response.StatusCode = (int)HttpStatusCode.Forbidden;
                byte[] rejectionBytes = Encoding.UTF8.GetBytes("Upload rejected: Asset does not match seeder sharding partition.");
                await response.OutputStream.WriteAsync(rejectionBytes, 0, rejectionBytes.Length);
                response.Close();
                return;
            }

            if (!_storage.CheckFreeDiskSpaceAcceptingUploads())
            {
                response.StatusCode = 507;
                byte[] spaceBytes = Encoding.UTF8.GetBytes("Upload rejected: Insufficient disk space (<10% available).");
                await response.OutputStream.WriteAsync(spaceBytes, 0, spaceBytes.Length);
                response.Close();
                return;
            }

            byte[] assetBytes;
            string? metadataJson = DecodeMetadataHeader(request.Headers["X-Asset-Metadata"]);
            string? authorPublicKey = request.Headers["X-Author-Public-Key"];
            string? authorSignature = request.Headers["X-Author-Signature"];
            string? fileExtension = request.Headers["X-File-Extension"];

            if (request.ContentType != null && request.ContentType.StartsWith("multipart/form-data", StringComparison.OrdinalIgnoreCase))
            {
                var parsedForm = await ParseMultipartFormAsync(request);
                assetBytes = parsedForm.FileBytes ?? Array.Empty<byte>();
                metadataJson ??= parsedForm.MetadataJson;
                authorPublicKey ??= parsedForm.AuthorPublicKey;
                authorSignature ??= parsedForm.AuthorSignature;
                if (!string.IsNullOrEmpty(parsedForm.FileName))
                {
                    string parsedExt = Path.GetExtension(parsedForm.FileName);
                    if (!string.IsNullOrEmpty(parsedExt))
                    {
                        fileExtension ??= parsedExt;
                    }
                }
            }
            else
            {
                using var memoryStream = new MemoryStream();
                await request.InputStream.CopyToAsync(memoryStream);
                assetBytes = memoryStream.ToArray();
            }

            fileExtension ??= ".bin";

            var storeResult = _storage.StoreAsset(assetBytes, fileExtension, metadataJson, authorPublicKey, authorSignature);

            var responseDto = new AssetUploadResponseDto
            {
                Success = storeResult.Success,
                Message = storeResult.Message,
                Deduplicated = storeResult.Deduplicated,
                Merged = storeResult.Merged,
                Blake3Hash = storeResult.Blake3Hash
            };

            byte[] jsonBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(responseDto));
            response.ContentType = "application/json";
            response.StatusCode = storeResult.Success ? (int)HttpStatusCode.OK : (int)HttpStatusCode.BadRequest;
            await response.OutputStream.WriteAsync(jsonBytes, 0, jsonBytes.Length);
            response.Close();
            return;
        }

        response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
        response.Close();
    }

    private async Task HandleManifestEndpointAsync(HttpListenerContext context, string method, string path)
    {
        var request = context.Request;
        var response = context.Response;

        if (method == "GET")
        {
            string mapId = path.Length > "/api/manifests/".Length ? path.Substring("/api/manifests/".Length) : string.Empty;
            string manifestFolder = Path.Combine(_storage.RootDirectory, "manifests");
            string manifestPath = Path.Combine(manifestFolder, $"{mapId}_manifest.json");

            if (!File.Exists(manifestPath))
            {
                manifestPath = Path.Combine(manifestFolder, $"{mapId}.json");
            }

            if (!File.Exists(manifestPath))
            {
                response.StatusCode = (int)HttpStatusCode.NotFound;
                response.Close();
                return;
            }

            byte[] manifestBytes = await File.ReadAllBytesAsync(manifestPath);
            response.ContentType = "application/json";
            response.StatusCode = (int)HttpStatusCode.OK;
            await response.OutputStream.WriteAsync(manifestBytes, 0, manifestBytes.Length);
            response.Close();
            return;
        }

        if (method == "POST")
        {
            using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
            string manifestJson = await reader.ReadToEndAsync();
            var manifest = MapManifest.LoadFromJson(manifestJson);

            if (manifest == null || string.IsNullOrWhiteSpace(manifest.MapName))
            {
                response.StatusCode = (int)HttpStatusCode.BadRequest;
                byte[] error = Encoding.UTF8.GetBytes("Invalid manifest payload.");
                await response.OutputStream.WriteAsync(error, 0, error.Length);
                response.Close();
                return;
            }

            bool isGreenlit = _greenlightChecker?.Invoke(manifest.MapName, manifest.Version) ?? false;
            string? bypassToken = request.Headers["X-Admin-Bypass"];

            if (!isGreenlit)
            {
                bool bypassValid = !string.IsNullOrEmpty(_adminPublicKeyBase64) &&
                                   AdminBypassAuth.VerifyBypassToken(_adminPublicKeyBase64, manifest.MapName, manifest.Version, bypassToken);

                if (!bypassValid)
                {
                    response.StatusCode = (int)HttpStatusCode.Forbidden;
                    var forbiddenDto = new MapPublishResponseDto
                    {
                        Success = false,
                        Status = "GreenlightRequired",
                        Message = "Map does not have sufficient greenlight metrics and no valid admin bypass token was provided."
                    };
                    byte[] forbiddenBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(forbiddenDto));
                    response.ContentType = "application/json";
                    await response.OutputStream.WriteAsync(forbiddenBytes, 0, forbiddenBytes.Length);
                    response.Close();
                    return;
                }
            }

            string manifestDirectory = Path.Combine(_storage.RootDirectory, "manifests");
            Directory.CreateDirectory(manifestDirectory);
            string savedManifestPath = Path.Combine(manifestDirectory, $"{manifest.MapName}_manifest.json");
            await File.WriteAllTextAsync(savedManifestPath, manifestJson);

            var missingHashes = new List<string>();
            foreach (var filePair in manifest.Files)
            {
                string hash = ContentAddressableStorage.NormalizeBlake3Hash(filePair.Value);
                if (!_storage.HasAsset(hash))
                {
                    missingHashes.Add(hash);
                }
            }

            var publishResponse = new MapPublishResponseDto
            {
                Success = true,
                Status = "Published",
                MapId = $"{manifest.MapName}_{manifest.Version}",
                MissingAssetHashes = missingHashes
            };

            byte[] responseBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(publishResponse));
            response.ContentType = "application/json";
            response.StatusCode = (int)HttpStatusCode.OK;
            await response.OutputStream.WriteAsync(responseBytes, 0, responseBytes.Length);
            response.Close();
            return;
        }

        response.StatusCode = (int)HttpStatusCode.MethodNotAllowed;
        response.Close();
    }

    private async Task HandleCatalogEndpointAsync(HttpListenerResponse response)
    {
        var hashes = _storage.GetAllStoredHashes().ToList();
        var catalog = new SeederCatalogResponseDto
        {
            SeederId = _seederId,
            CapacityPercentage = _capacityPercentage,
            AssetHashes = hashes
        };

        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(catalog));
        response.ContentType = "application/json";
        response.StatusCode = (int)HttpStatusCode.OK;
        await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        response.Close();
    }

    private async Task HandleBloomHeadersEndpointAsync(HttpListenerResponse response)
    {
        var hashes = _storage.GetAllStoredHashes().ToList();
        var bloomFilter = new BloomFilter(Math.Max(100, hashes.Count), 0.01);

        foreach (string hash in hashes)
        {
            string? metadataJson = _storage.GetAssetMetadata(hash);
            string key = BloomFilter.CreateHeaderKey(hash, metadataJson);
            bloomFilter.Add(key);
        }

        var dto = new BloomHeadersResponseDto
        {
            SeederId = _seederId,
            BitCount = bloomFilter.BitCount,
            HashCount = bloomFilter.HashCount,
            ItemCount = bloomFilter.ItemCount,
            FilterDataBase64 = Convert.ToBase64String(bloomFilter.BitArray)
        };

        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(dto));
        response.ContentType = "application/json";
        response.StatusCode = (int)HttpStatusCode.OK;
        await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        response.Close();
    }

    private async Task HandleHeaderSyncEndpointAsync(HttpListenerRequest request, HttpListenerResponse response)
    {
        using var reader = new StreamReader(request.InputStream, Encoding.UTF8);
        string json = await reader.ReadToEndAsync();
        var syncRequest = JsonSerializer.Deserialize<HeaderSyncRequestDto>(json);

        if (syncRequest == null || string.IsNullOrWhiteSpace(syncRequest.Blake3Hash))
        {
            response.StatusCode = (int)HttpStatusCode.BadRequest;
            response.Close();
            return;
        }

        string normalizedHash = ContentAddressableStorage.NormalizeBlake3Hash(syncRequest.Blake3Hash);
        bool updated = false;

        if (_storage.HasAsset(normalizedHash) && !string.IsNullOrWhiteSpace(syncRequest.MetadataHeadersJson))
        {
            byte[] existingBytes = _storage.GetAssetBytes(normalizedHash) ?? Array.Empty<byte>();
            string? filePath = _storage.FindAssetFilePath(normalizedHash);
            string extension = filePath != null ? Path.GetExtension(filePath) : ".bin";

            var storeResult = _storage.StoreAsset(
                existingBytes,
                extension,
                syncRequest.MetadataHeadersJson,
                syncRequest.AuthorPublicKey,
                syncRequest.AuthorSignature);

            updated = storeResult.Merged;
        }

        string currentMetadata = _storage.GetAssetMetadata(normalizedHash) ?? string.Empty;
        var syncResponse = new HeaderSyncResponseDto
        {
            Updated = updated,
            CurrentMetadataHeadersJson = currentMetadata
        };

        byte[] bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(syncResponse));
        response.ContentType = "application/json";
        response.StatusCode = (int)HttpStatusCode.OK;
        await response.OutputStream.WriteAsync(bytes, 0, bytes.Length);
        response.Close();
    }

    private async Task<(byte[]? FileBytes, string? FileName, string? MetadataJson, string? AuthorPublicKey, string? AuthorSignature)> ParseMultipartFormAsync(HttpListenerRequest request)
    {
        using var memoryStream = new MemoryStream();
        await request.InputStream.CopyToAsync(memoryStream);
        byte[] body = memoryStream.ToArray();

        string contentType = request.ContentType ?? string.Empty;
        int boundaryIndex = contentType.IndexOf("boundary=", StringComparison.OrdinalIgnoreCase);
        if (boundaryIndex < 0)
        {
            return (body, null, null, null, null);
        }

        string rawBoundary = contentType.Substring(boundaryIndex + 9).Split(';')[0].Trim().Trim('"');
        byte[] boundaryBytes = Encoding.UTF8.GetBytes("--" + rawBoundary);

        byte[]? fileBytes = null;
        string? fileName = null;
        string? metadataJson = null;
        string? authorPublicKey = null;
        string? authorSignature = null;

        var sections = SplitBytesByBoundary(body, boundaryBytes);
        foreach (var section in sections)
        {
            int headerEnd = FindByteSequence(section, new byte[] { (byte)'\r', (byte)'\n', (byte)'\r', (byte)'\n' });
            int delimLength = 4;
            if (headerEnd < 0)
            {
                headerEnd = FindByteSequence(section, new byte[] { (byte)'\n', (byte)'\n' });
                delimLength = 2;
            }
            if (headerEnd < 0)
            {
                continue;
            }

            string headers = Encoding.UTF8.GetString(section, 0, headerEnd);
            int contentStart = headerEnd + delimLength;
            int contentLength = section.Length - contentStart;
            if (contentLength < 0)
            {
                continue;
            }

            if (contentLength >= 2 && section[section.Length - 2] == '\r' && section[section.Length - 1] == '\n')
            {
                contentLength -= 2;
            }
            else if (contentLength >= 1 && section[section.Length - 1] == '\n')
            {
                contentLength -= 1;
            }

            byte[] contentBytes = new byte[contentLength];
            Buffer.BlockCopy(section, contentStart, contentBytes, 0, contentLength);

            string? partName = ExtractHeaderParameter(headers, "name");
            string? partFileName = ExtractHeaderParameter(headers, "filename");

            if (string.Equals(partName, "file", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrEmpty(partFileName))
            {
                if (!string.IsNullOrEmpty(partFileName))
                {
                    fileName = partFileName;
                }
                fileBytes = contentBytes;
            }
            else if (string.Equals(partName, "metadata", StringComparison.OrdinalIgnoreCase))
            {
                metadataJson = Encoding.UTF8.GetString(contentBytes);
            }
            else if (string.Equals(partName, "authorPublicKey", StringComparison.OrdinalIgnoreCase))
            {
                authorPublicKey = Encoding.UTF8.GetString(contentBytes);
            }
            else if (string.Equals(partName, "authorSignature", StringComparison.OrdinalIgnoreCase))
            {
                authorSignature = Encoding.UTF8.GetString(contentBytes);
            }
        }

        return (fileBytes ?? body, fileName, metadataJson, authorPublicKey, authorSignature);
    }

    private static string? ExtractHeaderParameter(string headers, string parameterName)
    {
        int index = headers.IndexOf(parameterName + "=", StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return null;
        }

        int valueStart = index + parameterName.Length + 1;
        if (valueStart >= headers.Length)
        {
            return null;
        }

        if (headers[valueStart] == '"')
        {
            valueStart++;
            int end = headers.IndexOf('"', valueStart);
            return end > valueStart ? headers.Substring(valueStart, end - valueStart) : null;
        }
        else
        {
            int end = headers.IndexOfAny(new[] { ';', '\r', '\n', ' ' }, valueStart);
            return end > valueStart ? headers.Substring(valueStart, end - valueStart) : headers.Substring(valueStart).Trim();
        }
    }

    private static string? DecodeMetadataHeader(string? headerValue)
    {
        if (string.IsNullOrWhiteSpace(headerValue))
        {
            return null;
        }

        try
        {
            byte[] decoded = Convert.FromBase64String(headerValue);
            return Encoding.UTF8.GetString(decoded);
        }
        catch
        {
            return headerValue;
        }
    }

    private static List<byte[]> SplitBytesByBoundary(byte[] source, byte[] boundary)
    {
        var result = new List<byte[]>();
        int currentIndex = 0;

        while (currentIndex < source.Length)
        {
            int nextMatch = FindByteSequence(source, boundary, currentIndex);
            if (nextMatch < 0)
            {
                if (currentIndex < source.Length)
                {
                    int tailLength = source.Length - currentIndex;
                    byte[] tail = new byte[tailLength];
                    Buffer.BlockCopy(source, currentIndex, tail, 0, tailLength);
                    result.Add(tail);
                }
                break;
            }

            if (nextMatch > currentIndex)
            {
                int segmentLength = nextMatch - currentIndex;
                byte[] segment = new byte[segmentLength];
                Buffer.BlockCopy(source, currentIndex, segment, 0, segmentLength);
                result.Add(segment);
            }

            currentIndex = nextMatch + boundary.Length;
        }

        return result;
    }

    private static int FindByteSequence(byte[] source, byte[] pattern, int startIndex = 0)
    {
        if (source.Length == 0 || pattern.Length == 0 || pattern.Length > source.Length)
        {
            return -1;
        }

        for (int i = startIndex; i <= source.Length - pattern.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (source[i + j] != pattern[j])
                {
                    match = false;
                    break;
                }
            }

            if (match)
            {
                return i;
            }
        }

        return -1;
    }
}
