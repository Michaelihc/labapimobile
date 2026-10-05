using CustomPlayerEffects;
using HarmonyLib;
using LabApi.Events.Arguments.Scp049Events;
using LabApi.Events.Handlers;
using LabApi.Features.Wrappers;
using Mirror;
using PlayerRoles;
using PlayerRoles.PlayableScps;
using PlayerRoles.PlayableScps.HumeShield;
using PlayerRoles.PlayableScps.Scp049;
using PlayerStatsSystem;
using System;
using UnityEngine;
using Utils.Networking;

namespace LabApi.Events.Patches.Scps;

// Official: PlayerRoles/PlayableScps/Scp049/Scp049AttackAbility.cs ServerProcessCmd
[HarmonyPatch(typeof(Scp049AttackAbility), nameof(Scp049AttackAbility.ServerProcessCmd))]
internal static class Scp049AttackingPatch
{
    private static readonly AccessTools.FieldRef<Scp049AttackAbility, Action<ReferenceHub>> OnServerHit =
        AccessTools.FieldRefAccess<Scp049AttackAbility, Action<ReferenceHub>>("OnServerHit");

    private static bool Prefix(Scp049AttackAbility __instance, NetworkReader reader)
    {
        if (!Scp049Events.HasAttacking && !Scp049Events.HasAttacked)
        {
            return true;
        }

        // Mirrors the fork method body with the official event points.
        if (!__instance.Cooldown.IsReady || (__instance._resurrect != null && __instance._resurrect.IsInProgress))
        {
            return false;
        }

        ReferenceHub target = reader.ReadReferenceHub();
        __instance._target = target;
        if (target == null || !__instance.IsTargetValid(target))
        {
            __instance.Cooldown.Trigger(1.5f);
            __instance.ServerSendRpc(toAll: true);
            return false;
        }

        ReferenceHub owner = __instance.Owner;
        CardiacArrest effect = target.playerEffectsController.GetEffect<CardiacArrest>();
        bool instantKill = effect != null && effect.IsEnabled;
        bool isSenseTarget = IsSenseTarget(__instance, target);
        float cooldown = 1.5f;

        if (Scp049Events.HasAttacking)
        {
            Scp049AttackingEventArgs e = new(owner, target, instantKill, isSenseTarget, cooldown);
            Scp049Events.OnAttacking(e);
            if (!e.IsAllowed)
            {
                return false;
            }

            instantKill = e.InstantKill;
            isSenseTarget = e.IsSenseTarget;
            cooldown = e.CooldownTime;
        }

        __instance.Cooldown.Trigger(cooldown);
        if (instantKill)
        {
            target.playerStats.DealDamage(new Scp049DamageHandler(owner, -1f, Scp049DamageHandler.AttackType.Instakill));
        }
        else if (effect != null)
        {
            effect.SetAttacker(owner);
            effect.Intensity = 1;
            effect.ServerChangeDuration(__instance._statusEffectDuration);
        }

        OnServerHit(__instance)?.Invoke(target);
        __instance.ServerSendRpc(toAll: true);
        Hitmarker.SendHitmarker(owner, 1f);

        if (Scp049Events.HasAttacked)
        {
            Scp049Events.OnAttacked(new Scp049AttackedEventArgs(owner, target, instantKill, isSenseTarget));
        }

        return false;
    }

    private static bool IsSenseTarget(Scp049AttackAbility ability, ReferenceHub target)
    {
        ability.GetSubroutine(out Scp049SenseAbility sense);
        return sense != null && sense.HasTarget && sense.Target == target;
    }
}

