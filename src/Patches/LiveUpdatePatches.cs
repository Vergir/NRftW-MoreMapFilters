using HarmonyLib;
using Il2CppQuantum;

namespace MapResourceMarkers.Patches;

// The game's moments that clear a marker (see LiveUpdates). Simulation thread: only raise the flag.

[HarmonyPatch(typeof(GameSystem), nameof(GameSystem.OnNpcDied))]
internal static class NpcDiedPatch
{
    private static void Postfix() => LiveUpdates.Notify();
}

[HarmonyPatch(typeof(RespawnablesAPI), nameof(RespawnablesAPI.MarkRespawnableAsDespawnedNow))]
internal static class RespawnableDespawnedPatch
{
    private static void Postfix() => LiveUpdates.Notify();
}

[HarmonyPatch(typeof(ContainerData), nameof(ContainerData.Interact))]
internal static class ContainerInteractPatch
{
    private static void Postfix() => LiveUpdates.Notify();
}

[HarmonyPatch(typeof(LinkedContainerData), nameof(LinkedContainerData.Interact))]
internal static class LinkedContainerInteractPatch
{
    private static void Postfix() => LiveUpdates.Notify();
}
