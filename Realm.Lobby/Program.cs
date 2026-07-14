using Realm.Lobby.Models;
using Realm.Lobby.Services;

using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);


builder.Services.AddSingleton<LobbyRegistry>();
builder.Services.AddSingleton<GeoIpService>();
builder.Services.AddSingleton<SeederRegistry>();
builder.Services.AddSingleton<DataStoreService>();
builder.Services.AddHttpClient();


var selfUrl = builder.Configuration.GetValue<string>("SelfUrl");
var peersStr = builder.Configuration.GetValue<string>("Peers") ?? "";
var peerUrls = peersStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

var peerRegistry = new PeerRegistry
{
    SelfUrl = selfUrl,
    PeerUrls = peerUrls
};
builder.Services.AddSingleton(peerRegistry);


builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader();
    });
});

var app = builder.Build();

app.UseCors("AllowAll");
app.UseWebSockets();


var hostConnections = new ConcurrentDictionary<string, WebSocket>();


_ = Task.Run(async () =>
{
    while (true)
    {
        try
        {
            await Task.Delay(5000);
            var registry = app.Services.GetRequiredService<LobbyRegistry>();
            var inactiveIds = registry.PruneExpiredLobbies(TimeSpan.FromSeconds(30));
            foreach (var id in inactiveIds)
            {
                if (hostConnections.TryRemove(id, out var socket))
                {
                    try
                    {
                        await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Lobby expired", CancellationToken.None);
                    }
                    catch { /* Ignore socket close errors */ }
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error pruning lobbies: {ex.Message}");
        }
    }
});




app.MapGet("/lobbies", (LobbyRegistry registry, GeoIpService geoIp, HttpContext context) =>
{
    var clientIpStr = context.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "127.0.0.1";
    var clientCoords = geoIp.GetCoordinates(clientIpStr);
    
    var list = registry.GetAllLobbies().Select(lobby =>
    {
        var distance = GeoIpService.CalculateDistance(
            clientCoords.lat, clientCoords.lon,
            lobby.Latitude, lobby.Longitude
        );
        

        int estimatedPing = 15 + (int)(distance / 100);
        if (clientIpStr == lobby.HostIP || clientIpStr == "127.0.0.1" || lobby.HostIP == "127.0.0.1")
        {
            estimatedPing = 5; // Local connection ping
        }

        return new LobbyResponseDto(
            lobby.LobbyId,
            lobby.Map,
            lobby.HostIP,
            lobby.HostPort,
            lobby.NatType,
            lobby.SlotsUsed,
            lobby.MaxPlayers,
            lobby.Latitude,
            lobby.Longitude,
            distance,
            estimatedPing,
            lobby.OriginServerUri,
            lobby.HostPingBaseline,
            lobby.GameVersion,
            lobby.LocalIP
        );
    });

    return Results.Ok(list);
});


app.MapPost("/lobbies/register", async (RegisterRequest req, LobbyRegistry registry, PeerRegistry registeredPeers, GeoIpService geoIp, IHttpClientFactory httpClientFactory, HttpContext context) =>
{

    if (!string.IsNullOrEmpty(req.NatType) && req.NatType.Equals("Symmetric", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new { Message = "Lobby creation rejected: Symmetric NAT is not supported." });
    }

    var hostIp = context.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "127.0.0.1";
    

    if (!string.IsNullOrEmpty(req.ReportedHostIP) && req.ReportedHostIP != "0.0.0.0" && req.ReportedHostIP != "127.0.0.1")
    {
        hostIp = req.ReportedHostIP;
    }

    var hostCoords = geoIp.GetCoordinates(hostIp);
    var lobbyId = Guid.NewGuid().ToString();
    var hostToken = Guid.NewGuid().ToString();

    var db = context.RequestServices.GetRequiredService<DataStoreService>();

    bool isDefault = req.Map.Equals("melee", StringComparison.OrdinalIgnoreCase) || 
                     req.Map.Equals("green_td", StringComparison.OrdinalIgnoreCase) || 
                     req.Map.Equals("legion_td", StringComparison.OrdinalIgnoreCase) ||
                     req.Map.Equals("Melee Battlefield", StringComparison.OrdinalIgnoreCase);

    if (!isDefault)
    {
        if (string.IsNullOrEmpty(req.Signature) || string.IsNullOrEmpty(req.PublicKey) || string.IsNullOrEmpty(req.MapHash))
        {
            return Results.BadRequest(new { Message = "Map signature, public key, and hash are required for custom maps." });
        }
        
        try
        {
            byte[] pubKeyBytes = Convert.FromBase64String(req.PublicKey);
            byte[] sigBytes = Convert.FromBase64String(req.Signature);
            var pubKey = NSec.Cryptography.PublicKey.Import(NSec.Cryptography.SignatureAlgorithm.Ed25519, pubKeyBytes, NSec.Cryptography.KeyBlobFormat.RawPublicKey);
            byte[] hashBytes = System.Text.Encoding.UTF8.GetBytes(req.MapHash);
            if (!NSec.Cryptography.SignatureAlgorithm.Ed25519.Verify(pubKey, hashBytes, sigBytes)) {
                return Results.BadRequest(new { Message = "Invalid map signature." });
            }
        }
        catch
        {
            return Results.BadRequest(new { Message = "Invalid signature or public key format." });
        }
        
        string slug = req.Map.ToLowerInvariant().Replace(" ", "-");
        var existingLock = db.Get<JsonDocument>("name_locks", slug);
        if (existingLock != null)
        {
            var root = existingLock.RootElement;
            string owner = root.GetProperty("owner_public_key").GetString() ?? "";
            if (owner != req.PublicKey)
            {
                return Results.BadRequest(new { Message = "This map name is officially reserved to another creator." });
            }
        }
    }

    string finalMapName = req.Map;
    string mapVersion = req.MapVersion ?? "1.0";
    string compositeKey = $"{req.Map}_{mapVersion}";
    var stats = db.Get<MapStats>("map_stats", compositeKey);
    bool isGreenlit = isDefault || (stats != null && stats.IsGreenlit);

    if (!isGreenlit) {
        string author = "Unknown";
        if (!string.IsNullOrEmpty(req.PublicKey)) {
            var creator = db.Get<JsonDocument>("creators", req.PublicKey);
            if (creator != null && creator.RootElement.TryGetProperty("username", out var uProp)) {
                author = uProp.GetString() ?? "Unknown";
            }
        }
        if (author == "Unknown") {
            var existingMap = db.Get<JsonDocument>("published_maps", compositeKey);
            if (existingMap != null) {
                var root = existingMap.RootElement;
                if (root.TryGetProperty("Contributors", out var conts) && conts.GetArrayLength() > 0) {
                    author = conts[0].GetString() ?? "Unknown";
                }
            }
        }
        if (!finalMapName.Contains("[Beta-Testing]")) {
            finalMapName = $"[Beta-Testing] {finalMapName} - {author}";
        }
    }

    var info = new LobbyInfo
    {
        LobbyId = lobbyId,
        Map = finalMapName,
        HostIP = hostIp,
        HostPort = req.HostPort,
        NatType = req.NatType,
        PasswordHash = req.PasswordHash ?? "",
        MaxPlayers = req.MaxPlayers,
        SlotsUsed = req.SlotsUsed,
        GameVersion = req.GameVersion,
        Latitude = hostCoords.lat,
        Longitude = hostCoords.lon,
        LastHeartbeat = DateTime.UtcNow,
        OriginServerUri = registeredPeers.SelfUrl,
        HostToken = hostToken,
        HostPingBaseline = req.HostPingBaseline,
        LocalIP = req.LocalIP
    };

    registry.AddOrUpdate(info);

    _ = Task.Run(async () =>
    {
        using var httpClient = httpClientFactory.CreateClient();
        foreach (var peerUrl in registeredPeers.PeerUrls)
        {
            try
            {
                var content = new StringContent(JsonSerializer.Serialize(info), Encoding.UTF8, "application/json");
                var response = await httpClient.PostAsync($"{peerUrl.TrimEnd('/')}/lobbies/propagate", content);
                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"[Registry] Propagated lobby {lobbyId} to peer {peerUrl}");
                }
                else
                {
                    Console.WriteLine($"[Registry] Failed to propagate lobby {lobbyId} to peer {peerUrl}: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Registry] Error propagating lobby {lobbyId} to peer {peerUrl}: {ex.Message}");
            }
        }
    });

    return Results.Ok(new { LobbyId = lobbyId, Latitude = hostCoords.lat, Longitude = hostCoords.lon, HostToken = hostToken });
});

app.MapPost("/lobbies/propagate", (LobbyInfo propagatedLobby, LobbyRegistry registry) =>
{

    if (!string.IsNullOrEmpty(propagatedLobby.NatType) && propagatedLobby.NatType.Equals("Symmetric", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new { Message = "Symmetric NAT lobbies are not supported." });
    }

    registry.AddOrUpdate(propagatedLobby);
    Console.WriteLine($"[Registry] Received propagated lobby {propagatedLobby.LobbyId} from {propagatedLobby.OriginServerUri}");
    return Results.Ok(new { Status = "Propagated" });
});


app.MapPost("/lobbies/heartbeat", async (HeartbeatRequest req, LobbyRegistry registry, PeerRegistry registeredPeers, IHttpClientFactory httpClientFactory) =>
{
    if (registry.TryGet(req.LobbyId, out var lobby) && lobby != null)
    {
        lobby.LastHeartbeat = DateTime.UtcNow;
        lobby.SlotsUsed = req.SlotsUsed;


        _ = Task.Run(async () =>
        {
            using var httpClient = httpClientFactory.CreateClient();
            foreach (var peerUrl in registeredPeers.PeerUrls)
            {
                try
                {
                    var content = new StringContent(JsonSerializer.Serialize(lobby), Encoding.UTF8, "application/json");
                    var response = await httpClient.PostAsync($"{peerUrl.TrimEnd('/')}/lobbies/propagate", content);
                    if (!response.IsSuccessStatusCode)
                    {
                        Console.WriteLine($"[Registry] Failed to propagate heartbeat for {lobby.LobbyId} to peer {peerUrl}: {response.StatusCode}");
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[Registry] Error propagating heartbeat for {lobby.LobbyId} to peer {peerUrl}: {ex.Message}");
                }
            }
        });

        return Results.Ok(new { Status = "Ok" });
    }
    return Results.NotFound(new { Message = "Lobby not found" });
});

app.MapPost("/lobbies/close", async (CloseLobbyRequest req, LobbyRegistry registry, PeerRegistry registeredPeers, IHttpClientFactory httpClientFactory) =>
{
    if (registry.TryGet(req.LobbyId, out var lobby) && lobby != null)
    {
        if (lobby.HostToken == req.HostToken)
        {
            registry.TryRemove(req.LobbyId, out _);

            if (hostConnections.TryRemove(req.LobbyId, out var socket))
            {
                try
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Lobby closed by host", CancellationToken.None);
                }
                catch { /* Ignore socket close errors */ }
            }

            _ = Task.Run(async () =>
            {
                using var httpClient = httpClientFactory.CreateClient();
                foreach (var peerUrl in registeredPeers.PeerUrls)
                {
                    try
                    {
                        var content = new StringContent(JsonSerializer.Serialize(req), Encoding.UTF8, "application/json");
                        await httpClient.PostAsync($"{peerUrl.TrimEnd('/')}/lobbies/propagate-close", content);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error propagating close: {ex.Message}");
                    }
                }
            });

            return Results.Ok(new { Status = "Closed" });
        }
        return Results.Json(new { Message = "Unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);
    }
    return Results.NotFound(new { Message = "Lobby not found" });
});

app.MapPost("/lobbies/propagate-close", async (CloseLobbyRequest req, LobbyRegistry registry) =>
{
    if (registry.TryGet(req.LobbyId, out var lobby) && lobby != null)
    {
        if (lobby.HostToken == req.HostToken)
        {
            registry.TryRemove(req.LobbyId, out _);

            if (hostConnections.TryRemove(req.LobbyId, out var socket))
            {
                try
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Lobby closed by host", CancellationToken.None);
                }
                catch { /* Ignore socket close errors */ }
            }

            return Results.Ok(new { Status = "Closed" });
        }
        return Results.Json(new { Message = "Unauthorized" }, statusCode: StatusCodes.Status401Unauthorized);
    }
    return Results.NotFound(new { Message = "Lobby not found" });
});


app.MapPost("/lobbies/join", async (JoinRequest req, LobbyRegistry registry, IHttpClientFactory httpClientFactory) =>
{
    if (!registry.TryGet(req.LobbyId, out var lobby) || lobby == null)
    {
        return Results.NotFound(new { Message = "Lobby not found" });
    }


    if (hostConnections.TryGetValue(req.LobbyId, out var hostSocket) && hostSocket.State == WebSocketState.Open)
    {
        var msg = JsonSerializer.Serialize(new
        {
            Action = "Punch",
            ClientIP = req.ClientPublicIP,
            ClientPort = req.ClientPublicPort
        });

        var bytes = Encoding.UTF8.GetBytes(msg);
        await hostSocket.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
        
        Console.WriteLine($"[Registry] Relayed punch request to host {lobby.LobbyId} for client {req.ClientPublicIP}:{req.ClientPublicPort}");
        
        return Results.Ok(new JoinResponseDto(lobby.HostIP, lobby.HostPort, lobby.LocalIP));
    }
    else
    {

        if (!string.IsNullOrEmpty(lobby.OriginServerUri))
        {
            Console.WriteLine($"[Registry] Relaying join request for lobby {lobby.LobbyId} to origin {lobby.OriginServerUri}");
            try
            {
                using var httpClient = httpClientFactory.CreateClient();
                var content = new StringContent(JsonSerializer.Serialize(req), Encoding.UTF8, "application/json");
                var response = await httpClient.PostAsync($"{lobby.OriginServerUri.TrimEnd('/')}/lobbies/join", content);
                if (response.IsSuccessStatusCode)
                {
                    var respText = await response.Content.ReadAsStringAsync();
                    var joinResp = JsonSerializer.Deserialize<JoinResponseDto>(respText, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    if (joinResp != null)
                    {
                        return Results.Ok(joinResp);
                    }
                }
                else
                {
                    var errText = await response.Content.ReadAsStringAsync();
                    return Results.BadRequest(new { Message = $"Relayed join failed: {errText}" });
                }
            }
            catch (Exception ex)
            {
                return Results.BadRequest(new { Message = $"Failed to relay join to origin: {ex.Message}" });
            }
        }
    }

    return Results.BadRequest(new { Message = "Host signaling channel is disconnected" });
});


app.Map("/lobbies/ws", async (HttpContext context, LobbyRegistry registry) =>
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        var lobbyId = context.Request.Query["lobbyId"].ToString();
        if (string.IsNullOrEmpty(lobbyId) || !registry.TryGet(lobbyId, out _))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        hostConnections[lobbyId] = webSocket;
        Console.WriteLine($"[Registry] Host connected WebSocket for Lobby {lobbyId}");

        var buffer = new byte[1024 * 4];
        while (webSocket.State == WebSocketState.Open)
        {
            var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
            }

        }

        hostConnections.TryRemove(lobbyId, out _);
        Console.WriteLine($"[Registry] Host disconnected WebSocket for Lobby {lobbyId}");
    }
    else
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
    }
});

