using System;

namespace Realm.MapAPI;

/// <summary>
/// Provides convenience extension methods for map authors using the <see cref="IGameAPI"/> interface.
/// </summary>
public static class GameAPIExtensions
{
    /// <summary>
    /// Retrieves the preferred language code of the primary local player.
    /// </summary>
    /// <param name="api">The game API instance.</param>
    /// <returns>ISO language code string (e.g. "en", "es").</returns>
    public static string GetPlayerLanguage(this IGameAPI api)
    {
        return api.GetPlayerLanguage(0);
    }

    /// <summary>
    /// Translates a localization key for the current local player with English fallback.
    /// </summary>
    /// <param name="api">The game API instance.</param>
    /// <param name="key">Localization translation key.</param>
    /// <returns>Translated text string.</returns>
    public static string Translate(this IGameAPI api, string key)
    {
        return api.Translate(key, -1);
    }
}
