using HarmonyLib;
using InventorySystem.Items;
using InventorySystem.Items.Armor;
using InventorySystem.Items.Pickups;
using LabApi.Events.Arguments.Scp914Events;
using LabApi.Events.Handlers;
using NorthwoodLib.Pools;
using PlayerRoles.FirstPersonControl;
using Scp914;
using Scp914.Processors;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace LabApi.Events.Patches.Facility;

// Official: Scp914/Scp914Controller.cs ServerInteract
[HarmonyPatch(typeof(Scp914Controller), nameof(Scp914Controller.ServerInteract))]
internal static class Scp914Patches
{
    /// <summary>
    /// Starts an upgrade sequence like the fork's Activate interaction.
    /// </summary>
    /// <param name="controller">The SCP-914 controller.</param>
    internal static void StartUpgrade(Scp914Controller controller)
    {
        controller._remainingCooldown = controller._totalSequenceTime;
        controller._isUpgrading = true;
        controller._itemsAlreadyUpgraded = false;
        controller.RpcPlaySound((byte)Scp914Sound.Upgrading);
    }

    private static bool Prefix(Scp914Controller __instance, ReferenceHub ply, byte colliderId)
    {
        if (!Scp914Events.HasKnobChanging && !Scp914Events.HasKnobChanged && !Scp914Events.HasActivating && !Scp914Events.HasActivated)
        {
            return true;
        }

        if (__instance._remainingCooldown > 0f)
        {
            return false;
        }

        switch ((Scp914InteractCode)colliderId)
        {
            case Scp914InteractCode.ChangeMode:
            {
                Scp914KnobSetting oldSetting = __instance._knobSetting;
                Scp914KnobSetting newSetting = oldSetting + 1;
                if (!Enum.IsDefined(typeof(Scp914KnobSetting), newSetting))
                {
                    newSetting = Scp914KnobSetting.Rough;
                }

                if (Scp914Events.HasKnobChanging)
                {
                    Scp914KnobChangingEventArgs e = new(oldSetting, newSetting, ply);
                    Scp914Events.OnKnobChanging(e);
                    if (!e.IsAllowed)
                    {
                        break;
                    }

                    newSetting = e.KnobSetting;
                }

                __instance._remainingCooldown = __instance._knobChangeCooldown;
                __instance.Network_knobSetting = newSetting;
                __instance.RpcPlaySound((byte)Scp914Sound.KnobChange);
                if (Scp914Events.HasKnobChanged)
                {
                    Scp914Events.OnKnobChanged(new Scp914KnobChangedEventArgs(oldSetting, newSetting, ply));
                }

                break;
            }

            case Scp914InteractCode.Activate:
            {
                if (Scp914Events.HasActivating)
                {
                    Scp914ActivatingEventArgs e = new(__instance._knobSetting, ply);
                    Scp914Events.OnActivating(e);
                    if (!e.IsAllowed)
                    {
                        break;
                    }

                    __instance.Network_knobSetting = e.KnobSetting;
                }

                StartUpgrade(__instance);
                if (Scp914Events.HasActivated)
                {
                    Scp914Events.OnActivated(new Scp914ActivatedEventArgs(__instance._knobSetting, ply));
                }

                break;
            }
        }

        return false;
    }
}

/// <summary>
/// Access to the fork's own <see cref="Scp914Upgrader"/> hook events, which the replacement methods keep invoking.
/// </summary>
internal static class Scp914UpgraderHooks
{
    private static readonly FieldInfo PlayerProcessField = AccessTools.Field(typeof(Scp914Upgrader), "OnPlayerProcess");

    private static readonly FieldInfo PickupProcessField = AccessTools.Field(typeof(Scp914Upgrader), "OnPickupProcess");

    private static readonly FieldInfo ItemAttemptField = AccessTools.Field(typeof(Scp914Upgrader), "OnInventoryItemProcessAttempt");

    private static readonly FieldInfo ItemUpgradedFullField = AccessTools.Field(typeof(Scp914Upgrader), "OnInventoryItemUpgradedFull");