app.MapGet("/auth/login", (string provider, int port) =>
{
    long part1 = Random.Shared.Next(100000000, 999999999);
    long part2 = Random.Shared.Next(100000000, 999999999);
    string randomSnowflake = $"{part1}{part2}";

    var html = $$"""
    <!DOCTYPE html>
    <html>
    <head>
        <title>Authorize Realm</title>
        <style>
            body {
                background: #0f111a;
                color: #e2e8f0;
                font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
                display: flex;
                align-items: center;
                justify-content: center;
                height: 100vh;
                margin: 0;
            }
            .card {
                background: #1a1d2e;
                border: 2px solid #3b3f5c;
                border-radius: 12px;
                padding: 40px;
                width: 400px;
                text-align: center;
                box-shadow: 0 8px 32px rgba(0,0,0,0.5);
            }
            h2 {
                color: #ffd700;
                margin-bottom: 10px;
            }
            .provider-title {
                text-transform: capitalize;
                font-weight: bold;
                color: #5cd6ff;
            }
            .btn {
                background: #5865F2;
                color: white;
                border: none;
                padding: 12px 24px;
                border-radius: 6px;
                font-size: 16px;
                font-weight: bold;
                cursor: pointer;
                transition: background 0.2s;
                width: 100%;
                margin-top: 20px;
            }
            .btn-steam {
                background: #171a21;
                border: 1px solid #66c0f4;
            }
            .btn:hover {
                filter: brightness(1.1);
            }
            input {
                width: 90%;
                padding: 10px;
                margin-top: 15px;
                border-radius: 4px;
                border: 1px solid #3b3f5c;
                background: #0f111a;
                color: #e2e8f0;
                text-align: center;
                font-size: 16px;
            }
        </style>
    </head>
    <body>
        <div class="card">
            <h2>Authorize Realm</h2>
            <p>Connect your <span class="provider-title">{{provider}}</span> account to play.</p>
            <form action="/auth/authorize" method="GET">
                <input type="hidden" name="provider" value="{{provider}}" />
                <input type="hidden" name="port" value="{{port}}" />
                <input type="text" name="username" placeholder="Enter Username" required value="Gamer_{{Random.Shared.Next(1000, 9999)}}" />
                {{(provider == "discord" ? $"<input type=\"text\" name=\"discord_id\" placeholder=\"Discord Snowflake ID\" required value=\"{randomSnowflake}\" />" : "")}}
                <button type="submit" class="btn {{ (provider == "steam" ? "btn-steam" : "") }}">
                    Login with {{provider}}
                </button>
            </form>
        </div>
    </body>
    </html>
    """;
    return Results.Content(html, "text/html");
});

