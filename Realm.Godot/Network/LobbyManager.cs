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
    }

    public class ServersConfig
    {
        public List<string> RegistryServers { get; set; } = new();
    }

    // Network configurations
    public List<string> RegistryServers { get; private set; } = new() { "http://127.0.0.1:5000" };
    private int _currentServerIndex = 0;
    public string RegistryServerUrl => RegistryServers.Count > 0 ? RegistryServers[_currentServerIndex] : "http://127.0.0.1:5000";
    public int ENetPort { get; private set; } = 8999;
    public int MaxPlayers { get; set; } = 8;
    
    // NAT and State
    public NatType LocalNatType { get; private set; } = NatType.Open;
    public bool IsHost { get; private set; }
    public string? ActiveLobbyId { get; private set; }
    public bool IsGameStarted { get; set; }
    public string ActiveMapName { get; set; } = "green_td";
    public bool SpectatorDelay { get; set; } = false;

    // Player List
    public List<PlayerInfo> PlayerList { get; } = new();
    public PlayerInfo LocalPlayer { get; private set; } = new();

    // Registry / WebSocket State
    private readonly System.Net.Http.HttpClient _httpClient = new();
    private string? _connectedHostIp;
    private ClientWebSocket? _hostWebSocket;
    private CancellationTokenSource? _wsCts;
    private string? _hostPublicIp;
    private int _hostPublicPort;
    private MapDistributionServer? _mapServer;

    public void SwitchToNextServer()
    {
        if (RegistryServers.Count > 1)
        {
            _currentServerIndex = (_currentServerIndex + 1) % RegistryServers.Count;
            GD.Print($"[LobbyManager] Switched to next bootstrap server: {RegistryServerUrl}");
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

    // Events
    public event Action? PlayerListUpdated;
    public event Action<string, string>? ChatReceived;
    public event Action<string>? ConnectionFailed;
    public event Action<string>? KickReceived;
    public event Action? NatTestCompleted;
    public event Action<float>? MapDownloadProgressChanged;
    public event Action? MapDownloadCompleted;
    public event Action? MapDownloadFailed;
    public event Action<bool>? SpectatorDelayChanged;

    public override void _Ready()
    {
        Instance = this;
        ProcessMode = ProcessModeEnum.Always;

        // Load registry URL from servers.json
        LoadServersConfig();

        // Bind Godot multiplayer signals
        Multiplayer.PeerConnected += OnPeerConnected;
        Multiplayer.PeerDisconnected += OnPeerDisconnected;
        Multiplayer.ConnectedToServer += OnConnectedToServer;
        Multiplayer.ConnectionFailed += OnConnectionFailedGodot;
        Multiplayer.ServerDisconnected += OnServerDisconnectedGodot;

        RunNatTypeTest();

        Task.Run(() => MapAssetManager.PruneGlobalArchive());
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
                var config = JsonSerializer.Deserialize<ServersConfig>(jsonText);
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

        // Fallback default
        RegistryServers = new List<string> { "http://127.0.0.1:5000" };
        _currentServerIndex = 0;
        GD.Print($"[LobbyManager] Config servers.json not found or invalid, using fallback: {RegistryServerUrl}");
    }

    public async Task RunNatTypeTestAsync()
    {
        GD.Print("[LobbyManager] Starting STUN NAT Type Test...");
        LocalNatType = await NatTypeTester.DetermineNatTypeAsync(ENetPort);
        GD.Print($"[LobbyManager] NAT Type Classified: {LocalNatType}");
        
        // Query a test to get our public mapped endpoint
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

    // --- LOBBY ACTIONS ---

    public async Task<bool> HostLobbyAsync(string mapName)
    {
        IsHost = true;
        IsGameStarted = false;
        SpectatorDelay = false;
        PlayerList.Clear();

        // 1. Initialize local player as slot 0
        LocalPlayer = new PlayerInfo
        {
            PeerId = 1,
            Slot = 0,
            Name = "Host_Player",
            Faction = "HUMAN",
            Team = "Team 1",
            Color = new Color(0.8f, 0.1f, 0.1f),
            IsHost = true
        };
        PlayerList.Add(LocalPlayer);

        // 2. STUN NAT Type Test required on lobby creation
        await RunNatTypeTestAsync();
        if (LocalNatType == NatType.Symmetric)
        {
            GD.PrintErr("[LobbyManager] Lobby creation rejected: Symmetric NAT is not supported.");
            return false;
        }

        // 3. Initialize ENet Server
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

        // Start diagnostic listener on ENetPort + 1
        Diagnostics.StartHostListener(ENetPort + 1);

        // Start Map Distribution Server on ENetPort + 10
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

        // 4. Register to Registry Server with failover
        try
        {
            var registerPayload = new
            {
                Map = mapName,
                HostPort = _hostPublicPort > 0 ? _hostPublicPort : ENetPort,
                NatType = LocalNatType.ToString(),
                ReportedHostIP = _hostPublicIp ?? "127.0.0.1",
                PasswordHash = "",
                MaxPlayers = MaxPlayers,
                SlotsUsed = PlayerList.Count
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
            GD.Print($"[LobbyManager] Lobby registered on server. LobbyId: {ActiveLobbyId}");

            // 5. Connect WebSocket for incoming client hole punching alerts
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

        // 1. Setup local player info
        LocalPlayer = new PlayerInfo
        {
            PeerId = 0, // Assigned by server
            Slot = -1,
            Name = "Client_Player",
            Faction = "HUMAN",
            Team = "Team 1",
            Color = new Color(0.1f, 0.4f, 0.8f),
            IsHost = false
        };

        // 2. STUN NAT Type Test required on lobby join
        await RunNatTypeTestAsync();

        string clientPublicIp = _hostPublicIp ?? "127.0.0.1";
        int clientPublicPort = _hostPublicPort > 0 ? _hostPublicPort : ENetPort; // Use same local port

        try
        {
            // 3. HTTP Join with failover
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
            _connectedHostIp = hostIp;

            GD.Print($"[LobbyManager] Joined. Host endpoint coordinates: {hostIp}:{hostPort}. Launching hole punch...");

            // 4. UDP Hole Punch directly to host ENet port
            await UdpHolePuncher.PunchHoleAsync(hostIp, hostPort, ENetPort);

            // 5. Initialize ENet Client on ENetPort
            var peer = new ENetMultiplayerPeer();
            var err = peer.CreateClient(hostIp, hostPort, localPort: (hostIp == "127.0.0.1" || hostIp == "localhost") ? 0 : ENetPort);
            if (err != Error.Ok)
            {
                GD.PrintErr($"[LobbyManager] Failed to create ENet Client: {err}");
                ConnectionFailed?.Invoke("Failed to bind network socket.");
                return false;
            }
            Multiplayer.MultiplayerPeer = peer;
            if (Multiplayer is SceneMultiplayer sceneMultiplayer)
            {
                sceneMultiplayer.ServerRelay = false;
            }
            GD.Print($"[LobbyManager] ENet Client initialized. Connecting to {hostIp}:{hostPort}...");
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
        
        // Stop server processes
        Diagnostics.StopHostListener();
        _wsCts?.Cancel();
        _hostWebSocket?.Dispose();
        _hostWebSocket = null;
        ActiveLobbyId = null;

        // Stop map server
        _mapServer?.Stop();
        _mapServer = null;

        // Disconnect ENet
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

    // --- WEBSOCKET & HEARTBEAT FOR HOST ---

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
                            
                            // Punch hole back to client
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

    // --- PEER CONNECTION SIGNAL HANDLERS ---

    private void OnPeerConnected(long peerId)
    {
        int id = (int)peerId;
        GD.Print($"[LobbyManager] Peer connected ENet ID: {id}");

        if (IsHost)
        {
            // 1. Check max players limit
            if (PlayerList.Count >= MaxPlayers)
            {
                GD.Print($"[LobbyManager] Rejecting peer {id}: Lobby is full.");
                RpcId(id, nameof(RejectConnection), "Lobby is full");
                
                // Disconnect peer after brief delay
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

            // 2. Assign slot and create PlayerInfo
            var newPlayer = new PlayerInfo
            {
                PeerId = id,
                Slot = PlayerList.Count,
                Name = $"Player_{id}",
                Faction = "HUMAN",
                Team = "Team 1",
                Color = GetNextColor(),
                IsHost = false
            };
            PlayerList.Add(newPlayer);

            // 3. Broadcast sync message to all peers
            BroadcastPlayerList();
            RpcId(id, nameof(SyncSpectatorDelay), SpectatorDelay);
        }
    }

    private void OnPeerDisconnected(long peerId)
    {
        int id = (int)peerId;
        GD.Print($"[LobbyManager] Peer disconnected ENet ID: {id}");

        if (IsHost)
        {
            // Remove player from list and adjust slot indices
            int removedIdx = PlayerList.FindIndex(p => p.PeerId == id);
            if (removedIdx >= 0)
            {
                PlayerList.RemoveAt(removedIdx);
                // Shift slots down to avoid gaps
                for (int i = 0; i < PlayerList.Count; i++)
                {
                    PlayerList[i].Slot = i;
                }
                BroadcastPlayerList();
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
            // UDP Diagnostics in background
            Task.Run(async () =>
            {
                GD.Print($"[LobbyManager] Running automatic UDP diagnostics against host {_connectedHostIp}...");
                var result = await Diagnostics.RunClientDiagnosticsAsync(_connectedHostIp, ENetPort + 1, (progress) =>
                {
                    UpdateDiagnostics(myId, progress.MinRtt, progress.MaxRtt, progress.AvgRtt, progress.Jitter, progress.LossPercentage, progress.MaxConsecutiveLoss);
                });
                UpdateDiagnostics(myId, result.MinRtt, result.MaxRtt, result.AvgRtt, result.Jitter, result.LossPercentage, result.MaxConsecutiveLoss);
            });

            // Map download in background
            Task.Run(async () =>
            {
                string hostIp = _connectedHostIp;
                int port = ENetPort + 10;
                GD.Print($"[LobbyManager] Triggering map download from host {hostIp}:{port}...");
                var client = new MapDistributionClient();
                client.DownloadProgressChanged += (progress) =>
                {
                    CallDeferred(nameof(EmitDownloadProgress), progress);
                };

                bool success = await client.DownloadMapAsync(hostIp, port, "downloaded_map");
                if (success)
                {
                    CallDeferred(nameof(EmitDownloadCompleted));
                }
                else
                {
                    CallDeferred(nameof(EmitDownloadFailed));
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
        KickReceived?.Invoke("Host closed the server.");
        Disconnect();
    }

    // --- RPCS ---

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
                
                // Update local reference to our own info
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
        }
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void RejectConnection(string reason)
    {
        GD.Print($"[LobbyManager] Connection Rejected: {reason}");
        ConnectionFailed?.Invoke(reason);
        Disconnect();
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
            // Send request to Server/Host
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

    [Rpc(MultiplayerApi.RpcMode.AnyPeer, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    public void SendChatMessage(string senderName, string message)
    {
        if (IsHost)
        {
            // Host relays to everyone
            Rpc(nameof(SendChatMessage), senderName, message);
        }
        
        ChatReceived?.Invoke(senderName, message);
    }

    [Rpc(MultiplayerApi.RpcMode.Authority, CallLocal = true, TransferMode = MultiplayerPeer.TransferModeEnum.Reliable)]
    private void LoadMap(string mapName)
    {
        GD.Print($"[LobbyManager] LoadMap RPC received for: {mapName}");
        ActiveMapName = mapName;

        // Dynamically compile local binaries into a cached .pck file and load it
        MapAssetManager.CompileAndLoadPck(mapName);
        
        // Load map.json mockup load
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

    // --- HELPERS ---

    public void StartGame(string mapName)
    {
        if (IsHost)
        {
            Rpc(nameof(LoadMap), mapName);
        }
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
                PacketLoss = "0%"
            };
            PlayerList.Add(botPlayer);
            BroadcastPlayerList();
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
}
