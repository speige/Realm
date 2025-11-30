using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using MaxMind.GeoIP2;
using MaxMind.GeoIP2.Exceptions;

var builder = WebApplication.CreateBuilder(args);

// Add services
builder.Services.AddSingleton<LobbyRegistry>();
builder.Services.AddSingleton<GeoIpService>();
builder.Services.AddHttpClient();

// Add Peer Registry configuration
var selfUrl = builder.Configuration.GetValue<string>("SelfUrl");
var peersStr = builder.Configuration.GetValue<string>("Peers") ?? "";
var peerUrls = peersStr.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

var peerRegistry = new PeerRegistry
{
    SelfUrl = selfUrl,
    PeerUrls = peerUrls
};
builder.Services.AddSingleton(peerRegistry);

// Allow all CORS for testing client requests
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

// Keep track of websocket sessions for hosts
var hostConnections = new ConcurrentDictionary<string, WebSocket>();

// Prune inactive lobbies background task
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

// REST ENDPOINTS

// GET /lobbies - Returns a list of active lobbies, with distance-based geo-ping calculation.
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
        
        // Simulating 1ms per 100km, with a baseline of 15ms
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
            lobby.OriginServerUri
        );
    });

    return Results.Ok(list);
});

// POST /lobbies/register - Saves lobby info and propagates to peers
app.MapPost("/lobbies/register", async (RegisterRequest req, LobbyRegistry registry, PeerRegistry peerRegistry, GeoIpService geoIp, IHttpClientFactory httpClientFactory, HttpContext context) =>
{
    // NAT type check: reject Symmetric NAT
    if (!string.IsNullOrEmpty(req.NatType) && req.NatType.Equals("Symmetric", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new { Message = "Lobby creation rejected: Symmetric NAT is not supported." });
    }

    var hostIp = context.Connection.RemoteIpAddress?.MapToIPv4().ToString() ?? "127.0.0.1";
    
    // If the host explicitly reports their public IP (e.g. from STUN), we can use it.
    if (!string.IsNullOrEmpty(req.ReportedHostIP) && req.ReportedHostIP != "0.0.0.0" && req.ReportedHostIP != "127.0.0.1")
    {
        hostIp = req.ReportedHostIP;
    }

    var hostCoords = geoIp.GetCoordinates(hostIp);
    var lobbyId = Guid.NewGuid().ToString();

    var info = new LobbyInfo
    {
        LobbyId = lobbyId,
        Map = req.Map,
        HostIP = hostIp,
        HostPort = req.HostPort,
        NatType = req.NatType,
        PasswordHash = req.PasswordHash ?? "",
        MaxPlayers = req.MaxPlayers,
        SlotsUsed = req.SlotsUsed,
        Latitude = hostCoords.lat,
        Longitude = hostCoords.lon,
        LastHeartbeat = DateTime.UtcNow,
        OriginServerUri = peerRegistry.SelfUrl
    };

    registry.AddOrUpdate(info);

    // Propagate registration to all configured peer servers
    _ = Task.Run(async () =>
    {
        using var httpClient = httpClientFactory.CreateClient();
        foreach (var peerUrl in peerRegistry.PeerUrls)
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

    return Results.Ok(new { LobbyId = lobbyId, Latitude = hostCoords.lat, Longitude = hostCoords.lon });
});

// POST /lobbies/propagate - Receives propagated lobby info from another node
app.MapPost("/lobbies/propagate", (LobbyInfo propagatedLobby, LobbyRegistry registry) =>
{
    // NAT check on propagated metadata
    if (!string.IsNullOrEmpty(propagatedLobby.NatType) && propagatedLobby.NatType.Equals("Symmetric", StringComparison.OrdinalIgnoreCase))
    {
        return Results.BadRequest(new { Message = "Symmetric NAT lobbies are not supported." });
    }

    registry.AddOrUpdate(propagatedLobby);
    Console.WriteLine($"[Registry] Received propagated lobby {propagatedLobby.LobbyId} from {propagatedLobby.OriginServerUri}");
    return Results.Ok(new { Status = "Propagated" });
});

// POST /lobbies/heartbeat - Host pings this every 1 second
app.MapPost("/lobbies/heartbeat", async (HeartbeatRequest req, LobbyRegistry registry, PeerRegistry peerRegistry, IHttpClientFactory httpClientFactory) =>
{
    if (registry.TryGet(req.LobbyId, out var lobby) && lobby != null)
    {
        lobby.LastHeartbeat = DateTime.UtcNow;
        lobby.SlotsUsed = req.SlotsUsed;

        // Propagate state update (heartbeat/slots) to peers
        _ = Task.Run(async () =>
        {
            using var httpClient = httpClientFactory.CreateClient();
            foreach (var peerUrl in peerRegistry.PeerUrls)
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

// POST /lobbies/join - Coordinates hole punching
app.MapPost("/lobbies/join", async (JoinRequest req, LobbyRegistry registry, IHttpClientFactory httpClientFactory) =>
{
    if (!registry.TryGet(req.LobbyId, out var lobby) || lobby == null)
    {
        return Results.NotFound(new { Message = "Lobby not found" });
    }

    // Try to find Host WebSocket locally to relay client coordinates
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
        
        return Results.Ok(new JoinResponseDto(lobby.HostIP, lobby.HostPort));
    }
    else
    {
        // Host is not connected locally. Relay the join request to the origin server.
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

// WebSocket Endpoint /lobbies/ws?lobbyId=xxx - Connection for host signaling
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
            // Keep alive / receive loop
        }

        hostConnections.TryRemove(lobbyId, out _);
        Console.WriteLine($"[Registry] Host disconnected WebSocket for Lobby {lobbyId}");
    }
    else
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
    }
});

app.Run();

// DTOs & Models

public record RegisterRequest(string Map, int HostPort, string NatType, string? ReportedHostIP, string? PasswordHash, int MaxPlayers, int SlotsUsed);
public record HeartbeatRequest(string LobbyId, int SlotsUsed);
public record JoinRequest(string LobbyId, string ClientPublicIP, int ClientPublicPort);
public record JoinResponseDto(string HostIP, int HostPort);
public record LobbyResponseDto(string LobbyId, string Map, string HostIP, int HostPort, string NatType, int SlotsUsed, int MaxPlayers, double Latitude, double Longitude, double DistanceKm, int EstimatedPingMs, string? OriginServerUri);

public class PeerRegistry
{
    public string? SelfUrl { get; set; }
    public List<string> PeerUrls { get; set; } = new();
}

public class LobbyInfo
{
    public required string LobbyId { get; set; }
    public required string Map { get; set; }
    public required string HostIP { get; set; }
    public required int HostPort { get; set; }
    public required string NatType { get; set; }
    public required string PasswordHash { get; set; }
    public required int MaxPlayers { get; set; }
    public required int SlotsUsed { get; set; }
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public DateTime LastHeartbeat { get; set; }
    public string? OriginServerUri { get; set; }
}

public class LobbyRegistry
{
    private readonly ConcurrentDictionary<string, LobbyInfo> _lobbies = new();

    public void AddOrUpdate(LobbyInfo info)
    {
        _lobbies[info.LobbyId] = info;
    }

    public bool TryGet(string lobbyId, out LobbyInfo? info)
    {
        return _lobbies.TryGetValue(lobbyId, out info);
    }

    public IEnumerable<LobbyInfo> GetAllLobbies()
    {
        return _lobbies.Values;
    }

    public List<string> PruneExpiredLobbies(TimeSpan expiry)
    {
        var cutoff = DateTime.UtcNow - expiry;
        var expiredIds = new List<string>();

        foreach (var (id, lobby) in _lobbies)
        {
            if (lobby.LastHeartbeat < cutoff)
            {
                expiredIds.Add(id);
            }
        }

        foreach (var id in expiredIds)
        {
            _lobbies.TryRemove(id, out _);
            Console.WriteLine($"[Registry] Pruned expired lobby {id}");
        }

        return expiredIds;
    }
}

public class GeoIpService
{
    private readonly DatabaseReader? _reader;

    public GeoIpService()
    {
        var dbPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "GeoLite2-City.mmdb");
        if (File.Exists(dbPath))
        {
            try
            {
                _reader = new DatabaseReader(dbPath);
                Console.WriteLine("[GeoIP] Loaded GeoLite2 database.");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[GeoIP] Failed to load GeoLite2 database: {ex.Message}");
            }
        }
        else
        {
            Console.WriteLine("[GeoIP] GeoLite2-City.mmdb not found. Falling back to simulation mode.");
        }
    }

    public (double lat, double lon) GetCoordinates(string ipAddress)
    {
        if (_reader != null && IPAddress.TryParse(ipAddress, out var ip) && !IPAddress.IsLoopback(ip))
        {
            try
            {
                var city = _reader.City(ip);
                if (city.Location.Latitude.HasValue && city.Location.Longitude.HasValue)
                {
                    return (city.Location.Latitude.Value, city.Location.Longitude.Value);
                }
            }
            catch (AddressNotFoundException) { /* Fallback */ }
            catch (Exception ex)
            {
                Console.WriteLine($"[GeoIP] Lookup error: {ex.Message}");
            }
        }

        // Simulated GeoIP Location based on IP Address hashing
        if (ipAddress == "127.0.0.1" || ipAddress == "localhost")
        {
            // Default center: Washington DC
            return (38.9072, -77.0369);
        }

        // Let's bucket IPs based on first octet to place them around the world
        string[] parts = ipAddress.Split('.');
        if (parts.Length > 0 && int.TryParse(parts[0], out int firstOctet))
        {
            int bucket = firstOctet % 4;
            return bucket switch
            {
                0 => (37.7749, -122.4194), // US West (San Francisco)
                1 => (40.7128, -74.0060),  // US East (New York)
                2 => (51.5074, -0.1278),   // Europe (London)
                3 => (35.6762, 139.6503),  // Asia (Tokyo)
                _ => (38.9072, -77.0369)
            };
        }

        return (38.9072, -77.0369);
    }

    public static double CalculateDistance(double lat1, double lon1, double lat2, double lon2)
    {
        var R = 6371; // In kilometers
        var dLat = ToRadians(lat2 - lat1);
        var dLon = ToRadians(lon2 - lon1);
        var a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                Math.Cos(ToRadians(lat1)) * Math.Cos(ToRadians(lat2)) *
                Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return R * c;
    }

    private static double ToRadians(double val) => (Math.PI / 180) * val;
}