app.MapGet("/auth/authorize", (string provider, int port, string username, string? discord_id, DataStoreService db) =>
{
    string finalUsername = username;
    if (provider == "discord")
    {
        string snowflake = discord_id ?? "";
        if (string.IsNullOrEmpty(snowflake))
        {
            long part1 = Random.Shared.Next(100000000, 999999999);
            long part2 = Random.Shared.Next(100000000, 999999999);
            snowflake = $"{part1}{part2}";
        }

        var existingPlayer = db.Get<JsonDocument>("players", snowflake);
        if (existingPlayer != null)
        {
            var root = existingPlayer.RootElement;
            if (root.TryGetProperty("username", out var uProp))
            {
                finalUsername = uProp.GetString() ?? username;
            }
        }
        else
        {
            var playerDoc = JsonSerializer.SerializeToDocument(new
            {
                id = snowflake,
                username = username,
                provider = "discord",
                registration_date = DateTime.UtcNow
            });
            db.Upsert("players", snowflake, playerDoc);
        }
    }

    var token = Guid.NewGuid().ToString("N");
    var callbackUrl = $"http://localhost:{port}/auth/callback/?username={Uri.EscapeDataString(finalUsername)}&token={token}&provider={provider}";
    return Results.Redirect(callbackUrl);
});

