using Footprinting;
using HarmonyLib;
using Interactables.Interobjects.DoorUtils;
using LabApi.Events.Arguments.Scp079Events;
using LabApi.Events.Handlers;
using MapGeneration;
using Mirror;
using PlayerRoles;
using PlayerRoles.PlayableScps.Scp079;
using PlayerRoles.PlayableScps.Scp079.Cameras;
using PlayerRoles.PlayableScps.Scp079.Pinging;
using PlayerRoles.PlayableScps.Scp079.Rewards;
using PlayerStatsSystem;
using RelativePositioning;
using System.Collections.Generic;
using UnityEngine;

namespace LabApi.Events.Patches.Scps;

// Official: PlayerRoles/PlayableScps/Scp079/Cameras/Scp079CurrentCameraSync.cs ServerProcessCmd
[HarmonyPatch(typeof(Scp079CurrentCameraSync), nameof(Scp079CurrentCameraSync.ServerProcessCmd))]
internal static class Scp079ChangingCameraPatch
{
    private static bool Prefix(Scp079CurrentCameraSync __instance, NetworkReader reader)
    {
        if (!Scp079Events.HasChangingCamera && !Scp079Events.HasChangedCamera)
        {
            return true;
        }

        int start = reader.Position;
        Scp079CurrentCameraSync.ClientSwitchState request = (Scp079CurrentCameraSync.ClientSwitchState)reader.ReadByte();
        if (request != Scp079CurrentCameraSync.ClientSwitchState.None)
        {
            // Spectator switch-state relay: no event, let the game handle it.
            reader.Position = start;
            return true;
        }

        __instance._clientSwitchRequest = request;
        __instance._requestedCamId = reader.ReadUShort();
        if (!Scp079InteractableBase.TryGetInteractable(__instance._requestedCamId, out __instance._switchTarget))
        {
            __instance._errorCode = Scp079HudTranslation.InvalidCamera;
            __instance.ServerSendRpc(toAll: true);
            return false;
        }

        float cost = __instance.GetSwitchCost(__instance._switchTarget);
        if (cost > __instance._auxManager.CurrentAux)
        {
            __instance._errorCode = Scp079HudTranslation.NotEnoughAux;
            __instance.ServerSendRpc(toAll: true);
            return false;
        }

        if (__instance._lostSignalHandler.Lost)
        {
            __instance._errorCode = Scp079HudTranslation.SignalLost;
            __instance.ServerSendRpc(toAll: true);
            return false;
        }

        if (Scp079Events.HasChangingCamera && __instance._switchTarget != __instance.CurrentCamera)
        {
            Scp079ChangingCameraEventArgs e = new(__instance.Owner, __instance._switchTarget);
            Scp079Events.OnChangingCamera(e);
            if (!e.IsAllowed)
            {
                __instance._errorCode = Scp079HudTranslation.SignalLost;
                __instance.ServerSendRpc(toAll: true);
                return false;
            }

            __instance._switchTarget = e.Camera.Base;
        }

        __instance._auxManager.CurrentAux -= cost;
        __instance._errorCode = Scp079HudTranslation.Zoom;
        if (__instance._switchTarget != __instance.CurrentCamera)
        {
            __instance.CurrentCamera = __instance._switchTarget;
            if (Scp079Events.HasChangedCamera)
            {
                Scp079Events.OnChangedCamera(new Scp079ChangedCameraEventArgs(__instance.Owner, __instance._switchTarget));
            }
        }
        else
        {
            __instance.ServerSendRpc(toAll: true);
        }

        return false;
    }
}

