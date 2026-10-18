using System;
using HarmonyLib;
using Il2CppMoon.Forsaken;
using MapResourceMarkers;
using MelonLoader;
using UnityEngine;

[assembly: MelonInfo(typeof(MapResourceMarkersMod), "Map Resource Markers", "1.0.0", "vergir")]
[assembly: MelonGame("Moon Studios", "NoRestForTheWicked")]
// MelonLoader would otherwise apply every [HarmonyPatch] itself, twice with our PatchAll and even when Enabled = false.
[assembly: HarmonyDontPatchAll]

namespace MapResourceMarkers;

/// <summary>Entry point. See docs/internal.md.</summary>
public class MapResourceMarkersMod : MelonMod
{
    public static MapResourceMarkersMod Instance { get; private set; } = null!;
    public static MelonLogger.Instance Log => Instance.LoggerInstance;

    /// <summary>Details for troubleshooting (indexing, area variants, cave rolls, live reads, timings): only with
    /// [MapResourceMarkers] Debug = true. Writing a log line costs ~1 ms, so the per-event lines stay off by default.</summary>
    public static void Debug(string message)
    {
        if (Prefs.Debug.Value) Log.Msg(message);
    }

    private string? _lastError;

    public override void OnInitializeMelon()
    {
        Instance = this;
        Prefs.Init();
        if (!Prefs.Enabled.Value)
        {
            LoggerInstance.Msg("Disabled via preferences.");
            return;
        }
        HarmonyInstance.PatchAll(typeof(MapResourceMarkersMod).Assembly);
        Heroes.Patch(HarmonyInstance, LoggerInstance);

        // After a hot reload the settings screens already exist; the Initialize postfix will not run for them.
        if (Prefs.AddSettingsRows.Value)
        {
            try { SettingsRows.AddToLiveScreens(); }
            catch (Exception e) { LoggerInstance.Warning("Adding rows to live settings screens: " + e.Message); }
        }

        // After a hot reload the map screen already exists and may be open; Show will not run for it.
        try
        {
            foreach (var screen in Resources.FindObjectsOfTypeAll<MapScreen>())
            {
                if (screen == null || !screen.gameObject.scene.IsValid() || screen.LocalPlayerIndicator == null) continue;
                FilterRow.Attach(screen);
                MapOverlay.Ensure(screen);
                break;
            }
        }
        catch (Exception e) { LoggerInstance.Warning("Attaching to the live map screen: " + e.Message); }
    }

    /// <summary>Unload / hot reload: take our markers, the Resources row and the settings rows back off.</summary>
    public override void OnDeinitializeMelon()
    {
        try { SettingsRows.RemoveAll(); } catch (Exception e) { LoggerInstance.Warning("SettingsRows.RemoveAll: " + e.Message); }
        try { MapOverlay.Destroy(); } catch (Exception e) { LoggerInstance.Warning("MapOverlay.Destroy: " + e.Message); }
        try { FilterRow.Destroy(); } catch (Exception e) { LoggerInstance.Warning("FilterRow.Destroy: " + e.Message); }
    }

    public override void OnLateUpdate()
    {
        if (!Prefs.Enabled.Value) return;
        try
        {
            MapOverlay.Sync();
            LiveUpdates.Tick();
            MapOverlay.LateUpdate();
            FilterRow.Tick();
            _lastError = null;
        }
        catch (Exception e)
        {
            // Log each distinct failure once instead of every frame.
            if (e.Message != _lastError) LoggerInstance.Error("LateUpdate: " + e);
            _lastError = e.Message;
        }
    }

    internal static void OnMapShown(MapScreen screen)
    {
        try
        {
            FilterRow.Attach(screen);
            MapOverlay.Ensure(screen);
        }
        catch (Exception e) { Log.Error("Map shown: " + e); }
    }
}

/// <summary>Adds our rows to the Gameplay tab. The game builds settings screens when a scene loads, not on menu open.</summary>
[HarmonyPatch(typeof(GameplaySettingsTab), nameof(GameplaySettingsTab.Initialize))]
internal static class GameplaySettingsTabInitializePatch
{
    private static void Postfix(GameplaySettingsTab __instance)
    {
        if (!Prefs.Enabled.Value || !Prefs.AddSettingsRows.Value) return;
        try { SettingsRows.AddTo(__instance.m_controls); }
        catch (Exception e) { MapResourceMarkersMod.Log.Error("Adding settings rows failed: " + e); }
    }
}

/// <summary>Fast travel / teleport finished. The event dispatcher calls these static wrappers through function pointers;
/// a Harmony detour on them still fires (verified by the Minimap mod's refresh triggers).</summary>
[HarmonyPatch(typeof(HeroView), nameof(HeroView.OnTeleportCompleteWrapper))]
internal static class HeroTeleportCompletePatch
{
    private static void Postfix(HeroView self)
    {
        try { if (self != null && self.IsLocalPlayer) MapOverlay.RequestLiveCheck(); }
        catch (Exception e) { MapResourceMarkersMod.Log.Warning("Teleport hook: " + e.Message); }
    }
}

[HarmonyPatch(typeof(MapScreen), nameof(MapScreen.Show))]
internal static class MapScreenShowPatch
{
    private static void Postfix(MapScreen __instance) => MapResourceMarkersMod.OnMapShown(__instance);
}