app.MapPost("/seeders/register", (SeederRegisterRequest req, SeederRegistry registry, HttpContext context) =>
{
    var ip = context.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "127.0.0.1";
    if (!string.IsNullOrEmpty(req.ReportedIP) && req.ReportedIP != "0.0.0.0" && req.ReportedIP != "127.0.0.1")
    {
        ip = req.ReportedIP;
    }

    var info = new SeederInfo
    {
        SeederId = req.SeederId,
        IP = ip,
        Port = req.Port,
        MapIds = req.MapIds
    };
    registry.Register(info);
    return Results.Ok(new { Status = "Registered" });
});

app.MapPost("/seeders/unregister", (SeederUnregisterRequest req, SeederRegistry registry) =>
{
    registry.Unregister(req.SeederId);
    return Results.Ok(new { Status = "Unregistered" });
});

app.MapGet("/seeders", (SeederRegistry registry) =>
{
    var list = registry.GetAll().Select(s => new
    {
        ip = s.IP,
        port = s.Port,
        mapIds = s.MapIds
    });
    return Results.Ok(list);
});

app.MapPost("/seeders/download", async (SeederDownloadRequest req, SeederRegistry registry) =>
{
    var seeders = registry.GetSeedersForMap(req.MapId);
    if (seeders.Count == 0)
    {
        return Results.NotFound(new { Message = "No seeders found for this map" });
    }

    var random = new Random();
    var attempts = seeders.OrderBy(_ => random.Next()).ToList();

    foreach (var seeder in attempts)
    {
        var ws = registry.GetConnection(seeder.SeederId);
        if (ws != null && ws.State == WebSocketState.Open)
        {
            try
            {
                var msg = JsonSerializer.Serialize(new
                {
                    Action = "Punch",
                    ClientIP = req.ClientPublicIP,
                    ClientPort = req.ClientPublicPort
                });
                var bytes = Encoding.UTF8.GetBytes(msg);
                await ws.SendAsync(new ArraySegment<byte>(bytes), WebSocketMessageType.Text, true, CancellationToken.None);
                
                Console.WriteLine($"[Registry] Relayed punch request to seeder {seeder.SeederId} ({seeder.IP}:{seeder.Port}) for client {req.ClientPublicIP}:{req.ClientPublicPort}");
                
                return Results.Ok(new { SeederIP = seeder.IP, SeederPort = seeder.Port });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Registry] Failed to send punch to seeder {seeder.SeederId}, removing: {ex.Message}");
                registry.Unregister(seeder.SeederId);
            }
        }
        else
        {
            Console.WriteLine($"[Registry] Seeder {seeder.SeederId} has disconnected or closed connection, removing.");
            registry.Unregister(seeder.SeederId);
        }
    }

    return Results.BadRequest(new { Message = "Failed to coordinate UDP punch with seeders" });
});


