using Hazards;
using HarmonyLib;
using Interactables.Interobjects;
using Interactables.Interobjects.DoorUtils;
using LabApi.Features.Wrappers;
using MapGeneration.Distributors;
using Mirror;
using PlayerRoles.PlayableScps.Scp079;
using PlayerRoles.PlayableScps.Scp079.Cameras;
using System;
using Camera = LabApi.Features.Wrappers.Camera;
using Logger = LabApi.Features.Console.Logger;

namespace LabApi.Events.Patches.Internal;

// Wrapper cache hooks for facility objects. Official SL raises static lifecycle events (DoorVariant.OnInstanceCreated,
// SpawnableStructure.OnAdded, EnvironmentalHazard.OnAdded, ...) that Carl Mod lacks; these patches call the wrappers instead.

// Official: Interactables/Interobjects/DoorUtils/DoorVariant.cs Start (OnInstanceCreated)
[HarmonyPatch(typeof(DoorVariant), nameof(DoorVariant.Start))]
internal static class FacilityDoorAddedPatch
{
    private static void Postfix(DoorVariant __instance) => Door.OnAdded(__instance);
}

// Official: Interactables/Interobjects/DoorUtils/DoorVariant.cs OnDestroy (OnInstanceRemoved)
[HarmonyPatch(typeof(DoorVariant), nameof(DoorVariant.OnDestroy))]
internal static class FacilityDoorRemovedPatch
{
    private static void Postfix(DoorVariant __instance) => Door.OnRemoved(__instance);
}

// Official: PlayerRoles/PlayableScps/Scp079/Cameras/Scp079Camera.cs Awake (OnInstanceCreated)
// The camera override is patched rather than the tiny base Awake, which the JIT may inline into it.
[HarmonyPatch(typeof(Scp079Camera), nameof(Scp079Camera.Awake))]
internal static class FacilityCameraAddedPatch
{
    private static void Postfix(Scp079Camera __instance) => Camera.OnAdded(__instance);
}

// Official: PlayerRoles/PlayableScps/Scp079/Cameras/Scp079Camera.cs OnDestroy (OnInstanceRemoved)
[HarmonyPatch(typeof(Scp079InteractableBase), nameof(Scp079InteractableBase.OnDestroy))]
internal static class FacilityCameraRemovedPatch
{
    private static void Postfix(Scp079InteractableBase __instance)
    {
        if (__instance is Scp079Camera camera)
        {
            Camera.Remove(camera);
        }
    }
}

// Official: RoomLightController.cs Start (OnAdded); Carl Mod registers FlickerableLightController instances in OnEnable.
[HarmonyPatch(typeof(FlickerableLightController), nameof(FlickerableLightController.OnEnable))]
internal static class FacilityLightAddedPatch
{
    private static void Postfix(FlickerableLightController __instance) => LightsController.OnAdded(__instance);
}

// Official: RoomLightController.cs OnDestroy (OnRemoved); Carl Mod unregisters FlickerableLightController instances in OnDisable.
[HarmonyPatch(typeof(FlickerableLightController), nameof(FlickerableLightController.OnDisable))]
internal static class FacilityLightRemovedPatch
{
    private static void Postfix(FlickerableLightController __instance) => LightsController.OnRemoved(__instance);
}

// Official: Interactables/Interobjects/ElevatorChamber.cs Start (OnElevatorSpawned)
[HarmonyPatch(typeof(ElevatorChamber), nameof(ElevatorChamber.Awake))]
internal static class FacilityElevatorAddedPatch
{
    private static void Postfix(ElevatorChamber __instance) => Elevator.OnAdded(__instance);
}

// Official: Interactables/Interobjects/ElevatorChamber.cs OnDestroy (OnElevatorRemoved)
[HarmonyPatch(typeof(ElevatorChamber), nameof(ElevatorChamber.OnDestroy))]
internal static class FacilityElevatorRemovedPatch
{
    private static void Postfix(ElevatorChamber __instance) => Elevator.OnRemoved(__instance);
}

// Official: MapGeneration/Distributors/SpawnableStructure.cs Awake/OnDestroy (OnAdded/OnRemoved).
// Carl Mod's SpawnableStructure has no lifecycle methods; every structure carries a StructurePositionSync whose Start runs once.
[HarmonyPatch(typeof(StructurePositionSync), nameof(StructurePositionSync.Start))]
internal static class FacilityStructureAddedPatch
{
    private static void Postfix(StructurePositionSync __instance)
    {
        if (!NetworkServer.active || !__instance.TryGetComponent(out SpawnableStructure structure))
        {
            return;
        }

        try
        {
            Structure.OnAdded(structure);
            FacilityDestroyNotifier.Attach(structure.gameObject, structure, static x => Structure.OnRemoved((SpawnableStructure)x));
        }
        catch (Exception e)
        {
            Logger.Error($"[FacilityLifecycle] Failed to register structure {structure.name}: {e}");
        }
    }
}

// Official: BreakableWindow.cs Start/OnDestroy (OnAdded/OnDestroyed); Carl Mod's window has only Awake.
// The wrapper is added from the notifier's Start: in Awake the window is not yet active and enabled, so it would not be cached.
[HarmonyPatch(typeof(BreakableWindow), nameof(BreakableWindow.Awake))]
internal static class FacilityWindowAddedPatch
{
    private static void Postfix(BreakableWindow __instance) =>
        FacilityDestroyNotifier.Attach(
            __instance.gameObject,
            __instance,
            static x => Window.OnRemoved((BreakableWindow)x),
            static x => Window.OnAdded((BreakableWindow)x));
}

// Official: Hazards/EnvironmentalHazard.cs Awake (OnAdded); Carl Mod's hazard has no Awake, Start is the first server hook.
[HarmonyPatch(typeof(EnvironmentalHazard), nameof(EnvironmentalHazard.Start))]
internal static class FacilityHazardAddedPatch
{
    private static void Postfix(EnvironmentalHazard __instance)
    {
        if (NetworkServer.active && __instance != null)
        {
            Hazard.AddHazard(__instance);
        }
    }
}

// Official: Hazards/EnvironmentalHazard.cs OnDestroy (OnRemoved)
[HarmonyPatch(typeof(EnvironmentalHazard), nameof(EnvironmentalHazard.OnDestroy))]
internal static class FacilityHazardRemovedPatch
{
    private static void Postfix(EnvironmentalHazard __instance) => Hazard.RemoveHazard(__instance);
}

// Official: TeslaGate.cs Start/OnDestroy (OnAdded/OnRemoved); Carl Mod's gate has only Start.
[HarmonyPatch(typeof(TeslaGate), nameof(TeslaGate.Start))]
internal static class FacilityTeslaAddedPatch
{
    private static void Postfix(TeslaGate __instance)
    {
        Tesla.OnAdded(__instance);
        FacilityDestroyNotifier.Attach(__instance.gameObject, __instance, static x => Tesla.OnRemoved((TeslaGate)x));
    }
}
