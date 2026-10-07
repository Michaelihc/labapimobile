namespace LabApi.Events.Patches.Rounds;

using GameCore;
using HarmonyLib;
using LabApi.Events.Arguments.ServerEvents;
using LabApi.Events.Handlers;
using Logger = LabApi.Features.Console.Logger;
using MEC;
using PlayerRoles;
using RoundRestarting;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Reflection.Emit;
using UnityEngine;
using RoundWrapper = LabApi.Features.Wrappers.Round;

/// <summary>
/// Raises <see cref="ServerEvents.RoundEndingConditionsCheck"/>, <see cref="ServerEvents.RoundEnding"/> and
/// <see cref="ServerEvents.RoundEnded"/>. Official: RoundSummary._ProcessServerSideCode.
/// </summary>
/// <remarks>
/// The fork's MEC coroutine is replaced with an equivalent one that raises the events at the official points. It keeps
/// the rules of the build's own coroutine: a check every 2.5 s after a 15 s grace period; on Carl Mod 0.0.4 the grace
/// period restarts while no round is in progress, a check runs only after the kill count changed, and a round ends after
/// 30 minutes (overtime); on 0.0.5 the grace period counts from the start of the coroutine (the lobby), every check is
/// evaluated and there is no overtime end. Differences: a vetoed or cancelled ending is re-evaluated on the next check even
/// without a new kill (as the official loop does), <see cref="RoundSummary.ForceEnd"/> (used by
/// <see cref="RoundWrapper.End"/>) ends the round through the same path instead of stalling it, and on the 0.0.4 build with
/// the deathmatch module, with its <c>deathmatch</c> config enabled, the round is checked every 2.5 s with <c>CanEnd = false</c>
/// while <see cref="ServerEvents.RoundEndingConditionsCheck"/> has subscribers, so a plugin can end a deathmatch round.
/// The iterator stub is small enough to be inlined, so its call in <c>RoundSummary.Start</c> is redirected instead.
/// Applied only when the native coroutine is one of the known Carl Mod bodies; otherwise the game's coroutine runs.
/// </remarks>
[HarmonyPatch(typeof(RoundSummary), nameof(RoundSummary.Start))]
internal static class RoundSummaryPatch
{
    private const string DeathmatchKey = "deathmatch";

    // The coroutine's MoveNext without and with the 0.0.4 deathmatch build's "deathmatch" config check, and the 0.0.5 body
    // (no kill-count gate, no overtime end, no grace period restart).
    private const string StandardBody = "27f62fd9e27b3dc7";
    private const string DeathmatchBody = "2edbce31b54627f4";
    private const string Version005Body = "289610b4a4ad274a";

    private static readonly MethodInfo? Target = FindCoroutineBody();

    private static readonly BodyVariant Variant = NativeBody.Identify(Target, StandardBody, DeathmatchBody, Version005Body, out Fingerprint);

    private static readonly string? Fingerprint;

    /// <summary>
    /// Gets whether the round cannot end naturally because of the deathmatch mode: the native round-end check is the 0.0.4
    /// deathmatch build's and its <c>deathmatch</c> config is enabled. Carl Mod 0.0.5 has a <c>deathmatch</c> config too, but
    /// its round-end check ignores it.
    /// </summary>
    internal static bool DeathmatchBlocksRoundEnd => Variant == BodyVariant.Deathmatch && CarlModDeathmatch.IsEnabled;

    private static bool Prepare()
    {
        if (Variant != BodyVariant.Unknown)
        {
            return true;
        }

        PatchManager.Skip(typeof(RoundSummaryPatch), NativeBody.UnknownBody(Target, Fingerprint, "the game's round-end check runs unchanged; RoundEndingConditionsCheck / RoundEnding / RoundEnded are not raised."));
        return false;
    }