app.MapPost("/api/publish_map", (PublishMapRequest req, DataStoreService db) =>
{
    try {
        var mapDoc = JsonDocument.Parse(req.MapJson);
        var root = mapDoc.RootElement;
        
        string mapTitle = "";
        string mapVersion = "1.0"; // default if missing
        
        if (root.TryGetProperty("MapProperties", out var mapProps)) {
            if (mapProps.TryGetProperty("MapName", out var nameProp)) mapTitle = nameProp.GetString() ?? "";
            if (mapProps.TryGetProperty("MapTitle", out var titleProp)) mapTitle = titleProp.GetString() ?? mapTitle;
            if (mapProps.TryGetProperty("MapVersion", out var versionProp)) mapVersion = versionProp.GetString() ?? "1.0";
        }
        
        if (string.IsNullOrEmpty(mapTitle)) {
            return Results.BadRequest(new { Message = "MapTitle is required in map.json MapProperties" });
        }
        
        
        if (string.IsNullOrEmpty(req.Signature) || string.IsNullOrEmpty(req.PublicKey)) {
            return Results.BadRequest(new { Message = "Map signature and public key are required." });
        }
        
        byte[] pubKeyBytes;
        byte[] sigBytes;
        try {
            pubKeyBytes = Convert.FromBase64String(req.PublicKey);
            sigBytes = Convert.FromBase64String(req.Signature);
        } catch {
            return Results.BadRequest(new { Message = "Invalid base64 encoding for signature or public key." });
        }
        
        var pubKey = NSec.Cryptography.PublicKey.Import(NSec.Cryptography.SignatureAlgorithm.Ed25519, pubKeyBytes, NSec.Cryptography.KeyBlobFormat.RawPublicKey);
        
        byte[] mapBytes = System.Text.Encoding.UTF8.GetBytes(req.MapJson);
        using var blake = System.Security.Cryptography.IncrementalHash.CreateHash(new System.Security.Cryptography.HashAlgorithmName("BLAKE3"));
        blake.AppendData(mapBytes);
        byte[] hashBytes = blake.GetHashAndReset();
        string mapHashStr = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        byte[] mapHashBytes = System.Text.Encoding.UTF8.GetBytes(mapHashStr);
        
        if (!NSec.Cryptography.SignatureAlgorithm.Ed25519.Verify(pubKey, mapHashBytes, sigBytes)) {
            return Results.BadRequest(new { Message = "Invalid map signature." });
        }

        string compositeKey = $"{mapTitle}_{mapVersion}";
        var existingMap = db.Get<JsonDocument>("published_maps", compositeKey);
        if (existingMap != null) {
            return Results.BadRequest(new { Message = $"Map with Title '{mapTitle}' and Version '{mapVersion}' already exists." });
        }
        
        var prevMapKey = $"{mapTitle}_1.0"; // Simplifying logic for previous versions to check if public keys match.
        var ownership = db.Get<string>("map_ownership", mapTitle);
        if (ownership != null && ownership != req.PublicKey) {
            return Results.BadRequest(new { Message = "A map with this title already exists and is owned by a different key." });
        }
        if (ownership == null) {
            db.Upsert("map_ownership", mapTitle, req.PublicKey);
        }

        
        // Verify contributors
        var contributors = new HashSet<string>();
        if (root.TryGetProperty("Contributors", out var contProp) && contProp.ValueKind == JsonValueKind.Array) {
            foreach (var element in contProp.EnumerateArray()) {
                var str = element.GetString();
                if (str != null) contributors.Add(str);
            }
        }
        
        // Check all referenced asset hashes
        foreach (var hash in req.ReferencedHashes) {
            var assetMeta = db.Get<JsonDocument>("asset_signatures", hash);
            if (assetMeta != null) {
                var author = assetMeta.RootElement.GetProperty("AuthorUsername").GetString();
                if (!string.IsNullOrEmpty(author) && !contributors.Contains(author)) {
                    return Results.BadRequest(new { Message = $"Author '{author}' from asset '{hash}' is missing from the Contributors list." });
                }
            }
        }
        
        db.Upsert("published_maps", compositeKey, mapDoc);
        return Results.Ok(new { Status = "Published", MapId = compositeKey });
    }
    catch (Exception ex) {
        return Results.BadRequest(new { Message = "Invalid JSON or request", Error = ex.Message });
    }
});