// Official: PlayerRoles/PlayableScps/Scp079/Pinging/Scp079PingAbility.cs ServerProcessCmd
[HarmonyPatch(typeof(Scp079PingAbility), nameof(Scp079PingAbility.ServerProcessCmd))]
internal static class Scp079PingingPatch
{
    private static bool Prefix(Scp079PingAbility __instance, NetworkReader reader)
    {
        if (!Scp079Events.HasPinging && !Scp079Events.HasPinged)
        {
            return true;
        }

        if (!__instance.IsReady || !__instance.Role.TryGetOwner(out _) || __instance.LostSignalHandler.Lost)
        {
            return false;
        }

        __instance._syncProcessorIndex = reader.ReadByte();
        if (__instance._syncProcessorIndex >= Scp079PingAbility.PingProcessors.Length)
        {
            return false;
        }

        __instance._syncPos = reader.ReadRelativePosition();
        __instance._syncNormal = reader.ReadVector3();

        if (Scp079Events.HasPinging)
        {
            Scp079PingingEventArgs e = new(__instance.Owner, __instance._syncPos.Position, __instance._syncNormal, __instance._syncProcessorIndex);
            Scp079Events.OnPinging(e);
            if (!e.IsAllowed)
            {
                return false;
            }

            byte index = (byte)e.PingType;
            if (index >= Scp079PingAbility.PingProcessors.Length)
            {
                return false;
            }

            __instance._syncPos = new RelativePosition(e.Position);
            __instance._syncNormal = e.Normal;
            __instance._syncProcessorIndex = index;
        }

        // Same receiver filter the fork builds per ping.
        __instance.ServerSendRpc(x => __instance.ServerCheckReceiver(x, __instance._syncPos.Position, __instance._syncProcessorIndex));
        __instance.AuxManager.CurrentAux -= __instance._cost;
        __instance._rateLimiter.RegisterInput();

        if (Scp079Events.HasPinged)
        {
            Scp079Events.OnPinged(new Scp079PingedEventArgs(__instance.Owner, __instance._syncPos.Position, __instance._syncNormal, __instance._syncProcessorIndex));
        }

        return false;
    }
}

// Official: PlayerRoles/PlayableScps/Scp079/Rewards/Scp079RewardManager.cs GrantExp
// The fork has no subject role parameter; Subject is always RoleTypeId.None and setting it has no effect.
[HarmonyPatch(typeof(Scp079RewardManager), nameof(Scp079RewardManager.GrantExp))]
internal static class Scp079GainingExperiencePatch
{
    private static bool Prefix(Scp079Role instance, int reward, Scp079HudTranslation gainReason)
    {
        if (!Scp079Events.HasGainingExperience && !Scp079Events.HasGainedExperience)
        {
            return true;
        }

        if (!instance.TryGetOwner(out ReferenceHub hub))
        {
            return true;
        }

        if (Scp079Events.HasGainingExperience)
        {
            Scp079GainingExperienceEventArgs e = new(hub, reward, gainReason, RoleTypeId.None);
            Scp079Events.OnGainingExperience(e);
            if (!e.IsAllowed)
            {
                return false;
            }

            reward = (int)e.Amount;
            gainReason = e.Reason;
        }

        if (instance.SubroutineModule.TryGetSubroutine(out Scp079TierManager tierManager))
        {
            tierManager.ServerGrantExperience(reward, gainReason);
            if (Scp079Events.HasGainedExperience)
            {
                Scp079Events.OnGainedExperience(new Scp079GainedExperienceEventArgs(hub, reward, gainReason, RoleTypeId.None));
            }
        }

        return false;
    }
}

// Official: PlayerRoles/PlayableScps/Scp079/Scp079TierManager.cs AccessTierIndex (setter)
[HarmonyPatch(typeof(Scp079TierManager), nameof(Scp079TierManager.AccessTierIndex), MethodType.Setter)]
internal static class Scp079LevelingUpPatch
{
    private static bool Prefix(Scp079TierManager __instance, int value)
    {
        if ((!Scp079Events.HasLevelingUp && !Scp079Events.HasLeveledUp) || !NetworkServer.active || __instance.Owner == null)
        {
            return true;
        }

        if (__instance._accessTier == value)
        {
            return false;
        }

        int levels = value - __instance._accessTier;
        for (int i = 0; i < levels; i++)
        {
            __instance._accessTier++;
            __instance.OnLevelledUp?.Invoke();
        }

        if (Scp079Events.HasLevelingUp)
        {
            Scp079LevelingUpEventArgs e = new(__instance.Owner, value + 1);
            Scp079Events.OnLevelingUp(e);
            if (!e.IsAllowed)
            {
                return false;
            }
        }

        __instance._accessTier = value;
        __instance.OnTierChanged?.Invoke();

        if (Scp079Events.HasLeveledUp)
        {
            Scp079Events.OnLeveledUp(new Scp079LeveledUpEventArgs(__instance.Owner, value + 1));
        }

        return false;
    }
}

