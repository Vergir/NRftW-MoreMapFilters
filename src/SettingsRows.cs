using System.Linq;
using Il2CppMoon.Forsaken;
using MelonLoader;
using NrftwShared;
using UnityEngine;

namespace MapMarkers;

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

    /// <summary>Row names; SettingsPreview recognizes the hovered row by them.</summary>
    private const string UnexploredLabel = "Map Markers in Unexplored Areas", SizeLabel = "Map Marker Size";

    private static readonly SettingsRowsKit Kit = new SettingsRowsKit(Prefix,
        new[] { SpacerId, UnexploredId, SizeId }.Concat(MarkerGroups.Categories.Select(CategoryId)).ToArray(),
        () => MapMarkersMod.Log, SettingsRowsKit.GameplayTabControls);

    /// <summary>Add the rows to every settings screen that already exists (after a hot reload).</summary>
    public static void AddToLiveScreens() => Kit.AddToLiveScreens(AddTo);

    public static void AddTo(SettingsScreenControls? controls)
    {
        if (controls != null) SettingsPreview.Track(controls.m_settingNameText, controls.m_specificationText);
        var rows = Kit.Begin(controls, Category, UnexploredId);
        if (rows == null) return;

        rows.Spacer(SpacerId);
        rows.Toggle(UnexploredId, UnexploredLabel,
            "Also show markers in parts of the map you have not explored yet, e.g. ore veins or enemy camps beyond the fog. " +
            "This might spoil the fun of exploring.",
            Prefs.ShowInUnrevealed, _ => MapOverlay.Refresh());
        SettingsPreview.RegisterUnexplored(UnexploredLabel);
        // Snaps to the step so a drag lands on 50%, 60%, ... like the arrow keys do.
        rows.Slider(SizeId, SizeLabel,
            "Size of the markers on the map.",
            0.5f, 2f, 0.1f, Prefs.MarkerScale.Value, v => Mathf.RoundToInt(v * 100f) + "%",
            v => { Prefs.MarkerScale.Value = v; MelonPreferences.Save(); });
        SettingsPreview.RegisterSize(SizeLabel);
        foreach (var c in MarkerGroups.Categories)
        {
            string label = "Map Markers: " + MarkerGroups.Label(c);
            rows.Toggle(CategoryId(c), label, MarkerGroups.Description(c),
                Prefs.CategoryEntry(c), _ => { FilterRow.UpdateVisibility(); MapOverlay.Refresh(); });
            SettingsPreview.Register(label, c);
        }
        MapMarkersMod.Log.Msg("Added Map Markers rows to Options > Gameplay");
    }

    /// <summary>Hot reload / unload: destroy our rows on every live settings screen and free their registry keys.</summary>
    public static void RemoveAll()
    {
        SettingsPreview.RemoveAll();
        Kit.RemoveAll();
    }
}
