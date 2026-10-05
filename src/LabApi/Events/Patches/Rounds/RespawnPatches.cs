namespace LabApi.Events.Patches.Rounds;

using CarlModExtras;
using HarmonyLib;
using LabApi.Events.Arguments.ServerEvents;
using LabApi.Events.Handlers;
using LabApi.Features.Console;
using Mirror;
using NorthwoodLib.Pools;
using PlayerRoles;
using Respawning;
using Respawning.NamingRules;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using PlayerWrapper = LabApi.Features.Wrappers.Player;

/// <summary>
/// Raises <see cref="ServerEvents.WaveTeamSelecting"/> and <see cref="ServerEvents.WaveTeamSelected"/> at the
/// respawn manager's team selection. Official: Respawning.WaveManager.InitiateRespawn.
/// </summary>
/// <remarks>
/// Replaces the <see cref="DmFun.ChooseTeam"/> call in <see cref="RespawnManager.Update"/>. A cancelled selection
/// restarts the respawn cooldown (the fork has no idle state to retry from), like a selection with no spectators.
/// </remarks>
[HarmonyPatch(typeof(RespawnManager), nameof(RespawnManager.Update))]
internal static class RespawnPatches
{
    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, ILGenerator generator)
    {
        MethodInfo chooseTeam = AccessTools.Method(typeof(DmFun), nameof(DmFun.ChooseTeam));
        bool patched = false;

        foreach (CodeInstruction instruction in instructions)
        {
            if (patched || !instruction.Calls(chooseTeam))
            {
                yield return instruction;
                continue;
            }

            patched = true;
            Label selected = generator.DefineLabel();

            // SpawnableTeamType team = SelectTeam(); if (team == None) { RestartSequence(); return; }
            yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(RespawnPatches), nameof(SelectTeam))).MoveLabelsFrom(instruction);
            yield return new CodeInstruction(OpCodes.Dup);
            yield return new CodeInstruction(OpCodes.Brtrue, selected);
            yield return new CodeInstruction(OpCodes.Pop);
            yield return new CodeInstruction(OpCodes.Ldarg_0);
            yield return new CodeInstruction(OpCodes.Call, AccessTools.Method(typeof(RespawnManager), nameof(RespawnManager.RestartSequence)));
            yield return new CodeInstruction(OpCodes.Ret);
            yield return new CodeInstruction(OpCodes.Nop).WithLabels(selected);
        }

        if (!patched)
        {
            Logger.Warn($"[PATCHES] {nameof(RespawnPatches)}: DmFun.ChooseTeam call not found, wave team selection events disabled.");
        }
    }

    /// <summary>
    /// Selects a team as <see cref="RespawnManager"/> does and raises the selection events.
    /// </summary>
    /// <returns>The selected team, or <see cref="SpawnableTeamType.None"/> when the selection was cancelled.</returns>
    private static SpawnableTeamType SelectTeam() => RaiseTeamSelection(DmFun.ChooseTeam());

    /// <summary>
    /// Raises the wave team selection events for a team.
    /// </summary>
    /// <param name="team">The team chosen by the game.</param>
    /// <returns>The team to spawn, or <see cref="SpawnableTeamType.None"/> when the selection was cancelled.</returns>
    internal static SpawnableTeamType RaiseTeamSelection(SpawnableTeamType team)
    {
        if (!RespawnManager.SpawnableTeams.TryGetValue(team, out SpawnableTeamHandlerBase handler))
        {
            return team;
        }

        if (ServerEvents.HasWaveTeamSelecting)
        {
            WaveTeamSelectingEventArgs e = new(handler);
            ServerEvents.OnWaveTeamSelecting(e);
            if (!e.IsAllowed)
            {
                return SpawnableTeamType.None;
            }

            if (e.Wave != null && e.Wave != handler && TryGetTeam(e.Wave, out SpawnableTeamType newTeam))
            {
                team = newTeam;
                handler = e.Wave;
            }
        }

        if (ServerEvents.HasWaveTeamSelected)
        {
            ServerEvents.OnWaveTeamSelected(new WaveTeamSelectedEventArgs(handler));
        }

        return team;
    }

    /// <summary>
    /// Selects and spawns a wave with its arrival effects, like the natural Carl Mod team selection.
    /// </summary>
    /// <param name="team">The team to spawn.</param>
    internal static void InitiateRespawn(SpawnableTeamType team)
    {
        RespawnManager manager = RespawnManager.Singleton;
        if (!NetworkServer.active || manager == null)
        {
            return;
        }

        team = RaiseTeamSelection(team);
        if (team == SpawnableTeamType.None)
        {
            return;
        }

        RespawnEffectsController.ExecuteAllEffects(RespawnEffectsController.EffectType.Selection, team);
        manager.ForceSpawnTeam(team);
    }

    private static bool TryGetTeam(SpawnableTeamHandlerBase handler, out SpawnableTeamType team)
    {
        foreach (KeyValuePair<SpawnableTeamType, SpawnableTeamHandlerBase> pair in RespawnManager.SpawnableTeams)
        {
            if (pair.Value == handler)
            {
                team = pair.Key;
                return true;
            }
        }

        team = SpawnableTeamType.None;
        return false;
    }
}