// Official: PlayerRoles/PlayableScps/Scp079/Scp079BlackoutRoomAbility.cs ServerProcessCmd
[HarmonyPatch(typeof(Scp079BlackoutRoomAbility), nameof(Scp079BlackoutRoomAbility.ServerProcessCmd))]
internal static class Scp079BlackingOutRoomPatch
{
    private static bool Prefix(Scp079BlackoutRoomAbility __instance)
    {
        if (!Scp079Events.HasBlackingOutRoom && !Scp079Events.HasBlackedOutRoom)
        {
            return true;
        }

        __instance.RefreshCurrentController();
        FlickerableLightController controller = __instance._roomController;
        if (__instance._hasController && !controller.LightsEnabled && __instance._blackoutCooldowns.ContainsKey(controller.netId) && !__instance.LostSignalHandler.Lost)
        {
            // Fork-only: re-enabling lights in a blacked-out room. No official event.
            controller.ServerFlickerLights(0.1f);
            __instance._successfulController = controller;
            __instance.ServerSendRpc(toAll: true);
            return false;
        }

        if (!__instance.IsReady || __instance.LostSignalHandler.Lost)
        {
            __instance._successfulController = null;
            __instance.ServerSendRpc(toAll: false);
            return false;
        }

        if (Scp079Events.HasBlackingOutRoom)
        {
            Scp079BlackingOutRoomEventsArgs e = new(__instance.Owner, controller.Room);
            Scp079Events.OnBlackingOutRoom(e);
            if (!e.IsAllowed)
            {
                return false;
            }
        }

        __instance.AuxManager.CurrentAux -= __instance._cost;
        __instance.RewardManager.MarkRoom(controller.Room);
        __instance._blackoutCooldowns[controller.netId] = NetworkTime.time + __instance._cooldown;
        controller.ServerFlickerLights(__instance._blackoutDuration);
        __instance._successfulController = controller;
        __instance.ServerSendRpc(toAll: true);

        if (Scp079Events.HasBlackedOutRoom)
        {
            Scp079Events.OnBlackedOutRoom(new Scp079BlackedOutRoomEventArgs(__instance.Owner, controller.Room));
        }

        return false;
    }
}

// Official: PlayerRoles/PlayableScps/Scp079/Scp079BlackoutZoneAbility.cs ServerProcessCmd
[HarmonyPatch(typeof(Scp079BlackoutZoneAbility), nameof(Scp079BlackoutZoneAbility.ServerProcessCmd))]
internal static class Scp079BlackingOutZonePatch
{
    private static bool Prefix(Scp079BlackoutZoneAbility __instance, NetworkReader reader)
    {
        if (!Scp079Events.HasBlackingOutZone && !Scp079Events.HasBlackedOutZone)
        {
            return true;
        }

        __instance._syncZone = (FacilityZone)reader.ReadByte();
        if (__instance.ErrorCode != Scp079HudTranslation.Zoom)
        {
            return false;
        }

        if (Scp079Events.HasBlackingOutZone)
        {
            Scp079BlackingOutZoneEventArgs e = new(__instance.Owner, __instance._syncZone);
            Scp079Events.OnBlackingOutZone(e);
            if (!e.IsAllowed)
            {
                return false;
            }
        }

        foreach (FlickerableLightController instance in FlickerableLightController.Instances)
        {
            if (instance.Room.Zone == __instance._syncZone)
            {
                instance.ServerFlickerLights(__instance._duration);
            }
        }

        __instance._cooldownTimer.Trigger(__instance._cooldown);
        __instance.AuxManager.CurrentAux -= __instance._cost;
        __instance.ServerSendRpc(toAll: true);

        if (Scp079Events.HasBlackedOutZone)
        {
            Scp079Events.OnBlackedOutZone(new Scp079BlackedOutZoneEventArgs(__instance.Owner, __instance._syncZone));
        }

        return false;
    }
}

