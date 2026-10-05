using HarmonyLib;
using Hazards;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using LabApi.Features.Wrappers;
using NorthwoodLib.Pools;
using PlayerRoles;
using PlayerRoles.FirstPersonControl;
using System;
using System.Collections.Generic;

namespace LabApi.Events.Patches.Facility;

/// <summary>
/// Shared state of the hazard patches.
/// </summary>
internal static class HazardPatchState
{
    /// <summary>
    /// Set while a hazard subclass override handles the events itself, so the base method patch does not raise them twice.
    /// </summary>
    [ThreadStatic]
    internal static bool InSubclassCall;
}

// Official: Hazards/EnvironmentalHazard.cs OnEnter (EnteringHazard) and the subclass overrides (EnteredHazard)
// Carl Mod's amnestic cloud uses the base OnEnter, so the base method raises both events for hazards without an override.
[HarmonyPatch(typeof(EnvironmentalHazard), nameof(EnvironmentalHazard.OnEnter))]
internal static class HazardBaseEnterPatch
{
    private static bool Prefix(EnvironmentalHazard __instance, ReferenceHub player, out bool __state)
    {
        __state = false;
        if (HazardPatchState.InSubclassCall || (!PlayerEvents.HasEnteringHazard && !PlayerEvents.HasEnteredHazard))
        {
            return true;
        }

        if (PlayerEvents.HasEnteringHazard)
        {
            PlayerEnteringHazardEventArgs e = new(player, __instance);
            PlayerEvents.OnEnteringHazard(e);
            if (!e.IsAllowed)
            {
                return false;
            }
        }

        __state = PlayerEvents.HasEnteredHazard;
        return true;
    }

    private static void Postfix(EnvironmentalHazard __instance, ReferenceHub player, bool __state)
    {
        if (__state)
        {
            PlayerEvents.OnEnteredHazard(new PlayerEnteredHazardEventArgs(player, __instance));
        }
    }
}

// Official: Hazards/SinkholeEnvironmentalHazard.cs and TantrumEnvironmentalHazard.cs OnEnter
// Both fork overrides apply their effect only to active hazards and non-SCPs, then call the base method.
[HarmonyPatch]
internal static class HazardSubclassEnterPatch
{
    private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(SinkholeEnvironmentalHazard), nameof(SinkholeEnvironmentalHazard.OnEnter));
        yield return AccessTools.Method(typeof(TantrumEnvironmentalHazard), nameof(TantrumEnvironmentalHazard.OnEnter));
    }

    private static bool Prefix(EnvironmentalHazard __instance, ReferenceHub player, out bool __state)
    {
        __state = false;
        if ((!PlayerEvents.HasEnteringHazard && !PlayerEvents.HasEnteredHazard) || !__instance.IsActive || player.IsSCP())
        {
            return true;
        }

        if (PlayerEvents.HasEnteringHazard)
        {
            PlayerEnteringHazardEventArgs e = new(player, __instance);
            PlayerEvents.OnEnteringHazard(e);
            if (!e.IsAllowed)
            {
                return false;
            }
        }

        HazardPatchState.InSubclassCall = true;
        __state = true;
        return true;
    }

    private static void Postfix(EnvironmentalHazard __instance, ReferenceHub player, bool __state)
    {
        if (!__state)
        {
            return;
        }

        HazardPatchState.InSubclassCall = false;
        if (PlayerEvents.HasEnteredHazard)
        {
            PlayerEvents.OnEnteredHazard(new PlayerEnteredHazardEventArgs(player, __instance));
        }
    }

    private static Exception? Finalizer(Exception? __exception)
    {
        HazardPatchState.InSubclassCall = false;
        return __exception;
    }
}

// Official: Hazards/EnvironmentalHazard.cs OnExit (LeavingHazard) and the subclass overrides (LeftHazard)
[HarmonyPatch(typeof(EnvironmentalHazard), nameof(EnvironmentalHazard.OnExit))]
internal static class HazardBaseExitPatch
{
    private static bool Prefix(EnvironmentalHazard __instance, ReferenceHub player, out bool __state)
    {
        __state = false;
        if (HazardPatchState.InSubclassCall || (!PlayerEvents.HasLeavingHazard && !PlayerEvents.HasLeftHazard))
        {
            return true;
        }

        if (PlayerEvents.HasLeavingHazard)
        {
            PlayerLeavingHazardEventArgs e = new(player, __instance);
            PlayerEvents.OnLeavingHazard(e);
            if (!e.IsAllowed)
            {
                return false;
            }
        }

        __state = PlayerEvents.HasLeftHazard;
        return true;
    }

