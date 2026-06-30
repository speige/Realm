namespace Realm.Lobby.Models;

public record CloseLobbyRequest(
	string LobbyId, 
	string HostToken
);