using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

public class MapDistributionServer
{
    private TcpListener? _tcpListener;
    private bool _isRunning;
    private string _mapPath = "";
    private MapManifest? _currentManifest;

    public void Start(int port, string mapPath)
    {
        _mapPath = mapPath;
        
        try
        {
            _currentManifest = MapAssetManager.IngestHostMap(mapPath);
            MapAssetManager.Log($"[MapDistributionServer] Ingested host map at {mapPath}. Manifest has {_currentManifest.Files.Count} files.");
        }
        catch (Exception ex)
        {
            MapAssetManager.LogErr($"[MapDistributionServer] Failed to ingest host map: {ex.Message}");
        }

        try
        {
            _tcpListener = new TcpListener(IPAddress.Any, port);
            _tcpListener.Server.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReuseAddress, true);
            _tcpListener.Start();
            _isRunning = true;
            MapAssetManager.Log($"[MapDistributionServer] Listening on 0.0.0.0:{port}");
            _ = Task.Run(AcceptConnectionsAsync);
        }
        catch (Exception ex)
        {
            MapAssetManager.LogErr($"[MapDistributionServer] TcpListener failed to start on port {port}: {ex.Message}");
        }
    }

    public void Stop()
    {
        _isRunning = false;
        try
        {
            if (_tcpListener != null)
            {
                _tcpListener.Stop();
                _tcpListener = null;
                MapAssetManager.Log("[MapDistributionServer] Stopped successfully.");
            }
        }
        catch (Exception ex)
        {
            MapAssetManager.LogErr($"[MapDistributionServer] Error stopping server: {ex.Message}");
        }
    }

    private async Task AcceptConnectionsAsync()
    {
        while (_isRunning && _tcpListener != null)
        {
            try
            {
                var client = await _tcpListener.AcceptTcpClientAsync();
                _ = Task.Run(() => HandleClientAsync(client));
            }
            catch (Exception)
            {
                break;
            }
        }
    }

    private async Task HandleClientAsync(TcpClient client)
    {
        using (client)
        using (var stream = client.GetStream())
        {
            try
            {
                var (method, path, headers, bodyBytes) = await ReadHttpRequestAsync(stream);
                if (string.IsNullOrEmpty(method) || string.IsNullOrEmpty(path))
                {
                    return;
                }

                MapAssetManager.Log($"[MapDistributionServer] Received HTTP request: {method} {path}");

                string cleanPath = path.TrimEnd('/');
                if (cleanPath.Contains('?'))
                {
                    cleanPath = cleanPath.Substring(0, cleanPath.IndexOf('?')).TrimEnd('/');
                }

                if (method.Equals("GET", StringComparison.OrdinalIgnoreCase) && cleanPath.StartsWith("/api/assets/"))
                {
                    string hash = cleanPath.Substring("/api/assets/".Length);
                    string normalizedHash = Realm.Shared.Distribution.ContentAddressableStorage.NormalizeBlake3Hash(hash);
                    byte[]? assetBytes = MapAssetManager.Storage.GetAssetBytes(normalizedHash);

                    if (assetBytes == null)
                    {
                        await WriteResponseAsync(stream, 404, "Not Found", "text/plain", Array.Empty<byte>());
                        return;
                    }

                    var extraHeaders = new Dictionary<string, string>();
                    string? metadata = MapAssetManager.Storage.GetAssetMetadata(normalizedHash);
                    if (!string.IsNullOrEmpty(metadata))
                    {
                        extraHeaders["X-Asset-Metadata"] = Convert.ToBase64String(Encoding.UTF8.GetBytes(metadata));
                    }

                    await WriteResponseAsync(stream, 200, "OK", "application/octet-stream", assetBytes, extraHeaders);
                    MapAssetManager.Log($"[MapDistributionServer] Served CAS asset {normalizedHash} ({assetBytes.Length} bytes)");
                    return;
                }

                if (method.Equals("GET", StringComparison.OrdinalIgnoreCase) && (cleanPath == "/map" || cleanPath == "/map/manifest" || cleanPath.StartsWith("/api/manifests")))
                {
                    if (_currentManifest == null)
                    {
                        _currentManifest = MapAssetManager.IngestHostMap(_mapPath);
                    }

                    string manifestJson = _currentManifest.ToJson();
                    byte[] rawBytes = Encoding.UTF8.GetBytes(manifestJson);

                    await WriteResponseAsync(stream, 200, "OK", "application/json", rawBytes);
                    MapAssetManager.Log($"[MapDistributionServer] Served map manifest ({rawBytes.Length} bytes)");
                    return;
                }
                else
                {
                    await WriteResponseAsync(stream, 404, "Not Found", "text/plain", Array.Empty<byte>());
                }
            }
            catch (Exception ex)
            {
                MapAssetManager.LogErr($"[MapDistributionServer] Exception handling request: {ex.Message}");
                try
                {
                    await WriteResponseAsync(stream, 500, "Internal Server Error", "text/plain", Array.Empty<byte>());
                }
                catch { }
            }
        }
    }

    private static async Task<(string Method, string Path, Dictionary<string, string> Headers, byte[] Body)> ReadHttpRequestAsync(NetworkStream stream)
    {
        var headerBuffer = new MemoryStream();
        byte[] readBuffer = new byte[4096];
        int headerEndIndex = -1;
        int delimiterLength = 0;

        while (true)
        {
            int bytesRead = await stream.ReadAsync(readBuffer, 0, readBuffer.Length);
            if (bytesRead <= 0) break;

            headerBuffer.Write(readBuffer, 0, bytesRead);
            byte[] currentBytes = headerBuffer.ToArray();

            (headerEndIndex, delimiterLength) = FindHeaderEnd(currentBytes);
            if (headerEndIndex >= 0)
            {
                break;
            }
        }

        if (headerEndIndex < 0)
        {
            return (string.Empty, string.Empty, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), Array.Empty<byte>());
        }

        byte[] allBytes = headerBuffer.ToArray();
        string headerString = Encoding.UTF8.GetString(allBytes, 0, headerEndIndex);
        var lines = headerString.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

        if (lines.Length == 0 || string.IsNullOrWhiteSpace(lines[0]))
        {
            return (string.Empty, string.Empty, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), Array.Empty<byte>());
        }

        string[] requestParts = lines[0].Split(' ');
        if (requestParts.Length < 2)
        {
            return (string.Empty, string.Empty, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), Array.Empty<byte>());
        }

        string method = requestParts[0];
        string path = requestParts[1];

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 1; i < lines.Length; i++)
        {
            string line = lines[i];
            int colonIndex = line.IndexOf(':');
            if (colonIndex > 0)
            {
                string key = line.Substring(0, colonIndex).Trim();
                string val = line.Substring(colonIndex + 1).Trim();
                headers[key] = val;
            }
        }

        int bodyStartIndex = headerEndIndex + delimiterLength;
        int existingBodyBytes = allBytes.Length - bodyStartIndex;

        int contentLength = 0;
        if (headers.TryGetValue("Content-Length", out var clStr) && int.TryParse(clStr, out int parsedCl))
        {
            contentLength = parsedCl;
        }

        byte[] body = Array.Empty<byte>();
        if (contentLength > 0)
        {
            body = new byte[contentLength];
            int copied = Math.Min(existingBodyBytes, contentLength);
            if (copied > 0)
            {
                Buffer.BlockCopy(allBytes, bodyStartIndex, body, 0, copied);
            }

            int remaining = contentLength - copied;
            int offset = copied;
            while (remaining > 0)
            {
                int read = await stream.ReadAsync(body, offset, remaining);
                if (read <= 0) break;
                offset += read;
                remaining -= read;
            }
        }

        return (method, path, headers, body);
    }

    private static (int HeaderEndIndex, int DelimiterLength) FindHeaderEnd(byte[] buffer)
    {
        for (int i = 0; i < buffer.Length - 1; i++)
        {
            if (buffer[i] == '\r' && i + 3 < buffer.Length && buffer[i + 1] == '\n' && buffer[i + 2] == '\r' && buffer[i + 3] == '\n')
            {
                return (i, 4);
            }
            if (buffer[i] == '\n' && buffer[i + 1] == '\n')
            {
                return (i, 2);
            }
        }
        return (-1, 0);
    }

    private static async Task WriteResponseAsync(
        NetworkStream stream,
        int statusCode,
        string statusDescription,
        string contentType,
        byte[] body,
        Dictionary<string, string>? extraHeaders = null)
    {
        var headerBuilder = new StringBuilder();
        headerBuilder.Append($"HTTP/1.1 {statusCode} {statusDescription}\r\n");
        headerBuilder.Append($"Content-Type: {contentType}\r\n");
        headerBuilder.Append($"Content-Length: {body.Length}\r\n");
        headerBuilder.Append("Connection: close\r\n");

        if (extraHeaders != null)
        {
            foreach (var kvp in extraHeaders)
            {
                headerBuilder.Append($"{kvp.Key}: {kvp.Value}\r\n");
            }
        }

        headerBuilder.Append("\r\n");

        byte[] headerBytes = Encoding.UTF8.GetBytes(headerBuilder.ToString());
        await stream.WriteAsync(headerBytes, 0, headerBytes.Length);

        if (body.Length > 0)
        {
            await stream.WriteAsync(body, 0, body.Length);
        }

        await stream.FlushAsync();
    }
}

