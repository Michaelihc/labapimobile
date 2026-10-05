using Generators;
using InventorySystem;
using InventorySystem.Items;
using InventorySystem.Items.Armor;
using InventorySystem.Items.Coin;
using InventorySystem.Items.Firearms;
using InventorySystem.Items.Pickups;
using InventorySystem.Items.Usables;
using NorthwoodLib.Pools;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;

namespace LabApi.Features.Wrappers;

/// <summary>
/// The wrapper representing <see cref="ItemBase">object</see>.
///
/// <para>Not to be confused with <see cref="Pickup">item pickup</see>.</para>
/// </summary>
public class Item
{
    /// <summary>
    /// Contains all the handlers for constructing wrappers for the associated base game types.
    /// </summary>
    private static readonly Dictionary<Type, Func<ItemBase, Item>> TypeWrappers = [];

    /// <summary>
    /// Contains all the cached items, accessible through their <see cref="Base"/>.
    /// </summary>
    public static Dictionary<ItemBase, Item> Dictionary { get; } = [];

    /// <summary>
    /// A reference to all instances of <see cref="Item"/>.
    /// </summary>
    public static IReadOnlyCollection<Item> List => Dictionary.Values;

    /// <summary>
    /// Contains all cached items with their <see cref="Item.Serial"/> as a key.
    /// </summary>
    private static Dictionary<ushort, Item> SerialsCache { get; } = [];

    /// <summary>
    /// Gets the item wrapper from the <see cref="Dictionary"/> if it exists and the <see cref="ItemBase"/> was not <see langword="null"/>.
    /// </summary>
    /// <param name="itemBase">The <see cref="Base"/> of the item.</param>
    /// <returns>The requested item or null.</returns>
    [return: NotNullIfNotNull(nameof(itemBase))]
    public static Item? Get(ItemBase? itemBase)
    {
        if (itemBase == null)
        {
            return null;
        }

        return Dictionary.TryGetValue(itemBase, out Item item) ? item : CreateItemWrapper(itemBase);
    }

    /// <summary>
    /// Tries to get the item wrapper from the <see cref="Dictionary"/>.
    /// </summary>
    /// <param name="itemBase">The <see cref="Base"/> of the item.</param>
    /// <param name="item">The requested item.</param>
    /// <returns>True if the item exists, otherwise false.</returns>
    public static bool TryGet(ItemBase? itemBase, [NotNullWhen(true)] out Item? item)
    {
        item = Get(itemBase);
        return item != null;
    }

    /// <summary>
    /// Gets the item wrapper or null from <see cref="SerialsCache"/>.
    /// </summary>
    /// <param name="serial">Serial of the item.</param>
    /// <returns>The requested item.</returns>
    public static Item? Get(ushort serial) => TryGet(serial, out Item? item) ? item : null;

