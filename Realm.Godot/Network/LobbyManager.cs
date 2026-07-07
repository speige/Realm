using Godot;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

public partial class LobbyManager : Node
{
    public static LobbyManager Instance { get; private set; }
    public static readonly string GameBinaryVersion = GetGameBinaryVersion();
    public bool IsSinglePlayer { get; set; } = false;

    private static string GetGameBinaryVersion()
    {
        try
        {
            if (OS.HasFeature("editor"))
            {
                var assembly = typeof(LobbyManager).Assembly;
                if (!string.IsNullOrEmpty(assembly.Location))
                {
                    var versionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(assembly.Location);
                    if (!string.IsNullOrEmpty(versionInfo.ProductVersion))
                    {
                        return versionInfo.ProductVersion.Trim();
                    }
                }
                return "1.0.0";
            }

            string exePath = OS.GetExecutablePath();
            if (!string.IsNullOrEmpty(exePath))
            {
                var versionInfo = System.Diagnostics.FileVersionInfo.GetVersionInfo(exePath);
                string version = versionInfo.ProductVersion;
                if (!string.IsNullOrEmpty(version))
                {
                    return version.Trim();
                }
                version = versionInfo.FileVersion;
                if (!string.IsNullOrEmpty(version))
                {
                    return version.Trim();
                }
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"Failed to read version from executable: {ex.Message}");
        }

        return "1.0.0";
    }


    public class PlayerInfo
    {
        public int PeerId { get; set; }
        public int Slot { get; set; }
        public string Name { get; set; } = "";
        public string Faction { get; set; } = "HUMAN";
        public string Team { get; set; } = "Team 1";
        public Color Color { get; set; } = new Color(0.8f, 0.1f, 0.1f);
        public bool IsHost { get; set; }
        public string Latency { get; set; } = "--";
        public string Jitter { get; set; } = "--";
        public string PacketLoss { get; set; } = "--";
        public bool IsReady { get; set; }
        public string BinaryVersion { get; set; } = "";
    }

    public class ServersConfig
    {
        public List<string> RegistryServers { get; set; } = new();
    }


    public List<string> RegistryServers { get; private set; } = new() { "http://127.0.0.1:5000" };
    private int _currentServerIndex = 0;
    public string RegistryServerUrl => RegistryServers.Count > 0 ? RegistryServers[_currentServerIndex] : "http://127.0.0.1:5000";
    public int ENetPort { get; private set; } = 8999;
    public int MaxPlayers { get; set; } = 8;
    
    public string AuthenticatedUsername { get; set; } = "Horaid_Topa";
    public string? AuthToken { get; set; }
    public string? AuthProvider { get; set; }


    public NatType LocalNatType { get; private set; } = NatType.Open;
    public bool IsHost { get; private set; }
    public string? ActiveLobbyId { get; private set; }
    public bool IsGameStarted { get; set; }
    public string ActiveMapName { get; set; } = "green_td";
    public bool SpectatorDelay { get; set; } = false;
    public string? LobbyJoinError { get; set; }
    public string HostStability { get; set; } = "Excellent";
    public event System.Action<string> HostStabilityUpdated;


    public List<PlayerInfo> PlayerList { get; } = new();
    public PlayerInfo LocalPlayer { get; private set; } = new();


    private readonly System.Net.Http.HttpClient _httpClient = new();
    private string? _connectedHostIp;
    private ClientWebSocket? _hostWebSocket;
    private CancellationTokenSource? _wsCts;
    private string? _hostPublicIp;
    private int _hostPublicPort;
    public string PublicIP => _hostPublicIp ?? "127.0.0.1";
    public int PublicPort => _hostPublicPort > 0 ? _hostPublicPort : ENetPort;
    private MapDistributionServer? _mapServer;
    private string? _hostToken;
    private string? _countdownMapName;
    private int _countdownRemaining;
    private SceneTreeTimer? _countdownTimer;
    private SceneTreeTimer? _diagnosticsTimer;
    private readonly List<(string Sender, string Message, bool IsMuted)> _chatHistory = new();

    public void SwitchToNextServer()
    {
        if (RegistryServers.Count > 1)
        {
            _currentServerIndex = (_currentServerIndex + 1) % RegistryServers.Count;
            GD.Print($"[LobbyManager] Switched to next bootstrap server: {RegistryServerUrl}");
        }
    }

    public void RandomizeServerIndex()
    {
        if (RegistryServers.Count > 1)
        {
            _currentServerIndex = Random.Shared.Next(RegistryServers.Count);
            GD.Print($"[LobbyManager] Randomized bootstrap server to: {RegistryServerUrl}");
        }
    }

    public async Task<string?> FetchLobbiesRawAsync()
    {
        for (int i = 0; i < RegistryServers.Count; i++)
        {
            try
            {
                var response = await _httpClient.GetAsync($"{RegistryServerUrl}/lobbies");
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[LobbyManager] Failed to fetch lobbies from {RegistryServerUrl}: {ex.Message}");
            }
            SwitchToNextServer();
        }
        return null;
    }

    public string? ConnectedHostIp => _connectedHostIp;


    public event Action? PlayerListUpdated;
    public event Action<string, string>? ChatReceived;
    public event Action<string>? ConnectionFailed;
    public event Action<string>? KickReceived;
    public event Action? NatTestCompleted;
    public event Action<float>? MapDownloadProgressChanged;
    public event Action? MapDownloadCompleted;
    public event Action? MapDownloadFailed;
    public event Action<bool>? SpectatorDelayChanged;
    public event Action<string, int>? CountdownStarted;
    public event Action<int>? CountdownTick;
    public event Action? CountdownCancelled;
    public event Action? CountdownFinished;
    public event Action<string>? ActiveMapChanged;

    public override void _Ready()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;


        LoadServersConfig();


        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.ConnectionFailed += OnConnectionFailedGodot;
        Multiplayer.ServerDisconnected += OnServerDisconnectedGodot;

        RunNatTypeTest();

        Task.Run(() => MapAssetManager.PruneGlobalArchive());
        AppDomain.CurrentDomain.ProcessExit += OnProcessExit;

