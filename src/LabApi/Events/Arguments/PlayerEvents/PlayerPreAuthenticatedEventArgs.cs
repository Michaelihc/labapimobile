using LiteNetLib;
using System;

namespace LabApi.Events.Arguments.PlayerEvents;

/// <summary>
/// Represents the arguments for the <see cref="Handlers.PlayerEvents.PreAuthenticated"/> event.
/// <b>DO NOT call Unity APIs in handlers of this event; doing so may cause the server to crash.</b>
/// </summary>
public class PlayerPreAuthenticatedEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PlayerPreAuthenticatedEventArgs"/> class.
    /// </summary>
    /// <param name="userId">User ID of the player.</param>
    /// <param name="ipAddress">IP Address the of player.</param>
    /// <param name="connectionRequest">Connection request to server.</param>
    /// <param name="readerStartPosition">Start position of stream.</param>
    public PlayerPreAuthenticatedEventArgs(string userId, string ipAddress, ConnectionRequest connectionRequest, int readerStartPosition)
    {
        UserId = userId;
        IpAddress = ipAddress;
        ConnectionRequest = connectionRequest;
        ReaderStartPosition = readerStartPosition;
    }

    /// <summary>
    /// Gets the user ID of the player.
    /// </summary>
    public string UserId { get; }

    /// <summary>
    /// Gets the IP Address of the player.
    /// </summary>
    public string IpAddress { get; }

    /// <summary>
    /// Gets the connection request to server.
    /// </summary>
    public ConnectionRequest ConnectionRequest { get; }

    /// <summary>
    /// Gets the start position of stream.
    /// </summary>
    public int ReaderStartPosition { get; }
}