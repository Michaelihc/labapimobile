using Generators;
using Interactables.Interobjects.DoorUtils;
using MapGeneration;
using MapGeneration.Distributors;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;
using static MapGeneration.Distributors.Scp079Generator;

namespace LabApi.Features.Wrappers;

/// <summary>
/// The wrapper representing <see cref="Scp079Generator">generators</see>, the in-game generators.
/// </summary>
public class Generator : Structure
{
    /// <summary>
    /// Contains all the cached <see cref="Scp079Generator">generators</see> in the game, accessible through their <see cref="Scp079Generator"/>.
    /// </summary>
    public static new Dictionary<Scp079Generator, Generator> Dictionary { get; } = [];

    /// <summary>
    /// A reference to all <see cref="Generator"/> instances currently in the game.
    /// </summary>
    public static new IReadOnlyCollection<Generator> List => Dictionary.Values;

    /// <summary>
    /// Contains generators in a list by room they are in. Generators that have been spawned without an assigned room are not inside of this collection.
    /// </summary>
    private static Dictionary<Room, List<Generator>> GeneratorsByRoom { get; } = [];

    /// <summary>
    /// Gets the generator wrapper from the <see cref="Dictionary"/>, or creates a new one if it doesn't exist and the provided <see cref="Scp079Generator"/> was not <see langword="null"/>.
    /// </summary>
    /// <param name="scp079Generator">The <see cref="Scp079Generator"/> of the generator.</param>
    /// <returns>The requested wrapper or <see langword="null"/>.</returns>
    [return: NotNullIfNotNull(nameof(scp079Generator))]
    public static Generator? Get(Scp079Generator? scp079Generator)
    {
        if (scp079Generator == null)
        {
            return null;
        }

        return Dictionary.TryGetValue(scp079Generator, out Generator generator) ? generator : (Generator)CreateStructureWrapper(scp079Generator);
    }

    /// <summary>
    /// Gets the generator wrapper from the <see cref="GeneratorsByRoom"/> or returns <see langword="null"/> if specified room does not have any.
    /// </summary>
    /// <param name="room">Target room.</param>
    /// <param name="generators">Generators found.</param>
    /// <returns>Whether the generator was found.</returns>
    public static bool TryGetFromRoom(Room room, [NotNullWhen(true)] out List<Generator>? generators) => GeneratorsByRoom.TryGetValue(room, out generators);

    /// <summary>
    /// Initializes the generators by room caching for map generation.
    /// </summary>
    [InitializeWrapper]
    internal static void InitializeCaching()
    {
        SeedSynchronizer.OnMapGenerated += SeedSynchronizer_OnGenerationFinished;
    }

    private static void SeedSynchronizer_OnGenerationFinished()
    {
        foreach (Generator generator in List)
        {
            generator.TryRegisterByRoom();
        }
    }

    /// <summary>
    /// An internal constructor to prevent external instantiation.
    /// </summary>
    /// <param name="generator">The <see cref="Scp079Generator"/> of the generator.</param>
    internal Generator(Scp079Generator generator)
        : base(generator)
    {
        Base = generator;

        if (CanCache)
        {
            Dictionary.Add(generator, this);
            TryRegisterByRoom();
        }
    }

    /// <summary>
    /// The base object.
    /// </summary>
    public new Scp079Generator Base { get; }

    /// <summary>
    /// Gets or sets the activation time it takes for generator to go from <see cref="TotalActivationTime">maximum time</see> (this value) to 0.
    /// </summary>
    public float TotalActivationTime
    {
        get => Base._totalActivationTime;
        set => Base._totalActivationTime = Mathf.Max(0f, value);
    }

    /// <summary>
    /// Gets or sets the activation time it takes for generator to go from 0 to <see cref="TotalActivationTime">maximum time</see>.
    /// </summary>
    public float TotalDeactivationTime
    {
        get => Base._totalDeactivationTime;
        set => Base._totalDeactivationTime = Mathf.Max(0f, value);
    }

