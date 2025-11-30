namespace Realm.AdminServer.Models;

public record HeartbeatRequest(
	string LobbyId, 
	int SlotsUsed
);