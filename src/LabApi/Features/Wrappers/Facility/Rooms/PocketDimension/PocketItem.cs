using Generators;
using InventorySystem.Items;
using InventorySystem.Items.Pickups;
using Mirror;
using PlayerRoles.PlayableScps.Scp106;
using RelativePositioning;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;

namespace LabApi.Features.Wrappers;

/// <summary>
/// The wrapper over the <see cref="Scp106PocketItemManager.PocketItem"/> base object.
/// </summary>
/// <remarks>
/// Contains the item pickup and its associated pocket dimension properties.
/// Carl Mod's item manager raises no add/remove events, so the wrappers are synchronized with its tracked items on access.
/// </remarks>
public class PocketItem
{
    /// <summary>
    /// Contains all the cached <see cref="PocketItem"/> instances, accessible through their <see cref="ItemBase"/>.
    /// </summary>
    private static readonly Dictionary<ItemPickupBase, PocketItem> Dictionary = [];

    /// <summary>
    /// A reference to all <see cref="PocketItem"/> instances currently in the game.
    /// </summary>
    public static IReadOnlyCollection<PocketItem> List
    {
        get
        {
            Synchronize();
            return Dictionary.Values;
        }
    }

    /// <summary>
    /// Tries to get the <see cref="PocketItem"/> associated with the <see cref="Wrappers.Pickup"/>.
    /// </summary>
    /// <param name="pickup">The <see cref="Wrappers.Pickup"/> inside the pocket dimension to get the <see cref="PocketItem"/> from.</param>
    /// <param name="pocketItem">The <see cref="PocketItem"/> associated with <see cref="Wrappers.Pickup"/> or null if it doesn't exists.</param>
    /// <returns>Whether the <see cref="PocketItem"/> was successfully retrieved.</returns>
    public static bool TryGet(Pickup pickup, [NotNullWhen(true)] out PocketItem? pocketItem)
    {
        if (!Scp106PocketItemManager.TrackedItems.TryGetValue(pickup.Base, out Scp106PocketItemManager.PocketItem baseItem))
        {
            Dictionary.Remove(pickup.Base);
            pocketItem = null;
            return false;
        }

        if (!Dictionary.TryGetValue(pickup.Base, out pocketItem) || pocketItem.Base != baseItem)
        {
            pocketItem = new PocketItem(pickup.Base, baseItem);
        }

        return true;
    }

    /// <summary>
    /// Gets the <see cref="PocketItem"/> associated with the <see cref="Wrappers.Pickup"/>.
    /// </summary>
    /// <param name="pickup">The <see cref="Wrappers.Pickup"/> inside the pocket dimension to get the <see cref="PocketItem"/> from.</param>
    /// <returns>The associated <see cref="PocketItem"/> for the <see cref="Wrappers.Pickup"/> or null if it doesn't exist.</returns>
    public static PocketItem? Get(Pickup pickup) => TryGet(pickup, out PocketItem? pocketItem) ? pocketItem : null;

    /// <summary>
    /// Gets or adds a <see cref="PocketItem"/>.
    /// </summary>
    /// <param name="pickup">The <see cref="Wrappers.Pickup"/> to get or add to the pocket dimension.</param>
    /// <returns>The <see cref="PocketItem"/> instance.</returns>
    /// <remarks>
    /// If the pickup is not in the pocket dimension it is teleported there on creation of the <see cref="PocketItem"/>.
    /// </remarks>
    public static PocketItem GetOrAdd(Pickup pickup)
    {
        if (TryGet(pickup, out PocketItem? pocketItem))
        {
            return pocketItem;
        }

        pickup.Position = PocketDimension.Instance!.Position + Vector3.up;

        // Carl Mod has no AddItem; its pickup-added handler starts tracking pickups inside the pocket dimension.
        Scp106PocketItemManager.OnAdded(pickup.Base);
        return Get(pickup)!;
    }

    /// <summary>
    /// Initializes the PocketItem wrapper cache.
    /// </summary>
    [InitializeWrapper]
    internal static void Initialize()
    {
        Dictionary.Clear();
    }

    /// <summary>
    /// Synchronizes the wrapper cache with the items tracked by <see cref="Scp106PocketItemManager"/>.
    /// </summary>
    private static void Synchronize()
    {
        List<ItemPickupBase> stale = NorthwoodLib.Pools.ListPool<ItemPickupBase>.Shared.Rent();
        foreach (KeyValuePair<ItemPickupBase, PocketItem> pair in Dictionary)
        {
            if (!Scp106PocketItemManager.TrackedItems.TryGetValue(pair.Key, out Scp106PocketItemManager.PocketItem baseItem) || baseItem != pair.Value.Base)
            {
                stale.Add(pair.Key);
            }
        }

        foreach (ItemPickupBase key in stale)
        {
            Dictionary.Remove(key);
        }

        NorthwoodLib.Pools.ListPool<ItemPickupBase>.Shared.Return(stale);

        foreach (KeyValuePair<ItemPickupBase, Scp106PocketItemManager.PocketItem> pair in Scp106PocketItemManager.TrackedItems)
        {
            if (pair.Key != null && !Dictionary.ContainsKey(pair.Key))
            {
                _ = new PocketItem(pair.Key, pair.Value);
            }
        }
    }

    /// <summary>
    /// An internal constructor to prevent external instantiation.
    /// </summary>
    /// <param name="pickup">The item pickup in the pocket dimension.</param>
    /// <param name="pocketItem">The base <see cref="Scp106PocketItemManager.PocketItem"/> object.</param>
    internal PocketItem(ItemPickupBase pickup, Scp106PocketItemManager.PocketItem pocketItem)
    {
        Dictionary[pickup] = this;
        Pickup = Pickup.Get(pickup);
        Base = pocketItem;
    }

    /// <summary>
    /// The item in the pocket dimension.
    /// </summary>
    public Pickup Pickup { get; }

    /// <summary>
    /// The base <see cref="Scp106PocketItemManager.PocketItem"/> object.
    /// </summary>
    public Scp106PocketItemManager.PocketItem Base { get; }

    /// <summary>
    /// Gets or sets the delay before the item pickup drops out or is destroyed from the pocket dimension.
    /// </summary>
    public double TriggerDelay
    {
        get => Base.TriggerTime - NetworkTime.time;
        set => Base.TriggerTime = NetworkTime.time + value;
    }

    /// <summary>
    /// Gets or sets whether the item pickup is destroyed after the <see cref="TriggerDelay"/>.
    /// </summary>
    public bool WillBeDestroyed
    {
        get => Base.Remove;
        set => Base.Remove = value;
    }

    /// <summary>
    /// The position to drop the item pickup if <see cref="WillBeDestroyed"/> is set to false.
    /// </summary>
    public Vector3 DropPosition
    {
        get => Base.DropPosition.Position;
        set => Base.DropPosition = new RelativePosition(value);
    }

    /// <summary>
    /// Gets whether a warning cue was sent to the players about a dropping item pickup.
    /// </summary>
    public bool IsWarningSent => Base.WarningSent;
}
