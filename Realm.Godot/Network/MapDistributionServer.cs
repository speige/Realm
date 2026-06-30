using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

public class MapDistributionServer
{
    private HttpListener? _listener;
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

        _listener = new HttpListener();
        

        string prefix = $"http://*:{port}/";
        _listener.Prefixes.Add(prefix);

        try
        {
            _listener.Start();
            _isRunning = true;
            MapAssetManager.Log($"[MapDistributionServer] Listening on {prefix}");
            Task.Run(AcceptConnectionsAsync);
        }
        catch (HttpListenerException ex)
        {
            MapAssetManager.LogErr($"[MapDistributionServer] HttpListener failed to start on prefix {prefix}: {ex.Message}");
            MapAssetManager.Log("[MapDistributionServer] Retrying with localhost fallback prefix...");
            try
            {
                _listener = new HttpListener();
                string fallbackPrefix = $"http://127.0.0.1:{port}/";
                _listener.Prefixes.Add(fallbackPrefix);
                _listener.Start();
                _isRunning = true;
                MapAssetManager.Log($"[MapDistributionServer] Listening on fallback {fallbackPrefix}");
                Task.Run(AcceptConnectionsAsync);
            }
            catch (Exception fallbackEx)
            {
                MapAssetManager.LogErr($"[MapDistributionServer] Fallback also failed: {fallbackEx.Message}");
            }
        }
    }

    public void Stop()
    {
        _isRunning = false;
        try
        {
            if (_listener != null && _listener.IsListening)
            {
                _listener.Stop();
                _listener.Close();
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
        while (_isRunning && _listener != null)
        {
            try
            {
                var context = await _listener.GetContextAsync();
                _ = Task.Run(() => HandleRequestAsync(context));
            }
            catch (Exception)
            {
                break;
            }
        }
    }

    private async Task HandleRequestAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        MapAssetManager.Log($"[MapDistributionServer] Received HTTP request: {request.HttpMethod} {request.Url?.LocalPath}");

        string path = request.Url?.LocalPath.TrimEnd('/') ?? "";

        try
        {
            if (request.HttpMethod == "GET" && (path == "/map" || path == "/map/manifest"))
            {

                if (_currentManifest == null)
                {
                    _currentManifest = MapAssetManager.IngestHostMap(_mapPath);
                }

                string manifestJson = JsonSerializer.Serialize(_currentManifest);
                byte[] rawBytes = Encoding.UTF8.GetBytes(manifestJson);

                response.ContentType = "application/json";
                response.ContentLength64 = rawBytes.Length;
                response.StatusCode = (int)HttpStatusCode.OK;

                using (var outputStream = response.OutputStream)
                {
                    await outputStream.WriteAsync(rawBytes, 0, rawBytes.Length);
                }

                MapAssetManager.Log($"[MapDistributionServer] Served map manifest ({rawBytes.Length} bytes)");
            }
            else if (request.HttpMethod == "POST" && path == "/map/delta")
            {
                string requestBody;
                using (var reader = new StreamReader(request.InputStream, request.ContentEncoding))
                {
                    requestBody = await reader.ReadToEndAsync();
                }

                var missingHashes = JsonSerializer.Deserialize<List<string>>(requestBody);
                if (missingHashes == null) missingHashes = new List<string>();

                MapAssetManager.Log($"[MapDistributionServer] Creating delta 7z for {missingHashes.Count} requested hashes...");
                string tempDeltaPath = MapAssetManager.CreateTemporaryDeltaArchive(missingHashes);

                byte[] rawBytes = await File.ReadAllBytesAsync(tempDeltaPath);
                
                response.ContentType = "application/x-7z-compressed";
                response.ContentLength64 = rawBytes.Length;
                response.StatusCode = (int)HttpStatusCode.OK;

                using (var outputStream = response.OutputStream)
                {
                    await outputStream.WriteAsync(rawBytes, 0, rawBytes.Length);
                }


                try
                {
                    if (File.Exists(tempDeltaPath))
                    {
                        File.Delete(tempDeltaPath);
                    }
                }
                catch { }

                MapAssetManager.Log($"[MapDistributionServer] Served delta 7z ({rawBytes.Length} bytes) for {missingHashes.Count} hashes");
            }
            else
            {
                response.StatusCode = (int)HttpStatusCode.NotFound;
                response.Close();
            }
        }
        catch (Exception ex)
        {
            MapAssetManager.LogErr($"[MapDistributionServer] Exception handling request: {ex.Message}");
            response.StatusCode = (int)HttpStatusCode.InternalServerError;
            response.Close();
        }
    }
}

