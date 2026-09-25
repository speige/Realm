using System;
using System.Collections.Generic;

namespace Realm.Lobby.Services;

public static class MapStatsHelper
{
    public static MapStats? GetStats(DataStoreService dataStore, string mapTitle, string? mapVersion = null, string? authorPublicKey = null)
    {
        if (string.IsNullOrWhiteSpace(mapTitle))
        {
            return null;
        }

        string trimmedTitle = mapTitle.Trim();
        string trimmedVersion = string.IsNullOrWhiteSpace(mapVersion) ? "1.0" : mapVersion.Trim();
        string compositeKey = $"{trimmedTitle}_{trimmedVersion}";
        string authorCompositeKey = !string.IsNullOrEmpty(authorPublicKey) ? $"{trimmedTitle}_{trimmedVersion}_{authorPublicKey.Trim()}" : compositeKey;
        string authorKey = !string.IsNullOrEmpty(authorPublicKey) ? $"{trimmedTitle}_{authorPublicKey.Trim()}" : trimmedTitle;

        var stats = (!string.IsNullOrEmpty(authorPublicKey) ? dataStore.Get<MapStats>("map_stats", authorCompositeKey) : null)
            ?? (!string.IsNullOrEmpty(authorPublicKey) ? dataStore.Get<MapStats>("map_stats", authorKey) : null)
            ?? dataStore.Get<MapStats>("map_stats", compositeKey)
            ?? dataStore.Get<MapStats>("map_stats", trimmedTitle)
            ?? (!string.IsNullOrEmpty(authorPublicKey) ? dataStore.Get<MapStats>("map_stats", authorCompositeKey.ToLowerInvariant()) : null)
            ?? (!string.IsNullOrEmpty(authorPublicKey) ? dataStore.Get<MapStats>("map_stats", authorKey.ToLowerInvariant()) : null)
            ?? dataStore.Get<MapStats>("map_stats", compositeKey.ToLowerInvariant())
            ?? dataStore.Get<MapStats>("map_stats", trimmedTitle.ToLowerInvariant());

        if (stats != null)
        {
            return stats;
        }

        var allStats = dataStore.GetAllWithKeys<MapStats>("map_stats");
        foreach (var pair in allStats)
        {
            if (string.Equals(pair.Key, authorCompositeKey, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(pair.Key, authorKey, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(pair.Key, compositeKey, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(pair.Key, trimmedTitle, StringComparison.OrdinalIgnoreCase))
            {
                return pair.Value;
            }
        }

        return null;
    }

    public static bool IsMapGreenlit(DataStoreService dataStore, string mapTitle, string? mapVersion = null, string? authorPublicKey = null)
    {
        var stats = GetStats(dataStore, mapTitle, mapVersion, authorPublicKey);
        return stats != null && stats.IsGreenlit;
    }
}
