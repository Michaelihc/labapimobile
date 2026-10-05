using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;
using BaseMicroHIDPickup = InventorySystem.Items.MicroHID.MicroHIDPickup;

namespace LabApi.Features.Wrappers;

/// <summary>
/// Wrapper for the <see cref="BaseMicroHIDPickup"/> class.
/// </summary>
/// <remarks>
/// The Carl Mod Micro-HID pickup only stores its energy; it has no cycle state or firing mode.
/// </remarks>
public class MicroHIDPickup : Pickup
{
    /// <summary>
    /// Contains all the cached micro hid pickups, accessible through their <see cref="BaseMicroHIDPickup"/>.
    /// </summary>
    public static new Dictionary<BaseMicroHIDPickup, MicroHIDPickup> Dictionary { get; } = [];

    /// <summary>
    /// A reference to all instances of <see cref="MicroHIDPickup"/>.
    /// </summary>
    public static new IReadOnlyCollection<MicroHIDPickup> List => Dictionary.Values;

    /// <summary>
    /// Gets the micro hid pickup from the <see cref="Dictionary"/> or creates a new if it doesn't exist and the provided <see cref="BaseMicroHIDPickup"/> was not null.
    /// </summary>
    /// <param name="pickup">The <see cref="Base"/> of the pickup.</param>
    /// <returns>The requested pickup or null.</returns>
    [return: NotNullIfNotNull(nameof(pickup))]
    public static MicroHIDPickup? Get(BaseMicroHIDPickup? pickup)
    {
        if (pickup == null)
        {
            return null;
        }

        return Dictionary.TryGetValue(pickup, out MicroHIDPickup wrapper) ? wrapper : (MicroHIDPickup)CreateItemWrapper(pickup);
    }

    /// <summary>
    /// An internal constructor to prevent external instantiation.
    /// </summary>
    /// <param name="baseMicroHIDPickup">The base <see cref="BaseMicroHIDPickup"/> object.</param>
    internal MicroHIDPickup(BaseMicroHIDPickup baseMicroHIDPickup)
        : base(baseMicroHIDPickup)
    {
        Base = baseMicroHIDPickup;

        if (CanCache)
        {
            Dictionary.Add(baseMicroHIDPickup, this);
        }
    }

    /// <summary>
    /// The base <see cref="BaseMicroHIDPickup"/> object.
    /// </summary>
    public new BaseMicroHIDPickup Base { get; }

    /// <summary>
    /// Gets or sets the energy of the micro hid pickup.
    /// 0.0 = empty, 1.0 = full.
    /// </summary>
    public float Energy
    {
        get => Base.Energy;
        set => Base.NetworkEnergy = Mathf.Clamp01(value);
    }

    /// <summary>
    /// An internal method to remove itself from the cache when the base object is destroyed.
    /// </summary>
    internal override void OnRemove()
    {
        base.OnRemove();
        Dictionary.Remove(Base);
    }
}
