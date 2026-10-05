using Generators;
using MapGeneration;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;
using Utils.NonAllocLINQ;

namespace LabApi.Features.Wrappers;

/// <summary>
/// The wrapper representing <see cref="TeslaGate">tesla gate</see>.
/// </summary>
public class Tesla
{
    /// <summary>
    /// Contains all the cached tesla gates, accessible through their <see cref="TeslaGate"/>.
    /// </summary>
    public static Dictionary<TeslaGate, Tesla> Dictionary { get; } = [];

    /// <summary>
    /// A reference to all instances of <see cref="Tesla"/>.
    /// </summary>
    public static IReadOnlyCollection<Tesla> List => Dictionary.Values;

    /// <summary>
    /// Gets the tesla wrapper from the <see cref="Dictionary"/> or creates a new one if it doesn't exist.
    /// </summary>
    /// <param name="teslaGate">The <see cref="TeslaGate"/> of the tesla.</param>
    /// <returns>The requested tesla.</returns>
    public static Tesla Get(TeslaGate teslaGate) => Dictionary.TryGetValue(teslaGate, out Tesla tesla) ? tesla : new Tesla(teslaGate);

    /// <summary>
    /// Gets the tesla wrapper inside of <see cref="Room"/> from the <see cref="TeslaByRoom"/>.
    /// </summary>
    /// <param name="room">The <see cref="Room"/> with the tesla.</param>
    /// <param name="tesla">The tesla to be returned.</param>
    /// <returns>Whether the tesla is in out parameter.</returns>
    public static bool TryGet(Room room, [NotNullWhen(true)] out Tesla? tesla)
    {
        // Carl Mod never assigns TeslaGate.Room, so the few gates are scanned by position instead of cached by room.
        foreach (Tesla candidate in Dictionary.Values)
        {
            if (candidate.Base != null && candidate.Room == room)
            {
                tesla = candidate;
                return true;
            }
        }

        tesla = null;
        return false;
    }

    /// <summary>
    /// Initializes the <see cref="Tesla"/> class to subscribe to <see cref="TeslaGate"/> events and handle the tesla caching.
    /// </summary>
    [InitializeWrapper]
    internal static void Initialize()
    {
        // Carl Mod has no TeslaGate.OnAdded/OnRemoved; the lifecycle patches call OnAdded/OnRemoved.
        Dictionary.Clear();
    }

    /// <summary>
    /// Called by the lifecycle patch when a tesla gate starts.
    /// </summary>
    /// <param name="tesla">The started gate.</param>
    internal static void OnAdded(TeslaGate tesla)
    {
        if (!Dictionary.ContainsKey(tesla))
        {
            _ = new Tesla(tesla);
        }
    }

    /// <summary>
    /// Called by the lifecycle patch when a tesla gate is destroyed.
    /// </summary>
    /// <param name="tesla">The destroyed gate.</param>
    internal static void OnRemoved(TeslaGate tesla) => Dictionary.Remove(tesla);

    /// <summary>
    /// A private constructor to prevent external instantiation.
    /// </summary>
    /// <param name="tesla">The <see cref="TeslaGate"/> of the item.</param>
    private Tesla(TeslaGate tesla)
    {
        Dictionary.Add(tesla, this);
        Base = tesla;
    }

    /// <summary>
    /// The base of the tesla.
    /// </summary>
    public TeslaGate Base { get; }

    /// <summary>
    /// Gets tesla gate's position.
    /// </summary>
    public Vector3 Position => Base.Position;

    /// <summary>
    /// Gets tesla gate's rotation.
    /// </summary>
    public Quaternion Rotation => Base.transform.rotation;

    /// <summary>
    /// Gets or sets the inactive time of tesla gate.
    /// <para>
    /// Inactive time is in seconds and is automatically decreased over time.
    /// Any value greater than 0 will result in tesla gate not activating.
    /// </para>
    /// </summary>
    public float InactiveTime
    {
        get => Base.NetworkInactiveTime;
        set
        {
            Base.NetworkInactiveTime = value;

            if (value > 0f)
            {
                Base.ServerSideIdle(false);
            }
        }
    }

    /// <summary>
    /// Gets the room the tesla gate is in.
    /// </summary>
    /// <remarks>
    /// Carl Mod leaves <see cref="TeslaGate.Room"/> unset, so the room is looked up from the gate position.
    /// </remarks>
    public Room? Room => Base.Room != null ? Room.Get(Base.Room) : Room.GetRoomAtPosition(Position);

    /// <summary>
    /// Returns if <see cref="Player"/> is in range where tesla gate starts idling.
    /// </summary>
    /// <param name="player">The player to check on.</param>
    /// <returns>Whether the player is in idle range.</returns>
    public bool IsPlayerInIdleRange(Player player) => Base.PlayerInIdleRange(player.ReferenceHub);

    /// <summary>
    /// Returns if <see cref="Player"/> is in range where tesla gate starts to burst.
    /// </summary>
    /// <param name="player">The player to check on.</param>
    /// <returns>Whether the player is within activation range.</returns>
    public bool IsPlayerInRange(Player player) => Base.PlayerInRange(player.ReferenceHub);

    /// <summary>
    /// Returns if any <see cref="Player"/> is in range where tesla gate starts idling.
    /// </summary>
    /// <returns>Whether any player is within the idle range.</returns>
    public bool IsAnyPlayerInIdleRange() => ReferenceHub.AllHubs.Any(Base.PlayerInIdleRange);

    /// <summary>
    /// Returns if any <see cref="Player"/> is in range where tesla gate starts to burst.
    /// </summary>
    /// <returns>Whether any player is within activation range.</returns>
    public bool IsAnyPlayerInRange() => ReferenceHub.AllHubs.Any(Base.PlayerInRange);

    /// <summary>
    /// Tesla gate electric burst. Will not do anything if burst is being active.
    /// </summary>
    public void Trigger() => Base.ServerSideCode();

    /// <summary>
    /// Tesla gate instant burst.
    /// </summary>
    public void InstantTrigger() => Base.RpcInstantBurst();
}