// Official: PlayerRoles/PlayableScps/Scp079/Scp079DoorLockChanger.cs ServerProcessCmd
[HarmonyPatch(typeof(Scp079DoorLockChanger), nameof(Scp079DoorLockChanger.ServerProcessCmd))]
internal static class Scp079LockingDoorPatch
{
    private static readonly AccessTools.FieldRef<System.Action<Scp079Role, DoorVariant>> OnServerDoorLocked =
        AccessTools.StaticFieldRefAccess<System.Action<Scp079Role, DoorVariant>>(AccessTools.Field(typeof(Scp079DoorLockChanger), "OnServerDoorLocked"));

    private static bool Prefix(Scp079DoorLockChanger __instance, NetworkReader reader)
    {
        if (!Scp079Events.HasLockingDoor && !Scp079Events.HasLockedDoor)
        {
            return true;
        }

        uint netId = reader.ReadUInt();
        if (!NetworkServer.spawned.TryGetValue(netId, out NetworkIdentity identity) || !identity.TryGetComponent(out DoorVariant door))
        {
            return false;
        }

        __instance.LastDoor = door;
        if (!__instance.IsReady)
        {
            return false;
        }

        if (__instance.TargetAction != DoorAction.Locked)
        {
            // Unlock events are raised by Scp079UnlockingDoorPatch.
            __instance.SetDoorLock(door, lockState: false);
            return false;
        }

        if (__instance.GetRemainingCooldown(door) > 0f || Scp079LockdownRoomAbility.IsLockedDown(door) || __instance.LostSignalHandler.Lost)
        {
            return false;
        }

        if (Scp079Events.HasLockingDoor)
        {
            Scp079LockingDoorEventArgs e = new(__instance.Owner, door);
            Scp079Events.OnLockingDoor(e);
            if (!e.IsAllowed)
            {
                return false;
            }
        }

        __instance.SetDoorLock(door, lockState: true);
        __instance.RewardManager.MarkRooms(door.Rooms);
        OnServerDoorLocked()?.Invoke(__instance.ScpRole, door);
        __instance.AuxManager.CurrentAux -= __instance.GetCostForDoor(DoorAction.Locked, door);

        if (Scp079Events.HasLockedDoor)
        {
            Scp079Events.OnLockedDoor(new Scp079LockedDoorEventArgs(__instance.Owner, door));
        }

        return false;
    }
}

// Official: PlayerRoles/PlayableScps/Scp079/Scp079DoorLockChanger.cs ServerUnlock
// The fork can hold several 079 locks; manual and automatic single-door unlocks go through SetDoorLock(false).
[HarmonyPatch(typeof(Scp079DoorLockChanger), nameof(Scp079DoorLockChanger.SetDoorLock))]
internal static class Scp079UnlockingDoorPatch
{
    private static bool Prefix(Scp079DoorLockChanger __instance, DoorVariant door, bool lockState, ref bool __result, out bool __state)
    {
        __state = false;
        if (lockState || door == null || (!Scp079Events.HasUnlockingDoor && !Scp079Events.HasUnlockedDoor) || !__instance._lockedDoors.Contains(door))
        {
            return true;
        }

        if (Scp079Events.HasUnlockingDoor)
        {
            Scp079UnlockingDoorEventArgs e = new(__instance.Owner, door);
            Scp079Events.OnUnlockingDoor(e);
            if (!e.IsAllowed)
            {
                __result = false;
                return false;
            }
        }

        __state = true;
        return true;
    }

    private static void Postfix(Scp079DoorLockChanger __instance, DoorVariant door, bool __state)
    {
        if (__state && Scp079Events.HasUnlockedDoor)
        {
            Scp079Events.OnUnlockedDoor(new Scp079UnlockedDoorEventArgs(__instance.Owner, door));
        }
    }
}

// Official: PlayerRoles/PlayableScps/Scp079/Scp079DoorLockChanger.cs ServerUnlock
// Fork-only bulk unlock (aux depleted, signal lost, lock releaser): one event pair per locked door.
[HarmonyPatch(typeof(Scp079DoorLockChanger), nameof(Scp079DoorLockChanger.ServerUnlockAll))]
internal static class Scp079UnlockingAllDoorsPatch
{
    private static readonly List<DoorVariant> Buffer = [];

