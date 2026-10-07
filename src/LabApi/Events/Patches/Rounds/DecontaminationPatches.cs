namespace LabApi.Events.Patches.Rounds;

using HarmonyLib;
using LabApi.Events.Arguments.ServerEvents;
using LabApi.Events.Handlers;
using LabApi.Features.Wrappers;
using LightContainmentZoneDecontamination;
using Mirror;
using System;
using System.Reflection;

/// <summary>
/// Raises <see cref="ServerEvents.LczDecontaminationStarting"/> and <see cref="ServerEvents.LczDecontaminationStarted"/>.
/// Official: LightContainmentZoneDecontamination.DecontaminationController.FinishDecontamination.
/// </summary>
[HarmonyPatch(typeof(DecontaminationController), nameof(DecontaminationController.FinishDecontamination))]
internal static class DecontaminationStartPatch
{
    private static bool Prefix(out bool __state)
    {
        __state = false;
        if (!NetworkServer.active)
        {
            return true;
        }

        if (ServerEvents.HasLczDecontaminationStarting)
        {
            LczDecontaminationStartingEventArgs e = new();
            ServerEvents.OnLczDecontaminationStarting(e);
            if (!e.IsAllowed)
            {
                return false;
            }
        }

        __state = true;
        return true;
    }

    private static void Postfix(bool __state)
    {
        if (__state)
        {
            ServerEvents.OnLczDecontaminationStarted();
        }
    }
}

/// <summary>
/// Raises <see cref="ServerEvents.LczDecontaminationAnnounced"/> when a non-final phase is processed and applies a
/// pending <see cref="Decontamination.Offset"/> once the timer starts.
/// Official: LightContainmentZoneDecontamination.DecontaminationController.UpdateTime.
/// </summary>
[HarmonyPatch(typeof(DecontaminationController), nameof(DecontaminationController.UpdateTime))]
internal static class DecontaminationAnnouncedPatch
{
    private static void Prefix(DecontaminationController __instance, out int __state)
    {
        __state = __instance._nextPhase;
        if (Decontamination.PendingOffset != 0f && NetworkServer.active)
        {
            Decontamination.ApplyPendingOffset(__instance);
        }
    }

    private static void Postfix(DecontaminationController __instance, int __state)
    {
        if (__instance._nextPhase != __state && ServerEvents.HasLczDecontaminationAnnounced && NetworkServer.active)
        {
            ServerEvents.OnLczDecontaminationAnnounced(new LczDecontaminationAnnouncedEventArgs(__state));
        }
    }
}

/// <summary>
/// Keeps <see cref="Decontamination.Offset"/> when the CarlModExtras module of Carl Mod 0.0.5 restarts the decontamination
/// timer at round start (<c>DmFun.ResetDecontamination</c>, which sets the synchronized round start time to the current
/// time; it runs unless the <c>disable_decontamination</c> config is set). The part of the offset that was folded into the
/// round start time is applied again. Not applied on builds without that method.
/// Official: no equivalent (LightContainmentZoneDecontamination.DecontaminationController keeps its own TimeOffset).
/// </summary>
[HarmonyPatch]
internal static class DecontaminationResetPatch
{
    private static readonly MethodInfo? Target = CarlModDeathmatch.ModuleType == null ? null : AccessTools.DeclaredMethod(CarlModDeathmatch.ModuleType, "ResetDecontamination", Type.EmptyTypes);

    private static bool Prepare() => Target != null;

    private static MethodBase TargetMethod() => Target!;

    private static void Prefix(out double __state)
    {
        DecontaminationController controller = DecontaminationController.Singleton;
        __state = controller == null ? 0.0 : controller.RoundStartTime;
    }

    private static void Postfix(double __state)
    {
        DecontaminationController controller = DecontaminationController.Singleton;
        if (controller != null && NetworkServer.active && controller.RoundStartTime != __state)
        {
            Decontamination.OnTimerRestarted(controller);
        }
    }
}
