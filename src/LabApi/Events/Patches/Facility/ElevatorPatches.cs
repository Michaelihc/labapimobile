using HarmonyLib;
using Interactables.Interobjects;
using Interactables.Interobjects.DoorUtils;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Arguments.ServerEvents;
using LabApi.Events.Handlers;
using Mirror;
using PlayerRoles;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Logger = LabApi.Features.Console.Logger;

namespace LabApi.Events.Patches.Facility;

// Official: Interactables/Interobjects/ElevatorChamber.cs ServerInteract
// Carl Mod clients request a floor with ElevatorManager.ElevatorSyncMsg; the server validates it in ServerReceiveMessage.
[HarmonyPatch(typeof(ElevatorManager), nameof(ElevatorManager.ServerReceiveMessage))]
internal static class ElevatorInteractPatch
{
    private static bool Prefix(NetworkConnection conn, ElevatorManager.ElevatorSyncMsg msg)
    {
        if (!PlayerEvents.HasInteractingElevator && !PlayerEvents.HasInteractedElevator)
        {
            return true;
        }

        if (conn.identity == null || !ReferenceHub.TryGetHubNetID(conn.identity.netId, out ReferenceHub hub) || !hub.IsAlive())
        {
            return false;
        }

        msg.Unpack(out ElevatorManager.ElevatorGroup group, out int level);
        if (!ElevatorManager.SpawnedChambers.TryGetValue(group, out ElevatorChamber chamber) || chamber == null)
        {
            return false;
        }

        // The fork accepts the request only from a player in range of one of the chamber's panels.
        foreach (ElevatorPanel panel in chamber.AllPanels)
        {
            if (panel.AssignedChamber != null && panel.AssignedChamber.AssignedGroup == group && panel.VerificationRule.ServerCanInteract(hub, panel))
            {
                ServerInteract(hub, chamber, panel, level);
                break;
            }
        }

        return false;
    }

    /// <summary>
    /// Runs an elevator interaction with the official event flow.
    /// </summary>
    /// <param name="hub">The interacting player.</param>
    /// <param name="chamber">The elevator chamber.</param>
    /// <param name="panel">The panel used, or <see langword="null"/> to use the chamber's first panel.</param>
    /// <param name="level">The requested floor.</param>
    internal static void ServerInteract(ReferenceHub hub, ElevatorChamber chamber, ElevatorPanel? panel, int level)
    {
        if (panel == null && chamber.AllPanels.Count > 0)
        {
            panel = chamber.AllPanels[0];
        }

        // Fork rules: the chamber must be idle and unlocked unless the player bypasses locks.
        bool isAllowed = chamber.IsReady && (chamber.ActiveLocks == DoorLockReason.None || hub.serverRoles.BypassMode);
        if (PlayerEvents.HasInteractingElevator)
        {
            PlayerInteractingElevatorEventArgs e = new(hub, chamber, panel!)
            {
                IsAllowed = isAllowed,
            };
            PlayerEvents.OnInteractingElevator(e);
            isAllowed = e.IsAllowed;
        }

        if (!isAllowed)
        {
            return;
        }

        // A plugin may allow a request the fork would reject while moving; force it like the official ServerSetDestination.
        if (!ElevatorManager.TrySetDestination(chamber.AssignedGroup, level, !chamber.IsReady))
        {
            return;
        }

        if (PlayerEvents.HasInteractedElevator)
        {
            PlayerEvents.OnInteractedElevator(new PlayerInteractedElevatorEventArgs(hub, chamber, panel!));
        }
    }
}

// Official: Interactables/Interobjects/ElevatorChamber.cs Update / ForceDestination (ElevatorSequenceChanged)
// Every store to the private _curSequence field is redirected through SetSequence, so the event fires on each change
// without a per-frame check.
[HarmonyPatch]
internal static class ElevatorSequencePatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(ElevatorChamber), nameof(ElevatorChamber.TrySetDestination));
        yield return AccessTools.Method(typeof(ElevatorChamber), nameof(ElevatorChamber.Update));
        yield return AccessTools.Method(typeof(ElevatorChamber), nameof(ElevatorChamber.UpdateMovement));
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        FieldInfo field = AccessTools.Field(typeof(ElevatorChamber), nameof(ElevatorChamber._curSequence));
        MethodInfo setter = AccessTools.Method(typeof(ElevatorSequencePatch), nameof(SetSequence));
        int replaced = 0;
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.StoresField(field))
            {
                replaced++;
                yield return new CodeInstruction(OpCodes.Call, setter).MoveLabelsFrom(instruction).MoveBlocksFrom(instruction);
                continue;
            }

            yield return instruction;
        }

        if (replaced == 0)
        {
            Logger.Error($"[PATCHES] ElevatorSequencePatch: no _curSequence store in {__originalMethod.Name}, ElevatorSequenceChanged may not fire.");
        }
    }

    private static void SetSequence(ElevatorChamber chamber, ElevatorChamber.ElevatorSequence value)
    {
        ElevatorChamber.ElevatorSequence previous = chamber._curSequence;
        chamber._curSequence = value;
        if (previous != value && ServerEvents.HasElevatorSequenceChanged && NetworkServer.active)
        {
            ServerEvents.OnElevatorSequenceChanged(new ElevatorSequenceChangedEventArgs(chamber, previous, value));
        }
    }
}
