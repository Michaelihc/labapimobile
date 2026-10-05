using HarmonyLib;
using Interactables.Interobjects;
using Interactables.Interobjects.DoorUtils;
using LabApi.Events.Arguments.Scp096Events;
using LabApi.Events.Handlers;
using Mirror;
using PlayerRoles.PlayableScps.Scp096;
using UnityEngine;

namespace LabApi.Events.Patches.Scps;

// Official: Interactables/Interobjects/PryableDoor.cs TryPryGate
[HarmonyPatch(typeof(PryableDoor), nameof(PryableDoor.TryPryGate))]
internal static class Scp096PryingGatePatch
{
    private static bool Prefix(PryableDoor __instance, ReferenceHub player, ref bool __result)
    {
        if ((!Scp096Events.HasPryingGate && !Scp096Events.HasPriedGate) || !NetworkServer.active)
        {
            return true;
        }

        __result = false;
        if (__instance._blockPryingMask != DoorLockReason.None && ((DoorLockReason)__instance.ActiveLocks).HasFlagFast(__instance._blockPryingMask))
        {
            return false;
        }

        if (!__instance.AllowInteracting(null, 0))
        {
            return false;
        }

        if (Scp096Events.HasPryingGate)
        {
            Scp096PryingGateEventArgs e = new(player, __instance);
            Scp096Events.OnPryingGate(e);
            if (!e.IsAllowed)
            {
                return false;
            }
        }

        __instance.RpcPryGate();
        __instance._remainingPryCooldown = __instance._pryAnimDuration;
        __result = true;

        if (Scp096Events.HasPriedGate)
        {
            Scp096Events.OnPriedGate(new Scp096PriedGateEventArgs(player, __instance));
        }

        return false;
    }
}

// Official: PlayerRoles/PlayableScps/Scp096/Scp096ChargeAbility.cs ServerProcessCmd
[HarmonyPatch(typeof(Scp096ChargeAbility), nameof(Scp096ChargeAbility.ServerProcessCmd))]
internal static class Scp096ChargingPatch
{
    private static bool Prefix(Scp096ChargeAbility __instance)
    {
        if (!Scp096Events.HasCharging && !Scp096Events.HasCharged)
        {
            return true;
        }

        if (!__instance.CanCharge)
        {
            return false;
        }

        if (Scp096Events.HasCharging)
        {
            Scp096ChargingEventArgs e = new(__instance.Owner);
            Scp096Events.OnCharging(e);
            if (!e.IsAllowed)
            {
                return false;
            }
        }

        __instance._hitHandler.Clear();
        __instance.Duration.Trigger(1f);
        __instance.ScpRole.StateController.SetAbilityState(Scp096AbilityState.Charging);
        __instance.ServerSendRpc(toAll: true);

        if (Scp096Events.HasCharged)
        {
            Scp096Events.OnCharged(new Scp096ChargedEventArgs(__instance.Owner));
        }

        return false;
    }
}

// Official: PlayerRoles/PlayableScps/Scp096/Scp096RageManager.cs ServerEnrage
[HarmonyPatch(typeof(Scp096RageManager), nameof(Scp096RageManager.ServerEnrage))]
internal static class Scp096EnragingPatch
{
    private static bool Prefix(Scp096RageManager __instance, float initialDuration)
    {
        if ((!Scp096Events.HasEnraging && !Scp096Events.HasEnraged) || !NetworkServer.active)
        {
            return true;
        }

        if (Scp096Events.HasEnraging)
        {
            Scp096EnragingEventArgs e = new(__instance.Owner, initialDuration);
            Scp096Events.OnEnraging(e);
            if (!e.IsAllowed)
            {
                return false;
            }

            initialDuration = e.InitialDuration;
        }

        __instance.EnragedTimeLeft = initialDuration;
        __instance.TotalRageTime = initialDuration;
        __instance.ScpRole.StateController.SetRageState(Scp096RageState.Distressed);
        __instance.ServerIncreaseDuration(Mathf.Max(__instance._targetsTracker.Targets.Count - 3f, 0f));

        if (Scp096Events.HasEnraged)
        {
            Scp096Events.OnEnraged(new Scp096EnragedEventArgs(__instance.Owner, initialDuration));
        }

        return false;
    }
}

