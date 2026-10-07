using Footprinting;
using HarmonyLib;
using Interactables;
using Interactables.Interobjects.DoorUtils;
using InventorySystem;
using InventorySystem.Items.Pickups;
using InventorySystem.Items.ThrowableProjectiles;
using LabApi.Events.Arguments.ServerEvents;
using LabApi.Events.Handlers;
using MapGeneration.Distributors;
using Mirror;
using NorthwoodLib.Pools;
using PlayerStatsSystem;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;

namespace LabApi.Events.Patches.ItemsGeneral;

/// <summary>
/// Raises ItemSpawning / ItemSpawned for every pickup placed by the map item distributor.
/// </summary>
/// <remarks>
/// The fork has no separate <c>ServerRegisterPickup</c>, so the event is raised before the pickup is instantiated:
/// a denied spawn creates nothing, and a changed <see cref="ItemSpawningEventArgs.ItemType"/> is spawned instead.
/// With subscribers the fork body runs here; without subscribers it runs untouched.
/// </remarks>
// Official: MapGeneration/Distributors/ItemDistributor.cs ServerRegisterPickup
[HarmonyPatch(typeof(ItemDistributor), nameof(ItemDistributor.CreatePickup))]
internal static class ItemSpawningPatch
{
    private static bool Prefix(ItemDistributor __instance, ItemType id, Transform parentTransform, string triggerDoorName)
    {
        if (!ServerEvents.HasItemSpawning && !ServerEvents.HasItemSpawned)
        {
            return true;
        }

        if (ServerEvents.HasItemSpawning)
        {
            ItemSpawningEventArgs e = new(id);
            ServerEvents.OnItemSpawning(e);
            if (!e.IsAllowed)
            {
                return false;
            }

            id = e.ItemType;
        }

        if (!InventoryItemLoader.AvailableItems.TryGetValue(id, out InventorySystem.Items.ItemBase template) || template == null
            || template.PickupDropModel == null || !template.PickupDropModel.GetComponent<Rigidbody>())
        {
            return false;
        }

        ItemPickupBase pickup = UnityEngine.Object.Instantiate(template.PickupDropModel, parentTransform.position, parentTransform.rotation);
        pickup.Info.ItemId = id;
        pickup.Info.Weight = template.Weight;
        (pickup as IPickupDistributorTrigger)?.OnDistributed();
        if (string.IsNullOrEmpty(triggerDoorName) || !DoorNametagExtension.NamedDoors.TryGetValue(triggerDoorName, out DoorNametagExtension door))
        {
            ItemDistributor.SpawnPickup(pickup);
        }
        else
        {
            __instance.RegisterUnspawnedObject(door.TargetDoor, pickup.gameObject);
        }

        if (ServerEvents.HasItemSpawned)
        {
            ServerEvents.OnItemSpawned(new ItemSpawnedEventArgs(pickup));
        }

        return false;
    }
}

/// <summary>
/// Raises ProjectileExploding / ProjectileExploded around the fuse end of explosive, flash and SCP-018 grenades,
/// and ProjectileExploded for SCP-2176.
/// </summary>
/// <remarks>
/// The fork's TimeGrenade.ServerFuseEnd is abstract; EffectGrenade carries the shared body. A denied fuse end leaves the
/// projectile in place, as in the official game.
/// </remarks>
// Official: InventorySystem/Items/ThrowableProjectiles/TimeGrenade.cs ServerFuseEnd (and the ProjectileExploded calls in its overrides)
[HarmonyPatch(typeof(EffectGrenade), nameof(EffectGrenade.ServerFuseEnd))]
internal static class ProjectileExplodingPatch
{
    private static bool Prefix(EffectGrenade __instance, out bool __state)
    {
        __state = false;
        if (!ServerEvents.HasProjectileExploding && !ServerEvents.HasProjectileExploded)
        {
            return true;
        }

        if (__instance._exploded || !NetworkServer.active)
        {
            return true;
        }

        // SCP-2176 raises ProjectileExploding in its own override before calling this base body, where the build has one.
        if ((!Scp2176ExplodingPatch.IsActive || __instance is not InventorySystem.Items.ThrowableProjectiles.Scp2176Projectile) && !GrenadeEvents.Exploding(__instance))
        {
            return false;
        }

        __state = true;
        return true;
    }