        Task.Run(async () =>
        {
            while (true)
            {
                try
                {
                    await Task.Delay(5000);
                    Callable.From(() => PeerSeederManager.Instance.CheckIdleAndSeedStatus()).CallDeferred();
                }
                catch { }
            }
        });
    }

    public override void _Notification(int what)
    {
        if (what == (int)NotificationWMCloseRequest || what == (int)NotificationPredelete)
        {
            CleanUpLobbySynchronously();
        }
        base._Notification(what);
    }

    private void OnProcessExit(object? sender, EventArgs e)
    {
        CleanUpLobbySynchronously();
    }

    private readonly object _cleanupLock = new object();
    private bool _cleanedUp;

    private void CleanUpLobbySynchronously()
    {
        lock (_cleanupLock)
        {
            if (_cleanedUp) return;
            _cleanedUp = true;
        }

        try
        {
            PeerSeederManager.Instance.Stop();
        }
        catch { }

        if (IsHost && !string.IsNullOrEmpty(ActiveLobbyId) && !string.IsNullOrEmpty(_hostToken))
        {
            string lobbyIdToClose = ActiveLobbyId;
            string tokenToClose = _hostToken;
            _hostToken = null;

            try
            {
                GD.Print($"[LobbyManager] Closing lobby {lobbyIdToClose} synchronously before exit...");
                var payload = new { LobbyId = lobbyIdToClose, HostToken = tokenToClose };
                var jsonContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                
                var task = Task.Run(async () =>
                {
                    await _httpClient.PostAsync($"{RegistryServerUrl}/lobbies/close", jsonContent);
                });
                task.Wait(2000);
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[LobbyManager] Failed synchronous lobby close on exit: {ex.Message}");
            }
        }
    }

    private void LoadServersConfig()
    {
        string path = "res://servers.json";
        if (FileAccess.FileExists(path))
        {
            using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            string jsonText = file.GetAsText();
            try
            {
                var config = JsonSerializer.Deserialize<ServersConfig>(jsonText, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (config != null && config.RegistryServers != null && config.RegistryServers.Count > 0)
                {
                    RegistryServers = config.RegistryServers;
                    _currentServerIndex = 0;
                    GD.Print($"[LobbyManager] Loaded registry servers: {string.Join(", ", RegistryServers)}");
                    return;
                }
            }
            catch (Exception ex)
            {
                GD.PrintErr($"[LobbyManager] Error parsing servers.json: {ex.Message}");
            }
        }


        RegistryServers = new List<string> { "http://127.0.0.1:5000" };
        _currentServerIndex = 0;
        GD.Print($"[LobbyManager] Config servers.json not found or invalid, using fallback: {RegistryServerUrl}");
    }

    public async Task RunNatTypeTestAsync()
    {
        GD.Print("[LobbyManager] Starting STUN NAT Type Test...");
        LocalNatType = await NatTypeTester.DetermineNatTypeAsync(ENetPort);
        GD.Print($"[LobbyManager] NAT Type Classified: {LocalNatType}");
        

        try
        {
            var dnsAddresses = await Dns.GetHostAddressesAsync("stun.l.google.com");
            if (dnsAddresses.Length > 0)
            {
                using var udp = new System.Net.Sockets.UdpClient();
                udp.ExclusiveAddressUse = false;
                udp.Client.SetSocketOption(System.Net.Sockets.SocketOptionLevel.Socket, System.Net.Sockets.SocketOptionName.ReuseAddress, true);
                udp.Client.Bind(new System.Net.IPEndPoint(System.Net.IPAddress.Any, ENetPort));
                
                var serverEp = new System.Net.IPEndPoint(dnsAddresses[0], 19302);
                byte[] req = new byte[20];
                req[0] = 0x00; req[1] = 0x01; // Binding Request
                new Random().NextBytes(new Span<byte>(req, 4, 16));
                
                await udp.SendAsync(req, req.Length, serverEp);
                var receiveTask = udp.ReceiveAsync();
                var timeoutTask = Task.Delay(1000);
                if (await Task.WhenAny(receiveTask, timeoutTask) == receiveTask)
                {
                    var response = await receiveTask;
                    var result = new NatTypeTester.StunResult();
                    NatTypeTester.ParseStunResponse(response.Buffer, result);
                    if (result.Success && result.MappedEndPoint != null)
                    {
                        _hostPublicIp = result.MappedEndPoint.Address.ToString();
                        _hostPublicPort = result.MappedEndPoint.Port;
                        GD.Print($"[LobbyManager] Public Endpoint Mapped: {_hostPublicIp}:{_hostPublicPort}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[LobbyManager] Failed to get public IP: {ex.Message}");
        }

        CallDeferred(nameof(EmitNatTestCompleted));
    }

    public void RunNatTypeTest()
    {
        Task.Run(RunNatTypeTestAsync);
    }

    private void EmitNatTestCompleted()
    {
        NatTestCompleted?.Invoke();
    }



    public void HostSinglePlayerGame(string mapPathName, string mapDisplayName)
    {
        IsSinglePlayer = true;
        IsHost = true;
        IsGameStarted = true;
        ActiveMapName = mapPathName;
        PlayerList.Clear();
        
        LocalPlayer = new PlayerInfo
        {
            PeerId = 1,
            Slot = 0,
            Name = AuthenticatedUsername,
            Faction = "HUMAN",
            Team = "Team 1",
            Color = new Color(0.8f, 0.1f, 0.1f),
            IsHost = true,
            Latency = "0 ms",
            Jitter = "0 ms",
            PacketLoss = "0%",
            BinaryVersion = GameBinaryVersion
        };
        PlayerList.Add(LocalPlayer);
        
        Multiplayer.MultiplayerPeer = new OfflineMultiplayerPeer();
        CallDeferred(nameof(LoadMap), mapPathName);
    }

    public async Task<bool> HostLobbyAsync(string mapPathName, string mapDisplayName)
    {
        IsHost = true;
        HostStability = HostStabilityTracker.GetOverallStability();
        IsGameStarted = false;
        SpectatorDelay = false;
        PlayerList.Clear();
        ActiveMapName = mapPathName;


        LocalPlayer = new PlayerInfo
        {
            PeerId = 1,
            Slot = 0,
            Name = AuthenticatedUsername,
            Faction = "HUMAN",
            Team = "Team 1",
            Color = new Color(0.8f, 0.1f, 0.1f),
            IsHost = true,
            Latency = "0 ms",
            Jitter = "0 ms",
            PacketLoss = "0%",
            BinaryVersion = GameBinaryVersion
        };
        PlayerList.Add(LocalPlayer);


        await RunNatTypeTestAsync();
        if (LocalNatType == NatType.Symmetric)
        {
            GD.PrintErr("[LobbyManager] Lobby creation rejected: Symmetric NAT is not supported.");
            return false;
        }


        var peer = new ENetMultiplayerPeer();
        var err = peer.CreateServer(ENetPort, MaxPlayers);
        if (err != Error.Ok)
        {
            GD.PrintErr($"[LobbyManager] Failed to create ENet Server: {err}");
            return false;
        }
        Multiplayer.MultiplayerPeer = peer;
        if (Multiplayer is SceneMultiplayer sceneMultiplayer)
        {
            sceneMultiplayer.ServerRelay = false;
        }
        GD.Print($"[LobbyManager] ENet Server initialized on port {ENetPort}");


        Diagnostics.StartHostListener(ENetPort + 1);
        StartHostDiagnosticsTimer();


        try
        {
            _mapServer?.Stop();
            _mapServer = new MapDistributionServer();
            _mapServer.Start(ENetPort + 10, "Realm.CustomMap/map.json");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[LobbyManager] Failed to start map distribution server: {ex.Message}");
        }


        try
        {
            int hostPingBaseline = await MeasurePingToRegistryAsync();
            string localIpAddress = GetLocalIPAddress();
            var registerPayload = new
            {
                Map = mapDisplayName,
                HostPort = _hostPublicPort > 0 ? _hostPublicPort : ENetPort,
                NatType = LocalNatType.ToString(),
                ReportedHostIP = _hostPublicIp ?? "127.0.0.1",
                PasswordHash = "",
                MaxPlayers = MaxPlayers,
                SlotsUsed = PlayerList.Count,
                HostPingBaseline = hostPingBaseline,
                LocalIP = localIpAddress
            };

            HttpResponseMessage? response = null;
            for (int i = 0; i < RegistryServers.Count; i++)
            {
                try
                {
                    var jsonContent = new StringContent(JsonSerializer.Serialize(registerPayload), Encoding.UTF8, "application/json");
                    response = await _httpClient.PostAsync($"{RegistryServerUrl}/lobbies/register", jsonContent);
                    if (response.IsSuccessStatusCode)
                    {
                        break;
                    }
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[LobbyManager] Register failed on {RegistryServerUrl}: {ex.Message}");
                }
                SwitchToNextServer();
            }

            if (response == null || !response.IsSuccessStatusCode)
            {
                GD.PrintErr($"[LobbyManager] Registry server registration failed on all nodes.");
                return true; // proceed locally even if registration fails
            }

            var respText = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(respText);
            ActiveLobbyId = doc.RootElement.GetProperty("lobbyId").GetString();
            if (doc.RootElement.TryGetProperty("hostToken", out var hostTokenProp))
            {
                _hostToken = hostTokenProp.GetString();
            }
            GD.Print($"[LobbyManager] Lobby registered on server. LobbyId: {ActiveLobbyId}");


            if (!string.IsNullOrEmpty(ActiveLobbyId))
            {
                StartHostWebSocketSignaling(ActiveLobbyId);
                StartHeartbeatLoop(ActiveLobbyId);
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[LobbyManager] Registry registration error: {ex.Message}");
        }

        PlayerListUpdated?.Invoke();
        return true;
    }

    public async Task<bool> JoinLobbyAsync(string lobbyId)
    {
        IsHost = false;
        IsGameStarted = false;
        PlayerList.Clear();


        LocalPlayer = new PlayerInfo
        {
            PeerId = 0, // Assigned by server
            Slot = -1,
            Name = AuthenticatedUsername,
            Faction = "HUMAN",
            Team = "Team 1",
            Color = new Color(0.1f, 0.4f, 0.8f),
            IsHost = false,
            BinaryVersion = GameBinaryVersion
        };


        await RunNatTypeTestAsync();

        string clientPublicIp = _hostPublicIp ?? "127.0.0.1";
        int clientPublicPort = _hostPublicPort > 0 ? _hostPublicPort : ENetPort; // Use same local port

        try
        {

            var joinPayload = new
            {
                LobbyId = lobbyId,
                ClientPublicIP = clientPublicIp,
                ClientPublicPort = clientPublicPort
            };

            GD.Print($"[LobbyManager] Joining Lobby {lobbyId} via registry server...");
            HttpResponseMessage? response = null;
            for (int i = 0; i < RegistryServers.Count; i++)
            {
                try
                {
                    var jsonContent = new StringContent(JsonSerializer.Serialize(joinPayload), Encoding.UTF8, "application/json");
                    response = await _httpClient.PostAsync($"{RegistryServerUrl}/lobbies/join", jsonContent);
                    if (response.IsSuccessStatusCode)
                    {
                        break;
                    }
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[LobbyManager] Join failed on {RegistryServerUrl}: {ex.Message}");
                }
                SwitchToNextServer();
            }

            if (response == null || !response.IsSuccessStatusCode)
            {
                var errorText = response != null ? await response.Content.ReadAsStringAsync() : "All registry nodes offline";
                GD.PrintErr($"[LobbyManager] Failed to join lobby: {errorText}");
                ConnectionFailed?.Invoke("Failed to coordinate join.");
                return false;
            }

            var respText = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(respText);
            string hostIp = doc.RootElement.GetProperty("hostIP").GetString() ?? "";
            int hostPort = doc.RootElement.GetProperty("hostPort").GetInt32();
            string? localIp = null;
            if (doc.RootElement.TryGetProperty("localIP", out var localIpProp))
            {
                localIp = localIpProp.GetString();
            }
            _connectedHostIp = hostIp;

            string connectIp = hostIp;
            int connectPort = hostPort;
            if (!string.IsNullOrEmpty(localIp) && hostIp == clientPublicIp)
            {
                GD.Print($"[LobbyManager] Host is on the same LAN (Public IP: {hostIp}). Connecting to local IP: {localIp}:{ENetPort}");
                connectIp = localIp;
                connectPort = ENetPort;
            }

            bool isLocalConnection = IsPrivateIp(connectIp);
            if (!isLocalConnection)
            {
                await UdpHolePuncher.PunchHoleAsync(connectIp, connectPort, ENetPort);
            }

            var peer = new ENetMultiplayerPeer();
            var err = peer.CreateClient(connectIp, connectPort, localPort: isLocalConnection ? 0 : ENetPort);
            if (err != Error.Ok)
            {
                GD.PrintErr($"[LobbyManager] Failed to create ENet Client: {err}");
                ConnectionFailed?.Invoke("Failed to bind network socket.");
                return false;
            }
            var packetPeer = peer.GetPeer(1);
            if (packetPeer != null)
            {
                packetPeer.SetTimeout(32, 5000, 15000);
            }
            Multiplayer.MultiplayerPeer = peer;
            if (Multiplayer is SceneMultiplayer sceneMultiplayer)
            {
                sceneMultiplayer.ServerRelay = false;
            }
            GD.Print($"[LobbyManager] ENet Client initialized. Connecting to {connectIp}:{connectPort}...");
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[LobbyManager] Join error: {ex.Message}");
            ConnectionFailed?.Invoke(ex.Message);
            return false;
        }

        return true;
    }

    public void Disconnect()
    {
        GD.Print("[LobbyManager] Disconnecting...");
        _countdownRemaining = 0;
        _countdownMapName = null;
        StopHostDiagnosticsTimer();
        _chatHistory.Clear();

        if (IsHost && !string.IsNullOrEmpty(ActiveLobbyId) && !string.IsNullOrEmpty(_hostToken))
        {
            string lobbyIdToClose = ActiveLobbyId;
            string tokenToClose = _hostToken;
            Task.Run(async () =>
            {
                try
                {
                    var payload = new { LobbyId = lobbyIdToClose, HostToken = tokenToClose };
                    var jsonContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
                    await _httpClient.PostAsync($"{RegistryServerUrl}/lobbies/close", jsonContent);
                }
                catch (Exception ex)
                {
                    GD.PrintErr($"[LobbyManager] Failed to close lobby {lobbyIdToClose} on server: {ex.Message}");
                }
            });
            _hostToken = null;
        }
        

        Diagnostics.StopHostListener();
        _wsCts?.Cancel();
        _hostWebSocket?.Dispose();
        _hostWebSocket = null;
        ActiveLobbyId = null;


        _mapServer?.Stop();
        _mapServer = null;


        if (Multiplayer.MultiplayerPeer != null)
        {
            Multiplayer.MultiplayerPeer.Close();
            Multiplayer.MultiplayerPeer = null;
        }

        IsHost = false;
        IsGameStarted = false;
        PlayerList.Clear();
        PlayerListUpdated?.Invoke();
    }



    private void StartHostWebSocketSignaling(string lobbyId)
    {
        _wsCts = new CancellationTokenSource();
        var token = _wsCts.Token;

        Task.Run(async () =>
        {
            _hostWebSocket = new ClientWebSocket();
            var wsUrl = RegistryServerUrl.Replace("http://", "ws://").Replace("https://", "wss://");
            var uri = new Uri($"{wsUrl}/lobbies/ws?lobbyId={lobbyId}");

            try
            {
                await _hostWebSocket.ConnectAsync(uri, token);
                GD.Print("[LobbyManager] Host WebSocket signaling connected.");

                byte[] buffer = new byte[1024 * 4];
                while (_hostWebSocket.State == WebSocketState.Open && !token.IsCancellationRequested)
                {
                    var result = await _hostWebSocket.ReceiveAsync(new ArraySegment<byte>(buffer), token);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        break;
                    }

                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        var json = Encoding.UTF8.GetString(buffer, 0, result.Count);
                        using var doc = JsonDocument.Parse(json);
                        var root = doc.RootElement;
                        if (root.GetProperty("Action").GetString() == "Punch")
                        {
                            string clientIp = root.GetProperty("ClientIP").GetString() ?? "";
                            int clientPort = root.GetProperty("ClientPort").GetInt32();
                            
                            GD.Print($"[LobbyManager] WebSocket signal: incoming client. Punching to {clientIp}:{clientPort}...");
                            

                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    await UdpHolePuncher.PunchHoleAsync(clientIp, clientPort, ENetPort);
                                }
                                catch (Exception ex)
                                {
                                    GD.PrintErr($"[LobbyManager] Async punch to client failed: {ex.Message}");
                                }
                            });
                        }
                    }
                }
            }
            catch (Exception ex) when (ex is OperationCanceledException || ex is ObjectDisposedException) { }
            catch (Exception ex)
            {
                GD.PrintErr($"[LobbyManager] Host WebSocket error: {ex.Message}");
            }
        }, token);
    }

    private void StartHeartbeatLoop(string lobbyId)
    {
        var token = _wsCts!.Token;
        Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    var heartbeat = new { LobbyId = lobbyId, SlotsUsed = PlayerList.Count };
                    var jsonContent = new StringContent(JsonSerializer.Serialize(heartbeat), Encoding.UTF8, "application/json");
                    await _httpClient.PostAsync($"{RegistryServerUrl}/lobbies/heartbeat", jsonContent, token);
                }
                catch { /* Ignore heartbeat errors */ }
                await Task.Delay(1000, token); // every 1s
            }
        }, token);
    }



    private void OnPeerConnected(long peerId)
    {
        int id = (int)peerId;
        GD.Print($"[LobbyManager] Peer connected ENet ID: {id}");

        if (IsHost)
        {

            if (PlayerList.Count >= MaxPlayers)
            {
                GD.Print($"[LobbyManager] Rejecting peer {id}: Lobby is full.");
                RpcId(id, nameof(RejectConnection), "Lobby is full");
                

                var timer = GetTree().CreateTimer(0.1f);
                timer.Timeout += () =>
                {
                    if (Multiplayer.MultiplayerPeer != null)
                    {
                        Multiplayer.MultiplayerPeer.DisconnectPeer(id);
                    }
                };
                return;
            }


            var newPlayer = new PlayerInfo
            {
                PeerId = id,
                Slot = PlayerList.Count,
                Name = $"Player_{id}",
                Faction = "HUMAN",
                Team = "Team 1",
                Color = GetNextColor(),
                IsHost = false,
                BinaryVersion = GameBinaryVersion
            };
            PlayerList.Add(newPlayer);
            SendChatMessage("System", string.Format(Tr("{0} joined the lobby."), newPlayer.Name));


            UpdateAllPeerDiagnostics();
            RpcId(id, nameof(SyncSpectatorDelay), SpectatorDelay);
            RpcId(id, nameof(SyncHostStability), HostStability);
            RpcId(id, nameof(SyncActiveMap), ActiveMapName);
        }
    }

    private void OnPeerDisconnected(long peerId)
    {
        int id = (int)peerId;
        GD.Print($"[LobbyManager] Peer disconnected ENet ID: {id}");

        if (IsHost)
        {

            int removedIdx = PlayerList.FindIndex(p => p.PeerId == id);
            if (removedIdx >= 0)
            {
                var leavingPlayer = PlayerList[removedIdx];
                string name = leavingPlayer.Name;
                PlayerList.RemoveAt(removedIdx);

                for (int i = 0; i < PlayerList.Count; i++)
                {
                    PlayerList[i].Slot = i;
                }
                UpdateAllPeerDiagnostics();
                SendChatMessage("System", string.Format(Tr("{0} left the lobby."), name));
            }
        }
    }

    private void OnConnectedToServer()
    {
        int myId = Multiplayer.GetUniqueId();
        GD.Print($"[LobbyManager] Connected to Host. Assigned local ENet ID: {myId}");
        LocalPlayer.PeerId = myId;
        
        if (!string.IsNullOrEmpty(_connectedHostIp))
        {
            Task.Run(async () =>
            {
                string hostIp = _connectedHostIp;
                int port = ENetPort + 10;
                bool p2pSuccess = false;

                if (!string.IsNullOrEmpty(ActiveMapName))
                {
                    GD.Print($"[LobbyManager] Attempting P2P download for map '{ActiveMapName}'...");
                    var p2pClient = new PeerMapDownloader();
                    p2pClient.DownloadProgressChanged += (progress) =>
                    {
                        CallDeferred(nameof(EmitDownloadProgress), progress);
                    };
                    p2pSuccess = await p2pClient.DownloadMapAsync(ActiveMapName);
                }

                if (p2pSuccess)
                {
                    CallDeferred(nameof(EmitDownloadCompleted));
                }
                else
                {
                    GD.Print("[LobbyManager] P2P map download failed or unavailable. Falling back to HTTP host download...");
                    var client = new MapDistributionClient();
                    client.DownloadProgressChanged += (progress) =>
                    {
                        CallDeferred(nameof(EmitDownloadProgress), progress);
                    };

                    string downloadMapName = !string.IsNullOrEmpty(ActiveMapName) ? ActiveMapName : "downloaded_map";
                    bool success = await client.DownloadMapAsync(hostIp, port, downloadMapName);
                    if (success)
                    {
                        CallDeferred(nameof(EmitDownloadCompleted));
                    }
                    else
                    {
                        CallDeferred(nameof(EmitDownloadFailed));
                    }
                }
            });
        }
    }

    private void EmitDownloadProgress(float progress)
    {
        MapDownloadProgressChanged?.Invoke(progress);
    }

    private void EmitDownloadCompleted()
    {
        MapDownloadCompleted?.Invoke();
    }

    private void EmitDownloadFailed()
    {
        MapDownloadFailed?.Invoke();
    }

    private void OnConnectionFailedGodot()
    {
        GD.PrintErr("[LobbyManager] Godot ENet connection failed.");
        ConnectionFailed?.Invoke("Direct connection handshake failed.");
    }

    private void OnServerDisconnectedGodot()
    {
        GD.Print("[LobbyManager] Host disconnected.");
        if (IsGameStarted)
        {
            GD.Print("[LobbyManager] Allowing local play after host disconnect.");
            return;
        }
        KickReceived?.Invoke("Host closed the server.");
        Disconnect();
    }



    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SyncLobbyData(string serializedData)
    {
        GD.Print($"[LobbyManager] SyncLobbyData received: {serializedData}");
        try
        {
            var newList = JsonSerializer.Deserialize<List<PlayerInfo>>(serializedData);
            if (newList != null)
            {
                PlayerList.Clear();
                PlayerList.AddRange(newList);
                

                int myId = Multiplayer.GetUniqueId();
                var me = PlayerList.Find(p => p.PeerId == myId);
                if (me != null)
                {
                    LocalPlayer = me;
                }

                PlayerListUpdated?.Invoke();
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[LobbyManager] Failed to deserialize sync: {ex.Message}");
            CallDeferred(nameof(HandleSyncDeserializationFailure));
        }
    }

    private void HandleSyncDeserializationFailure()
    {
        Disconnect();
        LobbyJoinError = "Error joining lobby: Game version mismatch with host";
        UIManager.Instance.TransitionTo(GameScreen.LobbyBrowser);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RejectConnection(string reason)
    {
        GD.Print($"[LobbyManager] Connection Rejected: {reason}");
        ConnectionFailed?.Invoke(reason);
        Disconnect();
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SyncActiveMap(string mapName)
    {
        ActiveMapName = mapName;
        ActiveMapChanged?.Invoke(mapName);
    }

    public void UpdateActiveMap(string mapName)
    {
        if (IsHost)
        {
            ActiveMapName = mapName;
            Rpc(nameof(SyncActiveMap), mapName);
            ActiveMapChanged?.Invoke(mapName);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void UpdatePlayerSlot(int peerId, string faction, string team, Color color, string name)
    {
        if (IsHost)
        {
            var p = PlayerList.Find(x => x.PeerId == peerId);
            if (p != null)
            {
                p.Faction = faction;
                p.Team = team;
                p.Color = color;
                p.Name = name;
                BroadcastPlayerList();
            }
        }
        else
        {

            RpcId(1, nameof(UpdatePlayerSlot), peerId, faction, team, color, name);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void UpdateDiagnostics(int peerId, float minRtt, float maxRtt, float avgRtt, float jitter, float lossRate, int consecutiveLoss)
    {
        if (IsHost)
        {
            var p = PlayerList.Find(x => x.PeerId == peerId);
            if (p != null)
            {
                p.Latency = $"{Math.Round(avgRtt)} ms";
                p.Jitter = $"{Math.Round(jitter)} ms";
                p.PacketLoss = $"{Math.Round(lossRate)}% (Burst: {consecutiveLoss})";
                BroadcastPlayerList();
            }
        }
        else
        {
            RpcId(1, nameof(UpdateDiagnostics), peerId, minRtt, maxRtt, avgRtt, jitter, lossRate, consecutiveLoss);
        }
    }
    public void UpdateReadyState(int peerId, bool isReady)
    {
        if (IsHost)
        {
            var p = PlayerList.Find(x => x.PeerId == peerId);
            if (p != null)
            {
                p.IsReady = isReady;
                UpdateAllPeerDiagnostics();
            }
        }
        else
        {
            RpcId(1, nameof(UpdateReadyStateOnHost), peerId, isReady);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void UpdateReadyStateOnHost(int peerId, bool isReady)
    {
        if (IsHost)
        {
            var p = PlayerList.Find(x => x.PeerId == peerId);
            if (p != null)
            {
                p.IsReady = isReady;
                UpdateAllPeerDiagnostics();
            }
        }
    }

    public void SendChatMessage(string senderName, string message)
    {
        if (IsHost)
        {
            if (senderName == "System")
            {
                _chatHistory.Add((senderName, message, false));
                Rpc(nameof(ReceiveChatMessage), senderName, message);
                ChatReceived?.Invoke(senderName, message);
            }
            else
            {
                _ = ProcessAndSendChatMessageAsync(senderName, message);
            }
        }
        else
        {
            RpcId(1, nameof(ReceiveChatMessage), senderName, message);
        }
    }

    private async Task<bool> IsMessageToxicAsync(string message)
    {
        try
        {
            string url = "https://api-inference.huggingface.co/models/unitary/multilingual-toxic-xlm-roberta";
            var payload = new { inputs = message };
            var jsonContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

            using var client = new System.Net.Http.HttpClient();
            // Optional: If an API key is available, it would be added here like:
            // client.DefaultRequestHeaders.Add("Authorization", "Bearer YOUR_API_KEY");
            
            var response = await client.PostAsync(url, jsonContent);
            if (response.IsSuccessStatusCode)
            {
                var result = await response.Content.ReadAsStringAsync();
                
                // Expected format: [[{"label":"toxic","score":0.9}]]
                using var doc = JsonDocument.Parse(result);
                if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                {
                    var innerArray = doc.RootElement[0];
                    if (innerArray.ValueKind == JsonValueKind.Array && innerArray.GetArrayLength() > 0)
                    {
                        var firstResult = innerArray[0];
                        if (firstResult.TryGetProperty("label", out var labelProp) && firstResult.TryGetProperty("score", out var scoreProp))
                        {
                            string label = labelProp.GetString() ?? "";
                            double score = scoreProp.GetDouble();
                            
                            if (label.Equals("toxic", StringComparison.OrdinalIgnoreCase) && score > 0.8)
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            else
            {
                GD.PrintErr($"[LobbyManager] Toxicity API returned {response.StatusCode}");
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[LobbyManager] Toxicity check failed: {ex.Message}");
        }
        
        return false;
    }

    private async Task ProcessAndSendChatMessageAsync(string senderName, string message)
    {
        bool isToxic = await IsMessageToxicAsync(message);
        _chatHistory.Add((senderName, message, isToxic));
        
        if (!isToxic)
        {
            Rpc(nameof(ReceiveChatMessage), senderName, message);
            ChatReceived?.Invoke(senderName, message);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void ReceiveChatMessage(string senderName, string message)
    {
        if (IsHost)
        {
            _ = ProcessAndSendChatMessageAsync(senderName, message);
        }
        else
        {
            ChatReceived?.Invoke(senderName, message);
        }
    }

    public void RequestChatHistory()
    {
        if (!IsHost && Multiplayer.MultiplayerPeer != null && Multiplayer.MultiplayerPeer.GetConnectionStatus() == MultiplayerPeer.ConnectionStatus.Connected)
        {
            RpcId(1, nameof(RequestChatHistoryFromHost));
        }
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RequestChatHistoryFromHost()
    {
        if (IsHost)
        {
            int senderId = Multiplayer.GetRemoteSenderId();
            foreach (var chat in _chatHistory)
            {
                if (!chat.IsMuted)
                {
                    RpcId(senderId, nameof(ReceiveChatMessage), chat.Sender, chat.Message);
                }
            }
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void LoadMap(string mapName)
    {
        GD.Print($"[LobbyManager] LoadMap RPC received for: {mapName}");
        ActiveMapName = mapName;


        MapAssetManager.CompileAndLoadPck(mapName);
        

        string path = "res://map.json";
        if (FileAccess.FileExists(path))
        {
            using var file = FileAccess.Open(path, FileAccess.ModeFlags.Read);
            string jsonText = file.GetAsText();
            GD.Print($"[LobbyManager] Loaded map.json map data successfully:\n{jsonText}");
        }
        else
        {
            GD.PrintErr("[LobbyManager] map.json mockup not found in project directory.");
        }

        IsGameStarted = true;
        
        GetTree().ChangeSceneToFile("res://Main.tscn");
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void UpdateSpectatorDelay(bool enabled)
    {
        if (IsHost)
        {
            SpectatorDelay = enabled;
            Rpc(nameof(SyncSpectatorDelay), enabled);
            SpectatorDelayChanged?.Invoke(enabled);
        }
        else
        {
            RpcId(1, nameof(UpdateSpectatorDelay), enabled);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SyncSpectatorDelay(bool enabled)
    {
        SpectatorDelay = enabled;
        SpectatorDelayChanged?.Invoke(enabled);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void SyncHostStability(string stability)
    {
        HostStability = stability;
        HostStabilityUpdated?.Invoke(stability);
    }



    public void StartGame(string mapName)
    {
        if (IsHost)
        {
            if (_countdownRemaining > 0)
            {
                return;
            }
            Rpc(nameof(BroadcastStartCountdown), mapName);
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void BroadcastStartCountdown(string mapName)
    {
        if (IsHost)
        {
            SendChatMessage("System", string.Format(Tr("Game starting in 5 seconds on map: {0}."), mapName));
        }
        _countdownMapName = mapName;
        _countdownRemaining = 5;
        CountdownStarted?.Invoke(mapName, _countdownRemaining);
        TickCountdown();
    }

    private void TickCountdown()
    {
        if (_countdownRemaining <= 0)
        {
            CountdownFinished?.Invoke();
            if (IsHost && _countdownMapName != null)
            {
                Rpc(nameof(LoadMap), _countdownMapName);
            }
            return;
        }

        _countdownTimer = GetTree().CreateTimer(1.0f);
        _countdownTimer.Timeout += OnCountdownTimerTimeout;
    }

    private void OnCountdownTimerTimeout()
    {
        if (_countdownRemaining > 0)
        {
            _countdownRemaining--;
            CountdownTick?.Invoke(_countdownRemaining);
            TickCountdown();
        }
    }

    public void RequestCancelCountdown()
    {
        Rpc(nameof(BroadcastCancelCountdown));
    }

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void BroadcastCancelCountdown()
    {
        if (IsHost)
        {
            int senderId = Multiplayer.GetRemoteSenderId();
            if (senderId == 0)
            {
                senderId = 1;
            }
            var player = PlayerList.Find(p => p.PeerId == senderId);
            string name = player != null ? player.Name : "Someone";
            SendChatMessage("System", string.Format(Tr("{0} cancelled the countdown."), name));
        }
        _countdownRemaining = 0;
        _countdownMapName = null;
        CountdownCancelled?.Invoke();
    }

    public void AddAIBot()
    {
        if (IsHost && PlayerList.Count < MaxPlayers)
        {
            int botId = -100;
            while (PlayerList.Exists(x => x.PeerId == botId))
            {
                botId--;
            }
            var botPlayer = new PlayerInfo
            {
                PeerId = botId,
                Slot = PlayerList.Count,
                Name = $"AI Bot {Math.Abs(botId) - 99}",
                Faction = "ORC",
                Team = "Team 2",
                Color = GetNextColor(),
                IsHost = false,
                Latency = "0 ms",
                Jitter = "0 ms",
                PacketLoss = "0%",
                BinaryVersion = GameBinaryVersion
            };
            PlayerList.Add(botPlayer);
            BroadcastPlayerList();
            SendChatMessage("System", string.Format(Tr("{0} added to the lobby."), botPlayer.Name));
        }
    }

    public void BootPlayer(int peerId)
    {
        if (IsHost && peerId != 1)
        {
            if (peerId < 0)
            {
                int removedIdx = PlayerList.FindIndex(p => p.PeerId == peerId);
                if (removedIdx >= 0)
                {
                    PlayerList.RemoveAt(removedIdx);
                    for (int i = 0; i < PlayerList.Count; i++)
                    {
                        PlayerList[i].Slot = i;
                    }
                    BroadcastPlayerList();
                }
                return;
            }
            RpcId(peerId, nameof(RejectConnection), "Kicked by Host");
            var timer = GetTree().CreateTimer(0.1f);
            timer.Timeout += () =>
            {
                if (Multiplayer.MultiplayerPeer != null)
                {
                    try
                    {
                        Multiplayer.MultiplayerPeer.DisconnectPeer(peerId);
                    }
                    catch { }
                }
            };
        }
    }

    private void BroadcastPlayerList()
    {
        string serialized = JsonSerializer.Serialize(PlayerList);
        Rpc(nameof(SyncLobbyData), serialized);
        PlayerListUpdated?.Invoke();
    }

    private Color GetNextColor()
    {
        Color[] available = new[]
        {
            new Color(0.1f, 0.4f, 0.8f), // Blue
            new Color(0.1f, 0.7f, 0.2f), // Green
            new Color(0.1f, 0.7f, 0.7f), // Cyan
            new Color(0.5f, 0.5f, 0.5f), // Grey
            new Color(0.6f, 0.2f, 0.8f), // Purple
            new Color(0.9f, 0.8f, 0.1f)  // Yellow
        };
        return available[PlayerList.Count % available.Length];
    }

    public async Task<int> MeasurePingToRegistryAsync()
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var response = await _httpClient.GetAsync($"{RegistryServerUrl}/lobbies");
            stopwatch.Stop();
            if (response.IsSuccessStatusCode)
            {
                return (int)stopwatch.ElapsedMilliseconds;
            }
        }
        catch { }
        return 100;
    }

    private HttpListener? _authHttpListener;

    public async Task<bool> StartOAuthFlowAsync(string provider)
    {
        _authHttpListener?.Stop();
        int port = 8089;
        _authHttpListener = new HttpListener();
        _authHttpListener.Prefixes.Add($"http://localhost:{port}/auth/callback/");
        try
        {
            _authHttpListener.Start();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[LobbyManager] Failed to start local OAuth listener on port {port}: {ex.Message}");
            return false;
        }

        string loginUrl = $"{RegistryServerUrl}/auth/login?provider={provider}&port={port}";
        OS.ShellOpen(loginUrl);

        try
        {
            var context = await _authHttpListener.GetContextAsync();
            var request = context.Request;
            var response = context.Response;

            string? username = request.QueryString["username"];
            string? token = request.QueryString["token"];
            string? returnedProvider = request.QueryString["provider"];

            if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(token))
            {
                AuthenticatedUsername = username;
                AuthToken = token;
                AuthProvider = returnedProvider;

                GD.Print($"[LobbyManager] OAuth Login Success! Provider: {returnedProvider}, User: {username}");

                var successHtml = """
                <!DOCTYPE html>
                <html>
                <head>
                    <title>Login Successful</title>
                    <style>
                        body { background: #0f111a; color: #e2e8f0; font-family: sans-serif; text-align: center; padding-top: 50px; }
                        h1 { color: #ffd700; }
                    </style>
                </head>
                <body>
                    <h1>Login Successful!</h1>
                    <p>You have successfully logged in to Realm. You may close this window and return to the game.</p>
                </body>
                </html>
                """;

                byte[] buffer = Encoding.UTF8.GetBytes(successHtml);
                response.ContentLength64 = buffer.Length;
                response.OutputStream.Write(buffer, 0, buffer.Length);
                response.OutputStream.Close();

                _authHttpListener.Stop();
                return true;
            }
            else
            {
                var errorHtml = "<h1>Login Failed</h1><p>Invalid parameters received.</p>";
                byte[] buffer = Encoding.UTF8.GetBytes(errorHtml);
                response.ContentLength64 = buffer.Length;
                response.OutputStream.Write(buffer, 0, buffer.Length);
                response.OutputStream.Close();
            }
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[LobbyManager] Error handling OAuth callback: {ex.Message}");
        }
        finally
        {
            _authHttpListener?.Stop();
        }

        return false;
    }

    public override void _ExitTree()
    {
        _authHttpListener?.Stop();
        StopHostDiagnosticsTimer();
        base._ExitTree();
    }

    private void StartHostDiagnosticsTimer()
    {
        StopHostDiagnosticsTimer();
        TickHostDiagnostics();
    }

    private void StopHostDiagnosticsTimer()
    {
        _diagnosticsTimer = null;
    }

    private void TickHostDiagnostics()
    {
        if (!IsHost)
        {
            return;
        }

        UpdateAllPeerDiagnostics();

        _diagnosticsTimer = GetTree().CreateTimer(2.0f);
        _diagnosticsTimer.Timeout += OnDiagnosticsTimerTimeout;
    }

    private void OnDiagnosticsTimerTimeout()
    {
        TickHostDiagnostics();
    }

    private void UpdateAllPeerDiagnostics()
    {
        if (Multiplayer.MultiplayerPeer is ENetMultiplayerPeer enetMultiplayer)
        {
            bool changed = false;
            float totalRtt = 0f;
            float totalJitter = 0f;
            float totalLoss = 0f;
            int clientCount = 0;

            foreach (var p in PlayerList)
            {
                if (p.PeerId == 1 || p.PeerId < 0) continue;

                try
                {
                    var peer = enetMultiplayer.GetPeer(p.PeerId);
                    if (peer != null)
                    {
                        float rtt = (float)peer.GetStatistic(ENetPacketPeer.PeerStatistic.RoundTripTime);
                        float jitter = (float)peer.GetStatistic(ENetPacketPeer.PeerStatistic.RoundTripTimeVariance);
                        float loss = (float)peer.GetStatistic(ENetPacketPeer.PeerStatistic.PacketLoss) / 65536.0f * 100.0f;

                        totalRtt += rtt;
                        totalJitter += jitter;
                        totalLoss += loss;
                        clientCount++;

                        string newLatency = $"{Math.Round(rtt)} ms";
                        string newJitter = $"{Math.Round(jitter)} ms";
                        string newLoss = $"{Math.Round(loss)}%";

                        if (p.Latency != newLatency || p.Jitter != newJitter || p.PacketLoss != newLoss)
                        {
                            p.Latency = newLatency;
                            p.Jitter = newJitter;
                            p.PacketLoss = newLoss;
                            changed = true;
                        }
                    }
                }
                catch { }
            }

            var hostInfo = PlayerList.Find(x => x.PeerId == 1);
            if (hostInfo != null)
            {
                string hostLatency = "0 ms";
                string hostJitter = "0 ms";
                string hostLoss = "0%";

                if (clientCount > 0)
                {
                    hostLatency = $"{Math.Round(totalRtt / clientCount)} ms";
                    hostJitter = $"{Math.Round(totalJitter / clientCount)} ms";
                    hostLoss = $"{Math.Round(totalLoss / clientCount)}%";
                }

                if (hostInfo.Latency != hostLatency || hostInfo.Jitter != hostJitter || hostInfo.PacketLoss != hostLoss)
                {
                    hostInfo.Latency = hostLatency;
                    hostInfo.Jitter = hostJitter;
                    hostInfo.PacketLoss = hostLoss;
                    changed = true;
                }
            }

            if (changed)
            {
                BroadcastPlayerList();
            }
        }
        else
        {
            BroadcastPlayerList();
        }
    }

    private static string GetLocalIPAddress()
    {
        try
        {
            using (var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Dgram, 0))
            {
                socket.Connect("8.8.8.8", 65530);
                var endPoint = socket.LocalEndPoint as IPEndPoint;
                if (endPoint != null)
                {
                    return endPoint.Address.ToString();
                }
            }
        }
        catch
        {
            try
            {
                var host = Dns.GetHostEntry(Dns.GetHostName());
                foreach (var ip in host.AddressList)
                {
                    if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        return ip.ToString();
                    }
                }
            }
            catch { }
        }
        return "127.0.0.1";
    }

    private static bool IsPrivateIp(string ip)
    {
        if (ip == "127.0.0.1" || ip == "localhost") return true;
        if (IPAddress.TryParse(ip, out var address))
        {
            byte[] bytes = address.GetAddressBytes();
            if (bytes.Length == 4)
            {
                if (bytes[0] == 10) return true;
                if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true;
                if (bytes[0] == 192 && bytes[1] == 168) return true;
            }
        }
        return false;
    }
}