app.MapPost("/api/publish_map/upload_asset", async (HttpRequest request, DataStoreService db) =>
{
    if (!request.HasFormContentType)
        return Results.BadRequest("Expected multipart/form-data");
        
    var form = await request.ReadFormAsync();
    
    string hash = form["Hash"].ToString();
    string signature = form["Signature"].ToString();
    string authorUsername = form["AuthorUsername"].ToString();
    string publicKey = form["PublicKey"].ToString();
    
    if (string.IsNullOrEmpty(hash) || hash.Contains('.') || hash.Contains('/') || hash.Contains('\\')) return Results.BadRequest("Invalid Hash.");

    try {
        byte[] pubKeyBytes = Convert.FromBase64String(publicKey);
        byte[] sigBytes = Convert.FromBase64String(signature);
        var pubKey = NSec.Cryptography.PublicKey.Import(NSec.Cryptography.SignatureAlgorithm.Ed25519, pubKeyBytes, NSec.Cryptography.KeyBlobFormat.RawPublicKey);
        byte[] hashBytes = System.Text.Encoding.UTF8.GetBytes(hash);
        if (!NSec.Cryptography.SignatureAlgorithm.Ed25519.Verify(pubKey, hashBytes, sigBytes)) {
            return Results.BadRequest("Invalid asset signature.");
        }
    } catch {
        return Results.BadRequest("Invalid asset signature format.");
    }
    
    var existingMeta = db.Get<JsonDocument>("asset_signatures", hash);
    if (existingMeta == null) {
        var metaDoc = JsonSerializer.SerializeToDocument(new { 
            Signature = signature, 
            AuthorUsername = authorUsername,
            PublicKey = publicKey,
            Hash = hash
        });
        db.Upsert("asset_signatures", hash, metaDoc);
    }
    
    var file = form.Files.GetFile("File");
    if (file != null && file.Length > 0)
    {
        string archiveDir = ".data/assets";
        if (!Directory.Exists(archiveDir))
            Directory.CreateDirectory(archiveDir);
            
        string filePath = Path.Combine(archiveDir, hash);
        if (!File.Exists(filePath))
        {
            using var stream = new FileStream(filePath, FileMode.Create);
            await file.CopyToAsync(stream);
        }
    }
    
    return Results.Ok(new { Status = "Asset registered" });
});

