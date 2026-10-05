using HarmonyLib;
using InventorySystem;
using InventorySystem.Items;
using InventorySystem.Items.Armor;
using InventorySystem.Items.Firearms.Ammo;
using InventorySystem.Items.Pickups;
using InventorySystem.Items.Usables.Scp244;
using InventorySystem.Items.Usables.Scp330;
using InventorySystem.Searching;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using System;
using UnityEngine;

namespace LabApi.Events.Patches.ItemsGeneral;

/// <summary>
/// Raises SearchingPickup before a search request is validated, and SearchingAmmo / SearchingArmor once the
/// ammo or armor search passed validation.
/// </summary>
/// <remarks>
/// The fork has no shared <c>PickupSearchCompletor.ValidateStart</c> event point (its <c>SearchCompletor.ValidateStart</c>
/// is small enough to be inlined into the derived overrides), so the request handler that calls it is patched instead.
/// A denied search is rejected exactly like a failed validation.
/// </remarks>
// Official: InventorySystem/Searching/PickupSearchCompletor.cs ValidateStart, AmmoSearchCompletor.cs ValidateStart, ArmorSearchCompletor.cs ValidateStart
[HarmonyPatch(typeof(SearchCoordinator), nameof(SearchCoordinator.ReceiveRequestUnsafe))]
internal static class SearchingPickupPatch
{
    private static bool Prefix(SearchCoordinator __instance, out SearchSession? session, out SearchCompletor? completor, ref bool __result)
    {
        session = null;
        completor = null;
        if (!PlayerEvents.HasSearchingPickup)
        {
            return true;
        }

        ItemPickupBase target = __instance.SessionPipe.Request.Target;
        if (target == null)
        {
            return true;
        }

        PlayerSearchingPickupEventArgs e = new(__instance.Hub, target);
        PlayerEvents.OnSearchingPickup(e);
        if (e.IsAllowed)
        {
            return true;
        }

        __result = true;
        return false;
    }

    private static void Postfix(SearchCoordinator __instance, ref SearchSession? session, ref SearchCompletor? completor)
    {
        switch (completor)
        {
            case AmmoSearchCompletor when PlayerEvents.HasSearchingAmmo:
            {
                PlayerSearchingAmmoEventArgs e = new(__instance.Hub, (AmmoPickup)__instance.SessionPipe.Request.Target);
                PlayerEvents.OnSearchingAmmo(e);
                if (!e.IsAllowed)
                {
                    session = null;
                    completor = null;
                }

                break;
            }

            case ArmorSearchCompletor when PlayerEvents.HasSearchingArmor:
            {
                PlayerSearchingArmorEventArgs e = new(__instance.Hub, (BodyArmorPickup)__instance.SessionPipe.Request.Target);
                PlayerEvents.OnSearchingArmor(e);
                if (!e.IsAllowed)
                {
                    session = null;
                    completor = null;
                }

                break;
            }
        }
    }
}

/// <summary>
/// Raises SearchedPickup / PickingUpItem / PickedUpItem when a regular item search completes.
/// </summary>
/// <remarks>
/// A denied pickup is released (<c>InUse</c> cleared) like a full inventory.
/// With subscribers the fork body runs here with the official event points; without subscribers it runs untouched.
/// </remarks>
// Official: InventorySystem/Searching/ItemSearchCompletor.cs Complete
[HarmonyPatch(typeof(ItemSearchCompletor), nameof(ItemSearchCompletor.Complete))]
internal static class PickingUpItemPatch
{
    private static bool Prefix(ItemSearchCompletor __instance)
    {
        if (!PlayerEvents.HasSearchedPickup && !PlayerEvents.HasPickingUpItem && !PlayerEvents.HasPickedUpItem)
        {
            return true;
        }

        ReferenceHub hub = __instance.Hub;
        ItemPickupBase target = __instance.TargetPickup;
        SearchEvents.Searched(hub, target);

        if (!SearchEvents.PickingUpItem(hub, target))
        {
            SearchEvents.Release(target);
            return false;
        }

        ItemBase item = hub.inventory.ServerAddItem(target.Info.ItemId, target.Info.Serial, target);
        if (item == null)
        {
            SearchEvents.Release(target);
            return false;
        }

        target.DestroySelf();
        __instance.CheckCategoryLimitHint();
        if (PlayerEvents.HasPickedUpItem)
        {
            PlayerEvents.OnPickedUpItem(new PlayerPickedUpItemEventArgs(hub, item));
        }

        return false;
    }
}