/// <summary>
/// Raises <see cref="ServerEvents.WaveRespawning"/> and <see cref="ServerEvents.WaveRespawned"/>.
/// Official: Respawning.Waves.WaveSpawner.SpawnWave.
/// </summary>
/// <remarks>
/// While either event has subscribers, <see cref="RespawnManager.Spawn"/> is replaced with the same steps so the
/// player-to-role map can be edited before roles are set. Without subscribers the original runs untouched.
/// </remarks>
[HarmonyPatch(typeof(RespawnManager), nameof(RespawnManager.Spawn))]
internal static class WaveSpawnPatch
{
    private static readonly FieldInfo ServerOnRespawnedField = AccessTools.Field(typeof(RespawnManager), nameof(RespawnManager.ServerOnRespawned));

    private static readonly Comparison<ReferenceHub> ByActiveTimeDescending = static (a, b) => b.roleManager.CurrentRole.ActiveTime.CompareTo(a.roleManager.CurrentRole.ActiveTime);

    private static bool Prefix(RespawnManager __instance)
    {
        if (!ServerEvents.HasWaveRespawning && !ServerEvents.HasWaveRespawned)
        {
            return true;
        }

        SpawnableTeamType team = __instance.NextKnownTeam;
        if (team == SpawnableTeamType.None || !RespawnManager.SpawnableTeams.TryGetValue(team, out SpawnableTeamHandlerBase handler))
        {
            // The original logs the undefined team.
            return true;
        }

        Spawn(__instance, team, handler);
        return false;
    }

