namespace LabApi.Events.Patches.Rounds;

using GameCore;
using HarmonyLib;
using LabApi.Events.Arguments.ServerEvents;
using LabApi.Events.Handlers;
using LabApi.Features.Console;
using LabApi.Features.Wrappers;
using Mirror;
using RoundRestarting;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;

/// <summary>
/// Raises <see cref="ServerEvents.WaitingForPlayers"/> and <see cref="ServerEvents.RoundStarted"/> from the host's
/// <see cref="CharacterClassManager"/> initialization coroutine.
/// </summary>
/// <remarks>
/// Official: CharacterClassManager.Start (OnWaitingForPlayers) and CharacterClassManager.Init (OnRoundStarted).
/// The fork's static <see cref="CharacterClassManager.OnRoundStarted"/> also fires from the RpcRoundStarted receive
/// path on the host client, so the event is raised from the server-only coroutine instead: once per round, right
/// after <c>NetworkRoundStarted = true</c> and before the game assigns roles.
/// </remarks>
[HarmonyPatch]
internal static class RoundInitPatch
{
    private static MethodBase TargetMethod()
        => AccessTools.EnumeratorMoveNext(AccessTools.Method(typeof(CharacterClassManager), nameof(CharacterClassManager.Init)));

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        MethodInfo markInitialized = AccessTools.Method(typeof(RoundStart), nameof(RoundStart.MarkInitialized));
        MethodInfo setRoundStarted = AccessTools.PropertySetter(typeof(CharacterClassManager), nameof(CharacterClassManager.NetworkRoundStarted));
        bool waitingPatched = false;
        bool startedPatched = false;

        foreach (CodeInstruction instruction in instructions)
        {
            yield return instruction;

            if (!waitingPatched && instruction.Calls(markInitialized))
            {
                waitingPatched = true;
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(RoundInitPatch), nameof(OnWaitingForPlayers)));
            }
            else if (!startedPatched && instruction.Calls(setRoundStarted))
            {
                startedPatched = true;
                yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(RoundInitPatch), nameof(OnRoundStarted)));
            }
        }

        if (!waitingPatched || !startedPatched)
        {
            Logger.Warn($"[PATCHES] {nameof(RoundInitPatch)}: IL pattern not found (WaitingForPlayers: {waitingPatched}, RoundStarted: {startedPatched}).");
        }
    }

    private static void OnWaitingForPlayers()
    {
        Decontamination.ResetOffset();
        ServerEvents.OnWaitingForPlayers();
    }

    private static void OnRoundStarted() => ServerEvents.OnRoundStarted();
}

/// <summary>
/// Raises <see cref="ServerEvents.RoundStarting"/>. Official: CharacterClassManager.ForceRoundStart.
/// </summary>
[HarmonyPatch(typeof(CharacterClassManager), nameof(CharacterClassManager.ForceRoundStart))]
internal static class RoundStartingPatch
{
    private static bool Prefix(ref bool __result)
    {
        if (!NetworkServer.active || !ServerEvents.HasRoundStarting)
        {
            return true;
        }

        RoundStartingEventArgs e = new();
        ServerEvents.OnRoundStarting(e);
        if (e.IsAllowed)
        {
            return true;
        }

        __result = false;
        return false;
    }
}

/// <summary>
/// Raises <see cref="ServerEvents.RoundRestarted"/>. Official: RoundRestarting.RoundRestart.InitiateRoundRestart.
/// </summary>
[HarmonyPatch(typeof(RoundRestart), nameof(RoundRestart.InitiateRoundRestart))]
internal static class RoundRestartedPatch
{
    private static void Prefix()
    {
        if (NetworkServer.active)
        {
            ServerEvents.OnRoundRestarted();
        }
    }
}
