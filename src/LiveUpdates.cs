using UnityEngine;

namespace MapResourceMarkers;

/// <summary>
/// Crosses right after a kill, a harvest or an opened chest, also with the map closed (the Minimap mod mirrors the
/// markers): Harmony postfixes on the game's own moments (Patches/LiveUpdatePatches: GameSystem.OnNpcDied,
/// RespawnablesAPI.MarkRespawnableAsDespawnedNow, ContainerData / LinkedContainerData.Interact) only raise a flag, since they
/// run inside the simulation (possibly twice, for predicted frames). On the main thread the flag starts a 2 s wait
/// (pickups with their animation come up to ~2 s apart; every new event restarts it, at most 4 s after the first), then
/// the live state close to the hero is re-read (ClearedState.RefreshLive: 25 m, 60 m for enemies, which die away from
/// their spawn point) and only those markers are redrawn (MapOverlay.RefreshNearHero). Nothing runs while nothing happens.
/// </summary>
internal static class LiveUpdates
{
    private static volatile bool _signal;
    private static float _dueAt = -1f, _firstAt;
    private const float Delay = 2f, MaxDelay = 4f;

    /// <summary>Simulation thread: something near the hero may have been cleared.</summary>
    public static void Notify() => _signal = true;

    /// <summary>Main thread, every frame.</summary>
    public static void Tick()
    {
        float now = Time.unscaledTime;
        if (_signal)
        {
            _signal = false;
            if (_dueAt < 0f) _firstAt = now;
            _dueAt = Mathf.Min(now + Delay, _firstAt + MaxDelay);
        }
        if (_dueAt < 0f || now < _dueAt) return;
        _dueAt = -1f;
        var f = NodeIndex.LocalFrame();
        if (f == null) return;
        ClearedState.RefreshLive(f);
        MapOverlay.RefreshNearHero();
    }
}
