using InventorySystem;
using InventorySystem.Items;
using InventorySystem.Items.Pickups;
using LabApi.Features.Interfaces;
using Scp914;
using Scp914.Processors;
using UnityEngine;

namespace LabApi.Features.Wrappers;

/// <summary>
/// An internal adapter class to handle the conversion of the wrapper <see cref="IScp914ItemProcessor"/> interface to the base game <see cref="Scp914ItemProcessor"/>.
/// </summary>
internal class ItemProcessorAdapter : Scp914ItemProcessor
{
    /// <summary>
    /// The user supplied <see cref="IScp914ItemProcessor"/> implementation.
    /// </summary>
    public IScp914ItemProcessor Processor { get; internal set; } = null!;

    /// <summary>
    /// Used internally by the base game.
    /// </summary>
    /// <param name="setting">The setting to update the item.</param>
    /// <param name="hub">The owner of the item.</param>
    /// <param name="serial">The serial of the item to upgrade.</param>
    /// <returns>The resulting item, or <see langword="null"/>.</returns>
    public override ItemBase? OnInventoryItemUpgraded(Scp914KnobSetting setting, ReferenceHub hub, ushort serial)
    {
        if (!hub.inventory.UserInventory.Items.TryGetValue(serial, out ItemBase item))
        {
            return null;
        }

        if (!Processor.UsePickupMethodOnly)
        {
            return Processor.UpgradeItem(setting, Item.Get(item)!);
        }

        // Same as the official default: the item is upgraded as a pickup and the result is given back to the owner.
        ItemPickupBase? dropped = hub.inventory.ServerDropItem(serial);
        if (dropped == null)
        {
            return null;
        }

        ItemPickupBase? result = Processor.UpgradePickup(setting, Pickup.Get(dropped)!);
        if (result == null)
        {
            return null;
        }

        ItemBase? newItem = hub.inventory.ServerAddItem(result.Info.ItemId, result.Info.Serial, result);
        result.DestroySelf();
        return newItem;
    }

    /// <summary>
    /// Used internally by the base game.
    /// </summary>
    /// <param name="setting">The setting to update the item.</param>
    /// <param name="ipb">The base game pickup instance.</param>
    /// <param name="newPosition">The output position computed by the game.</param>
    /// <returns>The resulting pickup, or <see langword="null"/>.</returns>
    public override ItemPickupBase? OnPickupUpgraded(Scp914KnobSetting setting, ItemPickupBase ipb, Vector3 newPosition)
        => Processor.UpgradePickup(setting, Pickup.Get(ipb)!);
}