/// <summary>
/// Raises SearchedPickup / PickingUpItem / PickedUpItem when an SCP-244 search completes.
/// </summary>
// Official: InventorySystem/Searching/Scp244SearchCompletor.cs Complete
[HarmonyPatch(typeof(Scp244SearchCompletor), nameof(Scp244SearchCompletor.Complete))]
internal static class PickingUpScp244Patch
{
    private static bool Prefix(Scp244SearchCompletor __instance)
    {
        if (!PlayerEvents.HasSearchedPickup && !PlayerEvents.HasPickingUpItem && !PlayerEvents.HasPickedUpItem)
        {
            return true;
        }

        if (__instance.TargetPickup is not Scp244DeployablePickup scp244)
        {
            return false;
        }

        ReferenceHub hub = __instance.Hub;
        SearchEvents.Searched(hub, scp244);
        if (!SearchEvents.PickingUpItem(hub, scp244))
        {
            SearchEvents.Release(scp244);
            return false;
        }

        ItemBase item = hub.inventory.ServerAddItem(scp244.Info.ItemId, scp244.Info.Serial, scp244);
        scp244.State = Scp244State.PickedUp;
        __instance.CheckCategoryLimitHint();
        if (item != null && PlayerEvents.HasPickedUpItem)
        {
            PlayerEvents.OnPickedUpItem(new PlayerPickedUpItemEventArgs(hub, item));
        }

        return false;
    }
}

/// <summary>
/// Raises SearchedPickup / PickingUpAmmo / PickedUpAmmo when an ammo search completes.
/// </summary>
/// <remarks>
/// The amount set on PickingUpAmmo is applied, as in the official game.
/// </remarks>
// Official: InventorySystem/Searching/AmmoSearchCompletor.cs Complete
[HarmonyPatch(typeof(AmmoSearchCompletor), nameof(AmmoSearchCompletor.Complete))]
internal static class PickingUpAmmoPatch
{
    private static bool Prefix(AmmoSearchCompletor __instance)
    {
        if (!PlayerEvents.HasSearchedPickup && !PlayerEvents.HasPickingUpAmmo && !PlayerEvents.HasPickedUpAmmo)
        {
            return true;
        }

        ReferenceHub hub = __instance.Hub;
        ItemPickupBase target = __instance.TargetPickup;
        SearchEvents.Searched(hub, target);
        if (target is not AmmoPickup ammoPickup)
        {
            Debug.LogError("The pickup needs to derive from AmmoPickup");
            return false;
        }

        ItemType ammoType = __instance._ammoType;
        ushort currentAmmo = __instance.CurrentAmmo;
        ushort maxAmmo = __instance.MaxAmmo;
        ushort ammoAmount = (ushort)(Math.Min(currentAmmo + ammoPickup.SavedAmmo, maxAmmo) - currentAmmo);
        if (PlayerEvents.HasPickingUpAmmo)
        {
            PlayerPickingUpAmmoEventArgs e = new(hub, ammoType, ammoAmount, ammoPickup);
            PlayerEvents.OnPickingUpAmmo(e);
            if (!e.IsAllowed)
            {
                SearchEvents.Release(target);
                return false;
            }

            ammoAmount = e.AmmoAmount;
        }

        if (ammoAmount >= ammoPickup.SavedAmmo)
        {
            target.DestroySelf();
        }
        else
        {
            ammoPickup.NetworkSavedAmmo = (ushort)(ammoPickup.SavedAmmo - ammoAmount);
            SearchEvents.Release(target);
            hub.hints.Show(new Hints.TranslationHint(
                Hints.HintTranslations.MaxAmmoReached,
                [new Hints.AmmoHintParameter((byte)ammoType), new Hints.PackedULongHintParameter(maxAmmo)],
                Hints.HintEffectPresets.FadeInAndOut(0.25f),
                1.5f));
        }

        __instance.CurrentAmmo = (ushort)(currentAmmo + ammoAmount);
        if (PlayerEvents.HasPickedUpAmmo)
        {
            PlayerEvents.OnPickedUpAmmo(new PlayerPickedUpAmmoEventArgs(hub, ammoType, ammoAmount, ammoPickup));
        }

        return false;
    }
}

