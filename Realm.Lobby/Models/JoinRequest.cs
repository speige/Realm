namespace Realm.Lobby.Models;

public record JoinRequest(
	string LobbyId, 
	string ClientPublicIP, 
	int ClientPublicPort
);