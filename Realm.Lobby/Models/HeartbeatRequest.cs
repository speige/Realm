namespace Realm.Lobby.Models;

public record HeartbeatRequest(
	string LobbyId, 
	int SlotsUsed
);