namespace LabApi.Events.Patches.Rounds;

using Footprinting;
using HarmonyLib;
using LabApi.Events.Arguments.WarheadEvents;
using LabApi.Events.Handlers;
using Mirror;

/// <summary>
/// Raises <see cref="WarheadEvents.Starting"/> and <see cref="WarheadEvents.Started"/>.
/// Official: AlphaWarheadController.StartDetonation.
/// </summary>
/// <remarks>
/// A changed <see cref="WarheadStartingEventArgs.WarheadState"/> scenario is seeded into the controller before the
/// original copies it; a changed start time is applied after the original runs. <see cref="WarheadEvents.Started"/>
/// fires after the start subtitles are sent.
/// </remarks>
[HarmonyPatch(typeof(AlphaWarheadController), nameof(AlphaWarheadController.StartDetonation))]
internal static class WarheadStartPatch
{
    private static bool Prefix(AlphaWarheadController __instance, ref bool isAutomatic, ref bool suppressSubtitles, ref ReferenceHub trigger, out StartState __state)
    {
        __state = default;
        if (!WarheadEvents.HasStarting && !WarheadEvents.HasStarted)
        {
            return true;
        }

        AlphaWarheadSyncInfo info = __instance.Info;
        if (info.InProgress || __instance.CooldownEndTime > NetworkTime.time || __instance.IsLocked)
        {
            return true;
        }

        __state.Proceed = true;
        if (!WarheadEvents.HasStarting)
        {
            return true;
        }

        info.StartTime = NetworkTime.time;
        WarheadStartingEventArgs e = new(trigger == null ? ReferenceHub.HostHub : trigger, isAutomatic, suppressSubtitles, info);
        WarheadEvents.OnStarting(e);
        if (!e.IsAllowed)
        {
            __state.Proceed = false;
            return false;
        }

        isAutomatic = e.IsAutomatic;
        suppressSubtitles = e.SuppressSubtitles;
        if (e.Player != null)
        {
            trigger = e.Player.ReferenceHub;
        }

        AlphaWarheadSyncInfo state = e.WarheadState;
        if (state.ScenarioId != info.ScenarioId || state.ResumeScenario != info.ResumeScenario)
        {
            // The original builds the synced info from the current one, so seed the chosen scenario (not in progress).
            __instance.Info = new AlphaWarheadSyncInfo
            {
                ScenarioId = state.ScenarioId,
                ResumeScenario = state.ResumeScenario,
                StartTime = 0.0,
            };
        }

        if (state.StartTime != info.StartTime)
        {
            __state.OverrideStartTime = true;
            __state.StartTime = state.StartTime;
        }

        return true;
    }

    private static void Postfix(AlphaWarheadController __instance, bool isAutomatic, bool suppressSubtitles, ReferenceHub trigger, StartState __state)
    {
        if (!__state.Proceed)
        {
            return;
        }

        if (__state.OverrideStartTime)
        {
            AlphaWarheadSyncInfo info = __instance.Info;
            info.StartTime = __state.StartTime;
            if (info != __instance.Info)
            {
                __instance.NetworkInfo = info;
            }
        }

        if (WarheadEvents.HasStarted)
        {
            WarheadEvents.OnStarted(new WarheadStartedEventArgs(trigger == null ? ReferenceHub.HostHub : trigger, isAutomatic, suppressSubtitles, __instance.Info));
        }
    }

    /// <summary>
    /// State passed from the prefix to the postfix.
    /// </summary>
    internal struct StartState
    {
        public bool Proceed;
        public bool OverrideStartTime;
        public double StartTime;
    }
}

/// <summary>
/// Raises <see cref="WarheadEvents.Stopping"/> and <see cref="WarheadEvents.Stopped"/>.
/// Official: AlphaWarheadController.CancelDetonation(ReferenceHub).
/// </summary>
/// <remarks>
/// As in the official method, the cancellation overwrites the start time and picks the resume scenario itself, so a
/// changed <see cref="WarheadStoppingEventArgs.WarheadState"/> has no effect.
/// </remarks>
[HarmonyPatch(typeof(AlphaWarheadController), nameof(AlphaWarheadController.CancelDetonation), new[] { typeof(ReferenceHub) })]
internal static class WarheadStopPatch
{
    private static bool Prefix(AlphaWarheadController __instance, ref ReferenceHub disabler, out bool __state)
    {
        __state = false;
        if (!WarheadEvents.HasStopping && !WarheadEvents.HasStopped)
        {
            return true;
        }

        if (!__instance.Info.InProgress || AlphaWarheadController.TimeUntilDetonation <= 10f || __instance.IsLocked)
        {
            return true;
        }

        __state = true;
        if (!WarheadEvents.HasStopping)
        {
            return true;
        }

        WarheadStoppingEventArgs e = new(disabler == null ? ReferenceHub.HostHub : disabler, __instance.Info);
        WarheadEvents.OnStopping(e);
        if (!e.IsAllowed)
        {
            __state = false;
            return false;
        }

        if (e.Player != null)
        {
            disabler = e.Player.ReferenceHub;
        }

        return true;
    }

    private static void Postfix(AlphaWarheadController __instance, ReferenceHub disabler, bool __state)
    {
        if (__state && NetworkServer.active && WarheadEvents.HasStopped)
        {
            WarheadEvents.OnStopped(new WarheadStoppedEventArgs(disabler == null ? ReferenceHub.HostHub : disabler, __instance.Info));
        }
    }
}

/// <summary>
/// Raises <see cref="WarheadEvents.Detonating"/> and <see cref="WarheadEvents.Detonated"/>.
/// Official: AlphaWarheadController.Detonate.
/// </summary>
[HarmonyPatch(typeof(AlphaWarheadController), nameof(AlphaWarheadController.Detonate))]
internal static class WarheadDetonatePatch
{
    private static bool Prefix(AlphaWarheadController __instance, out ReferenceHub? __state)
    {
        __state = null;
        if (!WarheadEvents.HasDetonating && !WarheadEvents.HasDetonated)
        {
            return true;
        }

        ReferenceHub triggering = __instance._triggeringPlayer.Hub;
        ReferenceHub hub = triggering == null ? ReferenceHub.HostHub : triggering;
        if (WarheadEvents.HasDetonating)
        {
            WarheadDetonatingEventArgs e = new(hub);
            WarheadEvents.OnDetonating(e);
            if (!e.IsAllowed)
            {
                return false;
            }

            ReferenceHub? newHub = e.Player?.ReferenceHub;
            if (newHub != null && newHub != hub)
            {
                __instance._triggeringPlayer = new Footprint(newHub);
            }
        }

        __state = hub;
        return true;
    }

    private static void Postfix(ReferenceHub? __state)
    {
        if (__state != null && WarheadEvents.HasDetonated)
        {
            WarheadEvents.OnDetonated(new WarheadDetonatedEventArgs(__state));
        }
    }
}
