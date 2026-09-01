using System.Collections.Generic;

public class MapManifest
{
	public string MapName { get; set; } = "";
	public Dictionary<string, string> Files { get; set; } = new(); // virtualPath -> {{blake3}}.{{fileExtension}}
}