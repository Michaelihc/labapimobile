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
using SequenceStage = Interactables.Interobjects.CheckpointDoor.CheckpointSequenceStage;

namespace LabApi.Events.Patches.Facility;

// Official: Interactables/Interobjects/DoorUtils/DoorVariant.cs ServerInteract (+ TryResolveLock)
[HarmonyPatch(typeof(DoorVariant), nameof(DoorVariant.ServerInteract))]
internal static class DoorInteractPatch
{
    private static bool Prefix(DoorVariant __instance, ReferenceHub ply, byte colliderId)
    {
        if ((!PlayerEvents.HasInteractingDoor && !PlayerEvents.HasInteractedDoor) || !NetworkServer.active)
        {
            return true;
        }

        // Fork ServerInteract with the official events: a locked door first asks plugins whether the lock is bypassed.
        bool pluginRequestSent = false;
        if (__instance.ActiveLocks > 0 && !ply.serverRoles.BypassMode)
        {
            DoorLockMode mode = DoorLockUtils.GetMode((DoorLockReason)__instance.ActiveLocks);
            bool targetState = __instance.TargetState;
            if ((!mode.HasFlagFast(DoorLockMode.CanClose) || !mode.HasFlagFast(DoorLockMode.CanOpen))
                && (!mode.HasFlagFast(DoorLockMode.ScpOverride) || !ply.IsSCP())
                && (mode == DoorLockMode.FullLock || (targetState && !mode.HasFlagFast(DoorLockMode.CanClose)) || (!targetState && !mode.HasFlagFast(DoorLockMode.CanOpen))))
            {
                bool resolved = false;
                if (PlayerEvents.HasInteractingDoor)
                {
                    PlayerInteractingDoorEventArgs lockEvent = new(ply, __instance, false);
                    PlayerEvents.OnInteractingDoor(lockEvent);
                    pluginRequestSent = true;
                    resolved = lockEvent.IsAllowed && lockEvent.CanOpen;
                }

                if (!resolved)
                {
                    __instance.LockBypassDenied(ply, colliderId);
                    if (PlayerEvents.HasInteractedDoor)
                    {
                        PlayerEvents.OnInteractedDoor(new PlayerInteractedDoorEventArgs(ply, __instance, false));
                    }

                    return false;
                }
            }
        }

        if (!__instance.AllowInteracting(ply, colliderId))
        {
            return false;
        }

        bool canOpen = ply.GetRoleId() == RoleTypeId.Scp079 || __instance.RequiredPermissions.CheckPermissions(ply.inventory.CurInstance, ply);
        if (pluginRequestSent)
        {
            canOpen = true;
        }
        else if (PlayerEvents.HasInteractingDoor)
        {
            PlayerInteractingDoorEventArgs e = new(ply, __instance, canOpen);
            PlayerEvents.OnInteractingDoor(e);
            if (!e.IsAllowed)
            {
                return false;
            }

            canOpen = e.CanOpen;
        }

        if (canOpen)
        {
            __instance.NetworkTargetState = !__instance.TargetState;
            __instance._triggerPlayer = ply;
        }
        else
        {
            __instance.PermissionsDenied(ply, colliderId);
            DoorEvents.TriggerAction(__instance, DoorAction.AccessDenied, ply);
        }

        if (PlayerEvents.HasInteractedDoor)
        {
            PlayerEvents.OnInteractedDoor(new PlayerInteractedDoorEventArgs(ply, __instance, canOpen));
        }

        return false;
    }
}

// Official: Interactables/Interobjects/DoorUtils/DoorVariant.cs Update (DoorLockChanged after LockChanged)
// Inserts a call after the virtual LockChanged(_prevLock) call, while _prevLock still holds the previous value.
[HarmonyPatch(typeof(DoorVariant), nameof(DoorVariant.Update))]
internal static class DoorLockChangedPatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo lockChanged = AccessTools.Method(typeof(DoorVariant), nameof(DoorVariant.LockChanged));
        FieldInfo prevLock = AccessTools.Field(typeof(DoorVariant), nameof(DoorVariant._prevLock));
        List<CodeInstruction> list = new(instructions);
        int index = list.FindIndex(x => x.Calls(lockChanged));
        if (index < 0)
        {
            Logger.Error("[PATCHES] DoorLockChangedPatch: LockChanged call not found, DoorLockChanged will not fire.");
            return list;
        }

        list.InsertRange(index + 1, new[]
        {
            new CodeInstruction(OpCodes.Ldarg_0),
            new CodeInstruction(OpCodes.Ldarg_0),
            new CodeInstruction(OpCodes.Ldfld, prevLock),
            new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(DoorLockChangedPatch), nameof(OnLockChanged))),
        });
        return list;
    }

    private static void OnLockChanged(DoorVariant door, ushort prevLock)
    {
        if (ServerEvents.HasDoorLockChanged && NetworkServer.active)
        {
            ServerEvents.OnDoorLockChanged(new DoorLockChangedEventArgs(door, prevLock, door.ActiveLocks));
        }
    }
}

