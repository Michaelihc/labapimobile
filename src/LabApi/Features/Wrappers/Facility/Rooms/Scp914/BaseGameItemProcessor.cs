using InventorySystem.Items;
using InventorySystem.Items.Pickups;
using LabApi.Features.Interfaces;
using Scp914;
using Scp914.Processors;

namespace LabApi.Features.Wrappers;

/// <summary>
/// Adapter for handling the base game <see cref="Scp914ItemProcessor"/>.
/// Used when <see cref="Scp914.GetItemProcessor(ItemType)"/> is used on a <see cref="ItemType"/> which is using a base game item processor.
/// </summary>
/// <remarks>
/// Carl Mod processors return the resulting item or pickup instead of a <c>Scp914Result</c>.
/// </remarks>
public class BaseGameItemProcessor : IScp914ItemProcessor
{
    /// <summary>
    /// An internal constructor to prevent external instantiation.
    /// </summary>
    /// <param name="processor">The base game <see cref="Scp914ItemProcessor"/>.</param>
    internal BaseGameItemProcessor(Scp914ItemProcessor processor)
    {
        Processor = processor;
    }

    /// <summary>
    /// Get base game <see cref="Scp914ItemProcessor"/> instance.
    /// </summary>
    public Scp914ItemProcessor Processor { get; internal set; }

    /// <inheritdoc/>
    public bool UsePickupMethodOnly => false;

    /// <inheritdoc/>
    public ItemBase? UpgradeItem(Scp914KnobSetting setting, Item item)
        => Processor.OnInventoryItemUpgraded(setting, item.Base.Owner, item.Serial);

    /// <inheritdoc/>
    public ItemPickupBase? UpgradePickup(Scp914KnobSetting setting, Pickup pickup)
        => Processor.OnPickupUpgraded(setting, pickup.Base, pickup.Position + IScp914ItemProcessor.MoveVector);
}