    private static void Postfix(EffectGrenade __instance, bool __state)
    {
        if (__state && ServerEvents.HasProjectileExploded)
        {
            ServerEvents.OnProjectileExploded(new ProjectileExplodedEventArgs(__instance, __instance.PreviousOwner.Hub, __instance.transform.position));
        }
    }
}

/// <summary>
/// Raises ProjectileExploding for SCP-2176 before it shatters.
/// </summary>
/// <remarks>
/// Builds whose SCP-2176 has no own <c>ServerFuseEnd</c> (Carl Mod 0.0.5, where the shared fuse end calls its <c>ServerDetonate</c>,
/// or a build without the <c>_hasTriggered</c> flag) skip this patch; SCP-2176 then
/// raises ProjectileExploding from <see cref="ProjectileExplodingPatch"/> like the other grenades. The target is resolved in
/// <see cref="TargetMethod"/> because it may be absent.
/// </remarks>
// Official: InventorySystem/Items/ThrowableProjectiles/Scp2176Projectile.cs ServerFuseEnd
[HarmonyPatch]
internal static class Scp2176ExplodingPatch
{
    private static readonly MethodInfo? Target = FindTarget();

    /// <summary>
    /// Gets whether SCP-2176's own fuse end raises ProjectileExploding.
    /// </summary>
    internal static bool IsActive => Target != null;

    private static MethodInfo? FindTarget()
    {
        Type type = typeof(InventorySystem.Items.ThrowableProjectiles.Scp2176Projectile);
        MethodInfo? method = AccessTools.DeclaredMethod(type, nameof(InventorySystem.Items.ThrowableProjectiles.Scp2176Projectile.ServerFuseEnd));
        return method != null && AccessTools.DeclaredField(type, nameof(InventorySystem.Items.ThrowableProjectiles.Scp2176Projectile._hasTriggered)) != null ? method : null;
    }

    private static bool Prepare()
    {
        if (Target == null)
        {
            Logger.Info("[PATCHES] SCP-2176 has no own ServerFuseEnd in this build (Carl Mod 0.0.5 shatters it from the shared grenade fuse end); its ProjectileExploding event is raised there.");
        }

        return Target != null;
    }

    private static MethodBase TargetMethod() => Target!;

    private static bool Prefix(InventorySystem.Items.ThrowableProjectiles.Scp2176Projectile __instance)
    {
        if (!ServerEvents.HasProjectileExploding || __instance._hasTriggered)
        {
            return true;
        }

        return GrenadeEvents.Exploding(__instance);
    }
}

/// <summary>
/// Raises ExplosionSpawning / ExplosionSpawned for every grenade-style explosion.
/// </summary>
/// <remarks>
/// The fork has no explosion type; its explosions always damage doors, which <see cref="ExplosionSpawningEventArgs.DestroyDoors"/>
/// can now turn off. With subscribers the fork body runs here; without subscribers it runs untouched.
/// </remarks>
// Official: InventorySystem/Items/ThrowableProjectiles/ExplosionGrenade.cs Explode
[HarmonyPatch(typeof(ExplosionGrenade), nameof(ExplosionGrenade.Explode))]
internal static class ExplosionSpawningPatch
{
    // The event's backing delegate; C# only allows invoking it from inside ExplosionGrenade.
    private static readonly FieldInfo ExplodedEventField = AccessTools.Field(typeof(ExplosionGrenade), nameof(ExplosionGrenade.OnExploded));

