using System.Collections.Generic;
using UnityEngine;

namespace MapResourceMarkers;

/// <summary>Loaded game sprites by name (e.g. the map glyphs SkullMapIcon, QuestionMapIcon). A scan of all loaded sprites
/// caches every name it sees; a name still missing is rescanned at most every 30 s.</summary>
internal static class GameSprites
{
    private static readonly Dictionary<string, Sprite> Cache = new();
    private static float _lastScan = float.NegativeInfinity;

    public static Sprite? Find(string name)
    {
        if (Cache.TryGetValue(name, out var s) && s != null) return s;
        if (Time.unscaledTime - _lastScan < 30f) return null;
        _lastScan = Time.unscaledTime;
        foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
            if (sprite != null) Cache[sprite.name] = sprite;
        if (Cache.TryGetValue(name, out s) && s != null) return s;
        MapResourceMarkersMod.Debug($"Sprite '{name}' not loaded yet; retrying later.");
        return null;
    }
}