app.MapGet("/api/publish_map/asset_author/{hash}", (string hash, DataStoreService db) => {
    var existingMeta = db.Get<JsonDocument>("asset_signatures", hash);
    if (existingMeta != null) {
        return Results.Ok(existingMeta);
    }
    return Results.NotFound();
});

app.MapGet("/api/data/{collection}/{id}", (string collection, string id, DataStoreService db, HttpContext context) =>
{
    if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader) || !authHeader.ToString().StartsWith("Bearer ")) return Results.Unauthorized();
    var item = db.Get<JsonDocument>(collection, id);
    if (item != null)
    {
        return Results.Ok(item);
    }
    return Results.NotFound(new { Message = "Not found" });
});

app.MapGet("/api/data/{collection}", (string collection, DataStoreService db, HttpContext context) =>
{
    if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader) || !authHeader.ToString().StartsWith("Bearer ")) return Results.Unauthorized();
    var items = db.GetAll<JsonDocument>(collection);
    return Results.Ok(items);
});

app.MapPost("/api/data/{collection}", (string collection, JsonDocument data, DataStoreService db, HttpContext context) =>
{
    if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader) || !authHeader.ToString().StartsWith("Bearer ")) return Results.Unauthorized();
    var id = Guid.NewGuid().ToString("N");
    db.Upsert(collection, id, data);
    return Results.Ok(new { Id = id });
});

app.MapPut("/api/data/{collection}/{id}", (string collection, string id, JsonDocument data, DataStoreService db, HttpContext context) =>
{
    if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader) || !authHeader.ToString().StartsWith("Bearer ")) return Results.Unauthorized();
    db.Upsert(collection, id, data);
    return Results.Ok(new { Status = "Updated" });
});

app.MapDelete("/api/data/{collection}/{id}", (string collection, string id, DataStoreService db, HttpContext context) =>
{
    if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader) || !authHeader.ToString().StartsWith("Bearer ")) return Results.Unauthorized();
    db.Delete(collection, id);
    return Results.Ok(new { Status = "Deleted" });
});

app.MapGet("/api/players", (DataStoreService db, HttpContext context) => 
{
    if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader) || !authHeader.ToString().StartsWith("Bearer ")) return Results.Unauthorized();
    return Results.Ok(db.GetAll<JsonDocument>("players"));
});
app.MapGet("/api/players/{id}", (string id, DataStoreService db, HttpContext context) => 
{
    if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader) || !authHeader.ToString().StartsWith("Bearer ")) return Results.Unauthorized();
    var p = db.Get<JsonDocument>("players", id);
    return p != null ? Results.Ok(p) : Results.NotFound();
});
app.MapPost("/api/players", (JsonDocument data, DataStoreService db, HttpContext context) => 
{
    if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader) || !authHeader.ToString().StartsWith("Bearer ")) return Results.Unauthorized();
    var id = Guid.NewGuid().ToString("N");
    db.Upsert("players", id, data);
    return Results.Ok(new { Id = id });
});
app.MapPut("/api/players/{id}", (string id, JsonDocument data, DataStoreService db, HttpContext context) => 
{
    if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader) || !authHeader.ToString().StartsWith("Bearer ")) return Results.Unauthorized();
    db.Upsert("players", id, data);
    return Results.Ok();
});
app.MapDelete("/api/players/{id}", (string id, DataStoreService db, HttpContext context) => 
{
    if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader) || !authHeader.ToString().StartsWith("Bearer ")) return Results.Unauthorized();
    db.Delete("players", id);
    return Results.Ok();
});

