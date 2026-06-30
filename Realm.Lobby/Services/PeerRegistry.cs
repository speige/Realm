namespace Realm.Lobby.Services;

public class PeerRegistry
{
    public string? SelfUrl { get; set; }
    public List<string> PeerUrls { get; set; } = new();
}
