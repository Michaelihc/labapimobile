using InventorySystem.Items.Firearms;
using InventorySystem.Items.Firearms.Modules;
using LabApi.Features.Console;
using MEC;

namespace LabApi.Features.Wrappers;

/// <summary>
/// Wrapper for shotgun firearm.
/// </summary>
/// <remarks>
/// The Carl Mod shotgun counts the rounds in its barrels inside the firearm's single ammo count.
/// <see cref="FirearmItem.StoredAmmo"/> is the tube and <see cref="ChamberedAmmo"/> the barrels.
/// </remarks>
public class ShotgunFirearm : FirearmItem
{
    /// <summary>
    /// An internal constructor to prevent external instantiation.
    /// </summary>
    /// <param name="firearm">The base <see cref="Firearm"/> object.</param>
    internal ShotgunFirearm(Firearm firearm)
        : base(firearm)
    {
    }

    /// <inheritdoc/>
    public override bool OpenBolt => false;

    /// <summary>
    /// Gets whether all of the hammers are cocked.
    /// Sets the cocked status of ALL hammers.
    /// </summary>
    public override bool Cocked
    {
        get
        {
            if (ActionModule is PumpAction actionModule)
            {
                return actionModule.CockedHammers == actionModule._chambersNumber;
            }

            return false;
        }

        set
        {
            if (ActionModule is not PumpAction actionModule)
            {
                Logger.Error($"Unable to set {nameof(Cocked)} as it is invalid.");
                return;
            }

            actionModule.CockedHammers = value ? actionModule._chambersNumber : 0;
            actionModule.ServerResync();
        }
    }

    /// <summary>
    /// Gets or sets the amount of currently cocked chambers.
    /// </summary>
    public int CockedChambers
    {
        get
        {
            if (ActionModule is PumpAction actionModule)
            {
                return actionModule.CockedHammers;
            }

            return 0;
        }

        set
        {
            if (ActionModule is not PumpAction actionModule)
            {
                Logger.Error($"Unable to set {nameof(CockedChambers)} as it is invalid.");
                return;
            }

            actionModule.CockedHammers = value;
            actionModule.ServerResync();
        }
    }

    /// <summary>
    /// Gets the amount of barrels this shotgun has.
    /// </summary>
    /// <remarks>
    /// Read-only: the fork fixes the barrel count when the firearm's modules are created.
    /// </remarks>
    public override int ChamberMax
    {
        get
        {
            if (ActionModule is PumpAction actionModule)
            {
                return actionModule._chambersNumber;
            }

            return 0;
        }
    }

    /// <summary>
    /// Gets or sets the current ammo in the barrels.
    /// </summary>
    /// <remarks>
    /// Setting this value keeps <see cref="FirearmItem.StoredAmmo"/> (the tube) unchanged.
    /// </remarks>
    public override int ChamberedAmmo
    {
        get
        {
            if (ActionModule is PumpAction actionModule)
            {
                return actionModule.ChamberedRounds;
            }

            return 0;
        }

        set
        {
            if (ActionModule is not PumpAction actionModule)
            {
                Logger.Error($"Unable to set {nameof(ChamberedAmmo)} as it is null.");
                return;
            }

            int stored = StoredAmmo;
            int chambered = value < 0 ? 0 : value;
            actionModule.ChamberedRounds = chambered;
            SetTotalAmmo(stored + chambered);
            actionModule.ServerResync();
        }
    }

    /// <summary>
    /// Schedules pumping for this firearm.<br/>
    /// Value of 0 pumps the firearm instantly. Any value above 0 delays the pump by <paramref name="shotsFired"/> * 0.5 second.
    /// </summary>
    /// <remarks>
    /// The fork pump returns live rounds in the barrels to the owner's inventory and chambers new ones from the tube.
    /// The owner is resynchronised; clients play no pump animation.
    /// </remarks>
    /// <param name="shotsFired">The amount of shots that has been fired. Pumping is delayed by <paramref name="shotsFired"/> * 0.5 second.</param>
    public void Pump(int shotsFired = 0)
    {
        if (ActionModule is not PumpAction actionModule)
        {
            Logger.Error($"Unable to pump {nameof(PumpAction)} as it is null.");
            return;
        }

        if (shotsFired <= 0)
        {
            actionModule.Pump(true);
            return;
        }

        Timing.CallDelayed(shotsFired * 0.5f, () =>
        {
            if (!IsDestroyed && Base.ActionModule == actionModule)
            {
                actionModule.Pump(true);
            }
        });
    }
}