    private static bool Prefix(Footprint attacker, Vector3 position, ExplosionGrenade settingsReference, ReferenceHub ignoredHub)
    {
        if (!ServerEvents.HasExplosionSpawning && !ServerEvents.HasExplosionSpawned)
        {
            return true;
        }

        bool destroyDoors = true;
        if (ServerEvents.HasExplosionSpawning)
        {
            ExplosionSpawningEventArgs e = new(attacker.Hub, position, settingsReference, destroyDoors);
            ServerEvents.OnExplosionSpawning(e);
            if (!e.IsAllowed)
            {
                return false;
            }

            position = e.Position;
            settingsReference = e.Settings;
            destroyDoors = e.DestroyDoors;
            if (attacker.Hub != e.Player?.ReferenceHub)
            {
                attacker = new Footprint(e.Player?.ReferenceHub);
            }
        }

        HashSet<uint> destructibles = HashSetPool<uint>.Shared.Rent();
        HashSet<uint> doors = HashSetPool<uint>.Shared.Rent();
        float maxRadius = settingsReference._maxRadius;
        ReferenceHub.TryGetHostHub(out ReferenceHub host);
        bool hostHitboxes = NetworkServer.active && HitboxIdentity.SetOwnHitboxes(host, state: true);
        Collider[] colliders = Physics.OverlapSphere(position, maxRadius, settingsReference._detectionMask);
        foreach (Collider collider in colliders)
        {
            if (NetworkServer.active)
            {
                if (collider.TryGetComponent(out IExplosionTrigger trigger))
                {
                    trigger.OnExplosionDetected(attacker, position, maxRadius);
                }

                if (collider.TryGetComponent(out IDestructible destructible))
                {
                    if (!destructibles.Contains(destructible.NetworkId) && ExplosionGrenade.ExplodeDestructible(destructible, attacker, position, settingsReference, ignoredHub))
                    {
                        destructibles.Add(destructible.NetworkId);
                    }
                }
                else if (destroyDoors && collider.TryGetComponent(out InteractableCollider interactable) && interactable.Target is DoorVariant door && doors.Add(door.netId))
                {
                    ExplosionGrenade.ExplodeDoor(door, position, settingsReference);
                }
            }

            if (collider.attachedRigidbody != null)
            {
                ExplosionGrenade.ExplodeRigidbody(collider.attachedRigidbody, position, maxRadius, settingsReference);
            }
        }

        HashSetPool<uint>.Shared.Return(destructibles);
        HashSetPool<uint>.Shared.Return(doors);
        ((Action<Footprint, Vector3, ExplosionGrenade>?)ExplodedEventField.GetValue(null))?.Invoke(attacker, position, settingsReference);
        if (ServerEvents.HasExplosionSpawned)
        {
            ServerEvents.OnExplosionSpawned(new ExplosionSpawnedEventArgs(attacker.Hub, position, settingsReference, destroyDoors));
        }

        if (hostHitboxes)
        {
            HitboxIdentity.SetOwnHitboxes(host, state: false);
        }

        return false;
    }
}

/// <summary>
/// Shared helpers for the grenade patches.
/// </summary>
internal static class GrenadeEvents
{
    /// <summary>
    /// Raises ProjectileExploding and applies the changed owner and position.
    /// </summary>
    /// <param name="grenade">The grenade whose fuse ends.</param>
    /// <returns>Whether the explosion may proceed.</returns>
    internal static bool Exploding(TimeGrenade grenade)
    {
        if (!ServerEvents.HasProjectileExploding)
        {
            return true;
        }

        ProjectileExplodingEventArgs e = new(grenade, grenade.PreviousOwner.Hub, grenade.transform.position);
        ServerEvents.OnProjectileExploding(e);
        if (!e.IsAllowed)
        {
            return false;
        }

        if (grenade.PreviousOwner.Hub != e.Player?.ReferenceHub)
        {
            grenade.PreviousOwner = new Footprint(e.Player?.ReferenceHub);
        }

        grenade.transform.position = e.Position;
        return true;
    }
}
