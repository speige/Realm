namespace Realm.MapAPI;

/// <summary>
/// Defines progression configuration settings for heroes, including experience curves, kill rewards, and starting resources.
/// </summary>
public readonly struct HeroProgressionConfig
{
    /// <summary>
    /// Gets the amount of experience required to advance one hero level.
    /// </summary>
    public float XpPerLevel { get; }

    /// <summary>
    /// Gets the amount of gold awarded for killing an enemy unit.
    /// </summary>
    public float KillGold { get; }

    /// <summary>
    /// Gets the amount of experience awarded for killing an enemy unit.
    /// </summary>
    public float KillXp { get; }

    /// <summary>
    /// Gets the zero-based player index associated with this progression configuration.
    /// </summary>
    public int PlayerIndex { get; }

    /// <summary>
    /// Gets the minimum starting gold allocated to the player.
    /// </summary>
    public float MinStartingGold { get; }

    /// <summary>
    /// Gets the identifier of the leaderboard associated with this hero progression.
    /// </summary>
    public string LeaderboardId { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="HeroProgressionConfig"/> struct.
    /// </summary>
    /// <param name="xpPerLevel">The amount of experience required per hero level.</param>
    /// <param name="killGold">The amount of gold awarded per enemy kill.</param>
    /// <param name="killXp">The amount of experience awarded per enemy kill.</param>
    /// <param name="playerIndex">The zero-based player index.</param>
    /// <param name="minStartingGold">The minimum starting gold amount.</param>
    /// <param name="leaderboardId">The identifier of the leaderboard.</param>
    public HeroProgressionConfig(
        float xpPerLevel,
        float killGold,
        float killXp,
        int playerIndex,
        float minStartingGold,
        string leaderboardId)
    {
        XpPerLevel = xpPerLevel;
        KillGold = killGold;
        KillXp = killXp;
        PlayerIndex = playerIndex;
        MinStartingGold = minStartingGold;
        LeaderboardId = leaderboardId;
    }
}

/// <summary>
/// Provides utility calculations for hero kill rewards and level progression.
/// </summary>
public static class HeroKillReward
{
    /// <summary>
    /// Calculates the resulting gold, experience, and level after an enemy kill based on the specified progression configuration.
    /// </summary>
    /// <param name="config">The hero progression configuration settings.</param>
    /// <param name="currentGold">The current amount of gold held prior to the kill.</param>
    /// <param name="currentXp">The current amount of experience accumulated prior to the kill.</param>
    /// <returns>A tuple containing the updated gold, updated experience, and calculated level.</returns>
    public static (float Gold, float Xp, int Level) AfterKill(
        HeroProgressionConfig config, float currentGold, float currentXp)
    {
        var gold = currentGold + config.KillGold;
        var xp = currentXp + config.KillXp;
        var level = 1 + (int)(xp / config.XpPerLevel);
        return (gold, xp, level);
    }
}

