namespace Realm.MapAPI;

public readonly struct HeroProgressionConfig
{
    public float XpPerLevel { get; }
    public float KillGold { get; }
    public float KillXp { get; }
    public int PlayerIndex { get; }
    public float MinStartingGold { get; }
    public string LeaderboardId { get; }

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

public static class HeroKillReward
{
    public static (float Gold, float Xp, int Level) AfterKill(
        HeroProgressionConfig config, float currentGold, float currentXp)
    {
        var gold = currentGold + config.KillGold;
        var xp = currentXp + config.KillXp;
        var level = 1 + (int)(xp / config.XpPerLevel);
        return (gold, xp, level);
    }
}
