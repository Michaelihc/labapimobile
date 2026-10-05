using Hazards;
using HarmonyLib;
using LabApi.Events.Arguments.Scp173Events;
using LabApi.Events.Handlers;
using Mirror;
using PlayerRoles;
using PlayerRoles.FirstPersonControl;
using PlayerRoles.PlayableScps.Scp173;
using RelativePositioning;
using UnityEngine;
using Utils.Networking;

namespace LabApi.Events.Patches.Scps;

// Official: PlayerRoles/PlayableScps/Scp173/Scp173AudioPlayer.cs ServerSendSound
[HarmonyPatch(typeof(Scp173AudioPlayer), nameof(Scp173AudioPlayer.ServerSendSound))]
internal static class Scp173PlayingSoundPatch
{
    private static bool Prefix(Scp173AudioPlayer __instance, ref Scp173AudioPlayer.Scp173SoundId soundId, out ReferenceHub? __state)
    {
        __state = null;
        if ((!Scp173Events.HasPlayingSound && !Scp173Events.HasPlayedSound) || !__instance.Role.TryGetOwner(out ReferenceHub hub))
        {
            return true;
        }

        if (Scp173Events.HasPlayingSound)
        {
            Scp173PlayingSoundEventArgs e = new(hub, soundId);
            Scp173Events.OnPlayingSound(e);
            if (!e.IsAllowed)
            {
                return false;
            }

            soundId = e.SoundId;
        }

        __state = hub;
        return true;
    }

    private static void Postfix(Scp173AudioPlayer.Scp173SoundId soundId, ReferenceHub? __state)
    {
        if (__state != null && Scp173Events.HasPlayedSound && Scp173AudioPlayer.Sounds.ContainsKey((byte)soundId))
        {
            Scp173Events.OnPlayedSound(new Scp173PlayedSoundEventArgs(__state, soundId));
        }
    }
}

// Official: PlayerRoles/PlayableScps/Scp173/Scp173BreakneckSpeedsAbility.cs IsActive (setter)
[HarmonyPatch(typeof(Scp173BreakneckSpeedsAbility), nameof(Scp173BreakneckSpeedsAbility.IsActive), MethodType.Setter)]
internal static class Scp173BreakneckSpeedChangingPatch
{
    private static bool Prefix(Scp173BreakneckSpeedsAbility __instance, bool value, out bool __state)
    {
        __state = false;
        if ((!Scp173Events.HasBreakneckSpeedChanging && !Scp173Events.HasBreakneckSpeedChanged) || !NetworkServer.active
            || value == __instance.IsActive)
        {
            return true;
        }

        if (Scp173Events.HasBreakneckSpeedChanging)
        {
            Scp173BreakneckSpeedChangingEventArgs e = new(__instance.Owner, value);
            Scp173Events.OnBreakneckSpeedChanging(e);
            if (!e.IsAllowed)
            {
                return false;
            }
        }

        __state = true;
        return true;
    }

    private static void Postfix(Scp173BreakneckSpeedsAbility __instance, bool value, bool __state)
    {
        if (__state && Scp173Events.HasBreakneckSpeedChanged)
        {
            Scp173Events.OnBreakneckSpeedChanged(new Scp173BreakneckSpeedChangedEventArgs(__instance.Owner, value));
        }
    }
}

