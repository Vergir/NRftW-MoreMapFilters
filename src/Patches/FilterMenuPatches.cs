using HarmonyLib;
using Il2CppMoon.Forsaken;

namespace MapResourceMarkers.Patches;

// Gamepad / keyboard navigation into the Resources row (see FilterRow class remarks). Prefixes return false when our row
// handled the press. OnSubmit and the parameterless ToggleFilterOption share one native body, so patching OnSubmit
// covers both.

[HarmonyPatch(typeof(MapMarkerFilterMenu), nameof(MapMarkerFilterMenu.OnUpButtonPressed))]
internal static class FilterMenuUpPatch
{
    private static bool Prefix(MapMarkerFilterMenu __instance) => !FilterRow.OnUp(__instance);
}

[HarmonyPatch(typeof(MapMarkerFilterMenu), nameof(MapMarkerFilterMenu.OnDownButtonPressed))]
internal static class FilterMenuDownPatch
{
    private static bool Prefix(MapMarkerFilterMenu __instance) => !FilterRow.OnDown(__instance);
}

[HarmonyPatch(typeof(MapMarkerFilterMenu), nameof(MapMarkerFilterMenu.OnLeftButtonPressed))]
internal static class FilterMenuLeftPatch
{
    private static bool Prefix() => !FilterRow.OnLeftRight(-1);
}

[HarmonyPatch(typeof(MapMarkerFilterMenu), nameof(MapMarkerFilterMenu.OnRightButtonPressed))]
internal static class FilterMenuRightPatch
{
    private static bool Prefix() => !FilterRow.OnLeftRight(1);
}

[HarmonyPatch(typeof(MapMarkerFilterMenu), nameof(MapMarkerFilterMenu.OnSubmit))]
internal static class FilterMenuSubmitPatch
{
    private static bool Prefix() => !FilterRow.OnSubmit();
}

// Opening the menu: OpenMarkerFilterMenu has no callers (MapScreen.ProcessInput inlines it), so FilterRow.Tick watches the
// panel becoming visible instead.

[HarmonyPatch(typeof(MapMarkerFilterMenu), nameof(MapMarkerFilterMenu.CloseMenu))]
internal static class FilterMenuClosePatch
{
    private static void Prefix() => FilterRow.ResetFocus();
}

/// <summary>A game button got selected (mouse hover over the game row): it owns the focus again.</summary>
[HarmonyPatch(typeof(MapMarkerFilterMenu), nameof(MapMarkerFilterMenu.OnMarkerOptionSelected))]
internal static class FilterMenuOptionSelectedPatch
{
    private static void Prefix() => FilterRow.ResetFocus();
}
