using HarmonyLib;
using InventorySystem;
using InventorySystem.Items;
using InventorySystem.Items.Firearms.Ammo;
using InventorySystem.Items.Pickups;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using Mirror;
using PlayerRoles.FirstPersonControl;
using UnityEngine;

namespace LabApi.Events.Patches.ItemsGeneral;

/// <summary>
/// Raises ChangingItem / ChangedItem when the server selects a player's held item.
/// </summary>
/// <remarks>
/// With subscribers the fork body runs here with the official event points; without subscribers it runs untouched.
/// </remarks>
// Official: InventorySystem/Inventory.cs ServerSelectItem
[HarmonyPatch(typeof(Inventory), nameof(Inventory.ServerSelectItem))]
internal static class ChangingItemPatch
{
    private static bool Prefix(Inventory __instance, ushort itemSerial)
    {
        if (!PlayerEvents.HasChangingItem && !PlayerEvents.HasChangedItem)
        {
            return true;
        }

        if (!NetworkServer.active || itemSerial == __instance.CurItem.SerialNumber)
        {
            return true;
        }

        ItemBase? oldItem = null;
        ItemBase? newItem = null;
        bool currentValid = __instance.CurItem.SerialNumber == 0
            || (__instance.UserInventory.Items.TryGetValue(__instance.CurItem.SerialNumber, out oldItem) && __instance.CurInstance != null);

        if (itemSerial != 0 && !__instance.UserInventory.Items.TryGetValue(itemSerial, out newItem))
        {
            // Unknown serial: the fork only clears an invalid current item, which raises no event.
            return true;
        }

        if ((__instance.CurItem.SerialNumber != 0 && currentValid && !oldItem!.CanHolster()) || (itemSerial != 0 && !newItem!.CanEquip()))
        {
            return false;
        }

        if (PlayerEvents.HasChangingItem)
        {
            PlayerChangingItemEventArgs e = new(__instance._hub, oldItem, newItem);
            PlayerEvents.OnChangingItem(e);
            if (!e.IsAllowed)
            {
                return false;
            }
        }

        if (itemSerial == 0)
        {
            __instance.NetworkCurItem = ItemIdentifier.None;
            if (!__instance.isLocalPlayer)
            {
                __instance.CurInstance = null;
            }
        }
        else
        {
            __instance.NetworkCurItem = new ItemIdentifier(newItem!.ItemTypeId, itemSerial);
            if (!__instance.isLocalPlayer)
            {
                __instance.CurInstance = newItem;
            }
        }

        if (PlayerEvents.HasChangedItem)
        {
            PlayerEvents.OnChangedItem(new PlayerChangedItemEventArgs(__instance._hub, oldItem, newItem));
        }

        return false;
    }
}

/// <summary>
/// Raises DroppingItem / DroppedItem / ThrowingItem / ThrewItem for a player's drop request.
/// </summary>
/// <remarks>
/// The fork lets a player drop any item that can be holstered (it has no separate <c>AllowDropping</c>).
/// With subscribers the fork body runs here with the official event points; without subscribers it runs untouched.
/// </remarks>
// Official: InventorySystem/Inventory.cs UserCode_CmdDropItem__UInt16__Boolean
[HarmonyPatch(typeof(Inventory), nameof(Inventory.UserCode_CmdDropItem__UInt16__Boolean))]
internal static class DroppingItemPatch
{
    private static bool Prefix(Inventory __instance, ushort itemSerial, bool tryThrow)
    {
        if (!PlayerEvents.HasDroppingItem && !PlayerEvents.HasDroppedItem && !PlayerEvents.HasThrowingItem && !PlayerEvents.HasThrewItem)
        {
            return true;
        }

        if (!__instance.UserInventory.Items.TryGetValue(itemSerial, out ItemBase item) || !item.CanHolster())
        {
            return false;
        }

        ReferenceHub hub = __instance._hub;
        if (PlayerEvents.HasDroppingItem)
        {
            PlayerDroppingItemEventArgs e = new(hub, item, tryThrow);
            PlayerEvents.OnDroppingItem(e);
            if (!e.IsAllowed)
            {
                return false;
            }

            tryThrow = e.Throw;
        }

        ItemPickupBase pickup = __instance.ServerDropItem(itemSerial);
        if (PlayerEvents.HasDroppedItem)
        {
            PlayerDroppedItemEventArgs e = new(hub, pickup, tryThrow);
            PlayerEvents.OnDroppedItem(e);
            tryThrow = e.Throw;
        }

        __instance.SendItemsNextFrame = true;
        if (!tryThrow || pickup == null || !pickup.TryGetComponent(out Rigidbody rb))
        {
            return false;
        }

        if (PlayerEvents.HasThrowingItem)
        {
            PlayerThrowingItemEventArgs e = new(hub, pickup, rb);
            PlayerEvents.OnThrowingItem(e);
            if (!e.IsAllowed)
            {
                // Official: the item goes back into the inventory and the pickup is destroyed.
                __instance.ServerAddItem(pickup.Info.ItemId, pickup.Info.Serial, pickup);
                pickup.DestroySelf();
                return false;
            }
        }

        Vector3 velocity = hub.GetVelocity();
        Vector3 linearVelocity = velocity / 3f + hub.PlayerCameraReference.forward * 6f * (Mathf.Clamp01(Mathf.InverseLerp(7f, 0.1f, rb.mass)) + 0.3f);
        linearVelocity.x = Mathf.Max(Mathf.Abs(velocity.x), Mathf.Abs(linearVelocity.x)) * (linearVelocity.x < 0f ? -1 : 1);
        linearVelocity.y = Mathf.Max(Mathf.Abs(velocity.y), Mathf.Abs(linearVelocity.y)) * (linearVelocity.y < 0f ? -1 : 1);
        linearVelocity.z = Mathf.Max(Mathf.Abs(velocity.z), Mathf.Abs(linearVelocity.z)) * (linearVelocity.z < 0f ? -1 : 1);
        rb.position = hub.PlayerCameraReference.position;
        rb.linearVelocity = linearVelocity;
        rb.angularVelocity = Vector3.Lerp(item.ThrowSettings.RandomTorqueA, item.ThrowSettings.RandomTorqueB, Random.value);
        float magnitude = rb.angularVelocity.magnitude;
        if (magnitude > rb.maxAngularVelocity)
        {
            rb.maxAngularVelocity = magnitude;
        }

        if (PlayerEvents.HasThrewItem)
        {
            PlayerEvents.OnThrewItem(new PlayerThrewItemEventArgs(hub, pickup, rb));
        }

        return false;
    }
}

