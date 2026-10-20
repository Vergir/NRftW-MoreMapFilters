using System.Collections.Generic;
using HarmonyLib;
using Il2CppMoon.Forsaken;
using MelonLoader;

namespace MapMarkers;

/// <summary>
/// The heroes in the scene (ours and other players'), kept by Harmony hooks on HeroView.OnEnable / OnDisable instead of
/// FindObjectsOfType&lt;HeroView&gt;, which walks the whole scene: measured 37 ms per call. One scan seeds the list the first
/// time it is needed, for heroes enabled before the hooks were in (the mod hot-reloaded mid-game).
/// </summary>
internal static class Heroes
{
    private static readonly Dictionary<System.IntPtr, HeroView> Active = new();
    private static bool _seeded;
    private static MelonLogger.Instance? _log;

    public static void Patch(HarmonyLib.Harmony harmony, MelonLogger.Instance log)
    {
        _log = log;
        var onEnable = AccessTools.Method(typeof(HeroView), "OnEnable");
        var onDisable = AccessTools.Method(typeof(HeroView), "OnDisable");
        if (onEnable == null || onDisable == null) { log.Warning("HeroView.OnEnable / OnDisable not found: falling back to scene scans"); return; }
        harmony.Patch(onEnable, postfix: new HarmonyMethod(typeof(Heroes), nameof(Enabled)));
        harmony.Patch(onDisable, postfix: new HarmonyMethod(typeof(Heroes), nameof(Disabled)));
        Hooked = true;
    }

    public static bool Hooked { get; private set; }

    private static void Enabled(HeroView __instance) { if (__instance != null) Active[__instance.Pointer] = __instance; }
    private static void Disabled(HeroView __instance) { if (__instance != null) Active.Remove(__instance.Pointer); }

    /// <summary>The active heroes. Without the hooks: a scene scan (slow, see above).</summary>
    public static IEnumerable<HeroView> All()
    {
        if (!Hooked) return UnityEngine.Object.FindObjectsOfType<HeroView>();
        if (!_seeded)
        {
            _seeded = true;
            int before = Active.Count;
            foreach (var v in UnityEngine.Object.FindObjectsOfType<HeroView>())
                if (v != null) Active[v.Pointer] = v;
            _log?.Msg($"hero list seeded: {before} hooked + {Active.Count - before} found by scan");
        }
        return Active.Values;
    }

    public static HeroView? Local()
    {
        foreach (var v in All())
            if (v != null && v.IsLocalPlayer) return v;
        return null;
    }
}
