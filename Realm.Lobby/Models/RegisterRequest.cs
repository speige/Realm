namespace Realm.Lobby.Models;

public record RegisterRequest(
    string Map, 
    int HostPort, 
    string NatType, 
    string? ReportedHostIP, 
    string? PasswordHash, 
    int MaxPlayers, 
    int SlotsUsed, 
    int HostPingBaseline,
    string? LocalIP = null
);