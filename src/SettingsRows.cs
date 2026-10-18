using System.Linq;
using Il2CppMoon.Forsaken;
using MelonLoader;
using NrftwShared;
using UnityEngine;

namespace MapResourceMarkers;

/// <summary>
/// Our rows at the end of Options > Gameplay, after a divider (same pattern as Dailies Reset Timer and Display &amp; UI
/// Tweaks), built with the shared kit (src/Shared/SettingsRowsKit.cs), which also removes them. The registry keys rows by
/// LocalizedMessage.Id, so adding a row twice throws after the prefab is instantiated: rows are found by name before adding.
/// </summary>
internal static class SettingsRows
{
    private const string Prefix = "MRM_";
    private const PlayerSettingCategory Category = PlayerSettingCategory.Gameplay;
    private const string SpacerId = "MRM_Spacer", UnexploredId = "MRM_Unexplored", SizeId = "MRM_MarkerSize";

    private static string CategoryId(MarkerCategory c) => "MRM_Show" + c;

    private static readonly SettingsRowsKit Kit = new SettingsRowsKit(Prefix,
        new[] { SpacerId, UnexploredId, SizeId }.Concat(MarkerGroups.Categories.Select(CategoryId)).ToArray(),
        () => MapResourceMarkersMod.Log, SettingsRowsKit.GameplayTabControls);

    /// <summary>Add the rows to every settings screen that already exists (after a hot reload).</summary>
    public static void AddToLiveScreens() => Kit.AddToLiveScreens(AddTo);

    public static void AddTo(SettingsScreenControls? controls)
    {
        var rows = Kit.Begin(controls, Category, UnexploredId);
        if (rows == null) return;

        rows.Spacer(SpacerId);
        rows.Toggle(UnexploredId, "Resource Markers in Unexplored Areas",
            "Also show markers in parts of the map you have not explored yet, e.g. ore veins or enemy camps beyond the fog. " +
            "This might spoil the fun of exploring (Map Resource Markers).",
            Prefs.ShowInUnrevealed, _ => MapOverlay.Refresh());
        // Snaps to the step so a drag lands on 50%, 60%, ... like the arrow keys do.
        rows.Slider(SizeId, "Resource Marker Size",
            "Size of the resource markers on the map (Map Resource Markers).",
            0.5f, 2f, 0.1f, Prefs.MarkerScale.Value, v => Mathf.RoundToInt(v * 100f) + "%",
            v => { Prefs.MarkerScale.Value = v; MelonPreferences.Save(); });
        foreach (var c in MarkerGroups.Categories)
            rows.Toggle(CategoryId(c), "Map Markers: " + MarkerGroups.Label(c),
                MarkerGroups.Description(c) + " (Map Resource Markers)",
                Prefs.CategoryEntry(c), _ => { FilterRow.UpdateVisibility(); MapOverlay.Refresh(); });
        MapResourceMarkersMod.Log.Msg("Added Map Resource Markers rows to Options > Gameplay");
    }

    /// <summary>Hot reload / unload: destroy our rows on every live settings screen and free their registry keys.</summary>
    public static void RemoveAll() => Kit.RemoveAll();
}