    private static bool Prefix(Scp079DoorLockChanger __instance)
    {
        if ((!Scp079Events.HasUnlockingDoor && !Scp079Events.HasUnlockedDoor) || !NetworkServer.active || __instance.Owner == null)
        {
            return true;
        }

        // Snapshot first: handlers may change the lock set.
        Buffer.Clear();
        foreach (DoorVariant door in __instance._lockedDoors)
        {
            if (door != null)
            {
                Buffer.Add(door);
            }
        }

        if (Scp079Events.HasUnlockingDoor)
        {
            int kept = 0;
            for (int i = 0; i < Buffer.Count; i++)
            {
                Scp079UnlockingDoorEventArgs e = new(__instance.Owner, Buffer[i]);
                Scp079Events.OnUnlockingDoor(e);
                if (e.IsAllowed)
                {
                    Buffer[kept++] = Buffer[i];
                }
            }

            Buffer.RemoveRange(kept, Buffer.Count - kept);
        }

        double now = NetworkTime.time;
        for (int i = 0; i < Buffer.Count; i++)
        {
            DoorVariant door = Buffer[i];
            door.ServerChangeLock(DoorLockReason.Regular079, newState: false);
            __instance._toggleTimes[door] = now;
            __instance._lockedDoors.Remove(door);
        }

        __instance._syncvarsDirty = true;

        if (Scp079Events.HasUnlockedDoor)
        {
            for (int i = 0; i < Buffer.Count; i++)
            {
                Scp079Events.OnUnlockedDoor(new Scp079UnlockedDoorEventArgs(__instance.Owner, Buffer[i]));
            }
        }

        Buffer.Clear();
        return false;
    }
}

// Official: PlayerRoles/PlayableScps/Scp079/Scp079LockdownRoomAbility.cs ServerProcessCmd
[HarmonyPatch(typeof(Scp079LockdownRoomAbility), nameof(Scp079LockdownRoomAbility.ServerProcessCmd))]
internal static class Scp079LockingDownRoomPatch
{
    private static bool Prefix(Scp079LockdownRoomAbility __instance)
    {
        if (!Scp079Events.HasLockingDownRoom && !Scp079Events.HasLockedDownRoom)
        {
            return true;
        }

        if (__instance.ErrorCode == Scp079HudTranslation.Zoom && !__instance.LostSignalHandler.Lost)
        {
            RoomIdentifier room = __instance.CurrentCamSync.CurrentCamera.Room;
            if (Scp079Events.HasLockingDownRoom)
            {
                Scp079LockingDownRoomEventArgs e = new(__instance.Owner, room);
                Scp079Events.OnLockingDownRoom(e);
                if (!e.IsAllowed)
                {
                    return false;
                }
            }

            __instance.AuxManager.CurrentAux -= __instance._cost;
            __instance.RemainingCooldown = __instance._lockdownDuration + __instance._cooldown;
            __instance.ServerInitLockdown();

            if (Scp079Events.HasLockedDownRoom)
            {
                Scp079Events.OnLockedDownRoom(new Scp079LockedDownRoomEventArgs(__instance.Owner, room));
            }
        }

        __instance.ServerSendRpc(toAll: false);
        return false;
    }
}

// Official: PlayerRoles/PlayableScps/Scp079/Scp079LockdownRoomAbility.cs ServerCancelLockdown
[HarmonyPatch(typeof(Scp079LockdownRoomAbility), nameof(Scp079LockdownRoomAbility.ServerCancelLockdown))]
internal static class Scp079CancellingRoomLockdownPatch
{
    private static bool Prefix(Scp079LockdownRoomAbility __instance)
    {
        if (!Scp079Events.HasCancellingRoomLockdown || __instance.Owner == null)
        {
            return true;
        }

        Scp079CancellingRoomLockdownEventArgs e = new(__instance.Owner, __instance._lastLockedRoom);
        Scp079Events.OnCancellingRoomLockdown(e);
        return e.IsAllowed;
    }