    /// <summary>
    /// Gets the item wrapper or null from the <see cref="Dictionary"/> based on provided serial number.
    /// </summary>
    /// <param name="serial">The serial number of the item.</param>
    /// <param name="item">The requested item.</param>
    /// <returns>Whether the was successfully retrieved, otherwise false.</returns>
    public static bool TryGet(ushort serial, [NotNullWhen(true)] out Item? item)
    {
        item = null;
        if (SerialsCache.TryGetValue(serial, out item))
        {
            return true;
        }

        foreach (Item candidate in Dictionary.Values)
        {
            if (candidate.Serial != serial)
            {
                continue;
            }

            item = candidate;
            SerialsCache[serial] = item;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Gets a pooled list of items having the same <see cref="ItemType"/>.
    /// </summary>
    /// <param name="type">Target type.</param>
    /// <returns>A List of items.</returns>
    public static List<Item> GetAll(ItemType type)
    {
        List<Item> list = ListPool<Item>.Shared.Rent();
        foreach (Item item in Dictionary.Values)
        {
            if (item.Type == type)
            {
                list.Add(item);
            }
        }

        return list;
    }

    /// <summary>
    /// Gets a pooled list of items having the same <see cref="ItemCategory"/>.
    /// </summary>
    /// <param name="category">Target category.</param>
    /// <returns>A List of items.</returns>
    public static List<Item> GetAll(ItemCategory category)
    {
        List<Item> list = ListPool<Item>.Shared.Rent();
        foreach (Item item in Dictionary.Values)
        {
            if (item.Category == category)
            {
                list.Add(item);
            }
        }

        return list;
    }

    /// <summary>
    /// Initializes the <see cref="Item"/> class by subscribing to the game's inventory events and registers derived wrappers.
    /// </summary>
    /// <remarks>
    /// The Carl Mod build has no <c>ItemBase.OnItemAdded</c>/<c>OnItemRemoved</c>; items are tracked through
    /// <see cref="InventoryExtensions.OnItemAdded"/>/<see cref="InventoryExtensions.OnItemRemoved"/> and the
    /// inventory of a destroyed player is released through <see cref="ReferenceHub.OnPlayerRemoved"/>.
    /// </remarks>
    [InitializeWrapper]
    internal static void Initialize()
    {
        Dictionary.Clear();
        SerialsCache.Clear();

        InventoryExtensions.OnItemAdded += OnInventoryItemAdded;
        InventoryExtensions.OnItemRemoved += OnInventoryItemRemoved;
        ReferenceHub.OnPlayerRemoved += OnHubRemoved;

        Register<ItemBase>(x => new Item(x));

        Register<Consumable>(x => new ConsumableItem(x));
        Register<Scp500>(x => new Scp500Item(x));
        Register<InventorySystem.Items.Usables.Scp1853Item>(x => new Scp1853Item(x));
        Register<Painkillers>(x => new PainkillersItem(x));
        Register<Adrenaline>(x => new AdrenalineItem(x));
        Register<Medkit>(x => new MedkitItem(x));
        Register<Scp207>(x => new Scp207Item(x));

        Register<InventorySystem.Items.Usables.UsableItem>(x => new UsableItem(x));
        Register<InventorySystem.Items.Usables.Scp1576.Scp1576Item>(x => new Scp1576Item(x));
        Register<InventorySystem.Items.Usables.Scp330.Scp330Bag>(x => new Scp330Item(x));
        Register<InventorySystem.Items.Usables.Scp244.Scp244Item>(x => new Scp244Item(x));
        Register<Scp268>(x => new Scp268Item(x));

        Register<Firearm>(FirearmItem.CreateFirearmWrapper);
        Register<ParticleDisruptor>(FirearmItem.CreateFirearmWrapper);

        Register<InventorySystem.Items.Jailbird.JailbirdItem>(x => new JailbirdItem(x));
        Register<Coin>(x => new CoinItem(x));

        Register<InventorySystem.Items.Flashlight.FlashlightItem>(x => new FlashlightItem(x));

        Register<InventorySystem.Items.Radio.RadioItem>(x => new RadioItem(x));
        Register<InventorySystem.Items.Firearms.Ammo.AmmoItem>(x => new AmmoItem(x));
        Register<BodyArmor>(x => new BodyArmorItem(x));
        Register<InventorySystem.Items.ThrowableProjectiles.ThrowableItem>(x => new ThrowableItem(x));
        Register<InventorySystem.Items.Keycards.KeycardItem>(x => new KeycardItem(x));
        Register<InventorySystem.Items.MicroHID.MicroHIDItem>(x => new MicroHIDItem(x));
    }

    /// <summary>
    /// Creates a new wrapper from the base item object.
    /// </summary>
    /// <param name="item">The base object.</param>
    /// <returns>The newly created wrapper.</returns>
    protected static Item CreateItemWrapper(ItemBase item)
    {
        Type targetType = item.GetType();
        if (!TypeWrappers.TryGetValue(targetType, out Func<ItemBase, Item>? ctorFunc))
        {
            // Fork item classes are often concrete subclasses of the type the official game uses directly
            // (e.g. AutomaticFirearm, Shotgun and Revolver for Firearm), so resolve through the base types once
            // and cache the result.
            for (Type? baseType = targetType.BaseType; baseType != null && baseType != typeof(object); baseType = baseType.BaseType)
            {
                if (TypeWrappers.TryGetValue(baseType, out ctorFunc))
                {
                    break;
                }
            }

            if (ctorFunc == null)
            {
#if DEBUG
                Logger.Warn($"Unable to find LabApi wrapper for {nameof(Item)} {targetType.Name}, backup up to base constructor!");
#endif
                return new Item(item);
            }

            TypeWrappers[targetType] = ctorFunc;
        }

        return ctorFunc.Invoke(item);
    }

    private static void OnInventoryItemAdded(ReferenceHub hub, ItemBase item, ItemPickupBase pickup) => AddItem(item);

    private static void OnInventoryItemRemoved(ReferenceHub hub, ItemBase item, ItemPickupBase pickup) => RemoveItem(item);

    /// <summary>
    /// Releases the wrappers of a destroyed player's items, which the fork destroys together with the player object.
    /// </summary>
    /// <param name="hub">The destroyed player.</param>
    private static void OnHubRemoved(ReferenceHub hub)
    {
        if (hub == null || hub.inventory == null || hub.inventory.UserInventory == null)
        {
            return;
        }

        foreach (ItemBase item in hub.inventory.UserInventory.Items.Values)
        {
            if (item != null)
            {
                RemoveItem(item);
            }
        }
    }

    /// <summary>
    /// A private method to handle the creation of new items in the server.
    /// </summary>
    /// <param name="item">The created <see cref="ItemBase"/> instance.</param>
    private static void AddItem(ItemBase item)
    {
        try
        {
            if (!Dictionary.ContainsKey(item))
            {
                _ = CreateItemWrapper(item);
            }
        }
        catch (Exception e)
        {
            Logger.InternalError($"Failed to handle item creation with error: {e}");
        }
    }

    /// <summary>
    /// A private method to handle the removal of items from the server.
    /// </summary>
    /// <param name="itemBase">The to be destroyed <see cref="ItemBase"/> instance.</param>
    private static void RemoveItem(ItemBase itemBase)
    {
        try
        {
            SerialsCache.Remove(itemBase.ItemSerial);
            if (Dictionary.TryGetValue(itemBase, out Item item))
            {
                Dictionary.Remove(itemBase);
                item.OnRemove();
            }
        }
        catch (Exception e)
        {
            Console.Logger.InternalError($"Failed to handle item destruction with error: {e}");
        }
    }

    /// <summary>
    /// A private method to handle the addition of wrapper handlers.
    /// </summary>
    /// <typeparam name="T">The derived base game type to handle.</typeparam>
    /// <param name="constructor">A handler to construct the wrapper with the base game instance.</param>
    private static void Register<T>(Func<T, Item> constructor)
        where T : ItemBase
    {
        TypeWrappers.Add(typeof(T), x => constructor((T)x));
    }

    /// <summary>
    /// A private constructor to prevent external instantiation.
    /// </summary>
    /// <param name="itemBase">The <see cref="Base"/> of the item.</param>
    protected Item(ItemBase itemBase)
    {
        Base = itemBase;
        IsPrefab = InventoryItemLoader.TryGetItem(itemBase.ItemTypeId, out ItemBase prefab) && prefab == itemBase;

        if (CanCache)
        {
            if (Dictionary.ContainsKey(itemBase))
            {
                Console.Logger.InternalError($"Failed to create an item for base: {itemBase}");
                return;
            }

            Dictionary[itemBase] = this;
            SerialsCache[itemBase.ItemSerial] = this;
        }
    }

    /// <summary>
    /// The <see cref="Base"/> of the item.
    /// </summary>
    public ItemBase Base { get; }

    /// <summary>
    /// Gets the item's <see cref="UnityEngine.GameObject"/>.
    /// </summary>
    public GameObject GameObject => Base.gameObject;

    /// <summary>
    /// Gets whether the item was destroyed.
    /// </summary>
    /// <remarks>
    /// Happens when an item is either dropped, removed or used up.
    /// </remarks>
    public bool IsDestroyed => Base == null || GameObject == null;

    /// <summary>
    /// Gets whether or not this instance is used as a prefab.
    /// </summary>
    /// <remarks>
    /// Changes made to the prefab instance will be reflected across all subsequent new instances.
    /// </remarks>
    public bool IsPrefab { get; }

    /// <summary>
    /// Gets the item's <see cref="ItemType"/>.
    /// </summary>
    public ItemType Type => Base.ItemTypeId;

    /// <summary>
    /// Gets or sets the item's <see cref="ItemCategory"/>.
    /// <para>
    /// Category is not saved and is discarded when the item is dropped.
    /// </para>
    /// </summary>
    public ItemCategory Category
    {
        get => Base.Category;
        set => Base.Category = value;
    }

    /// <summary>
    /// Gets or sets the item's <see cref="ItemTierFlags"/>.
    /// <para>
    /// Flags are not saved and are discarded when the item is dropped.
    /// </para>
    /// </summary>
    public ItemTierFlags TierFlags
    {
        get => Base.TierFlags;
        set => Base.TierFlags = value;
    }

    /// <summary>
    /// Gets or sets the item's <see cref="ItemThrowSettings"/>.
    /// <para>
    /// Settings are not saved and are discarded when the item is dropped.
    /// </para>
    /// </summary>
    public ItemThrowSettings ThrowSettings
    {
        get => Base.ThrowSettings;
        set => Base.ThrowSettings = value;
    }

    /// <summary>
    /// Gets whether the item is being held.
    /// </summary>
    public bool IsEquipped => Base.IsEquipped;

    /// <summary>
    /// Gets whether the item can be equipped.
    /// </summary>
    /// <remarks>
    /// Only applies to player interactions, forcefully equipping an item is always possible.
    /// </remarks>
    public bool CanEquip => Base.CanEquip();

    /// <summary>
    /// Gets whether the item can be holstered.
    /// </summary>
    /// <remarks>
    /// An item is holstered when either changing to another item or deflecting the item.
    /// Only applies to player interactions, forcefully holstering an item is always possible.
    /// </remarks>
    public bool CanHolster => Base.CanHolster();

    /// <summary>
    /// Gets whether the item can be dropped.
    /// </summary>
    /// <remarks>
    /// Only applies to player interactions, forcefully dropping an item is always possible.
    /// The Carl Mod build has no separate drop restriction: a player may drop an item whenever it can be holstered.
    /// </remarks>
    public bool CanDrop => Base.CanHolster();

    /// <summary>
    /// Gets the item's current owner.
    /// </summary>
    public Player? CurrentOwner => Player.Get(Base.Owner);

    /// <summary>
    /// Gets the item's serial.
    /// </summary>
    public ushort Serial => Base.ItemSerial;

    /// <summary>
    /// Gets the item's weight.
    /// </summary>
    public float Weight => Base.Weight;

    /// <summary>
    /// Gets whether the item wrapper is allowed to be cached.
    /// </summary>
    protected bool CanCache => !IsDestroyed && !IsPrefab && Serial != 0 && Base.isActiveAndEnabled;

    /// <summary>
    /// Drops this item from player's inventory.
    /// </summary>
    /// <returns>The dropped item as a <see cref="Pickup"/>.</returns>
    public Pickup DropItem() => Pickup.Get(Base.ServerDropItem());

    /// <summary>
    /// Moves the item to the specified players inventory.
    /// </summary>
    /// <param name="player">The player to move this item to.</param>
    public void MoveTo(Player player) => player.AddItem(DropItem());

    /// <inheritdoc />
    public override string ToString()
    {
        return $"[{GetType().Name}: Type={Type}, IsDestroyed={IsDestroyed}, IsEquipped={IsEquipped}, Serial={Serial}]";
    }

    /// <summary>
    /// An internal virtual method to signal to derived implementations to uncache when the base object is destroyed.
    /// </summary>
    internal virtual void OnRemove()
    {
    }
}