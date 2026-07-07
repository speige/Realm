namespace Realm.Lobby.Models;

public record LobbyResponseDto(
	string LobbyId, 
	string Map, 
	string HostIP, 
	int HostPort, 
	string NatType, 
	int SlotsUsed, 
	int MaxPlayers, 
	double Latitude, 
	double Longitude, 
	double DistanceKm, 
	int EstimatedPingMs, 
	string? OriginServerUri, 
	int HostPingBaseline,
	string? LocalIP = null
);