using Generators;
using Interactables.Interobjects;
using Interactables.Interobjects.DoorUtils;
using LabApi.Events.Patches.Facility;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using BaseElevatorDoor = Interactables.Interobjects.ElevatorDoor;
using ElevatorGroup = Interactables.Interobjects.ElevatorManager.ElevatorGroup;

namespace LabApi.Features.Wrappers;

/// <summary>
/// The wrapper representing <see cref="ElevatorChamber">elevators</see>, the in-game elevators.
/// </summary>
/// <remarks>
/// Carl Mod elevators are local <see cref="ElevatorChamber"/> simulations driven by <see cref="ElevatorManager"/> sync messages;
/// groups are <see cref="ElevatorManager.ElevatorGroup"/> and destinations are changed through <see cref="ElevatorManager.TrySetDestination"/>.
/// </remarks>
public class Elevator
{
    /// <summary>
    /// Contains all the cached <see cref="ElevatorChamber">elevators</see> in the game, accessible through their <see cref="ElevatorChamber"/>.
    /// </summary>
    public static Dictionary<ElevatorChamber, Elevator> Dictionary { get; } = [];

    /// <summary>
    /// A reference to all <see cref="Elevator"/> instances currently in the game.
    /// </summary>
    public static IReadOnlyCollection<Elevator> List => Dictionary.Values;

    /// <summary>
    /// Locks every door of every elevator on map.
    /// </summary>
    public static void LockAll()
    {
        foreach (Elevator el in List)
        {
            el.LockAllDoors();
        }
    }

    /// <summary>
    /// Unlocks every door of every elevator on map.
    /// </summary>
    public static void UnlockAll()
    {
        foreach (Elevator el in List)
        {
            el.UnlockAllDoors();
        }
    }

    /// <summary>
    /// Gets the elevator wrapper from the <see cref="Dictionary"/>, or creates a new one if it doesn't exist.
    /// </summary>
    /// <param name="elevatorChamber">The <see cref="ElevatorChamber"/> of the elevator.</param>
    /// <returns>The requested elevator.</returns>
    public static Elevator Get(ElevatorChamber elevatorChamber) =>
        Dictionary.TryGetValue(elevatorChamber, out Elevator generator) ? generator : new Elevator(elevatorChamber);

    /// <summary>
    /// Gets the enumerable of elevators that are assigned to the specific group.
    /// </summary>
    /// <param name="group">The specified elevator group.</param>
    /// <returns>Enumerable where the group is equal to the one specified.</returns>
    public static IEnumerable<Elevator> GetByGroup(ElevatorGroup group) => List.Where(n => n.Group == group);

    /// <summary>
    /// Initializes the <see cref="Elevator"/> class to subscribe to <see cref="ElevatorChamber"/> events.
    /// </summary>
    [InitializeWrapper]
    internal static void Initialize()
    {
        Dictionary.Clear();
    }

    /// <summary>
    /// Called by the lifecycle patch when a chamber awakes (Carl Mod has no <c>ElevatorChamber.OnElevatorSpawned</c>).
    /// </summary>
    /// <param name="chamber">The spawned chamber.</param>
    internal static void OnAdded(ElevatorChamber chamber)
    {
        if (!Dictionary.ContainsKey(chamber))
        {
            _ = new Elevator(chamber);
        }
    }

    /// <summary>
    /// Called by the lifecycle patch when a chamber is destroyed.
    /// </summary>
    /// <param name="chamber">The destroyed chamber.</param>
    internal static void OnRemoved(ElevatorChamber chamber) => Dictionary.Remove(chamber);

    /// <summary>
    /// A private constructor to prevent external instantiation.
    /// </summary>
    /// <param name="elevator">The <see cref="ElevatorChamber"/> of the elevator.</param>
    private Elevator(ElevatorChamber elevator)
    {
        Dictionary.Add(elevator, this);
        Base = elevator;
    }

    /// <summary>
    /// The base <see cref="ElevatorChamber"/> object.
    /// </summary>
    public ElevatorChamber Base { get; }

    /// <summary>
    /// Gets all the doors this elevator can travel to, ordered from the lowest to the highest floor.
    /// </summary>
    public IEnumerable<ElevatorDoor> Doors => BaseElevatorDoor.AllElevatorDoors.TryGetValue(Group, out List<BaseElevatorDoor> doors) ? doors.Select(static x => ElevatorDoor.Get(x)!) : [];

    /// <summary>
    /// Gets all the rooms this elevator can travel to.
    /// </summary>
    public IEnumerable<Room> Rooms => Doors.SelectMany(static x => x.Rooms);