// Official: Interactables/Interobjects/BreakableDoor.cs ServerDamage
[HarmonyPatch(typeof(BreakableDoor), nameof(BreakableDoor.ServerDamage))]
internal static class DoorDamagePatch
{
    private static bool Prefix(BreakableDoor __instance, float hp, DoorDamageType type, ref bool __result)
    {
        if ((!ServerEvents.HasDoorDamaging && !ServerEvents.HasDoorDamaged) || !NetworkServer.active)
        {
            return true;
        }

        __result = false;
        if (__instance._destroyed || __instance._ignoredDamageSources.HasFlagFast(type) || __instance._brokenPrefab == null || __instance._objectToReplace == null)
        {
            return false;
        }

        if (ServerEvents.HasDoorDamaging)
        {
            DoorDamagingEventArgs e = new(__instance, hp, type);
            ServerEvents.OnDoorDamaging(e);
            if (!e.IsAllowed)
            {
                return false;
            }

            hp = e.Damage;
            type = e.DamageType;
        }

        __instance.RemainingHealth -= hp;
        if (ServerEvents.HasDoorDamaged)
        {
            ServerEvents.OnDoorDamaged(new DoorDamagedEventArgs(__instance, hp, type));
        }

        if (__instance.RemainingHealth <= 0f)
        {
            __instance.Network_destroyed = true;
            DoorEvents.TriggerAction(__instance, DoorAction.Destroyed, null);
        }

        __result = true;
        return false;
    }
}

// Official: Interactables/Interobjects/CheckpointDoor.cs CurSequence setter
// Carl Mod stores the sequence in the private _currentSequence field inside UpdateSequence; every store is redirected
// through CheckpointSequenceHelper.SetSequence, which behaves like the official setter.
[HarmonyPatch(typeof(CheckpointDoor), nameof(CheckpointDoor.UpdateSequence))]
internal static class CheckpointSequencePatch
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        FieldInfo field = AccessTools.Field(typeof(CheckpointDoor), nameof(CheckpointDoor._currentSequence));
        MethodInfo setter = AccessTools.Method(typeof(CheckpointSequenceHelper), nameof(CheckpointSequenceHelper.SetSequence));
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
            Logger.Error("[PATCHES] CheckpointSequencePatch: no _currentSequence store found, checkpoint sequence events will not fire.");
        }
    }
}

/// <summary>
/// Applies checkpoint sequence changes with the official <c>CheckpointDoor.CurSequence</c> setter semantics.
/// </summary>
internal static class CheckpointSequenceHelper
{
    /// <summary>
    /// Sets the sequence of a checkpoint door, raising the checkpoint sequence events on the server.
    /// </summary>
    /// <param name="door">The checkpoint door.</param>
    /// <param name="value">The requested sequence.</param>
    internal static void SetSequence(CheckpointDoor door, SequenceStage value)
    {
        SequenceStage current = door._currentSequence;
        if (current == value)
        {
            return;
        }

        bool server = NetworkServer.active;
        if (server && ServerEvents.HasCheckpointDoorSequenceChanging)
        {
            CheckpointDoorSequenceChangingEventArgs e = new(door, current, value);
            ServerEvents.OnCheckpointDoorSequenceChanging(e);
            if (!e.IsAllowed)
            {
                return;
            }

            value = e.NewSequence;
        }

        door._currentSequence = value;
        if (server && ServerEvents.HasCheckpointDoorSequenceChanged)
        {
            ServerEvents.OnCheckpointDoorSequenceChanged(new CheckpointDoorSequenceChangedEventArgs(door, value));
        }
    }
}

// Official: BlastDoor.cs SetDoorState
// Carl Mod's warhead closes blast doors through SetClosed(prev, closed); the official state is "open", so it is inverted.
// SetClosed is also the SyncVar hook, which the host replays at spawn and from inside SetClosed itself; calls that do not
// change the state raise nothing.
[HarmonyPatch(typeof(BlastDoor), nameof(BlastDoor.SetClosed))]
internal static class BlastDoorPatch
{
    private static bool Prefix(BlastDoor __instance, ref bool b, out bool __state)
    {
        __state = false;
        if (!NetworkServer.active || b == __instance.isClosed || (!ServerEvents.HasBlastDoorChanging && !ServerEvents.HasBlastDoorChanged))
        {
            return true;
        }

        if (ServerEvents.HasBlastDoorChanging)
        {
            BlastDoorChangingEventArgs e = new(__instance, !b);
            ServerEvents.OnBlastDoorChanging(e);
            if (!e.IsAllowed)
            {
                return false;
            }

            b = !e.NewState;
        }

        __state = ServerEvents.HasBlastDoorChanged && b != __instance.isClosed;
        return true;
    }

    private static void Postfix(BlastDoor __instance, bool __state)
    {
        if (__state)
        {
            ServerEvents.OnBlastDoorChanged(new BlastDoorChangedEventArgs(__instance, !__instance.isClosed));
        }
    }
}
