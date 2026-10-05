using HarmonyLib;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using PlayerRoles;
using PlayerRoles.FirstPersonControl;
using PlayerStatsSystem;
using System;
using UnityEngine;

namespace LabApi.Events.Patches.Players;

// Official: PlayerStatsSystem/PlayerStats.cs DealDamage
// Runs the original untouched unless a damage event has subscribers; otherwise replays the Carl Mod body with the events.
[HarmonyPatch(typeof(PlayerStats), nameof(PlayerStats.DealDamage))]
internal static class PlayerDamagePatch
{
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
        if (!PlayerEvents.HasHurting && !PlayerEvents.HasHurt && !PlayerEvents.HasDying && !PlayerEvents.HasDeath)
        {
            return true;
        }

        __result = DealDamage(__instance, handler);
        return false;
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
        stats.KillPlayer(handler);

        if (PlayerEvents.HasDeath)
        {
            PlayerEvents.OnDeath(new PlayerDeathEventArgs(hub, attacker, handler, oldRole, oldPosition, oldVelocity, oldCameraRotation));
        }

        return true;
    }
}
