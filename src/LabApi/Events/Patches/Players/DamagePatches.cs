using CarlModExtras;
using GameCore;
using HarmonyLib;
using InventorySystem;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using PlayerRoles;
using PlayerRoles.FirstPersonControl;
using PlayerRoles.Ragdolls;
using PlayerRoles.Spectating;
using PlayerStatsSystem;
using System;
using UnityEngine;

namespace LabApi.Events.Patches.Players;

/// <summary>
/// Replaces <see cref="PlayerStats.DealDamage"/> with the Carl Mod body plus the Hurting / Hurt / Dying / Death events, and
/// its private <c>KillPlayer</c> with <see cref="KillPlayer"/>.
/// </summary>
/// <remarks>
/// The fork's <c>KillPlayer</c> IL is corrupt: when the player is not a <see cref="SpectatorRole"/> after
/// <c>ServerSetRole(Spectator, Died)</c>, its <c>brfalse.s</c> at IL_00a9 jumps into the middle of the call at IL_002d.
/// Vanilla never takes that branch, but with LabAPI a plugin can cancel or change the role in ChangingRole, or set another role
/// from an event raised inside the role change. Harmony cannot patch <c>KillPlayer</c> itself (it cannot resolve that branch
/// target), and <c>DealDamage</c> is its only caller, so <c>DealDamage</c> is always replaced, also without event subscribers.
/// </remarks>
// Official: PlayerStatsSystem/PlayerStats.cs DealDamage
[HarmonyPatch(typeof(PlayerStats), nameof(PlayerStats.DealDamage))]
internal static class PlayerDamagePatch
{
    // The fork's deathmatch broadcasts: "You died, auto-respawning in 5 seconds" and "You killed ".
    private const string DmDiedBroadcast = "你已阵亡，将在 5 秒后自动复活";

    private const string DmKilledBroadcast = "你击杀了 ";

    private static readonly AccessTools.FieldRef<Action<ReferenceHub, DamageHandlerBase>> OnAnyPlayerDamaged =
        AccessTools.StaticFieldRefAccess<Action<ReferenceHub, DamageHandlerBase>>(AccessTools.Field(typeof(PlayerStats), nameof(PlayerStats.OnAnyPlayerDamaged)));

    private static readonly AccessTools.FieldRef<Action<ReferenceHub, DamageHandlerBase>> OnAnyPlayerDied =
        AccessTools.StaticFieldRefAccess<Action<ReferenceHub, DamageHandlerBase>>(AccessTools.Field(typeof(PlayerStats), nameof(PlayerStats.OnAnyPlayerDied)));

    private static readonly AccessTools.FieldRef<PlayerStats, Action<DamageHandlerBase>> OnThisPlayerDamaged =
        AccessTools.FieldRefAccess<PlayerStats, Action<DamageHandlerBase>>(nameof(PlayerStats.OnThisPlayerDamaged));

    private static readonly AccessTools.FieldRef<PlayerStats, Action<DamageHandlerBase>> OnThisPlayerDied =
        AccessTools.FieldRefAccess<PlayerStats, Action<DamageHandlerBase>>(nameof(PlayerStats.OnThisPlayerDied));

    private static bool Prefix(PlayerStats __instance, DamageHandlerBase handler, ref bool __result)
    {
        __result = DealDamage(__instance, handler);
        return false;
    }

    /// <summary>
    /// The Carl Mod <c>PlayerStats.KillPlayer</c> with its branch fixed: ragdoll, item drop unless <c>death_no_drop</c>,
    /// one <c>ServerSetRole(Spectator, Died)</c>, the death console message, the deathmatch broadcasts, and
    /// <see cref="SpectatorRole.ServerSetData"/> only when the player is a spectator afterwards.
    /// </summary>
    /// <param name="stats">The dying player's stats.</param>
    /// <param name="handler">The damage handler that killed the player.</param>
    internal static void KillPlayer(PlayerStats stats, DamageHandlerBase handler)
    {
        ReferenceHub hub = stats._hub;
        RagdollManager.ServerSpawnRagdoll(hub, handler);
        if (!ConfigFile.ServerConfig.GetBool("death_no_drop"))
        {
            hub.inventory.ServerDropEverything();
        }

        hub.roleManager.ServerSetRole(RoleTypeId.Spectator, RoleChangeReason.Died);
        hub.gameConsoleTransmission.SendToClient("You died. Reason: " + handler.ServerLogsText, "yellow");
        if (DmFun.DmEnabledBool())
        {
            Broadcast.Singleton.TargetAddElement(hub.connectionToClient, DmDiedBroadcast, 5, Broadcast.BroadcastFlags.Normal);
        }

        if (hub.roleManager.CurrentRole is SpectatorRole spectator)
        {
            spectator.ServerSetData(handler);
        }

        if (handler is AttackerDamageHandler attackerHandler)
        {
            ReferenceHub attacker = attackerHandler.Attacker.Hub;
            if ((object)attacker != null && (object)attacker != hub && DmFun.DmEnabledBool())
            {
                Broadcast.Singleton.TargetAddElement(attacker.connectionToClient, DmKilledBroadcast + hub.nicknameSync.MyNick, 2, Broadcast.BroadcastFlags.Normal);
            }
        }

        PlayerStats.DmCleanup();
    }

    private static bool DealDamage(PlayerStats stats, DamageHandlerBase handler)
    {
        ReferenceHub hub = stats._hub;
        if (hub.characterClassManager.GodMode)
        {
            return false;
        }

        if (hub.roleManager.CurrentRole is IDamageHandlerProcessingRole processingRole)
        {
            handler = processingRole.ProcessDamageHandler(handler);
        }

        ReferenceHub? attacker = handler is AttackerDamageHandler attackerHandler ? attackerHandler.Attacker.Hub : null;

        if (PlayerEvents.HasHurting)
        {
            PlayerHurtingEventArgs hurting = new(attacker, hub, handler);
            PlayerEvents.OnHurting(hurting);
            if (!hurting.IsAllowed)
            {
                return false;
            }
        }

        DamageHandlerBase.HandlerOutput output = handler.ApplyDamage(hub);
        if (PlayerEvents.HasHurt)
        {
            PlayerEvents.OnHurt(new PlayerHurtEventArgs(attacker, hub, handler));
        }

        if (output == DamageHandlerBase.HandlerOutput.Nothing)
        {
            return false;
        }

        OnAnyPlayerDamaged()?.Invoke(hub, handler);
        OnThisPlayerDamaged(stats)?.Invoke(handler);
        if (output != DamageHandlerBase.HandlerOutput.Death)
        {
            return true;
        }

        if (PlayerEvents.HasDying)
        {
            PlayerDyingEventArgs dying = new(hub, attacker, handler);
            PlayerEvents.OnDying(dying);
            if (!dying.IsAllowed)
            {
                return false;
            }
        }

        RoleTypeId oldRole = hub.GetRoleId();
        Vector3 oldPosition = hub.transform.position;
        Vector3 oldVelocity = hub.GetVelocity();
        Quaternion oldCameraRotation = hub.PlayerCameraReference.rotation;

        OnAnyPlayerDied()?.Invoke(hub, handler);
        OnThisPlayerDied(stats)?.Invoke(handler);
        KillPlayer(stats, handler);

        if (PlayerEvents.HasDeath)
        {
            PlayerEvents.OnDeath(new PlayerDeathEventArgs(hub, attacker, handler, oldRole, oldPosition, oldVelocity, oldCameraRotation));
        }

        return true;
    }
}