    private static void Spawn(RespawnManager manager, SpawnableTeamType team, SpawnableTeamHandlerBase handler)
    {
        List<ReferenceHub> candidates = ListPool<ReferenceHub>.Shared.Rent();
        foreach (ReferenceHub hub in ReferenceHub.AllHubs)
        {
            if (manager.CheckSpawnable(hub))
            {
                candidates.Add(hub);
            }
        }

        if (manager._prioritySpawn)
        {
            candidates.Sort(ByActiveTimeDescending);
        }
        else
        {
            candidates.ShuffleList();
        }

        int maxWaveSize = handler.MaxWaveSize;
        if (candidates.Count > maxWaveSize)
        {
            candidates.RemoveRange(maxWaveSize, candidates.Count - maxWaveSize);
        }

        if (candidates.Count == 0)
        {
            // Official WaveSpawner.SpawnWave returns before the events when nobody can spawn; the fork spawns nobody,
            // reports an empty wave and clears the selected team.
            ListPool<ReferenceHub>.Shared.Return(candidates);
            List<ReferenceHub> none = ListPool<ReferenceHub>.Shared.Rent();
            (ServerOnRespawnedField.GetValue(null) as Action<SpawnableTeamType, List<ReferenceHub>>)?.Invoke(team, none);
            ListPool<ReferenceHub>.Shared.Return(none);
            manager.NextKnownTeam = SpawnableTeamType.None;
            return;
        }

        if (UnitNamingRule.TryGetNamingRule(team, out UnitNamingRule rule))
        {
            UnitNameMessageHandler.SendNew(team, rule);
        }

        candidates.ShuffleList();
        Queue<RoleTypeId> queue = new();
        handler.GenerateQueue(queue, candidates.Count);

        Dictionary<ReferenceHub, RoleTypeId> roles = UnityEngine.Pool.DictionaryPool<ReferenceHub, RoleTypeId>.Get();
        foreach (ReferenceHub hub in candidates)
        {
            if (queue.Count == 0)
            {
                ServerLogs.AddLog(ServerLogs.Modules.ClassChange, "Player " + hub.LoggedNameFromRefHub() + " couldn't be spawned. Err msg: Queue empty.", ServerLogs.ServerLogType.GameEvent);
                continue;
            }

            roles[hub] = queue.Dequeue();
        }

        ListPool<ReferenceHub>.Shared.Return(candidates);
        List<ReferenceHub> spawned = ListPool<ReferenceHub>.Shared.Rent();

        if (ServerEvents.HasWaveRespawning)
        {
            WaveRespawningEventArgs e = new(handler, roles);
            ServerEvents.OnWaveRespawning(e);
            if (!e.IsAllowed)
            {
                UnityEngine.Pool.DictionaryPool<PlayerWrapper, RoleTypeId>.Release(e.Roles);
                UnityEngine.Pool.DictionaryPool<ReferenceHub, RoleTypeId>.Release(roles);
                (ServerOnRespawnedField.GetValue(null) as Action<SpawnableTeamType, List<ReferenceHub>>)?.Invoke(team, spawned);
                ListPool<ReferenceHub>.Shared.Return(spawned);
                manager.NextKnownTeam = SpawnableTeamType.None;
                return;
            }

            roles.Clear();
            foreach (KeyValuePair<PlayerWrapper, RoleTypeId> pair in e.Roles)
            {
                if (pair.Key != null && pair.Key.ReferenceHub != null)
                {
                    roles[pair.Key.ReferenceHub] = pair.Value;
                }
            }

            UnityEngine.Pool.DictionaryPool<PlayerWrapper, RoleTypeId>.Release(e.Roles);
        }

        foreach (KeyValuePair<ReferenceHub, RoleTypeId> pair in roles)
        {
            if (spawned.Count >= maxWaveSize)
            {
                break;
            }

            ReferenceHub hub = pair.Key;
            try
            {
                hub.roleManager.ServerSetRole(pair.Value, RoleChangeReason.Respawn);
                spawned.Add(hub);
                ServerLogs.AddLog(ServerLogs.Modules.ClassChange, "Player " + hub.LoggedNameFromRefHub() + " respawned as " + pair.Value.ToString() + ".", ServerLogs.ServerLogType.GameEvent);
            }
            catch (Exception ex)
            {
                if (hub != null)
                {
                    ServerLogs.AddLog(ServerLogs.Modules.ClassChange, "Player " + hub.LoggedNameFromRefHub() + " couldn't be spawned. Err msg: " + ex.Message, ServerLogs.ServerLogType.GameEvent);
                }
                else
                {
                    ServerLogs.AddLog(ServerLogs.Modules.ClassChange, "Couldn't spawn a player - target's ReferenceHub is null.", ServerLogs.ServerLogType.GameEvent);
                }
            }
        }

        if (spawned.Count > 0)
        {
            ServerLogs.AddLog(ServerLogs.Modules.ClassChange, "RespawnManager has successfully spawned " + spawned.Count + " players as " + team.ToString() + "!", ServerLogs.ServerLogType.GameEvent);
            RespawnEffectsController.ExecuteAllEffects(RespawnEffectsController.EffectType.UponRespawn, team);
            RespawnTokensManager.RemoveTokens(team, spawned.Count);
        }

        if (ServerEvents.HasWaveRespawned)
        {
            List<PlayerWrapper> players = new(roles.Count);
            foreach (ReferenceHub hub in roles.Keys)
            {
                PlayerWrapper? player = PlayerWrapper.Get(hub);
                if (player != null)
                {
                    players.Add(player);
                }
            }

            ServerEvents.OnWaveRespawned(new WaveRespawnedEventArgs(handler, players));
        }

        UnityEngine.Pool.DictionaryPool<ReferenceHub, RoleTypeId>.Release(roles);
        (ServerOnRespawnedField.GetValue(null) as Action<SpawnableTeamType, List<ReferenceHub>>)?.Invoke(team, spawned);
        ListPool<ReferenceHub>.Shared.Return(spawned);
        manager.NextKnownTeam = SpawnableTeamType.None;
    }
}
