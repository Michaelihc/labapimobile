using InventorySystem.Items.Firearms;
using InventorySystem.Items.Firearms.Modules;
using LabApi.Features.Console;

namespace LabApi.Features.Wrappers;

/// <summary>
/// Wrapper for revolver firearm.
/// </summary>
/// <remarks>
/// The Carl Mod revolver has no per-chamber cylinder state and no roulette spin: it fires while any round is loaded.
/// </remarks>
public class RevolverFirearm : FirearmItem
{
    /// <summary>
    /// An internal constructor to prevent external instantiation.
    /// </summary>
    /// <param name="firearm">The base <see cref="Firearm"/> object.</param>
    internal RevolverFirearm(Firearm firearm)
        : base(firearm)
    {
    }

    /// <inheritdoc/>
    public override bool OpenBolt => false;

    /// <inheritdoc/>
    public override bool Cocked
    {
        get
        {
            if (ActionModule is DoubleAction actionModule)
            {
                return actionModule.Cocked;
            }

            return false;
        }

        set
        {
            if (ActionModule is not DoubleAction actionModule)
            {
                Logger.Error($"Unable to set {nameof(Cocked)} as the {nameof(DoubleAction)} is null.");
                return;
            }

            actionModule.Cocked = value;
        }
    }

    /// <summary>
    /// Gets or sets the stored ammo in a <b>ammo container</b> for this firearm.
    /// </summary>
    /// <remarks>
    /// Stored ammo in revolver firearm cannot exceed its <see cref="FirearmItem.MaxAmmo"/> and any additional values are ignored.
    /// </remarks>
    public override int StoredAmmo
    {
        get => Base.Status.Ammo;
        set
        {
            int max = MaxAmmo;
            SetTotalAmmo(value < max ? value : max);
        }
    }

    /// <summary>
    /// Gets whether a round is ready to fire: 1 while the cylinder holds any round, otherwise 0.
    /// </summary>
    /// <remarks>
    /// The fork does not track individual chambers, so this value cannot be set.
    /// </remarks>
    public override int ChamberedAmmo
    {
        get => Base.Status.Ammo > 0 ? 1 : 0;
        set => Logger.Error($"Unable to set {nameof(ChamberedAmmo)} as the revolver has no per-chamber state; use {nameof(StoredAmmo)}.");
    }

    /// <inheritdoc/>
    public override int ChamberMax => 1;
}