// Official: PlayerRoles/PlayableScps/Scp049/Scp049CallAbility.cs ServerProcessCmd
[HarmonyPatch(typeof(Scp049CallAbility), nameof(Scp049CallAbility.ServerProcessCmd))]
internal static class Scp049UsingDoctorsCallPatch
{
    private static bool Prefix(Scp049CallAbility __instance)
    {
        if (!Scp049Events.HasUsingDoctorsCall && !Scp049Events.HasUsedDoctorsCall)
        {
            return true;
        }

        if (__instance._serverTriggered || !__instance.Cooldown.IsReady)
        {
            return false;
        }

        if (Scp049Events.HasUsingDoctorsCall)
        {
            Scp049UsingDoctorsCallEventArgs e = new(__instance.Owner);
            Scp049Events.OnUsingDoctorsCall(e);
            if (!e.IsAllowed)
            {
                return false;
            }
        }

        __instance.Duration.Trigger(20f);
        __instance._serverTriggered = true;
        __instance.ServerSendRpc(toAll: true);

        if (Scp049Events.HasUsedDoctorsCall)
        {
            Scp049Events.OnUsedDoctorsCall(new Scp049UsedDoctorsCallEventArgs(__instance.Owner));
        }

        return false;
    }
}

// Official: PlayerRoles/PlayableScps/Scp049/Scp049SenseAbility.cs ServerProcessCmd
[HarmonyPatch(typeof(Scp049SenseAbility), nameof(Scp049SenseAbility.ServerProcessCmd))]
internal static class Scp049UsingSensePatch
{
    private static bool Prefix(Scp049SenseAbility __instance, NetworkReader reader)
    {
        if (!Scp049Events.HasUsingSense && !Scp049Events.HasUsedSense)
        {
            return true;
        }

        if (!__instance.Cooldown.IsReady || !__instance.Duration.IsReady)
        {
            return false;
        }

        __instance.HasTarget = false;
        __instance.Target = reader.ReadReferenceHub();
        if (__instance.Target == null)
        {
            __instance.Cooldown.Trigger(5f);
            __instance.ServerSendRpc(toAll: true);
            return false;
        }

        if (__instance.Target.roleManager.CurrentRole is not HumanRole humanRole)
        {
            return false;
        }

        float radius = humanRole.FpcModule.CharController.radius;
        Vector3 cameraPosition = humanRole.CameraPosition;
        if (!VisionInformation.GetVisionInformation(__instance.Owner, __instance.Owner.PlayerCameraReference, cameraPosition, radius, __instance._distanceThreshold).IsLooking)
        {
            return false;
        }

        if (Scp049Events.HasUsingSense)
        {
            Scp049UsingSenseEventArgs e = new(__instance.Owner, __instance.Target);
            Scp049Events.OnUsingSense(e);
            if (!e.IsAllowed)
            {
                return false;
            }

            __instance.Target = e.Target.ReferenceHub;
        }

        __instance.Duration.Trigger(20f);
        __instance.HasTarget = true;
        __instance.ServerSendRpc(toAll: true);

        if (Scp049Events.HasUsedSense)
        {
            Scp049Events.OnUsedSense(new Scp049UsedSenseEventArgs(__instance.Owner, __instance.Target));
        }

        return false;
    }
}

// Official: PlayerRoles/PlayableScps/Scp049/Scp049SenseAbility.cs ServerLoseTarget
[HarmonyPatch(typeof(Scp049SenseAbility), nameof(Scp049SenseAbility.ServerLoseTarget))]
internal static class Scp049SenseLostTargetPatch
{
    private static void Postfix(Scp049SenseAbility __instance)
    {
        if (Scp049Events.HasSenseLostTarget)
        {
            Scp049Events.OnSenseLostTarget(new Scp049SenseLostTargetEventArgs(__instance.Owner, __instance.Target));
        }
    }
}

// Official: PlayerRoles/PlayableScps/Scp049/Scp049SenseAbility.cs ServerProcessKilledPlayer
[HarmonyPatch(typeof(Scp049SenseAbility), nameof(Scp049SenseAbility.ServerProcessKilledPlayer))]
internal static class Scp049SenseKilledTargetPatch
{
    private static void Prefix(Scp049SenseAbility __instance, ReferenceHub hub, out bool __state)
    {
        __state = Scp049Events.HasSenseKilledTarget && __instance.HasTarget && __instance.Target == hub;
    }