    private static void Postfix(EnvironmentalHazard __instance, ReferenceHub player, bool __state)
    {
        if (__state)
        {
            PlayerEvents.OnLeftHazard(new PlayerLeftHazardEventArgs(player, __instance));
        }
    }
}

// Official: Hazards/SinkholeEnvironmentalHazard.cs and TantrumEnvironmentalHazard.cs OnExit
// The tantrum raises LeftHazard only for an active hazard and a non-SCP, like the official override.
[HarmonyPatch]
internal static class HazardSubclassExitPatch
{
    private static IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(SinkholeEnvironmentalHazard), nameof(SinkholeEnvironmentalHazard.OnExit));
        yield return AccessTools.Method(typeof(TantrumEnvironmentalHazard), nameof(TantrumEnvironmentalHazard.OnExit));
    }

    private static bool Prefix(EnvironmentalHazard __instance, ReferenceHub player, out bool __state)
    {
        __state = false;
        if (!PlayerEvents.HasLeavingHazard && !PlayerEvents.HasLeftHazard)
        {
            return true;
        }

        if (PlayerEvents.HasLeavingHazard)
        {
            PlayerLeavingHazardEventArgs e = new(player, __instance);
            PlayerEvents.OnLeavingHazard(e);
            if (!e.IsAllowed)
            {
                return false;
            }
        }

        HazardPatchState.InSubclassCall = true;
        __state = true;
        return true;
    }

    private static void Postfix(EnvironmentalHazard __instance, ReferenceHub player, bool __state)
    {
        if (!__state)
        {
            return;
        }

        HazardPatchState.InSubclassCall = false;
        if (!PlayerEvents.HasLeftHazard || (__instance is TantrumEnvironmentalHazard && (!__instance.IsActive || player.IsSCP())))
        {
            return;
        }

        PlayerEvents.OnLeftHazard(new PlayerLeftHazardEventArgs(player, __instance));
    }

    private static Exception? Finalizer(Exception? __exception)
    {
        HazardPatchState.InSubclassCall = false;
        return __exception;
    }
}

// Official: Hazards/EnvironmentalHazard.cs UpdateTargets (StayingInHazard)
// Runs every frame for every hazard: the fork method runs untouched unless StayingInHazard has subscribers.
[HarmonyPatch(typeof(EnvironmentalHazard), nameof(EnvironmentalHazard.UpdateTargets))]
internal static class HazardStayPatch
{
    private static bool Prefix(EnvironmentalHazard __instance)
    {
        if (!PlayerEvents.HasStayingInHazard)
        {
            return true;
        }

        List<ReferenceHub> staying = ListPool<ReferenceHub>.Shared.Rent();
        foreach (ReferenceHub hub in ReferenceHub.AllHubs)
        {
            if (hub.roleManager.CurrentRole is not IFpcRole fpcRole)
            {
                continue;
            }

            bool affected = __instance.AffectedPlayers.Contains(hub);
            if (__instance.IsInArea(__instance.SourcePosition, fpcRole.FpcModule.Position))
            {
                if (!affected)
                {
                    __instance.OnEnter(hub);
                }
                else
                {
                    staying.Add(hub);
                }
            }
            else if (affected)
            {
                __instance.OnExit(hub);
            }
        }

        if (staying.Count == 0)
        {
            ListPool<ReferenceHub>.Shared.Return(staying);
            return false;
        }

        PlayersStayingInHazardEventArgs e = new(staying, __instance);
        PlayerEvents.OnStayingInHazard(e);
        staying.Clear();
        foreach (Player player in e.AffectedPlayers)
        {
            staying.Add(player.ReferenceHub);
        }

        ListPool<Player>.Shared.Return(e.AffectedPlayers);
        foreach (ReferenceHub hub in staying)
        {
            __instance.OnStay(hub);
        }

        ListPool<ReferenceHub>.Shared.Return(staying);
        return false;
    }
}