// Official: PlayerRoles/PlayableScps/Scp096/Scp096StateController.cs SetRageState
// SetRageState is a one-line forwarder in the fork, so the RageState setter it calls is patched instead.
[HarmonyPatch(typeof(Scp096StateController), nameof(Scp096StateController.RageState), MethodType.Setter)]
internal static class Scp096ChangingStatePatch
{
    private static bool Prefix(Scp096StateController __instance, ref Scp096RageState value, out bool __state)
    {
        __state = false;
        if ((!Scp096Events.HasChangingState && !Scp096Events.HasChangedState) || !NetworkServer.active || Scp096StateResetPatch.Resetting)
        {
            return true;
        }

        if (Scp096Events.HasChangingState)
        {
            Scp096ChangingStateEventArgs e = new(__instance.Owner, value);
            Scp096Events.OnChangingState(e);
            if (!e.IsAllowed)
            {
                return false;
            }

            value = e.State;
        }

        __state = true;
        return true;
    }

    private static void Postfix(Scp096StateController __instance, Scp096RageState value, bool __state)
    {
        if (__state && Scp096Events.HasChangedState)
        {
            Scp096Events.OnChangedState(new Scp096ChangedStateEventArgs(__instance.Owner, value));
        }
    }
}

// Official: PlayerRoles/PlayableScps/Scp096/Scp096StateController.cs ResetObject
// The official reset assigns the state without raising ChangingState; this keeps the fork's reset silent too.
[HarmonyPatch(typeof(Scp096StateController), nameof(Scp096StateController.ResetObject))]
internal static class Scp096StateResetPatch
{
    internal static bool Resetting;

    private static void Prefix() => Resetting = true;

    private static void Finalizer() => Resetting = false;
}

// Official: PlayerRoles/PlayableScps/Scp096/Scp096TargetsTracker.cs AddTarget
[HarmonyPatch(typeof(Scp096TargetsTracker), nameof(Scp096TargetsTracker.AddTarget))]
internal static class Scp096AddingTargetPatch
{
    private static bool Prefix(Scp096TargetsTracker __instance, ReferenceHub target, bool isForLook, ref bool __result)
    {
        if (!Scp096Events.HasAddingTarget || target == null || __instance.Targets.Contains(target))
        {
            return true;
        }

        Scp096AddingTargetEventArgs e = new(__instance.Owner, target, isForLook);
        Scp096Events.OnAddingTarget(e);
        if (e.IsAllowed)
        {
            return true;
        }

        __result = false;
        return false;
    }

    private static void Postfix(Scp096TargetsTracker __instance, ReferenceHub target, bool isForLook, bool __result)
    {
        if (__result && Scp096Events.HasAddedTarget)
        {
            Scp096Events.OnAddedTarget(new Scp096AddedTargetEventArgs(__instance.Owner, target, isForLook));
        }
    }
}

// Official: PlayerRoles/PlayableScps/Scp096/Scp096TryNotToCryAbility.cs IsActive (setter)
// The fork has no regeneration boost while trying not to cry, so only the state change is gated.
[HarmonyPatch(typeof(Scp096TryNotToCryAbility), nameof(Scp096TryNotToCryAbility.IsActive), MethodType.Setter)]
internal static class Scp096TryingNotToCryPatch
{
    private static bool Prefix(Scp096TryNotToCryAbility __instance, bool value)
    {
        if (!NetworkServer.active)
        {
            return true;
        }

        bool active = __instance.IsActive;
        if (active == value)
        {
            return true;
        }

        if (value)
        {
            if (!Scp096Events.HasTryingNotToCry && !Scp096Events.HasTriedNotToCry)
            {
                return true;
            }

            if (Scp096Events.HasTryingNotToCry)
            {
                Scp096TryingNotToCryEventArgs e = new(__instance.Owner);
                Scp096Events.OnTryingNotToCry(e);
                if (!e.IsAllowed)
                {
                    return false;
                }
            }

            __instance.ScpRole.StateController.SetAbilityState(Scp096AbilityState.TryingNotToCry);
            if (Scp096Events.HasTriedNotToCry)
            {
                Scp096Events.OnTriedNotToCry(new Scp096TriedNotToCryEventArgs(__instance.Owner));
            }

            return false;
        }

        if (!Scp096Events.HasStartCrying && !Scp096Events.HasStartedCrying)
        {
            return true;
        }

        if (Scp096Events.HasStartCrying)
        {
            Scp096StartCryingEventArgs e = new(__instance.Owner);
            Scp096Events.OnStartCrying(e);
            if (!e.IsAllowed)
            {
                return false;
            }
        }

        __instance.ScpRole.ResetAbilityState();
        if (Scp096Events.HasStartedCrying)
        {
            Scp096Events.OnStartedCrying(new Scp096StartedCryingEventArgs(__instance.Owner));
        }

        return false;
    }
}