    private static void Postfix(Scp079LockdownRoomAbility __instance, bool __runOriginal)
    {
        if (__runOriginal && Scp079Events.HasCancelledRoomLockdown && __instance.Owner != null)
        {
            Scp079Events.OnCancelledRoomLockdown(new Scp079CancelledRoomLockdownEventArgs(__instance.Owner, __instance._lastLockedRoom));
        }
    }
}

// Official: PlayerRoles/PlayableScps/Scp079/Scp079TeslaAbility.cs ServerProcessCmd
[HarmonyPatch(typeof(Scp079TeslaAbility), nameof(Scp079TeslaAbility.ServerProcessCmd))]
internal static class Scp079UsingTeslaPatch
{
    private static bool Prefix(Scp079TeslaAbility __instance)
    {
        if (!Scp079Events.HasUsingTesla && !Scp079Events.HasUsedTesla)
        {
            return true;
        }

        if (!__instance.IsReady)
        {
            return false;
        }

        Scp079Camera cam = __instance.CurrentCamSync.CurrentCamera;
        if (cam == null)
        {
            return false;
        }

        TeslaGate? tesla = null;
        Vector3 camPosition = cam.Position;
        List<TeslaGate> gates = TeslaGateController.Singleton.TeslaGates;
        for (int i = 0; i < gates.Count; i++)
        {
            if (RoomIdUtils.IsTheSameRoom(camPosition, gates[i].transform.position))
            {
                tesla = gates[i];
                break;
            }
        }

        if (tesla == null)
        {
            return false;
        }

        if (Scp079Events.HasUsingTesla)
        {
            Scp079UsingTeslaEventArgs e = new(__instance.Owner, tesla);
            Scp079Events.OnUsingTesla(e);
            if (!e.IsAllowed)
            {
                return false;
            }
        }

        __instance.RewardManager.MarkRoom(cam.Room);
        __instance.AuxManager.CurrentAux -= __instance._cost;
        tesla.RpcInstantBurst();
        __instance._nextUseTime = NetworkTime.time + __instance._cooldown;
        __instance.ServerSendRpc(toAll: false);

        if (Scp079Events.HasUsedTesla)
        {
            Scp079Events.OnUsedTesla(new Scp079UsedTeslaEventArgs(__instance.Owner, tesla));
        }

        return false;
    }
}

// Official: PlayerRoles/PlayableScps/Scp079/Scp079Recontainer.cs TryKill079
[HarmonyPatch(typeof(Scp079Recontainer), nameof(Scp079Recontainer.TryKill079))]
internal static class Scp079RecontainingPatch
{
    private static readonly List<ReferenceHub> Targets = [];

    private static bool Prefix(Scp079Recontainer __instance, ref bool __result)
    {
        if (!Scp079Events.HasRecontaining && !Scp079Events.HasRecontained)
        {
            return true;
        }

        Targets.Clear();
        foreach (Scp079Role instance in Scp079Role.ActiveInstances)
        {
            if (instance.TryGetOwner(out ReferenceHub hub) && !Targets.Contains(hub))
            {
                Targets.Add(hub);
            }
        }

        Footprint attacker = __instance._activatorGlass.LastAttacker;
        bool result = false;
        for (int i = 0; i < Targets.Count; i++)
        {
            ReferenceHub hub = Targets[i];
            if (Scp079Events.HasRecontaining)
            {
                Scp079RecontainingEventArgs e = new(hub, attacker.Hub);
                Scp079Events.OnRecontaining(e);
                if (!e.IsAllowed)
                {
                    continue;
                }
            }

            result = true;
            if (attacker.IsSet)
            {
                hub.playerStats.DealDamage(new RecontainmentDamageHandler(attacker));
            }
            else
            {
                hub.playerStats.DealDamage(new UniversalDamageHandler(-1f, DeathTranslations.Recontained));
            }

            if (Scp079Events.HasRecontained)
            {
                Scp079Events.OnRecontained(new Scp079RecontainedEventArgs(hub, attacker.Hub));
            }
        }

        Targets.Clear();
        __result = result;
        return false;
    }
}
