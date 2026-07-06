using System.Collections.Generic;

namespace Realm.Lobby.Models;

public class PublishMapRequest
{
    public string MapJson { get; set; } = "";
    public List<string> ReferencedHashes { get; set; } = new();
}
