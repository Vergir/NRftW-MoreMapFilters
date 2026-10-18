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

[HarmonyPatch(typeof(CerimWhisperAPI), nameof(CerimWhisperAPI.SetActivationState))]
internal static class WhisperActivatedPatch
{
    private static void Postfix() => LiveUpdates.Notify();
}

[HarmonyPatch(typeof(LadderData), nameof(LadderData.KickDown))]
internal static class LadderKickPatch
{
    private static void Postfix() => LiveUpdates.Notify();
}

[HarmonyPatch(typeof(DoorAPI), nameof(DoorAPI.Open))]
internal static class DoorOpenPatch
{
    private static void Postfix() => LiveUpdates.Notify();
}

[HarmonyPatch(typeof(PuzzleData), nameof(PuzzleData.Trigger))]
internal static class PuzzleTriggerPatch
{
    private static void Postfix() => LiveUpdates.Notify();
}

[HarmonyPatch(typeof(LinkedContainerData), nameof(LinkedContainerData.Interact))]
internal static class LinkedContainerInteractPatch
{
    private static void Postfix() => LiveUpdates.Notify();
}