    /// <summary>
    /// Gets the current destination / location of the elevator.
    /// </summary>
    public ElevatorDoor CurrentDestination => ElevatorDoor.Get(Base.CurrentDestination)!;

    /// <summary>
    /// Gets the index of the destination floor.
    /// </summary>
    public int CurrentDestinationLevel => Base.CurrentLevel;

    /// <summary>
    /// Gets the next destination of the elevator.
    /// </summary>
    public ElevatorDoor? NextDestination
    {
        get
        {
            if (!BaseElevatorDoor.AllElevatorDoors.TryGetValue(Group, out List<BaseElevatorDoor> doors) || doors.Count == 0)
            {
                return null;
            }

            return ElevatorDoor.Get(doors[NextDestinationLevel]);
        }
    }

    /// <summary>
    /// Gets the next level index of the elevator, wrapping back to the lowest floor like the in-game panel.
    /// </summary>
    public int NextDestinationLevel
    {
        get
        {
            int next = Base.CurrentLevel + 1;
            return BaseElevatorDoor.AllElevatorDoors.TryGetValue(Group, out List<BaseElevatorDoor> doors) && next < doors.Count ? next : 0;
        }
    }

    /// <summary>
    /// Gets whether the elevator is ready for a new destination.
    /// </summary>
    public bool IsReady => Base.IsReady;

    /// <summary>
    /// Gets whether the elevator is going up.
    /// </summary>
    public bool GoingUp => Base._goingUp;

    /// <summary>
    /// Gets or sets the <see cref="ElevatorManager.ElevatorGroup"/> of this elevator.
    /// </summary>
    public ElevatorGroup Group
    {
        get => Base.AssignedGroup;
        set => Base.AssignedGroup = value;
    }

    /// <summary>
    /// Gets the current <see cref="ElevatorChamber.ElevatorSequence"/> of the elevator.
    /// </summary>
    public ElevatorChamber.ElevatorSequence CurrentSequence => Base._curSequence;

    /// <summary>
    /// Gets the world space bounds of this elevator.
    /// </summary>
    public Bounds WorldSpaceBounds => Base.WorldspaceBounds;

    /// <summary>
    /// Gets the reason why is ANY of the elevator doors locked.
    /// </summary>
    public DoorLockReason AnyDoorLockedReason => Base.ActiveLocks;

    /// <summary>
    /// Gets the reason why are ALL the elevator doors locked.
    /// </summary>
    public DoorLockReason AllDoorsLockedReason
    {
        get
        {
            if (!BaseElevatorDoor.AllElevatorDoors.TryGetValue(Group, out List<BaseElevatorDoor> doors) || doors.Count == 0)
            {
                return DoorLockReason.None;
            }

            ushort locks = ushort.MaxValue;
            foreach (BaseElevatorDoor door in doors)
            {
                locks &= door.ActiveLocks;
            }

            return (DoorLockReason)locks;
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"[Elevator: Group={Group}, IsReady={IsReady}, GoingUp={GoingUp}, CurrentSequence={CurrentSequence}]";
    }

    /// <summary>
    /// Sends the elevator to specified destination.
    /// </summary>
    /// <param name="targetLevel">The target floor index.</param>
    /// <param name="force">Whether to move even if the elevator is moving or already at the level.</param>
    public void SetDestination(int targetLevel, bool force = false) => ElevatorManager.TrySetDestination(Group, targetLevel, force);

    /// <summary>
    /// Interacts with the elevator as the specified player, raising the elevator interaction events.
    /// </summary>
    /// <param name="player">The player who interacted.</param>
    public void Interact(Player player) => ElevatorInteractPatch.ServerInteract(player.ReferenceHub, Base, null, NextDestinationLevel);

    /// <summary>
    /// Sends the elevator to the next floor.
    /// </summary>
    public void SendToNextFloor() => SetDestination(NextDestinationLevel, false);

    /// <summary>
    /// Locks every door of this elevator.
    /// </summary>
    public void LockAllDoors() => SetAdminLock(true);

    /// <summary>
    /// Unlocks every door of this elevator.
    /// </summary>
    public void UnlockAllDoors() => SetAdminLock(false);

    private void SetAdminLock(bool state)
    {
        if (!BaseElevatorDoor.AllElevatorDoors.TryGetValue(Group, out List<BaseElevatorDoor> doors))
        {
            return;
        }

        foreach (BaseElevatorDoor door in doors)
        {
            door.ServerChangeLock(DoorLockReason.AdminCommand, state);
        }
    }
}
