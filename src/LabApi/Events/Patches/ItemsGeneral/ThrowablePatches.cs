using Footprinting;
using HarmonyLib;
using InventorySystem.Items;
using InventorySystem.Items.Pickups;
using InventorySystem.Items.ThrowableProjectiles;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using Mirror;
using PlayerRoles.FirstPersonControl;
using UnityEngine;
using Utils.Networking;

namespace LabApi.Events.Patches.ItemsGeneral;

/// <summary>
/// Raises ThrowingProjectile / ThrewProjectile when the server confirms a throw.
/// </summary>
/// <remarks>
/// The fork has no throw cancellation message. A denied throw resets the server throw state, plays the cancel cue for
/// other players and holsters the item so the owner's client drops its local throw state; the item stays in the inventory.
/// With subscribers the fork body (including <c>ServerThrow</c>, which returns no projectile in the fork) runs here;
/// without subscribers it runs untouched.
/// </remarks>
// Official: InventorySystem/Items/ThrowableProjectiles/ThrowableItem.cs ServerProcessThrowConfirmation
[HarmonyPatch(typeof(ThrowableItem), nameof(ThrowableItem.ServerProcessThrowConfirmation))]
internal static class ThrowingProjectilePatch
{
    private static bool Prefix(ThrowableItem __instance, bool fullForce, Vector3 startPos, Quaternion startRot, Vector3 startVel)
    {
        if (!PlayerEvents.HasThrowingProjectile && !PlayerEvents.HasThrewProjectile)
        {
            return true;
        }

        if (!__instance.ReadyToThrow)
        {
            return false;
        }

        if (__instance._serverThrown || __instance._alreadyFired)
        {
            // Already thrown (a repeated confirmation, or the item was thrown on removal): the fork only resends the throw cue.
            // No event, so a denial can never undo a completed throw.
            return true;
        }

        ReferenceHub owner = __instance.Owner;
        Transform camera = owner.PlayerCameraReference;
        Vector3 position = camera.position;
        Quaternion rotation = camera.rotation;
        Bounds bounds = owner.GenerateTracerBounds(0.1f, ignoreTeleports: false);
        bounds.Encapsulate(camera.position + owner.GetVelocity() * 0.2f);
        camera.SetPositionAndRotation(bounds.ClosestPoint(startPos), startRot);
        ThrowableItem.ProjectileSettings settings = fullForce ? __instance.FullThrowSettings : __instance.WeakThrowSettings;
        startVel = ThrowableNetworkHandler.GetLimitedVelocity(startVel);

        if (PlayerEvents.HasThrowingProjectile)
        {
            PlayerThrowingProjectileEventArgs e = new(owner, __instance, settings, fullForce);
            PlayerEvents.OnThrowingProjectile(e);
            if (!e.IsAllowed)
            {
                camera.SetPositionAndRotation(position, rotation);
                Cancel(__instance);
                return false;
            }

            settings = e.ProjectileSettings;
            fullForce = e.FullForce;
        }

        ThrownProjectile? projectile = ServerThrow(__instance, settings.StartVelocity, settings.UpwardsFactor, settings.StartTorque, startVel);
        ThrowableNetworkHandler.RequestType request = fullForce ? ThrowableNetworkHandler.RequestType.ConfirmThrowFullForce : ThrowableNetworkHandler.RequestType.ConfirmThrowWeak;
        new ThrowableNetworkHandler.ThrowableItemAudioMessage(__instance.ItemSerial, request).SendToAuthenticated();
        camera.SetPositionAndRotation(position, rotation);

        if (projectile != null && PlayerEvents.HasThrewProjectile)
        {
            PlayerEvents.OnThrewProjectile(new PlayerThrewProjectileEventArgs(owner, __instance, projectile, settings, fullForce));
        }

        return false;
    }

    /// <summary>
    /// The fork's <c>ThrowableItem.ServerThrow</c>, returning the spawned projectile.
    /// </summary>
    private static ThrownProjectile? ServerThrow(ThrowableItem item, float forceAmount, float upwardFactor, Vector3 torque, Vector3 startVel)
    {
        if (item._serverThrown)
        {
            return null;
        }

        item._serverThrown = true;
        item._destroyTime = Time.timeSinceLevelLoad + item._postThrownAnimationTime;
        item._alreadyFired = true;
        Transform camera = item.Owner.PlayerCameraReference;
        ThrownProjectile projectile = Object.Instantiate(item.Projectile, camera.position, camera.rotation);
        PickupSyncInfo info = new(item.ItemTypeId, projectile.transform.position, projectile.transform.rotation, item.Weight, item.ItemSerial)
        {
            Locked = !item._repickupable,
        };
        projectile.NetworkInfo = info;
        projectile.PreviousOwner = new Footprint(item.Owner);
        projectile.ServerActivate();
        projectile.InfoReceived(default, projectile.Info);
        NetworkServer.Spawn(projectile.gameObject);
        if (projectile.TryGetComponent(out Rigidbody rb))
        {
            item.PropelBody(rb, torque, startVel, forceAmount, upwardFactor);
        }

        return projectile;
    }

    private static void Cancel(ThrowableItem item)
    {
        if (item._serverThrown || item._alreadyFired)
        {
            // A handler threw the item (for example by dropping it). Holstering would reset the throw state and keep the item.
            return;
        }

        new ThrowableNetworkHandler.ThrowableItemAudioMessage(item.ItemSerial, ThrowableNetworkHandler.RequestType.CancelThrow).SendToAuthenticated();

        InventorySystem.Inventory inventory = item.OwnerInventory;
        if (inventory.CurInstance == item)
        {
            // Holstering runs ThrowableItem.OnHolstered, which resets the throw stopwatches and fired state.
            inventory.NetworkCurItem = ItemIdentifier.None;
            inventory.CurInstance = null;
        }
        else
        {
            item.ThrowStopwatch.Reset();
            item.CancelStopwatch.Reset();
        }
    }
}
