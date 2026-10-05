using InventorySystem.Items.MicroHID;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;
using BaseMicroHIDItem = InventorySystem.Items.MicroHID.MicroHIDItem;

namespace LabApi.Features.Wrappers;

/// <summary>
/// The wrapper representing <see cref="BaseMicroHIDItem"/>.
/// </summary>
/// <remarks>
/// The Carl Mod build uses the pre-14.0 Micro-HID: a single <see cref="HidState"/> machine with energy stored on the item,
/// no firing modes, no broken state and no module system.
/// </remarks>
public class MicroHIDItem : Item
{
    /// <summary>
    /// Contains all the cached micro hid items, accessible through their <see cref="BaseMicroHIDItem"/>.
    /// </summary>
    public static new Dictionary<BaseMicroHIDItem, MicroHIDItem> Dictionary { get; } = [];

    /// <summary>
    /// A reference to all instances of <see cref="MicroHIDItem"/>.
    /// </summary>
    public static new IReadOnlyCollection<MicroHIDItem> List => Dictionary.Values;

    /// <summary>
    /// Gets the micro hid item wrapper from the <see cref="Dictionary"/> or creates a new one if it doesn't exist and the provided <see cref="BaseMicroHIDItem"/> was not null.
    /// </summary>
    /// <param name="baseMicroHIDItem">The <see cref="Base"/> of the item.</param>
    /// <returns>The requested item or null.</returns>
    [return: NotNullIfNotNull(nameof(baseMicroHIDItem))]
    public static MicroHIDItem? Get(BaseMicroHIDItem? baseMicroHIDItem)
    {
        if (baseMicroHIDItem == null)
        {
            return null;
        }

        return Dictionary.TryGetValue(baseMicroHIDItem, out MicroHIDItem item) ? item : (MicroHIDItem)CreateItemWrapper(baseMicroHIDItem);
    }

    /// <summary>
    /// An internal constructor to prevent external instantiation.
    /// </summary>
    /// <param name="baseMicroHIDItem">The base <see cref="BaseMicroHIDItem"/> object.</param>
    internal MicroHIDItem(BaseMicroHIDItem baseMicroHIDItem)
        : base(baseMicroHIDItem)
    {
        Base = baseMicroHIDItem;

        if (CanCache)
        {
            Dictionary.Add(baseMicroHIDItem, this);
        }
    }

    /// <summary>
    /// The base <see cref="BaseMicroHIDItem"/> object.
    /// </summary>
    public new BaseMicroHIDItem Base { get; }

    /// <summary>
    /// Gets or sets the remaining energy left in the micro.
    /// 0.0 = empty, 1.0 = full.
    /// </summary>
    public float Energy
    {
        get => Base.RemainingEnergy;
        set
        {
            byte previous = Base.EnergyToByte;
            Base.RemainingEnergy = Mathf.Clamp01(value);
            if (previous != Base.EnergyToByte)
            {
                Base.ServerSendStatus(HidStatusMessageType.EnergySync, Base.EnergyToByte);
            }
        }
    }

    /// <summary>
    /// Gets or sets the current <see cref="HidState"/> of the micro.
    /// </summary>
    /// <remarks>
    /// Official LabAPI exposes <c>MicroHidPhase</c>; the Carl Mod equivalent is <see cref="HidState"/>.
    /// Setting the state restarts the state timer and synchronizes it to clients.
    /// </remarks>
    public HidState Phase
    {
        get => Base.State;
        set
        {
            if (Base.State == value)
            {
                return;
            }

            Base.State = value;
            Base._stopwatch.Restart();
            Base.ServerSendStatus(HidStatusMessageType.State, (byte)value);
        }
    }

    /// <summary>
    /// The progress from 0 to 1 for how ready the micro is to fire.
    /// Goes up when winding up.
    /// </summary>
    public float WindUpProgress => Base.Readiness;

    /// <summary>
    /// Time in seconds that the current phase has been active.
    /// </summary>
    public float PhaseElapsed => (float)Base._stopwatch.Elapsed.TotalSeconds;

    /// <summary>
    /// Gets whether the primary fire is being held by the <see cref="Item.CurrentOwner"/>.
    /// </summary>
    public bool IsPrimaryHeld => Base.UserInput == HidUserInput.Fire;

    /// <summary>
    /// Gets whether the secondary fire (priming) is being held by the <see cref="Item.CurrentOwner"/>.
    /// </summary>
    public bool IsSecondaryHeld => Base.UserInput == HidUserInput.Prime;

    /// <summary>
    /// An internal method to remove itself from the cache when the base object is destroyed.
    /// </summary>
    internal override void OnRemove()
    {
        base.OnRemove();
        Dictionary.Remove(Base);
    }
}
