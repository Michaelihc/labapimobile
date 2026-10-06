using HarmonyLib;
using InventorySystem;
using InventorySystem.Configs;
using InventorySystem.Items;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using Mirror;
using NorthwoodLib.Pools;
using PlayerRoles;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace LabApi.Events.Patches.Players;

/// <summary>
/// Raises ReceivingLoadout / ReceivedLoadout.
/// </summary>
/// <remarks>
/// Runs the original untouched unless a loadout event has subscribers; otherwise replays the Carl Mod body with the events,
/// including the deathmatch module's loadout hook on the build that has it. Applied only when the native body is one of
/// the known Carl Mod 0.0.4 bodies.
/// </remarks>
// Official: InventorySystem/InventoryItemProvider.cs ServerGrantLoadout
[HarmonyPatch(typeof(InventoryItemProvider), nameof(InventoryItemProvider.ServerGrantLoadout))]
internal static class PlayerLoadoutPatch
{
    // InventoryItemProvider.ServerGrantLoadout without and with the deathmatch module's DmFun.OnLoadout call.
    private const string StandardBody = "7464e8e33b1f2c00";
    private const string DeathmatchBody = "08878d7420d7a1db";

    private static readonly MethodInfo? Target = AccessTools.DeclaredMethod(typeof(InventoryItemProvider), nameof(InventoryItemProvider.ServerGrantLoadout));

    private static readonly BodyVariant Variant = NativeBody.Identify(Target, StandardBody, DeathmatchBody, out Fingerprint);

    private static readonly string? Fingerprint;

    private static bool Prepare()
    {
        if (Variant == BodyVariant.Standard || (Variant == BodyVariant.Deathmatch && CarlModDeathmatch.HasLoadoutHook))
        {
            return true;
        }

        PatchManager.Skip(typeof(PlayerLoadoutPatch), NativeBody.UnknownBody(Target, Fingerprint, "ReceivingLoadout / ReceivedLoadout are not raised."));
        return false;
    }

    private static bool Prefix(ReferenceHub target, RoleTypeId roleTypeId, bool resetInventory)
    {
        if (!PlayerEvents.HasReceivingLoadout && !PlayerEvents.HasReceivedLoadout)
        {
            return true;
        }

        if (!NetworkServer.active)
        {
            throw new InvalidOperationException("Method ServerGrantLoadout can only be executed on the server.");
        }

        bool defined = StartingInventories.DefinedInventories.TryGetValue(roleTypeId, out InventoryRoleInfo info);
        List<ItemType> items = ListPool<ItemType>.Shared.Rent();
        Dictionary<ItemType, ushort> ammo = [];
        if (defined)
        {
            items.AddRange(info.Items);
            foreach (KeyValuePair<ItemType, ushort> pair in info.Ammo)
            {
                ammo[pair.Key] = pair.Value;
            }
        }

        if (PlayerEvents.HasReceivingLoadout)
        {
            PlayerReceivingLoadoutEventArgs e = new(target, items, ammo, resetInventory);
            PlayerEvents.OnReceivingLoadout(e);
            if (!e.IsAllowed)
            {
                ListPool<ItemType>.Shared.Return(items);
                return false;
            }
        }

        Inventory inventory = target.inventory;
        if (resetInventory)
        {
            while (inventory.UserInventory.Items.Count > 0)
            {
                inventory.ServerRemoveItem(inventory.UserInventory.Items.ElementAt(0).Key, null);
            }

            inventory.UserInventory.ReserveAmmo.Clear();
            inventory.SendAmmoNextFrame = true;
        }

        foreach (KeyValuePair<ItemType, ushort> pair in ammo)
        {
            inventory.ServerAddAmmo(pair.Key, pair.Value);
        }

        for (int i = 0; i < items.Count; i++)
        {
            ItemBase item = inventory.ServerAddItem(items[i]);
            InventoryItemProvider.OnItemProvided?.Invoke(target, item);
        }

        if (defined && Variant == BodyVariant.Deathmatch)
        {
            CarlModDeathmatch.OnLoadout(target);
        }

        if (PlayerEvents.HasReceivedLoadout)
        {
            PlayerEvents.OnReceivedLoadout(new PlayerReceivedLoadoutEventArgs(target, items, ammo, resetInventory));
        }

        ListPool<ItemType>.Shared.Return(items);
        return false;
    }
}
