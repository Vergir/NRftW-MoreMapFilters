using HarmonyLib;
using Il2CppQuantum;

namespace MapResourceMarkers.Patches;

// The game's moments that clear a marker (see LiveUpdates). Simulation thread: only raise the flag. Several hooks per
// kind: one may be inlined into its caller in the native build, and a later one then still catches it. None of these take
// a struct by ref (see the workspace's Harmony pitfalls).

[HarmonyPatch(typeof(GameSystem), nameof(GameSystem.OnNpcDied))]
internal static class NpcDiedPatch
{
    private static void Postfix() => LiveUpdates.Notify("kill");
}

[HarmonyPatch(typeof(RespawnablesAPI), nameof(RespawnablesAPI.MarkRespawnableAsDespawnedNow))]
internal static class RespawnableDespawnedPatch
{
    private static void Postfix() => LiveUpdates.Notify("despawn");
}

[HarmonyPatch(typeof(ContainerData), nameof(ContainerData.Interact))]
internal static class ContainerInteractPatch
{
    private static void Postfix() => LiveUpdates.Notify("chest");
}

[HarmonyPatch(typeof(LinkedContainerData), nameof(LinkedContainerData.Interact))]
internal static class LinkedContainerInteractPatch
{
    private static void Postfix() => LiveUpdates.Notify("chest");
}

// Keys: picked up from a static pickup (the 2 s wait covers the animation).
[HarmonyPatch(typeof(StaticPickupData), nameof(StaticPickupData.Interact))]
internal static class PickupInteractPatch
{
    private static void Postfix() => LiveUpdates.Notify("pickup");
}

// Whispers.
[HarmonyPatch(typeof(CerimWhisperAPI), nameof(CerimWhisperAPI.Activate))]
internal static class WhisperActivatePatch
{
    private static void Postfix() => LiveUpdates.Notify("whisper");
}

[HarmonyPatch(typeof(CerimWhisperAPI), nameof(CerimWhisperAPI.SetActivationState))]
internal static class WhisperActivatedPatch
{
    private static void Postfix() => LiveUpdates.Notify("whisper");
}

// Shortcuts: ladders, doors, levers, puzzles (trapdoors, gates, drawbridges), breakable walls.
[HarmonyPatch(typeof(LadderData), nameof(LadderData.KickDown))]
internal static class LadderKickPatch
{
    private static void Postfix() => LiveUpdates.Notify("ladder");
}

[HarmonyPatch(typeof(LadderData), nameof(LadderData.Trigger))]
internal static class LadderTriggerPatch
{
    private static void Postfix() => LiveUpdates.Notify("ladder");
}

[HarmonyPatch(typeof(DoorAPI), nameof(DoorAPI.Open))]
internal static class DoorOpenPatch
{
    private static void Postfix() => LiveUpdates.Notify("door");
}

[HarmonyPatch(typeof(DoorAPI), nameof(DoorAPI.SetDoorState))]
internal static class DoorStatePatch
{
    private static void Postfix() => LiveUpdates.Notify("door");
}

[HarmonyPatch(typeof(DoorData), nameof(DoorData.Trigger))]
internal static class DoorTriggerPatch
{
    private static void Postfix() => LiveUpdates.Notify("door");
}

[HarmonyPatch(typeof(LeverData), nameof(LeverData.Interact))]
internal static class LeverInteractPatch
{
    private static void Postfix() => LiveUpdates.Notify("lever");
}

[HarmonyPatch(typeof(PuzzleData), nameof(PuzzleData.Trigger))]
internal static class PuzzleTriggerPatch
{
    private static void Postfix() => LiveUpdates.Notify("puzzle");
}

[HarmonyPatch(typeof(PuzzleAPI), nameof(PuzzleAPI.SetPuzzleState), typeof(Frame), typeof(EntityRef), typeof(long))]
internal static class PuzzleStatePatch
{
    private static void Postfix() => LiveUpdates.Notify("puzzle");
}

[HarmonyPatch(typeof(PuzzleAPI), nameof(PuzzleAPI.SetPuzzleState), typeof(Frame), typeof(EntityRef), typeof(bool))]
internal static class PuzzleCompletePatch
{
    private static void Postfix() => LiveUpdates.Notify("puzzle");
}

[HarmonyPatch(typeof(DestructibleData), nameof(DestructibleData.Trigger))]
internal static class DestructibleTriggerPatch
{
    private static void Postfix() => LiveUpdates.Notify("wall");
}
