using Generators;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using MapGeneration;
using Mirror;
using PlayerRoles;
using RoundRestarting;
using System;
using PlayerWrapper = LabApi.Features.Wrappers.Player;

namespace LabApi.Events.Patches.Players;

/// <summary>
/// Raises <see cref="PlayerEvents.RoomChanged"/> and <see cref="PlayerEvents.ZoneChanged"/>.
/// </summary>
/// <remarks>
/// Official SL raises both from the per-player <c>CurrentRoomPlayerCache</c> component, which Carl Mod lacks.
/// This tracker revalidates each player's room once per frame while either event has subscribers and does nothing otherwise.
/// When the first subscriber appears, the current rooms are recorded without raising events.
/// </remarks>
// Official: MapGeneration/CurrentRoomPlayerCache.cs ValidateCache
internal static class PlayerRoomTracker
{
    private static bool _tracking;

    [InitializeWrapper]
    internal static void Initialize()
    {
        _tracking = false;
        StaticUnityMethods.OnUpdate -= OnUpdate;
        StaticUnityMethods.OnUpdate += OnUpdate;
    }

    private static void OnUpdate()
    {
        if (!PlayerEvents.HasRoomChanged && !PlayerEvents.HasZoneChanged)
        {
            _tracking = false;
            return;
        }

        if (!NetworkServer.active || RoundRestart.IsRoundRestarting)
        {
            return;
        }

        bool raise = _tracking;
        _tracking = true;
        try
        {
            Validate(raise);
        }
        catch (InvalidOperationException)
        {
            // A handler added or removed a player mid-iteration; the remaining players are revalidated next frame.
        }
    }

    private static void Validate(bool raise)
    {
        foreach (PlayerWrapper player in PlayerWrapper.Dictionary.Values)
        {
            ReferenceHub hub = player.ReferenceHub;
            if (hub == null)
            {
                continue;
            }

            RoomIdentifier? room = null;
            if (hub.IsAlive())
            {
                room = RoomIdUtils.RoomAtPosition(hub.roleManager.CurrentRole is ICameraController camera ? camera.CameraPosition : hub.transform.position);
            }

            RoomIdentifier? previous = player.TrackedRoom;
            if (previous == room)
            {
                continue;
            }

            player.TrackedRoom = room;
            if (!raise)
            {
                continue;
            }

            if (PlayerEvents.HasRoomChanged)
            {
                PlayerEvents.OnRoomChanged(new PlayerRoomChangedEventArgs(hub, previous!, room!));
            }

            if (PlayerEvents.HasZoneChanged && previous != null && room != null && previous.Zone != room.Zone)
            {
                PlayerEvents.OnZoneChanged(new PlayerZoneChangedEventArgs(hub, previous.Zone, room.Zone));
            }
        }
    }
}
