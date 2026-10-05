using CustomPlayerEffects;
using InventorySystem;
using InventorySystem.Items;
using MapGeneration;
using PlayerRoles.FirstPersonControl;
using PlayerRoles.PlayableScps.Scp106;
using PlayerStatsSystem;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace LabApi.Features.Wrappers;

/// <summary>
/// The wrapper over the <see cref="RoomIdentifier"/> that represents the pocket dimension.
/// </summary>
public class PocketDimension : Room
{
    /// <summary>
    /// Gets the current <see cref="PocketDimension"/> instance.
    /// </summary>
    /// <remarks>
    /// May be null if the map has not been generated yet or was previously destroyed.
    /// </remarks>
    public static PocketDimension? Instance { get; private set; }

    /// <summary>
    /// A reference to all <see cref="PocketTeleport"/> instances currently in the game.
    /// </summary>
    public static IReadOnlyCollection<PocketTeleport> PocketTeleports => PocketTeleport.List;

    /// <summary>
    /// Gets all items pickup in the pocket dimension by their associated <see cref="PocketItem"/> instances.
    /// </summary>
    public static IEnumerable<PocketItem> PocketItems => PocketItem.List;

    /// <summary>
    /// Gets an array of the recycle chances.
    /// </summary>
    /// <remarks>
    /// Indexing the array by the rarity of the item see <see cref="GetRarity(Item)"/> gives the chance for the item to be dropped from 0.0 to 1.0.
    /// </remarks>
    public static float[] RecycleChances => Scp106PocketItemManager.RecycleChances;

    /// <summary>
    /// Gets or sets the minimum time that an item can remain in the pocket dimension.
    /// </summary>
    public static float MinPocketItemTriggerDelay
    {
        get => Scp106PocketItemManager.TimerRage.x;
    }

    /// <summary>
    /// Gets or sets the maximum time that an item can remain in the pocket dimension.
    /// </summary>
    public static float MaxPocketItemTriggerDelay
    {
        get => Scp106PocketItemManager.TimerRage.y;
    }

    /// <summary>
    /// Force a <see cref="Player"/> inside the pocket dimension.
    /// </summary>
    /// <param name="player">The <see cref="Player"/> to send.</param>
    public static void ForceInside(Player player) => player.EnableEffect<Corroding>();

    /// <summary>
    /// Gets whether a <see cref="Player"/> is considered inside the pocket dimension.
    /// </summary>
    /// <param name="player">The <see cref="Player"/> to check.</param>
    /// <returns>True if inside otherwise false.</returns>
    public static bool IsPlayerInside(Player player) => player.HasEffect<Corroding>();

    /// <summary>
    /// Gets the position at which the <paramref name="player"/> was caught.
    /// </summary>
    /// <param name="player">The player.</param>
    /// <returns>Returns caught position, also returns <see cref="Vector3.zero"/> if the player is not in <see cref="PocketDimension"/>.</returns>
    public static Vector3 GetCaughtPosition(Player player)
    {
        Corroding? effect = player.GetEffect<Corroding>();

        if (effect != null && effect.Intensity > 0)
        {
            return effect.CapturePosition.Position;
        }

        return Vector3.zero;
    }

    /// <summary>
    /// Force a player to exit the pocket dimension.
    /// </summary>
    /// <param name="player">The player inside the pocket dimension.</param>
    /// <remarks>
    /// Player must be inside the pocket dimension, see <see cref="IsPlayerInside(Player)"/>.
    /// Triggers pocket dimension leaving/left events.
    /// </remarks>
    public static void ForceExit(Player player)
    {
        // Carl Mod has no PocketDimensionTeleport.TryExit; this mirrors the exit branch of PocketDimensionTeleport.OnTriggerEnter.
        ReferenceHub hub = player.ReferenceHub;
        if (hub.roleManager.CurrentRole is not IFpcRole fpcRole)
        {
            return;
        }

        fpcRole.FpcModule.ServerOverridePosition(Scp106PocketExitFinder.GetBestExitPosition(fpcRole), Vector3.zero);
        hub.playerEffectsController.EnableEffect<Disabled>(10f, addDuration: true);
        hub.playerEffectsController.DisableEffect<Corroding>();
    }

    /// <summary>
    /// Force a player to be killed by the pocket dimension.
    /// </summary>
    /// <param name="player">The player to kill.</param>
    /// <remarks>
    /// Instantly pocket decays the player.
    /// Triggers pocket dimension leaving/left events.
    /// </remarks>
    public static void ForceKill(Player player)
        => player.ReferenceHub.playerStats.DealDamage(new UniversalDamageHandler(-1f, DeathTranslations.PocketDecay));

    /// <summary>
    /// Gets whether a <see cref="Pickup"/> is inside the pocket dimension.
    /// </summary>
    /// <param name="pickup">The <see cref="Pickup"/> to check.</param>
    /// <returns>True if inside otherwise false.</returns>
    public static bool IsPickupInside(Pickup pickup)
        => Scp106PocketItemManager.TrackedItems.ContainsKey(pickup.Base);

    /// <summary>
    /// Randomizes which pocket dimension's teleports are exits.
    /// </summary>
    public static void RandomizeExits() => ImageGenerator.pocketDimensionGenerator.GenerateRandom();

    /// <summary>
    /// Gets the rarity of the item using its <see cref="Pickup"/> wrapper see <see cref="RecycleChances"/>.
    /// </summary>
    /// <param name="pickup">The <see cref="Pickup"/> to get the rarity from.</param>
    /// <returns>The rarity of the item.</returns>
    public static int GetRarity(Pickup pickup)
        => GetRarity(InventoryItemLoader.AvailableItems[pickup.Type]);

    /// <summary>
    /// Gets the rarity of the item using its <see cref="Item"/> wrapper see <see cref="RecycleChances"/>.
    /// </summary>
    /// <param name="item">The <see cref="Item"/> to get the rarity from.</param>
    /// <returns>The rarity of the item.</returns>
    public static int GetRarity(Item item) => GetRarity(item.Base);

    /// <summary>
    /// Gets the rarity of the item using its <see cref="ItemBase"/> base object see <see cref="RecycleChances"/>.
    /// </summary>
    /// <param name="item">The <see cref="ItemBase"/> to get the rarity from.</param>
    /// <returns>The rarity of the item.</returns>
    public static int GetRarity(ItemBase item)
        => Scp106PocketItemManager.GetRarity(item);

    /// <summary>
    /// An internal constructor to prevent external instantiation.
    /// </summary>
    /// <param name="room">The room identifier for the pocket dimension.</param>
    internal PocketDimension(RoomIdentifier room)
        : base(room)
    {
        if (CanCache)
        {
            Instance = this;
        }
    }

    /// <summary>
    /// An internal method to set the instance to null when the base object is destroyed.
    /// </summary>
    internal override void OnRemoved()
    {
        base.OnRemoved();
        Instance = null;
    }
}