    private static MethodInfo? FindCoroutineBody()
    {
        MethodInfo? coroutine = AccessTools.DeclaredMethod(typeof(RoundSummary), nameof(RoundSummary._ProcessServerSideCode));
        Type? stateMachine = coroutine?.GetCustomAttribute<IteratorStateMachineAttribute>()?.StateMachineType;
        return stateMachine == null ? null : AccessTools.DeclaredMethod(stateMachine, "MoveNext");
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo original = AccessTools.Method(typeof(RoundSummary), nameof(RoundSummary._ProcessServerSideCode));
        bool patched = false;
        foreach (CodeInstruction instruction in instructions)
        {
            if (!patched && instruction.Calls(original))
            {
                // this._ProcessServerSideCode() -> ProcessServerSideCode(this): same stack shape.
                patched = true;
                instruction.opcode = OpCodes.Call;
                instruction.operand = AccessTools.Method(typeof(RoundSummaryPatch), nameof(ProcessServerSideCode));
            }

            yield return instruction;
        }

        if (!patched)
        {
            Logger.Warn($"[PATCHES] {nameof(RoundSummaryPatch)}: _ProcessServerSideCode call not found, round ending events disabled.");
        }
    }

    private static IEnumerator<float> ProcessServerSideCode(RoundSummary summary)
    {
        float time = Time.unscaledTime;

        // The 0.0.4 coroutines keep this in RoundSummary.__lastCheckKills, which nothing else reads (0.0.5 has no such field).
        int lastCheckKills = 0;
        bool version005 = Variant == BodyVariant.Version005;
        bool ended = false;

        while (summary != null)
        {
            yield return Timing.WaitForSeconds(2.5f);

            bool forced = !ended && summary._roundEnded && RoundStart.RoundStarted;
            bool deathmatch = false;
            if (!forced)
            {
                deathmatch = Variant == BodyVariant.Deathmatch && ConfigFile.ServerConfig.GetBool(DeathmatchKey);
                if ((deathmatch && !ServerEvents.HasRoundEndingConditionsCheck)
                    || RoundSummary.RoundLock
                    || (summary.KeepRoundOnOne && RoundWrapper.CountNonServerHubs() < 2))
                {
                    continue;
                }

                if (!RoundSummary.RoundInProgress())
                {
                    if (!version005)
                    {
                        time = Time.unscaledTime;
                    }

                    continue;
                }

                if (Time.unscaledTime - time < 15f)
                {
                    continue;
                }

                if (!deathmatch && !version005)
                {
                    if (RoundSummary.Kills == lastCheckKills)
                    {
                        continue;
                    }

                    lastCheckKills = RoundSummary.Kills;
                }
            }

            RoundSummary.SumInfo_ClassList newList = default;
            foreach (ReferenceHub hub in ReferenceHub.AllHubs)
            {
                switch (hub.GetTeam())
                {
                    case Team.ClassD:
                        newList.class_ds++;
                        break;
                    case Team.ChaosInsurgency:
                        newList.chaos_insurgents++;
                        break;
                    case Team.FoundationForces:
                        newList.mtf_and_guards++;
                        break;
                    case Team.Scientists:
                        newList.scientists++;
                        break;
                    case Team.SCPs:
                        if (hub.GetRoleId() == RoleTypeId.Scp0492)
                        {
                            newList.zombies++;
                        }
                        else
                        {
                            newList.scps_except_zombies++;
                        }

                        break;
                }
            }

            yield return float.NegativeInfinity;
            newList.warhead_kills = AlphaWarheadController.Detonated ? AlphaWarheadController.Singleton.WarheadKills : -1;
            yield return float.NegativeInfinity;

            if (summary == null)
            {
                yield break;
            }

            int facilityForces = newList.mtf_and_guards + newList.scientists;
            int chaos = newList.chaos_insurgents + newList.class_ds;
            int anomalies = newList.scps_except_zombies + newList.zombies;
            int escapedClassD = newList.class_ds + RoundSummary.EscapedClassD;
            int escapedScientists = newList.scientists + RoundSummary.EscapedScientists;
            RoundSummary.SurvivingSCPs = newList.scps_except_zombies;
            float classDPercentage = summary.classlistStart.class_ds != 0 ? (float)escapedClassD / summary.classlistStart.class_ds : 0f;
            float scientistPercentage = summary.classlistStart.scientists == 0 ? 1f : (float)escapedScientists / summary.classlistStart.scientists;
            bool overtime = !version005 && RoundStart.RoundLength.TotalSeconds >= 1800.0;

            bool canEnd;
            if (newList.class_ds <= 0 && facilityForces <= 0)
            {
                canEnd = true;
            }
            else
            {
                int aliveTeams = 0;
                if (facilityForces > 0)
                {
                    aliveTeams++;
                }

                if (chaos > 0)
                {
                    aliveTeams++;
                }

                if (anomalies > 0)
                {
                    aliveTeams++;
                }

                canEnd = aliveTeams <= 1 || overtime;
            }

            if (deathmatch)
            {
                canEnd = false;
            }

            if (forced)
            {
                canEnd = true;
            }

            if (ServerEvents.HasRoundEndingConditionsCheck)
            {
                RoundEndingConditionsCheckEventArgs check = new(canEnd);
                ServerEvents.OnRoundEndingConditionsCheck(check);
                if (canEnd && !check.CanEnd)
                {
                    // Vetoed: check again on the next cycle, as the official loop does.
                    lastCheckKills = -1;
                }

                canEnd = check.CanEnd;
            }

            if (!canEnd)
            {
                if (forced)
                {
                    summary._roundEnded = false;
                }

                continue;
            }

            summary._roundEnded = true;

            RoundSummary.LeadingTeam leadingTeam = RoundSummary.LeadingTeam.Draw;
            if (facilityForces > 0)
            {
                leadingTeam = RoundSummary.EscapedScientists < RoundSummary.EscapedClassD ? RoundSummary.LeadingTeam.Draw : RoundSummary.LeadingTeam.FacilityForces;
            }
            else if (anomalies > 0)
            {
                leadingTeam = RoundSummary.EscapedClassD > RoundSummary.SurvivingSCPs
                    ? RoundSummary.LeadingTeam.ChaosInsurgency
                    : (RoundSummary.SurvivingSCPs > RoundSummary.EscapedScientists ? RoundSummary.LeadingTeam.Anomalies : RoundSummary.LeadingTeam.Draw);
            }
            else if (chaos > 0)
            {
                leadingTeam = RoundSummary.EscapedClassD >= RoundSummary.EscapedScientists ? RoundSummary.LeadingTeam.ChaosInsurgency : RoundSummary.LeadingTeam.Draw;
                if (overtime)
                {
                    leadingTeam = RoundSummary.LeadingTeam.Draw;
                }
            }

            if (ServerEvents.HasRoundEnding)
            {
                RoundEndingEventArgs ending = new(leadingTeam);
                ServerEvents.OnRoundEnding(ending);
                if (!ending.IsAllowed)
                {
                    summary._roundEnded = false;
                    lastCheckKills = -1;
                    continue;
                }

                leadingTeam = ending.LeadingTeam;
            }

            ended = true;
            FriendlyFireConfig.PauseDetector = true;
            string text = "Round finished! Anomalies: " + anomalies + " | Chaos: " + chaos + " | Facility Forces: " + facilityForces + " | D escaped percentage: " + classDPercentage + " | S escaped percentage: : " + scientistPercentage;
            GameCore.Console.AddLog(text, Color.gray);
            ServerLogs.AddLog(ServerLogs.Modules.Logger, text, ServerLogs.ServerLogType.GameEvent);
            yield return Timing.WaitForSeconds(1.5f);

            bool showSummary = true;
            if (ServerEvents.HasRoundEnded)
            {
                RoundEndedEventArgs endedArgs = new(leadingTeam);
                ServerEvents.OnRoundEnded(endedArgs);
                showSummary = endedArgs.ShowSummary;
            }

            int restartTime = Mathf.Clamp(ConfigFile.ServerConfig.GetInt("auto_round_restart_time", 10), 5, 1000);
            if (summary != null && showSummary)
            {
                summary.RpcShowRoundSummary(summary.classlistStart, newList, leadingTeam, RoundSummary.EscapedClassD, RoundSummary.EscapedScientists, RoundSummary.KilledBySCPs, restartTime, (int)RoundStart.RoundLength.TotalSeconds);
            }

            yield return Timing.WaitForSeconds(restartTime - 1);
            if (summary != null)
            {
                summary.RpcDimScreen();
            }

            yield return Timing.WaitForSeconds(1f);
            RoundRestart.InitiateRoundRestart();
            if (!version005)
            {
                time = Time.unscaledTime;
            }
        }
    }
}