    private static void Postfix(Scp049SenseAbility __instance, bool __state)
    {
        if (__state)
        {
            Scp049Events.OnSenseKilledTarget(new Scp049SenseKilledTargetEventArgs(__instance.Owner, __instance.Target));
        }
    }
}

// Official: PlayerRoles/PlayableScps/Scp049/Scp049ResurrectAbility.cs ServerValidateBegin
[HarmonyPatch(typeof(Scp049ResurrectAbility), nameof(Scp049ResurrectAbility.ServerValidateBegin))]
internal static class Scp049StartingResurrectionPatch
{
    private static bool Prefix(Scp049ResurrectAbility __instance, BasicRagdoll ragdoll, ref byte __result)
    {
        if (!Scp049Events.HasStartingResurrection)
        {
            return true;
        }

        Scp049ResurrectAbility.ResurrectError error = __instance.CheckBeginConditions(ragdoll);
        if (error == Scp049ResurrectAbility.ResurrectError.None && !__instance.ServerValidateAny())
        {
            error = Scp049ResurrectAbility.ResurrectError.TargetNull;
        }

        Scp049StartingResurrectionEventArgs e = new(error == Scp049ResurrectAbility.ResurrectError.None, ragdoll, ragdoll.Info.OwnerHub, __instance.Owner);
        Scp049Events.OnStartingResurrection(e);
        if (!e.IsAllowed)
        {
            __result = (byte)Scp049ResurrectAbility.ResurrectError.TargetNull;
            return false;
        }

        if (e.CanResurrect)
        {
            __result = (byte)Scp049ResurrectAbility.ResurrectError.None;
        }
        else
        {
            __result = (byte)(error == Scp049ResurrectAbility.ResurrectError.None ? Scp049ResurrectAbility.ResurrectError.TargetInvalid : error);
        }

        return false;
    }
}

// Official: PlayerRoles/PlayableScps/Scp049/Scp049ResurrectAbility.cs ServerComplete
[HarmonyPatch(typeof(Scp049ResurrectAbility), nameof(Scp049ResurrectAbility.ServerComplete))]
internal static class Scp049ResurrectingBodyPatch
{
    private static bool Prefix(Scp049ResurrectAbility __instance)
    {
        if (!Scp049Events.HasResurrectingBody && !Scp049Events.HasResurrectedBody)
        {
            return true;
        }

        BasicRagdoll ragdoll = __instance.CurRagdoll;
        ReferenceHub ownerHub = ragdoll.Info.OwnerHub;
        if (Scp049Events.HasResurrectingBody)
        {
            Scp049ResurrectingBodyEventArgs e = new(Ragdoll.Get(ragdoll), ownerHub, __instance.Owner);
            Scp049Events.OnResurrectingBody(e);
            if (!e.IsAllowed)
            {
                return false;
            }

            ownerHub = e.Target.ReferenceHub;
        }

        ownerHub.transform.position = __instance.ScpRole.FpcModule.Position;
        if (__instance._senseAbility.DeadTargets.Contains(ownerHub))
        {
            HumeShieldModuleBase humeShield = __instance.ScpRole.HumeShieldModule;
            humeShield.HsCurrent = Mathf.Min(humeShield.HsCurrent + 100f, humeShield.HsMax);
        }

        ownerHub.roleManager.ServerSetRole(RoleTypeId.Scp0492, RoleChangeReason.Revived);
        NetworkServer.Destroy(ragdoll.gameObject);

        if (Scp049Events.HasResurrectedBody)
        {
            Scp049Events.OnResurrectedBody(new Scp049ResurrectedBodyEventArgs(ownerHub, __instance.Owner));
        }

        return false;
    }
}