    /// <summary>
    /// Gets or sets the required <see cref="KeycardPermissions"/> to unlock the generator.
    /// </summary>
    public KeycardPermissions RequiredPermissions
    {
        get => Base._requiredPermission;
        set => Base._requiredPermission = value;
    }

    /// <summary>
    /// Gets the dropdown speed at which is generator countdown going back up to the maximum value.
    /// </summary>
    public float DropdownSpeed => Base.DropdownSpeed;

    /// <summary>
    /// Gets whether the generation is ready to be activated.
    /// </summary>
    public bool ActivationReady => Base.ActivationReady;

    /// <summary>
    /// Gets or sets whether the generator is opened.
    /// </summary>
    public bool IsOpen
    {
        get => Base.IsOpen;
        set => Base.IsOpen = value;
    }

    /// <summary>
    /// Gets or sets whether the generator is unlocked.
    /// </summary>
    public bool IsUnlocked
    {
        get => Base.IsUnlocked;
        set => Base.IsUnlocked = value;
    }

    /// <summary>
    /// Gets the time it takes the generator to be activated (lever pulled).
    /// </summary>
    public float ActivationTime => Base._leverDelay;

    /// <summary>
    /// Gets or sets whether the generator is engaged.
    /// </summary>
    public bool Engaged
    {
        get => Base.Engaged;
        set => Base.Engaged = value;
    }

    /// <summary>
    /// Gets or sets whether the generator is activating.
    /// </summary>
    public bool Activating
    {
        get => Base.Activating;
        set => Base.Activating = value;
    }

    /// <summary>
    /// Gets or sets the remaining amount of seconds till activation.
    /// </summary>
    public short RemainingTime
    {
        get => Base._syncTime;
        set
        {
            Base._currentTime = Base._totalActivationTime - value;
            Base.Network_syncTime = value;
        }
    }

    /// <summary>
    /// Runs the interaction of specified <see cref="Player"/> on specified <see cref="GeneratorColliderId"/> collider.
    /// </summary>
    /// <param name="player">The player to trigger the interaction.</param>
    /// <param name="collider">The <see cref="GeneratorColliderId"/> triggered.</param>
    public void ServerInteract(Player player, GeneratorColliderId collider) => Base.ServerInteract(player.ReferenceHub, (byte)collider);

    /// <summary>
    /// Plays the denied sound cue on the client.
    /// </summary>
    /// <remarks>
    /// Carl Mod's denied RPC carries no permission flags, so the official permission parameter is absent.
    /// </remarks>
    public void PlayerDeniedBeep() => Base.RpcDenied();

    /// <summary>
    /// An internal method remove itself from the cache when the base object is destroyed.
    /// </summary>
    internal override void OnRemove()
    {
        base.OnRemove();

        if (Base.Room == null)
        {
            Dictionary.Remove(Base);
            return;
        }

        Room? room = Room.Get(Base.Room);

        if (room == null) // Room is null after round restart, try find it by iterating over the existing dictionary
        {
            Room? potentialRoom = null;
            foreach (var kvp in GeneratorsByRoom)
            {
                if (kvp.Value.Contains(this))
                {
                    potentialRoom = kvp.Key;
                    break;
                }
            }

            room = potentialRoom;
        }

        if (room != null && GeneratorsByRoom.TryGetValue(room, out List<Generator> list))
        {
            list.Remove(this);

            if (list.Count == 0)
            {
                GeneratorsByRoom.Remove(room);
            }
        }

        Dictionary.Remove(Base);
    }

    private void TryRegisterByRoom()
    {
        foreach (var kvp in GeneratorsByRoom)
        {
            if (kvp.Value.Contains(this))
            {
                return;
            }
        }

        // Carl Mod assigns Scp079Generator.Room in Start; fall back to the position lookup before that.
        Room? room = Base.Room != null ? Room.Get(Base.Room) : Room.GetRoomAtPosition(Position);
        if (room == null)
        {
            return;
        }

        if (!GeneratorsByRoom.TryGetValue(room, out List<Generator> list))
        {
            list = [];
            GeneratorsByRoom.Add(room, list);
        }

        list.Add(this);
    }
}