using Interactables.Interobjects;
using Interactables.Interobjects.DoorUtils;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using BaseCheckpointDoor = Interactables.Interobjects.CheckpointDoor;
using BaseBreakableDoor = Interactables.Interobjects.BreakableDoor;
using LabApi.Events.Patches.Facility;

namespace LabApi.Features.Wrappers;

/// <summary>
/// The wrapper representing the <see cref="BaseCheckpointDoor"/>.
/// </summary>
public class CheckpointDoor : Door
{
    /// <summary>
    /// Contains all the cached <see cref="CheckpointDoor"/> instances, accessible through their <see cref="BaseCheckpointDoor"/>.
    /// </summary>
    public static new Dictionary<BaseCheckpointDoor, CheckpointDoor> Dictionary { get; } = [];

    /// <summary>
    /// A reference to all <see cref="CheckpointDoor"/> instances currently in the game.
    /// </summary>
    public static new IReadOnlyCollection<CheckpointDoor> List => Dictionary.Values;

    /// <summary>
    /// Gets the <see cref="CheckpointDoor"/> wrapper from the <see cref="Dictionary"/>, or creates a new one if it doesn't exist.
    /// </summary>
    /// <param name="baseCheckpointDoor">The <see cref="BaseCheckpointDoor"/> of the door.</param>
    /// <returns>The requested door wrapper or null if the input was null.</returns>
    [return: NotNullIfNotNull(nameof(baseCheckpointDoor))]
    public static CheckpointDoor? Get(BaseCheckpointDoor? baseCheckpointDoor)
    {
        if (baseCheckpointDoor == null)
        {
            return null;
        }

        if (Dictionary.TryGetValue(baseCheckpointDoor, out CheckpointDoor door))
        {
            return door;
        }

        return (CheckpointDoor)CreateDoorWrapper(baseCheckpointDoor);
    }

    /// <summary>
    /// An internal constructor to prevent external instantiation.
    /// </summary>
    /// <param name="baseCheckpointDoor">The base <see cref="BaseCheckpointDoor"/> object.</param>
    internal CheckpointDoor(BaseCheckpointDoor baseCheckpointDoor)
        : base(baseCheckpointDoor)
    {
        Base = baseCheckpointDoor;
        SubDoors = new Door[baseCheckpointDoor.SubDoors.Length];

        for (int i = 0; i < baseCheckpointDoor.SubDoors.Length; i++)
        {
            SubDoors[i] = Get(baseCheckpointDoor.SubDoors[i]);
        }

        if (CanCache)
        {
            Dictionary.Add(baseCheckpointDoor, this);
        }
    }

    /// <summary>
    /// The base <see cref="BaseCheckpointDoor"/> object.
    /// </summary>
    public new BaseCheckpointDoor Base { get; }

    /// <summary>
    /// All <see cref="Door"/> instances operated by this checkpoint.
    /// </summary>
    public Door[] SubDoors { get; }

    /// <summary>
    /// Gets or sets whether all the sub doors are open.
    /// </summary>
    public bool IsSubOpened
    {
        get => SubDoors.All(x => x.IsOpened);
        set => Base.ToggleAllDoors(value);
    }

    /// <summary>
    /// Gets or sets whether the doors are broken.
    /// </summary>
    /// <remarks>
    /// Carl Mod doors can not be unbroken; setting <see langword="false"/> does nothing.
    /// </remarks>
    public bool IsBroken
    {
        get => Base.IsDestroyed;
        set
        {
            if (value)
            {
                TryBreak();
            }
        }
    }

    /// <summary>
    /// Gets or sets the max health on damageable sub doors used when spawning.
    /// </summary>
    public float MaxHealth
    {
        get
        {
            float total = 0f;
            int count = 0;
            foreach (DoorVariant door in Base.SubDoors)
            {
                if (door is BaseBreakableDoor breakable)
                {
                    total += breakable.MaxHealth;
                    count++;
                }
            }

            return count == 0 ? 0f : total / count;
        }

        set
        {
            foreach (DoorVariant door in Base.SubDoors)
            {
                if (door is BaseBreakableDoor breakable)
                {
                    breakable.MaxHealth = value;
                }
            }
        }
    }

    /// <summary>
    /// Gets or sets the remaining health on damageable sub door.
    /// </summary>
    public float Health
    {
        get
        {
            float total = 0f;
            int count = 0;
            foreach (DoorVariant door in Base.SubDoors)
            {
                if (door is BaseBreakableDoor breakable)
                {
                    total += breakable.RemainingHealth;
                    count++;
                }
            }

            return count == 0 ? 0f : total / count;
        }

        set
        {
            foreach (DoorVariant door in Base.SubDoors)
            {
                if (door is BaseBreakableDoor breakable)
                {
                    breakable.RemainingHealth = value;
                }
            }
        }
    }

    /// <summary>
    /// Gets or sets the current <see cref="BaseCheckpointDoor.CheckpointSequenceStage"/> of the checkpoint door.
    /// </summary>
    /// <remarks>
    /// Carl Mod's sequence enum is <see cref="BaseCheckpointDoor.CheckpointSequenceStage"/> (official: <c>SequenceState</c>).
    /// Setting it raises the checkpoint sequence events like the official setter.
    /// </remarks>
    public BaseCheckpointDoor.CheckpointSequenceStage SequenceState
    {
        get => Base._currentSequence;
        set => CheckpointSequenceHelper.SetSequence(Base, value);
    }

    /// <summary>
    /// Gets or sets the time in seconds for which the checkpoint doors are opened when interacted with.
    /// </summary>
    public float OpenTime
    {
        get => Base._waitTime;
        set => Base._waitTime = value;
    }

    /// <summary>
    /// Gets or sets the time in seconds for which the checkpoint alarm is playing the alarm sound.
    /// </summary>
    public float WarningTime
    {
        get => Base._warningTime;
        set => Base._warningTime = value;
    }

    /// <summary>
    /// Gets the health as a percentage from 0 to 1.
    /// </summary>
    public float HealthPercent => Base.GetHealthPercent();

    /// <summary>
    /// Damage all the sub doors by specified amount.
    /// </summary>
    /// <param name="damage">The amount of damage to apply.</param>
    /// <param name="type">The <see cref="DoorDamageType"/> to apply.</param>
    /// <returns>True if the doors took damage, otherwise false.</returns>
    public bool TryDamage(float damage, DoorDamageType type = DoorDamageType.ServerCommand)
        => Base.ServerDamage(damage, type);

    /// <summary>
    /// Break all the sub doors.
    /// </summary>
    /// <param name="type">The <see cref="DoorDamageType"/> to apply.</param>
    /// <returns>True if the doors took damage, otherwise false.</returns>
    public bool TryBreak(DoorDamageType type = DoorDamageType.ServerCommand)
        => TryDamage(float.MaxValue, type);

    /// <summary>
    /// An internal method to remove itself from the cache when the base object is destroyed.
    /// </summary>
    internal override void OnRemove()
    {
        base.OnRemove();
        Dictionary.Remove(Base);
    }
}
