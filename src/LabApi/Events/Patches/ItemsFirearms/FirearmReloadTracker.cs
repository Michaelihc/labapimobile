using InventorySystem.Items.Firearms;
using InventorySystem.Items.Firearms.Modules;
using System.Collections.Generic;

namespace LabApi.Events.Patches.ItemsFirearms;

/// <summary>
/// Tracks which server-approved reload or unload is running on each firearm.
/// </summary>
/// <remarks>
/// The fork's ammo managers only expose a shared busy state. Official LabAPI distinguishes reloading from
/// unloading (<c>IsReloading</c> / <c>IsUnloading</c>) and raises Reloaded / Unloaded when the action stops,
/// so the approved action is recorded here and cleared when the ammo manager returns to standby.
/// </remarks>
internal static class FirearmReloadTracker
{
    /// <summary>
    /// No reload or unload in progress.
    /// </summary>
    internal const byte None = 0;

    /// <summary>
    /// A reload is in progress.
    /// </summary>
    internal const byte Reloading = 1;

    /// <summary>
    /// An unload is in progress.
    /// </summary>
    internal const byte Unloading = 2;

    private const int PurgeThreshold = 64;

    /// <summary>
    /// The current action per firearm instance.
    /// </summary>
    internal static readonly Dictionary<Firearm, byte> States = [];

    private static readonly List<Firearm> PurgeBuffer = [];

    /// <summary>
    /// Gets the tracked action of a firearm.
    /// </summary>
    /// <param name="firearm">The firearm.</param>
    /// <returns>The tracked action, or <see cref="None"/>.</returns>
    internal static byte Get(Firearm firearm)
    {
        return States.Count != 0 && States.TryGetValue(firearm, out byte state) ? state : None;
    }

    /// <summary>
    /// Records an approved action unless one is already running (the fork treats a second request as a cancel).
    /// </summary>
    /// <param name="firearm">The firearm.</param>
    /// <param name="state">The action that started.</param>
    internal static void Begin(Firearm firearm, byte state)
    {
        if (firearm == null || States.ContainsKey(firearm))
        {
            return;
        }

        if (States.Count >= PurgeThreshold)
        {
            PurgeDestroyed();
        }

        States[firearm] = state;
    }

    /// <summary>
    /// Forgets a firearm without raising events.
    /// </summary>
    /// <param name="firearm">The firearm.</param>
    internal static void Forget(Firearm firearm)
    {
        if (States.Count != 0)
        {
            States.Remove(firearm);
        }
    }

    /// <summary>
    /// Gets the firearm that owns an ammo manager module.
    /// </summary>
    /// <param name="module">The ammo manager.</param>
    /// <returns>The owning firearm, or <see langword="null"/> for unsupported managers.</returns>
    internal static Firearm? GetFirearm(IAmmoManagerModule module)
    {
        return module switch
        {
            AutomaticAmmoManager automatic => automatic._firearm,
            ClipLoadedInternalMagAmmoManager clipLoaded => clipLoaded._firearm,
            TubularMagazineAmmoManager tubular => tubular._firearm,
            _ => null,
        };
    }

    private static void PurgeDestroyed()
    {
        foreach (KeyValuePair<Firearm, byte> pair in States)
        {
            if (pair.Key == null)
            {
                PurgeBuffer.Add(pair.Key!);
            }
        }

        foreach (Firearm firearm in PurgeBuffer)
        {
            States.Remove(firearm);
        }

        PurgeBuffer.Clear();
    }
}
