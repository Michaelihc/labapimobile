namespace LabApi.Events.Patches.Rounds;

using HarmonyLib;
using LabApi.Events.Arguments.ServerEvents;
using LabApi.Events.Handlers;
using LabApi.Features.Wrappers;
using LightContainmentZoneDecontamination;
using Mirror;

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
