using System;
using System.Collections.Generic;
using System.Linq;
using MelonLoader;

namespace MapResourceMarkers;

/// <summary>User settings, stored in UserData/MelonPreferences.cfg under [MapResourceMarkers].</summary>
internal static class Prefs
{
    public static MelonPreferences_Entry<bool> Enabled = null!;
    public static MelonPreferences_Entry<string> Selected = null!;
    public static MelonPreferences_Entry<bool> ShowInUnrevealed = null!;
    public static MelonPreferences_Entry<float> MarkerScale = null!;
    public static MelonPreferences_Entry<bool> AddSettingsRows = null!;
    public static MelonPreferences_Entry<bool> Debug = null!;
    public static MelonPreferences_Entry<string> Unlocked = null!;
    public static MelonPreferences_Entry<bool> ShowPossible = null!;
    private static readonly Dictionary<MarkerCategory, MelonPreferences_Entry<bool>> Categories = new();

    /// <summary>The group's buttons are in the Markers row and its markers on the map (its category is switched on).</summary>
    public static bool GroupOn(MarkerGroup g)
    {
        foreach (var c in MarkerGroups.Categories)
            if (MarkerGroups.In(g, c) && CategoryOn(c)) return true;
        return false;
    }

    public static bool CategoryOn(MarkerCategory c) => !Categories.TryGetValue(c, out var e) || e.Value;
    public static MelonPreferences_Entry<bool> CategoryEntry(MarkerCategory c) => Categories[c];

    private static HashSet<string> _selected = new();

    public static void Init()
    {
        var cat = MelonPreferences.CreateCategory("MapResourceMarkers", "Map Resource Markers");
        Enabled = cat.CreateEntry("Enabled", true, description: "Master switch.");
        Selected = cat.CreateEntry("Selected", "",
            description: "Resource types shown on the map, comma separated: " + string.Join(", ", MarkerTypes.All.Select(t => t.Id)) +
                         ". Toggled from the Markers row of the map's Filters menu.");
        ShowInUnrevealed = cat.CreateEntry("ShowInUnrevealed", true,
            description: "Also show markers in parts of the map you have not explored yet; this might spoil the fun of exploring. " +
                         "Also a checkbox in Options > Gameplay.");
        // Not "MarkerSize": that one meant a smaller base size; a new key starts everyone at the new default.
        MarkerScale = cat.CreateEntry("MarkerScale", 1f,
            description: "Marker size, 1 = default (0.5..2). Also a slider in Options > Gameplay.");
        AddSettingsRows = cat.CreateEntry("AddSettingsRows", true,
            description: "Add this mod's rows to Options > Gameplay.");
        Debug = cat.CreateEntry("Debug", false,
            description: "Log details for troubleshooting: indexing, area variants, cave rolls, live reads, timings.");
        foreach (var c in MarkerGroups.Categories)
            Categories[c] = cat.CreateEntry("Show" + c, MarkerGroups.DefaultOn(c),
                description: MarkerGroups.Description(c) + " Also a checkbox in Options > Gameplay.");
        ShowPossible = cat.CreateEntry("ShowPossible", true,
            description: "Show \"?\" markers (spots that only may have the type when you get there). Toggled by the \"?\" " +
                         "button next to the Markers title in the map's Filters menu.");
        Unlocked = cat.CreateEntry("Unlocked", "",
            description: "Per world: marker types that have had something to show, so their buttons stay in the Markers row. " +
                         "Format: worldId=id,id;worldId=id,... Written by the mod.");
        _selected = new HashSet<string>(Selected.Value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        // 0.1 prototype had one "fish" type; it became carp / trout / bass.
        if (_selected.Remove("fish")) { _selected.Add("carp"); _selected.Add("trout"); _selected.Add("bass"); }
        // Prototype names of two enemy kinds; now the game's own (enemyTypeLocalization: tag 25 False Ones, 26 Harvesters).
        if (_selected.Remove("corruptcerim")) _selected.Add("falseones");
        if (_selected.Remove("lumberjack")) _selected.Add("harvesters");
    }

    public static bool IsSelected(MarkerType type) => _selected.Contains(type.Id);

    private static Dictionary<string, HashSet<string>>? _unlocked;

    private static Dictionary<string, HashSet<string>> UnlockedByWorld()
    {
        if (_unlocked != null) return _unlocked;
        _unlocked = new Dictionary<string, HashSet<string>>();
        foreach (var world in Unlocked.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = world.IndexOf('=');
            if (eq != 32) continue;   // world = 32-hex realm guid; prototype keys and "" (unknown world, session only) are dropped
            _unlocked[world.Substring(0, eq)] = new HashSet<string>(
                world.Substring(eq + 1).Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        }
        return _unlocked;
    }

    public static bool IsUnlocked(string world, MarkerType type) =>
        UnlockedByWorld().TryGetValue(world, out var ids) && ids.Contains(type.Id);

    /// <summary>Remember that the type had something to show in this world. True when it is new (call SaveUnlocked after a
    /// batch).</summary>
    public static bool Unlock(string world, MarkerType type)
    {
        var all = UnlockedByWorld();
        if (!all.TryGetValue(world, out var ids)) all[world] = ids = new HashSet<string>();
        if (!ids.Add(type.Id)) return false;
        Unlocked.Value = string.Join(";", all.Select(w =>
            w.Key + "=" + string.Join(",", MarkerTypes.All.Where(t => w.Value.Contains(t.Id)).Select(t => t.Id))));
        return true;
    }

    public static void SaveUnlocked() => MelonPreferences.Save();

    public static void SetSelected(MarkerType type, bool on)
    {
        if (on ? !_selected.Add(type.Id) : !_selected.Remove(type.Id)) return;
        // Keep the catalogue order so the file stays readable.
        Selected.Value = string.Join(",", MarkerTypes.All.Where(t => _selected.Contains(t.Id)).Select(t => t.Id));
        MelonPreferences.Save();
    }
}
