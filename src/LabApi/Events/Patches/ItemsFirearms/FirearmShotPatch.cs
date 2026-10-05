using HarmonyLib;
using InventorySystem.Items.Firearms;
using InventorySystem.Items.Firearms.BasicMessages;
using InventorySystem.Items.Firearms.Modules;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using Mirror;

namespace LabApi.Events.Patches.ItemsFirearms;

/// <summary>
/// Raises ShootingWeapon before the fork authorizes a shot request and ShotWeapon after it was processed.
/// </summary>
/// <remarks>
/// The fork's <see cref="IActionModule.ServerAuthorizeShot"/> validates and consumes ammo in one call, so ShootingWeapon
/// is raised after a side-effect-free copy of each action module's checks. One invocation per shot request
/// (a shotgun request covers every barrel fired and every pellet).
/// </remarks>
// Official: InventorySystem/Items/Firearms/Modules/AutomaticActionModule.cs UpdateServer (also DoubleActionModule.FireLive, PumpActionModule.UpdateServer, DisruptorActionModule.ServerProcessStartCmd)
[HarmonyPatch(typeof(FirearmBasicMessagesHandler), nameof(FirearmBasicMessagesHandler.ServerShotReceived))]
internal static class FirearmShotPatch
{
    private static bool Prefix(NetworkConnection conn, ShotMessage msg, out ShotState __state)
    {
        __state = default;
        if (!PlayerEvents.HasShootingWeapon && !PlayerEvents.HasShotWeapon)
        {
            return true;
        }

        if (conn.identity == null || !ReferenceHub.TryGetHub(conn.identity.gameObject, out ReferenceHub hub))
        {
            return true;
        }

        if (msg.ShooterWeaponSerial != hub.inventory.CurItem.SerialNumber || hub.inventory.CurInstance is not Firearm firearm || firearm == null)
        {
            return true;
        }

        IActionModule action = firearm.ActionModule;
        if (action == null || !WillAuthorize(firearm, action))
        {
            return true;
        }

        if (PlayerEvents.HasShootingWeapon)
        {
            PlayerShootingWeaponEventArgs e = new(hub, firearm);
            PlayerEvents.OnShootingWeapon(e);
            if (!e.IsAllowed)
            {
                Refuse(conn, action);
                return false;
            }
        }

        __state = new ShotState(firearm, firearm.Status.Ammo);
        return true;
    }

    private static void Postfix(ShotState __state)
    {
        Firearm? firearm = __state.Firearm;
        if (firearm == null || !PlayerEvents.HasShotWeapon || firearm.Status.Ammo >= __state.Ammo)
        {
            return;
        }

        PlayerEvents.OnShotWeapon(new PlayerShotWeaponEventArgs(firearm.Owner, firearm));
    }

    /// <summary>
    /// Mirrors each fork action module's <c>ServerAuthorizeShot</c> checks without consuming ammo or touching timers.
    /// </summary>
    private static bool WillAuthorize(Firearm firearm, IActionModule action)
    {
        FirearmStatus status = firearm.Status;
        switch (action)
        {
            case AutomaticAction automatic:
                return status.Ammo >= automatic._ammoConsumption
                    && status.Flags.HasFlagFast(FirearmStatusFlags.Cocked)
                    && status.Flags.HasFlagFast(FirearmStatusFlags.Chambered)
                    && automatic.ModulesReady;
            case DoubleAction doubleAction:
                return status.Ammo > 0 && (doubleAction.ServerTriggerReady || firearm.IsLocalPlayer);
            case PumpAction pump:
                return status.Ammo > 0 && pump.ChamberedRounds > 0 && pump.Standby;
            case DisruptorAction disruptor:
                return status.Ammo > 0 && disruptor.ModulesReady && (firearm.IsLocalPlayer || disruptor.TimeSinceLastShot >= 1.5f);
            default:
                return status.Ammo > 0;
        }
    }

    /// <summary>
    /// Restores the owner's client-side prediction after a cancelled shot.
    /// </summary>
    private static void Refuse(NetworkConnection conn, IActionModule action)
    {
        switch (action)
        {
            case AutomaticAction:
                conn.Send(default(AutomaticAction.RefusedShotMessage));
                break;
            case PumpAction pump:
                pump.ServerResync();
                break;
        }
    }

    internal readonly struct ShotState(Firearm firearm, byte ammo)
    {
        public readonly Firearm? Firearm = firearm;

        public readonly byte Ammo = ammo;
    }
}
