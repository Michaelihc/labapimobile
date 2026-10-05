using AdminToys;
using HarmonyLib;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using PlayerStatsSystem;

namespace LabApi.Events.Patches.Core;

/// <summary>
/// Raises DamagingShootingTarget / DamagedShootingTarget around <see cref="ShootingTarget.Damage"/>.
/// </summary>
/// <remarks>
/// Same checks as the official method: only attacker damage with a living attacker hub fires the events. Cancelling
/// makes <see cref="ShootingTarget.Damage"/> return <see langword="false"/> without sending hit feedback.
/// </remarks>
// Official: AdminToys/ShootingTarget.cs ShootingTarget.Damage(float, DamageHandlerBase, Vector3)
[HarmonyPatch(typeof(ShootingTarget), nameof(ShootingTarget.Damage))]
internal static class ShootingTargetDamagePatch
{
    private static bool Prefix(ShootingTarget __instance, DamageHandlerBase handler, ref bool __result, out ReferenceHub? __state)
    {
        __state = null;
        if (!PlayerEvents.HasDamagingShootingTarget && !PlayerEvents.HasDamagedShootingTarget)
        {
            return true;
        }

        // The original returns false for these cases itself.
        if (handler is not AttackerDamageHandler attackerHandler || attackerHandler.Attacker.Hub == null)
        {
            return true;
        }

        ReferenceHub hub = attackerHandler.Attacker.Hub;
        if (PlayerEvents.HasDamagingShootingTarget)
        {
            PlayerDamagingShootingTargetEventArgs e = new(hub, __instance, handler);
            PlayerEvents.OnDamagingShootingTarget(e);
            if (!e.IsAllowed)
            {
                __result = false;
                return false;
            }
        }

        __state = hub;
        return true;
    }

    private static void Postfix(ShootingTarget __instance, DamageHandlerBase handler, bool __result, ReferenceHub? __state)
    {
        if (!__result || __state == null || !PlayerEvents.HasDamagedShootingTarget)
        {
            return;
        }

        PlayerEvents.OnDamagedShootingTarget(new PlayerDamagedShootingTargetEventArgs(__state, __instance, handler));
    }
}

/// <summary>
/// Raises InteractingShootingTarget / InteractedShootingTarget around <see cref="ShootingTarget.ServerInteract"/>.
/// </summary>
/// <remarks>
/// Like the official method, the events fire only for players with <see cref="PlayerPermissions.FacilityManagement"/>,
/// and cancelling skips every button action.
/// </remarks>
// Official: AdminToys/ShootingTarget.cs ShootingTarget.ServerInteract(ReferenceHub, byte)
[HarmonyPatch(typeof(ShootingTarget), nameof(ShootingTarget.ServerInteract))]
internal static class ShootingTargetInteractPatch
{
    private static bool Prefix(ShootingTarget __instance, ReferenceHub ply, out bool __state)
    {
        __state = false;
        if (!PlayerEvents.HasInteractingShootingTarget && !PlayerEvents.HasInteractedShootingTarget)
        {
            return true;
        }

        // The original returns before doing anything for these players.
        if (!PermissionsHandler.IsPermitted(ply.serverRoles.Permissions, PlayerPermissions.FacilityManagement))
        {
            return true;
        }

        if (PlayerEvents.HasInteractingShootingTarget)
        {
            PlayerInteractingShootingTargetEventArgs e = new(ply, __instance);
            PlayerEvents.OnInteractingShootingTarget(e);
            if (!e.IsAllowed)
            {
                return false;
            }
        }

        __state = true;
        return true;
    }

    private static void Postfix(ShootingTarget __instance, ReferenceHub ply, bool __state)
    {
        if (!__state || !PlayerEvents.HasInteractedShootingTarget)
        {
            return;
        }

        PlayerEvents.OnInteractedShootingTarget(new PlayerInteractedShootingTargetEventArgs(ply, __instance));
    }
}