/// <summary>
/// Raises DroppingAmmo / DroppedAmmo when ammo is dropped from a player's reserve.
/// </summary>
/// <remarks>
/// As in the official game, changes to the event's type and amount are not applied.
/// DroppedAmmo is raised once per spawned ammo pickup, after the pickup is spawned with its amount.
/// </remarks>
// Official: InventorySystem/InventoryExtensions.cs ServerDropAmmo
[HarmonyPatch(typeof(InventoryExtensions), nameof(InventoryExtensions.ServerDropAmmo))]
internal static class DroppingAmmoPatch
{
    private static bool Prefix(Inventory inv, ItemType ammoType, ushort amount, bool checkMinimals, ref bool __result)
    {
        if (!PlayerEvents.HasDroppingAmmo && !PlayerEvents.HasDroppedAmmo)
        {
            return true;
        }

        if (!NetworkServer.active)
        {
            return true;
        }

        __result = false;
        if (!inv.UserInventory.ReserveAmmo.TryGetValue(ammoType, out ushort reserve) || !InventoryItemLoader.AvailableItems.TryGetValue(ammoType, out ItemBase template))
        {
            return false;
        }

        if (template.PickupDropModel == null)
        {
            Debug.LogError("No pickup drop model set. Could not drop the ammo.");
            return false;
        }

        if (checkMinimals && template.PickupDropModel is AmmoPickup modelPickup)
        {
            int half = Mathf.FloorToInt(modelPickup.SavedAmmo / 2f);
            if (amount < half && reserve > half)
            {
                amount = (ushort)half;
            }
        }

        int remaining = Mathf.Min(amount, reserve);
        if (PlayerEvents.HasDroppingAmmo)
        {
            PlayerDroppingAmmoEventArgs e = new(inv._hub, ammoType, remaining);
            PlayerEvents.OnDroppingAmmo(e);
            if (!e.IsAllowed)
            {
                return false;
            }
        }

        inv.UserInventory.ReserveAmmo[ammoType] = (ushort)(reserve - remaining);
        inv.SendAmmoNextFrame = true;
        while (remaining > 0)
        {
            PickupSyncInfo psi = new(ammoType, inv.transform.position, Quaternion.identity, template.Weight);
            ItemPickupBase pickup = inv.ServerCreatePickup(template, psi, spawn: false);
            if (pickup is AmmoPickup ammoPickup)
            {
                ushort dropped = (ushort)Mathf.Min(ammoPickup.MaxAmmo, remaining);
                ammoPickup.NetworkSavedAmmo = dropped;
                remaining -= ammoPickup.SavedAmmo;
                NetworkServer.Spawn(pickup.gameObject);
                if (PlayerEvents.HasDroppedAmmo)
                {
                    PlayerEvents.OnDroppedAmmo(new PlayerDroppedAmmoEventArgs(inv._hub, ammoType, dropped, ammoPickup));
                }
            }
            else
            {
                remaining--;
                NetworkServer.Spawn(pickup.gameObject);
            }
        }

        __result = amount <= reserve;
        return false;
    }
}