// Official: PlayerRoles/PlayableScps/Scp173/Scp173ObserversTracker.cs UpdateObserver
[HarmonyPatch(typeof(Scp173ObserversTracker), nameof(Scp173ObserversTracker.UpdateObserver))]
internal static class Scp173AddingObserverPatch
{
    private static bool Prefix(Scp173ObserversTracker __instance, ReferenceHub targetHub, ref int __result)
    {
        if (!Scp173Events.HasAddingObserver && !Scp173Events.HasAddedObserver && !Scp173Events.HasRemovingObserver && !Scp173Events.HasRemovedObserver)
        {
            return true;
        }

        __result = 0;
        if (targetHub == null)
        {
            return false;
        }

        if (!targetHub.IsHuman())
        {
            // Not an observer candidate (official: not an enemy): dropped without events, as officially.
            if (__instance.Observers.Remove(targetHub))
            {
                __result = -1;
            }

            return false;
        }

        bool observed = __instance.IsObservedBy(targetHub, 0.2f);
        bool tracked = __instance.Observers.Contains(targetHub);
        if (observed)
        {
            if (tracked)
            {
                return false;
            }

            if (Scp173Events.HasAddingObserver)
            {
                Scp173AddingObserverEventArgs e = new(targetHub, __instance.Owner);
                Scp173Events.OnAddingObserver(e);
                if (!e.IsAllowed)
                {
                    return false;
                }
            }

            __instance.Observers.Add(targetHub);
            if (Scp173Events.HasAddedObserver)
            {
                Scp173Events.OnAddedObserver(new Scp173AddedObserverEventArgs(targetHub, __instance.Owner));
            }

            __result = 1;
            return false;
        }

        if (!tracked)
        {
            return false;
        }

        if (Scp173Events.HasRemovingObserver)
        {
            Scp173RemovingObserverEventArgs e = new(targetHub, __instance.Owner);
            Scp173Events.OnRemovingObserver(e);
            if (!e.IsAllowed)
            {
                return false;
            }
        }

        __instance.Observers.Remove(targetHub);
        if (Scp173Events.HasRemovedObserver)
        {
            Scp173Events.OnRemovedObserver(new Scp173RemovedObserverEventArgs(targetHub, __instance.Owner));
        }

        __result = -1;
        return false;
    }
}

// Official: PlayerRoles/PlayableScps/Scp173/Scp173SnapAbility.cs ServerProcessCmd
[HarmonyPatch(typeof(Scp173SnapAbility), nameof(Scp173SnapAbility.ServerProcessCmd))]
internal static class Scp173SnappingPatch
{
    private static bool Prefix(Scp173SnapAbility __instance, NetworkReader reader)
    {
        if (!Scp173Events.HasSnapping && !Scp173Events.HasSnapped)
        {
            return true;
        }

        __instance._targetHub = reader.ReadReferenceHub();
        if (__instance._observersTracker.IsObserved || __instance._targetHub == null || __instance._targetHub.roleManager.CurrentRole is not IFpcRole fpcRole || __instance.IsSpeeding)
        {
            return false;
        }

        if (Scp173Events.HasSnapping)
        {
            Scp173SnappingEventArgs e = new(__instance.Owner, __instance._targetHub);
            Scp173Events.OnSnapping(e);
            if (!e.IsAllowed)
            {
                return false;
            }

            __instance._targetHub = e.Target.ReferenceHub;
        }

        // Fork body: backtrack both players, raycast the snap, restore.
        FirstPersonMovementModule ownModule = __instance.ScpRole.FpcModule;
        FirstPersonMovementModule targetModule = fpcRole.FpcModule;
        Transform camera = __instance.Owner.PlayerCameraReference;
        Vector3 targetPosition = targetModule.Position;
        Vector3 ownPosition = ownModule.Position;
        Quaternion rotation = camera.rotation;
        targetModule.Position = targetModule.Tracer.GenerateBounds(0.4f, ignoreTeleports: true).ClosestPoint(reader.ReadRelativePosition().Position);
        Bounds bounds = ownModule.Tracer.GenerateBounds(0.1f, ignoreTeleports: true);
        bounds.Encapsulate(ownModule.Position + ownModule.Motor.Velocity * 0.2f);
        ownModule.Position = bounds.ClosestPoint(reader.ReadRelativePosition().Position);
        camera.rotation = reader.ReadLowPrecisionQuaternion().Value;
        ReferenceHub.TryGetHostHub(out ReferenceHub host);
        bool hitboxesSet = !__instance.Owner.isLocalPlayer && HitboxIdentity.SetOwnHitboxes(host, state: true);
        bool hit = Scp173SnapAbility.TryHitTarget(camera, out ReferenceHub target);
        if (hitboxesSet)
        {
            HitboxIdentity.SetOwnHitboxes(host, state: false);
        }

        if (hit && target.playerStats.DealDamage(__instance.ScpRole.DamageHandler))
        {
            Hitmarker.SendHitmarker(__instance.Owner, 1f);
            if (__instance.ScpRole.SubroutineModule.TryGetSubroutine(out Scp173AudioPlayer audio))
            {
                audio.ServerSendSound(Scp173AudioPlayer.Scp173SoundId.Snap);
            }
        }

        targetModule.Position = targetPosition;
        ownModule.Position = ownPosition;
        camera.rotation = rotation;

        if (Scp173Events.HasSnapped)
        {
            Scp173Events.OnSnapped(new Scp173SnappedEventArgs(__instance.Owner, __instance._targetHub));
        }

        return false;
    }
}

