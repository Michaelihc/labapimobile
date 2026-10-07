using Footprinting;
using HarmonyLib;
using InventorySystem.Items;
using InventorySystem.Items.Pickups;
using InventorySystem.Items.ThrowableProjectiles;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using Mirror;
using PlayerRoles.FirstPersonControl;
using System.Reflection;
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
/// without subscribers it runs untouched. Carl Mod 0.0.5 ignores a confirmation for an item that is already thrown, not being
/// thrown or being cancelled, and keeps one that arrives before the item is ready to throw until it is (its per-frame update
/// then confirms it again, which raises the events). Applied only when the native body is one of the known Carl Mod bodies.
/// </remarks>
// Official: InventorySystem/Items/ThrowableProjectiles/ThrowableItem.cs ServerProcessThrowConfirmation
[HarmonyPatch(typeof(ThrowableItem), nameof(ThrowableItem.ServerProcessThrowConfirmation))]
internal static class ThrowingProjectilePatch
{
    // ThrowableItem.ServerProcessThrowConfirmation of both Carl Mod 0.0.4 builds, and of 0.0.5 (early throws kept for later).
    private const string CarlMod004Body = "58bd9c6f958a9331";
    private const string Version005Body = "c8ef78531d5ac028";

    private static readonly MethodInfo? Target = AccessTools.DeclaredMethod(typeof(ThrowableItem), nameof(ThrowableItem.ServerProcessThrowConfirmation));

    private static readonly BodyVariant Variant = NativeBody.Identify(Target, CarlMod004Body, Version005Body, out Fingerprint);

    private static readonly string? Fingerprint;

    // 0.0.5 only: ThrowableItem._pendingThrow, the confirmation kept until the item is ready to throw.
    private static readonly AccessTools.FieldRef<ThrowableItem, (bool FullForce, Vector3 Position, Quaternion Rotation, Vector3 Velocity)?>? PendingThrow =
        AccessTools.DeclaredField(typeof(ThrowableItem), "_pendingThrow") is FieldInfo field && field.FieldType == typeof((bool, Vector3, Quaternion, Vector3)?)
            ? AccessTools.FieldRefAccess<ThrowableItem, (bool FullForce, Vector3 Position, Quaternion Rotation, Vector3 Velocity)?>(field)
            : null;

    private static bool Prepare()
    {
        if (Variant == BodyVariant.Standard || (Variant == BodyVariant.Version005 && PendingThrow != null))
        {
            return true;
        }

        PatchManager.Skip(typeof(ThrowingProjectilePatch), NativeBody.UnknownBody(Target, Fingerprint, "ThrowingProjectile / ThrewProjectile are not raised."));
        return false;
    }

    private static bool Prefix(ThrowableItem __instance, bool fullForce, Vector3 startPos, Quaternion startRot, Vector3 startVel)
    {
        if (!PlayerEvents.HasThrowingProjectile && !PlayerEvents.HasThrewProjectile)
        {
            return true;
        }

        if (Variant == BodyVariant.Version005)
        {
            return ProcessVersion005(__instance, fullForce, startPos, startRot, startVel);
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
    /// The Carl Mod 0.0.5 body with the events.
    /// </summary>
    private static bool ProcessVersion005(ThrowableItem item, bool fullForce, Vector3 startPos, Quaternion startRot, Vector3 startVel)
    {
        // Ignored, or kept until the item is ready to throw: the game handles both, and confirms a kept throw through this
        // method again.
        if (item._serverThrown || !item.ThrowStopwatch.IsRunning || item.CancelStopwatch.IsRunning || !item.ReadyToThrow)
        {
            return true;
        }

        PendingThrow!(item) = null;
        ReferenceHub owner = item.Owner;
        Transform camera = owner.PlayerCameraReference;
        Vector3 position = camera.position;
        Quaternion rotation = camera.rotation;
        Bounds bounds = owner.GenerateTracerBounds(0.1f, ignoreTeleports: false);
        bounds.Encapsulate(camera.position + owner.GetVelocity() * 0.2f);
        ThrownProjectile? projectile;
        ThrowableItem.ProjectileSettings settings;
        try
        {
            camera.SetPositionAndRotation(bounds.ClosestPoint(startPos), startRot);
            settings = fullForce ? item.FullThrowSettings : item.WeakThrowSettings;
            startVel = ThrowableNetworkHandler.GetLimitedVelocity(startVel);
            if (PlayerEvents.HasThrowingProjectile)
            {
                PlayerThrowingProjectileEventArgs e = new(owner, item, settings, fullForce);
                PlayerEvents.OnThrowingProjectile(e);
                if (!e.IsAllowed)
                {
                    Cancel(item);
                    return false;
                }

                settings = e.ProjectileSettings;
                fullForce = e.FullForce;
            }

            projectile = ServerThrow(item, settings.StartVelocity, settings.UpwardsFactor, settings.StartTorque, startVel);
            ThrowableNetworkHandler.RequestType request = fullForce ? ThrowableNetworkHandler.RequestType.ConfirmThrowFullForce : ThrowableNetworkHandler.RequestType.ConfirmThrowWeak;
            new ThrowableNetworkHandler.ThrowableItemAudioMessage(item.ItemSerial, request).SendToAuthenticated();
        }
        finally
        {
            camera.SetPositionAndRotation(position, rotation);
        }

        if (projectile != null && PlayerEvents.HasThrewProjectile)
        {
            PlayerEvents.OnThrewProjectile(new PlayerThrewProjectileEventArgs(owner, item, projectile, settings, fullForce));
        }

        return false;
    }

    /// <summary>
    /// The fork's <c>ThrowableItem.ServerThrow</c> (the same in 0.0.4 and 0.0.5), returning the spawned projectile.
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