    private static readonly FieldInfo PickupUpgradedFullField = AccessTools.Field(typeof(Scp914Upgrader), "OnPickupUpgradedFull");

    internal static Func<ReferenceHub, Scp914KnobSetting, Vector3, (bool Allow, Scp914KnobSetting Setting, Vector3 Position)>? PlayerProcess =>
        PlayerProcessField?.GetValue(null) as Func<ReferenceHub, Scp914KnobSetting, Vector3, (bool Allow, Scp914KnobSetting Setting, Vector3 Position)>;

    internal static Func<ItemPickupBase, Vector3, Scp914KnobSetting, (bool Allow, Scp914KnobSetting Setting, Vector3 Position)>? PickupProcess =>
        PickupProcessField?.GetValue(null) as Func<ItemPickupBase, Vector3, Scp914KnobSetting, (bool Allow, Scp914KnobSetting Setting, Vector3 Position)>;

    internal static Action<ReferenceHub, ItemBase, Scp914KnobSetting>? ItemAttempt =>
        ItemAttemptField?.GetValue(null) as Action<ReferenceHub, ItemBase, Scp914KnobSetting>;

    internal static Action<ReferenceHub, ItemBase, Scp914KnobSetting>? ItemUpgradedFull =>
        ItemUpgradedFullField?.GetValue(null) as Action<ReferenceHub, ItemBase, Scp914KnobSetting>;

    internal static Action<ItemPickupBase, Vector3, Scp914KnobSetting>? PickupUpgradedFull =>
        PickupUpgradedFullField?.GetValue(null) as Action<ItemPickupBase, Vector3, Scp914KnobSetting>;
}

// Official: Scp914/Scp914Upgrader.cs ProcessPlayer
[HarmonyPatch(typeof(Scp914Upgrader), nameof(Scp914Upgrader.ProcessPlayer))]
internal static class Scp914ProcessPlayerPatch
{
    private static bool Prefix(ReferenceHub ply, bool upgradeInventory, bool heldOnly, Vector3 moveVector, Scp914KnobSetting setting)
    {
        if (!Scp914Events.HasProcessingPlayer && !Scp914Events.HasProcessedPlayer
            && !Scp914Events.HasProcessingInventoryItem && !Scp914Events.HasProcessedInventoryItem)
        {
            return true;
        }

        if (Physics.Linecast(ply.transform.position, Scp914Controller.Singleton.IntakeChamber.position, Scp914Upgrader.SolidObjectMask))
        {
            return false;
        }

        Vector3 newPosition = ply.transform.position + moveVector;
        var playerHook = Scp914UpgraderHooks.PlayerProcess;
        if (playerHook != null)
        {
            (bool allow, Scp914KnobSetting hookSetting, Vector3 hookPosition) = playerHook(ply, setting, newPosition);
            if (!allow)
            {
                return false;
            }

            setting = hookSetting;
            newPosition = hookPosition;
        }

        if (Scp914Events.HasProcessingPlayer)
        {
            Scp914ProcessingPlayerEventArgs e = new(newPosition, setting, ply);
            Scp914Events.OnProcessingPlayer(e);
            if (!e.IsAllowed)
            {
                return false;
            }

            setting = e.KnobSetting;
            newPosition = e.NewPosition;
        }

        ply.TryOverridePosition(newPosition, Vector3.zero);
        if (!upgradeInventory)
        {
            return false;
        }

        HashSet<ushort> serials = HashSetPool<ushort>.Shared.Rent();
        foreach (KeyValuePair<ushort, ItemBase> item in ply.inventory.UserInventory.Items)
        {
            if (!heldOnly || item.Key == ply.inventory.CurItem.SerialNumber)
            {
                serials.Add(item.Key);
            }
        }

        foreach (ushort serial in serials)
        {
            if (!ply.inventory.UserInventory.Items.TryGetValue(serial, out ItemBase item) || !Scp914Upgrader.TryGetProcessor(item.ItemTypeId, out Scp914ItemProcessor processor))
            {
                continue;
            }

            ItemType oldType = item.ItemTypeId;
            if (Scp914Events.HasProcessingInventoryItem)
            {
                Scp914ProcessingInventoryItemEventArgs e = new(item, setting, ply);
                Scp914Events.OnProcessingInventoryItem(e);
                if (!e.IsAllowed)
                {
                    continue;
                }

                setting = e.KnobSetting;
            }

            Scp914UpgraderHooks.ItemAttempt?.Invoke(ply, item, setting);
            Scp914Upgrader.OnInventoryItemUpgraded?.Invoke(item, setting);
            ItemBase? result = processor.OnInventoryItemUpgraded(setting, ply, serial);
            if (result != null)
            {
                Scp914UpgraderHooks.ItemUpgradedFull?.Invoke(ply, result, setting);
            }

            if (!Scp914Events.HasProcessedInventoryItem)
            {
                continue;
            }

            // Carl Mod processors return null when the item is kept; the kept item is reported like an official result.
            if (result == null && ply.inventory.UserInventory.Items.TryGetValue(serial, out ItemBase kept))
            {
                result = kept;
            }

            if (result != null)
            {
                Scp914Events.OnProcessedInventoryItem(new Scp914ProcessedInventoryItemEventArgs(oldType, result, setting, ply));
            }
        }

        HashSetPool<ushort>.Shared.Return(serials);
        BodyArmorUtils.RemoveEverythingExceedingLimits(ply.inventory, ply.inventory.TryGetBodyArmor(out BodyArmor bodyArmor) ? bodyArmor : null);
        if (Scp914Events.HasProcessedPlayer)
        {
            Scp914Events.OnProcessedPlayer(new Scp914ProcessedPlayerEventArgs(newPosition, setting, ply));
        }

        return false;
    }
}