app.MapGet("/api/maps", (DataStoreService db, HttpContext context) => 
{
    if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader) || !authHeader.ToString().StartsWith("Bearer ")) return Results.Unauthorized();
    return Results.Ok(db.GetAll<JsonDocument>("maps"));
});
app.MapGet("/api/maps/{id}", (string id, DataStoreService db, HttpContext context) => 
{
    if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader) || !authHeader.ToString().StartsWith("Bearer ")) return Results.Unauthorized();
    var m = db.Get<JsonDocument>("maps", id);
    return m != null ? Results.Ok(m) : Results.NotFound();
});
app.MapPost("/api/maps", (JsonDocument data, DataStoreService db, HttpContext context) => 
{
    if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader) || !authHeader.ToString().StartsWith("Bearer ")) return Results.Unauthorized();
    var id = Guid.NewGuid().ToString("N");
    db.Upsert("maps", id, data);
    return Results.Ok(new { Id = id });
});
app.MapPut("/api/maps/{id}", (string id, JsonDocument data, DataStoreService db, HttpContext context) => 
{
    if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader) || !authHeader.ToString().StartsWith("Bearer ")) return Results.Unauthorized();
    db.Upsert("maps", id, data);
    return Results.Ok();
});
app.MapDelete("/api/maps/{id}", (string id, DataStoreService db, HttpContext context) => 
{
    if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader) || !authHeader.ToString().StartsWith("Bearer ")) return Results.Unauthorized();
    db.Delete("maps", id);
    return Results.Ok();
});

app.MapGet("/api/admin/bans", (DataStoreService db, HttpContext context) => 
{
    if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader) || !authHeader.ToString().StartsWith("Bearer ")) return Results.Unauthorized();
    return Results.Ok(db.GetAll<JsonDocument>("bans"));
});
app.MapGet("/api/admin/bans/{id}", (string id, DataStoreService db, HttpContext context) => 
{
    if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader) || !authHeader.ToString().StartsWith("Bearer ")) return Results.Unauthorized();
    var b = db.Get<JsonDocument>("bans", id);
    return b != null ? Results.Ok(b) : Results.NotFound();
});
app.MapPost("/api/admin/bans", (JsonDocument data, DataStoreService db, HttpContext context) => 
{
    if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader) || !authHeader.ToString().StartsWith("Bearer ")) return Results.Unauthorized();
    var id = Guid.NewGuid().ToString("N");
    db.Upsert("bans", id, data);
    return Results.Ok(new { Id = id });
});
app.MapPut("/api/admin/bans/{id}", (string id, JsonDocument data, DataStoreService db, HttpContext context) => 
{
    if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader) || !authHeader.ToString().StartsWith("Bearer ")) return Results.Unauthorized();
    db.Upsert("bans", id, data);
    return Results.Ok();
});
app.MapDelete("/api/admin/bans/{id}", (string id, DataStoreService db, HttpContext context) => 
{
    if (!context.Request.Headers.TryGetValue("Authorization", out var authHeader) || !authHeader.ToString().StartsWith("Bearer ")) return Results.Unauthorized();
    db.Delete("bans", id);
    return Results.Ok();
});


app.Map("/seeders/ws", async (HttpContext context, SeederRegistry registry) =>
{
    if (context.WebSockets.IsWebSocketRequest)
    {
        var seederId = context.Request.Query["seederId"].ToString();
        if (string.IsNullOrEmpty(seederId))
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
        registry.AddConnection(seederId, webSocket);
        Console.WriteLine($"[Registry] Seeder connected WebSocket for Seeder {seederId}");

        var buffer = new byte[1024 * 4];
        try
        {
            while (webSocket.State == WebSocketState.Open)
            {
                var result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing", CancellationToken.None);
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Registry] Seeder {seederId} WebSocket error: {ex.Message}");
        }
        finally
        {
            registry.RemoveConnection(seederId);
            Console.WriteLine($"[Registry] Seeder disconnected WebSocket for Seeder {seederId}");
        }
    }
    else
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
    }
});

app.Run();


public class MapMetricsReport
{
    public string MapTitle { get; set; } = "";
    public string MapVersion { get; set; } = "";
    public double PlaytimeMinutes { get; set; }
    public int Stars { get; set; }
    public bool IsCompleteGame { get; set; }
}

public class MapStats
{
    public double TotalPlaytimeMinutes { get; set; }
    public int TotalGamesPlayed { get; set; }
    public int ReviewsCount { get; set; }
    public int TotalStars { get; set; }
    public bool IsGreenlit => TotalPlaytimeMinutes >= 30 && TotalGamesPlayed >= 3 && ReviewsCount >= 100 && (ReviewsCount > 0 && (double)TotalStars / ReviewsCount >= 3.0);
}