/// <summary>
/// Raises SearchedPickup / PickingUpArmor / PickedUpArmor when a body armor search completes.
/// </summary>
// Official: InventorySystem/Searching/ArmorSearchCompletor.cs Complete
[HarmonyPatch(typeof(ArmorSearchCompletor), nameof(ArmorSearchCompletor.Complete))]
internal static class PickingUpArmorPatch
{
    private static bool Prefix(ArmorSearchCompletor __instance)
    {
        if (!PlayerEvents.HasSearchedPickup && !PlayerEvents.HasPickingUpArmor && !PlayerEvents.HasPickedUpArmor)
        {
            return true;
        }

        ReferenceHub hub = __instance.Hub;
        ItemPickupBase target = __instance.TargetPickup;
        SearchEvents.Searched(hub, target);
        if (PlayerEvents.HasPickingUpArmor)
        {
            PlayerPickingUpArmorEventArgs e = new(hub, (BodyArmorPickup)target);
            PlayerEvents.OnPickingUpArmor(e);
            if (!e.IsAllowed)
            {
                SearchEvents.Release(target);
                return false;
            }
        }

        if (hub.inventory.TryGetBodyArmorAndItsSerial(out BodyArmor current, out ushort serial))
        {
            current.DontRemoveExcessOnDrop = true;
            hub.inventory.ServerDropItem(serial);
        }

        BodyArmor? armor = hub.inventory.ServerAddItem(target.Info.ItemId, target.Info.Serial, target) as BodyArmor;
        BodyArmorUtils.RemoveEverythingExceedingLimits(hub.inventory, armor);
        target.DestroySelf();
        if (PlayerEvents.HasPickedUpArmor)
        {
            PlayerEvents.OnPickedUpArmor(new PlayerPickedUpArmorEventArgs(hub, armor));
        }

        return false;
    }
}

/// <summary>
/// Raises SearchedPickup / PickingUpScp330 / PickedUpScp330 when an SCP-330 search completes.
/// </summary>
// Official: InventorySystem/Searching/Scp330SearchCompletor.cs Complete
[HarmonyPatch(typeof(Scp330SearchCompletor), nameof(Scp330SearchCompletor.Complete))]
internal static class PickingUpScp330Patch
{
    private static bool Prefix(Scp330SearchCompletor __instance)
    {
        if (!PlayerEvents.HasSearchedPickup && !PlayerEvents.HasPickingUpScp330 && !PlayerEvents.HasPickedUpScp330)
        {
            return true;
        }

        if (__instance.TargetPickup is not Scp330Pickup scp330Pickup)
        {
            return false;
        }

        ReferenceHub hub = __instance.Hub;
        SearchEvents.Searched(hub, scp330Pickup);
        if (PlayerEvents.HasPickingUpScp330)
        {
            PlayerPickingUpScp330EventArgs e = new(hub, scp330Pickup);
            PlayerEvents.OnPickingUpScp330(e);
            if (!e.IsAllowed)
            {
                SearchEvents.Release(scp330Pickup);
                return false;
            }
        }

        if (!Scp330Bag.ServerProcessPickup(hub, scp330Pickup, out Scp330Bag bag))
        {
            return false;
        }

        if (scp330Pickup.StoredCandies.Count > 0)
        {
            SearchEvents.Release(scp330Pickup);
        }
        else
        {
            scp330Pickup.DestroySelf();
        }

        if (PlayerEvents.HasPickedUpScp330)
        {
            PlayerEvents.OnPickedUpScp330(new PlayerPickedUpScp330EventArgs(hub, scp330Pickup, bag));
        }

        return false;
    }
}

/// <summary>
/// Shared helpers for the search completion patches.
/// </summary>
internal static class SearchEvents
{
    internal static void Searched(ReferenceHub hub, ItemPickupBase pickup)
    {
        if (PlayerEvents.HasSearchedPickup)
        {
            PlayerEvents.OnSearchedPickup(new PlayerSearchedPickupEventArgs(hub, pickup));
        }
    }

    internal static bool PickingUpItem(ReferenceHub hub, ItemPickupBase pickup)
    {
        if (!PlayerEvents.HasPickingUpItem)
        {
            return true;
        }

        PlayerPickingUpItemEventArgs e = new(hub, pickup);
        PlayerEvents.OnPickingUpItem(e);
        return e.IsAllowed;
    }

    internal static void Release(ItemPickupBase pickup)
    {
        PickupSyncInfo info = pickup.Info;
        info.InUse = false;
        pickup.NetworkInfo = info;
    }
}