// Official: PlayerRoles/PlayableScps/Scp173/Scp173TantrumAbility.cs ServerProcessCmd
[HarmonyPatch(typeof(Scp173TantrumAbility), nameof(Scp173TantrumAbility.ServerProcessCmd))]
internal static class Scp173CreatingTantrumPatch
{
    private static bool Prefix(Scp173TantrumAbility __instance)
    {
        if (!Scp173Events.HasCreatingTantrum && !Scp173Events.HasCreatedTantrum)
        {
            return true;
        }

        if (!__instance.Cooldown.IsReady || __instance._observersTracker.IsObserved
            || !Physics.Raycast(__instance.ScpRole.FpcModule.Position, Vector3.down, out RaycastHit hitInfo, 3f, __instance._tantrumMask))
        {
            return false;
        }

        if (Scp173Events.HasCreatingTantrum)
        {
            Scp173CreatingTantrumEventArgs e = new(__instance.Owner);
            Scp173Events.OnCreatingTantrum(e);
            if (!e.IsAllowed)
            {
                return false;
            }
        }

        __instance.Cooldown.Trigger(30f);
        __instance.ServerSendRpc(toAll: true);
        TantrumEnvironmentalHazard tantrum = Object.Instantiate(__instance._tantrumPrefab);
        tantrum.SynchronizedPosition = new RelativePosition(hitInfo.point + Vector3.up * 1.25f);
        NetworkServer.Spawn(tantrum.gameObject);
        foreach (TeslaGate gate in TeslaGateController.Singleton.TeslaGates)
        {
            if (gate.PlayerInHurtRange(__instance.Owner.gameObject))
            {
                gate.TantrumsToBeDestroyed.Add(tantrum);
            }
        }

        if (Scp173Events.HasCreatedTantrum)
        {
            Scp173Events.OnCreatedTantrum(new Scp173CreatedTantrumEventArgs(tantrum, __instance.Owner));
        }

        return false;
    }
}

// Official: PlayerRoles/PlayableScps/Scp173/Scp173TeleportAbility.cs TryBlink
[HarmonyPatch(typeof(Scp173TeleportAbility), nameof(Scp173TeleportAbility.TryBlink))]
internal static class Scp173TeleportingPatch
{
    private static bool Prefix(Scp173TeleportAbility __instance, float maxDis, ref bool __result)
    {
        if (!Scp173Events.HasTeleporting && !Scp173Events.HasTeleported)
        {
            return true;
        }

        __result = false;
        if (__instance._fpcModule == null || __instance._blinkTimer == null)
        {
            return false;
        }

        maxDis = Mathf.Clamp(maxDis, 0f, __instance.EffectiveBlinkDistance);
        if (!__instance._blinkTimer.AbilityReady || !__instance._fpcModule.TryGetTeleportPos(maxDis, out __instance._tpPosition, out _))
        {
            return false;
        }

        if (Scp173Events.HasTeleporting)
        {
            Scp173TeleportingEventArgs e = new(__instance.Owner, __instance._tpPosition);
            Scp173Events.OnTeleporting(e);
            if (!e.IsAllowed)
            {
                return false;
            }

            __instance._tpPosition = e.Position;
        }

        float halfHeight = __instance._fpcModule.CharController.height / 2f;
        __instance._blinkTimer.ServerBlink(__instance._tpPosition + Vector3.up * halfHeight);
        __result = true;

        if (Scp173Events.HasTeleported)
        {
            Scp173Events.OnTeleported(new Scp173TeleportedEventArgs(__instance.Owner, __instance._tpPosition));
        }

        return false;
    }
}
