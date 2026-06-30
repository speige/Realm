namespace Realm.Lobby.Models;

public record SeederDownloadRequest(
	string MapId, 
	string ClientPublicIP, 
	int ClientPublicPort
);