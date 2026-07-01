namespace Realm.Ecs.Components.Core;

/// <summary>
/// Represents the properties of a single player within a map script.
/// </summary>
internal struct ScriptPlayer
{
	public float Gold;
	public float Wood;
	public bool Active;
	public string Name;
	public int KillCount;
}

/// <summary>
/// Stores the simulation state for all 12 script players.
/// </summary>
internal struct ScriptPlayersState
{
	public ScriptPlayersState(ScriptPlayer[] players)
	{
		Players = players;
	}

	public ScriptPlayer[] Players { get; }
}
