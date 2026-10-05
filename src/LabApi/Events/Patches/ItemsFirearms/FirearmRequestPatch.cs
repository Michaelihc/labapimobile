using HarmonyLib;
using InventorySystem.Items.Firearms;
using InventorySystem.Items.Firearms.Attachments;
using InventorySystem.Items.Firearms.BasicMessages;
using InventorySystem.Items.Firearms.Modules;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using Mirror;
using Utils.Networking;

namespace LabApi.Events.Patches.ItemsFirearms;

/// <summary>
/// Raises the firearm request events (reload, unload, dry fire, aim, flashlight) from the fork's single request handler.
/// </summary>
/// <remarks>
/// Official SL raises these from separate module commands:
/// AnimatorReloaderModuleBase.ServerProcessCmd (Reloading/Unloading), AutomaticActionModule.UpdateServer and
/// DoubleActionModule.FireDry (DryFiring/DryFired), LinearAdsModule.ServerProcessCmd (Aimed) and
/// FlashlightAttachment.ServerProcessCmd (TogglingWeaponFlashlight/ToggledWeaponFlashlight).
/// The dry fire, aim and flashlight branches mirror the fork method body with the official event points.
/// </remarks>
// Official: InventorySystem/Items/Firearms/Modules/AnimatorReloaderModuleBase.cs ServerProcessCmd (and the module commands listed above)
[HarmonyPatch(typeof(FirearmBasicMessagesHandler), nameof(FirearmBasicMessagesHandler.ServerRequestReceived))]
internal static class FirearmRequestPatch
{
    private static bool Prefix(NetworkConnection conn, RequestMessage msg)
    {
        switch (msg.Request)
        {
            case RequestType.Reload:
                if (!PlayerEvents.HasReloadingWeapon)
                {
                    return true;
                }

                break;
            case RequestType.Unload:
                if (!PlayerEvents.HasUnloadingWeapon)
                {
                    return true;
                }

                break;
            case RequestType.Dryfire:
                if (!PlayerEvents.HasDryFiringWeapon && !PlayerEvents.HasDryFiredWeapon)
                {
                    return true;
                }

                break;
            case RequestType.AdsIn:
            case RequestType.AdsOut:
                if (!PlayerEvents.HasAimedWeapon)
                {
                    return true;
                }

                break;
            case RequestType.ToggleFlashlight:
                if (!PlayerEvents.HasTogglingWeaponFlashlight && !PlayerEvents.HasToggledWeaponFlashlight)
                {
                    return true;
                }

                break;
            default:
                return true;
        }

        if (conn.identity == null || !ReferenceHub.TryGetHub(conn.identity.gameObject, out ReferenceHub hub))
        {
            return true;
        }

        if (msg.Serial != hub.inventory.CurItem.SerialNumber || hub.inventory.CurInstance is not Firearm firearm || firearm == null)
        {
            return true;
        }

        switch (msg.Request)
        {
            // The disruptor sends a reload request as its charge-up sound cue, never as a reload.
            case RequestType.Reload when firearm.AmmoManagerModule is DisruptorAction:
            case RequestType.Unload when firearm.AmmoManagerModule is DisruptorAction:
                return true;
            case RequestType.Reload:
            {
                PlayerReloadingWeaponEventArgs e = new(hub, firearm);
                PlayerEvents.OnReloadingWeapon(e);
                return e.IsAllowed;
            }

            case RequestType.Unload:
            {
                PlayerUnloadingWeaponEventArgs e = new(hub, firearm);
                PlayerEvents.OnUnloadingWeapon(e);
                return e.IsAllowed;
            }

            case RequestType.Dryfire:
                // Requests the fork rejects keep the original path (it resyncs the shotgun on rejection).
                if (firearm.ActionModule == null || !WillAuthorizeDryFire(firearm, firearm.ActionModule))
                {
                    return true;
                }

                HandleDryFire(hub, firearm, msg);
                return false;
            case RequestType.AdsIn:
            case RequestType.AdsOut:
            {
                bool aiming = msg.Request == RequestType.AdsIn;
                firearm.AdsModule.ServerAds = aiming;
                msg.SendToAuthenticated();
                PlayerEvents.OnAimedWeapon(new PlayerAimedWeaponEventArgs(hub, firearm, aiming));
                return false;
            }

            default:
                HandleFlashlight(hub, firearm);
                return false;
        }
    }

    /// <summary>
    /// Mirrors each fork action module's <c>ServerAuthorizeDryFire</c> checks without side effects.
    /// </summary>
    private static bool WillAuthorizeDryFire(Firearm firearm, IActionModule action)
    {
        FirearmStatus status = firearm.Status;
        switch (action)
        {
            case AutomaticAction automatic:
                return status.Flags.HasFlagFast(FirearmStatusFlags.Cocked)
                    && (firearm.IsLocalPlayer || (status.Ammo == 0 && automatic.ModulesReady));
            case DoubleAction doubleAction:
                return status.Ammo == 0 && doubleAction.ServerTriggerReady;
            case PumpAction pump:
                return pump.ChamberedRounds <= 0 && pump.CockedHammers > 0;
            case DisruptorAction:
                return false;
            default:
                return true;
        }
    }

    private static void HandleDryFire(ReferenceHub hub, Firearm firearm, RequestMessage msg)
    {
        if (PlayerEvents.HasDryFiringWeapon)
        {
            PlayerDryFiringWeaponEventArgs e = new(hub, firearm);
            PlayerEvents.OnDryFiringWeapon(e);
            if (!e.IsAllowed)
            {
                if (firearm.ActionModule is PumpAction pump)
                {
                    pump.ServerResync();
                }

                return;
            }
        }

        if (!firearm.ActionModule.ServerAuthorizeDryFire())
        {
            return;
        }

        msg.SendToAuthenticated();
        firearm.OnWeaponDryfired();
        if (PlayerEvents.HasDryFiredWeapon)
        {
            PlayerEvents.OnDryFiredWeapon(new PlayerDryFiredWeaponEventArgs(hub, firearm));
        }
    }

    private static void HandleFlashlight(ReferenceHub hub, Firearm firearm)
    {
        if (!firearm.HasAdvantageFlag(AttachmentDescriptiveAdvantages.Flashlight))
        {
            return;
        }

        bool newState = !firearm.Status.Flags.HasFlagFast(FirearmStatusFlags.FlashlightEnabled);
        if (PlayerEvents.HasTogglingWeaponFlashlight)
        {
            PlayerTogglingWeaponFlashlightEventArgs e = new(hub, firearm, newState);
            PlayerEvents.OnTogglingWeaponFlashlight(e);
            if (!e.IsAllowed)
            {
                return;
            }

            newState = e.NewState;
        }

        FirearmStatus status = firearm.Status;
        firearm.Status = new FirearmStatus(status.Ammo, firearm.OverrideFlashlightFlags(newState), status.Attachments);
        if (PlayerEvents.HasToggledWeaponFlashlight)
        {
            PlayerEvents.OnToggledWeaponFlashlight(new PlayerToggledWeaponFlashlightEventArgs(hub, firearm, newState));
        }
    }
}