// Official: Scp914/Scp914Upgrader.cs ProcessPickup
[HarmonyPatch(typeof(Scp914Upgrader), nameof(Scp914Upgrader.ProcessPickup))]
internal static class Scp914ProcessPickupPatch
{
    private static bool Prefix(ItemPickupBase pickup, bool upgradeDropped, Vector3 moveVector, Scp914KnobSetting setting)
    {
        if (!Scp914Events.HasProcessingPickup && !Scp914Events.HasProcessedPickup)
        {
            return true;
        }

        if (pickup.Info.Locked || !upgradeDropped || !Scp914Upgrader.TryGetProcessor(pickup.Info.ItemId, out Scp914ItemProcessor processor))
        {
            return false;
        }

        Vector3 newPosition = pickup.transform.position + moveVector;
        var pickupHook = Scp914UpgraderHooks.PickupProcess;
        if (pickupHook != null)
        {
            (bool allow, Scp914KnobSetting hookSetting, Vector3 hookPosition) = pickupHook(pickup, newPosition, setting);
            if (!allow)
            {
                return false;
            }

            setting = hookSetting;
            newPosition = hookPosition;
        }

        ItemType oldType = pickup.Info.ItemId;
        if (Scp914Events.HasProcessingPickup)
        {
            Scp914ProcessingPickupEventArgs e = new(newPosition, setting, pickup);
            Scp914Events.OnProcessingPickup(e);
            if (!e.IsAllowed)
            {
                return false;
            }

            newPosition = e.NewPosition;
            setting = e.KnobSetting;
        }

        Scp914Upgrader.OnPickupUpgraded?.Invoke(pickup, setting);
        ItemPickupBase? result = processor.OnPickupUpgraded(setting, pickup, newPosition);
        if (result != null)
        {
            Scp914UpgraderHooks.PickupUpgradedFull?.Invoke(result, newPosition, setting);
        }

        if (Scp914Events.HasProcessedPickup)
        {
            Scp914Events.OnProcessedPickup(new Scp914ProcessedPickupEventArgs(oldType, newPosition, setting, result!));
        }

        return false;
    }
}
