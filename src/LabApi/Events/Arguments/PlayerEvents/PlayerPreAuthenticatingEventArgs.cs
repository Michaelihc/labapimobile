using LabApi.Events.Arguments.Interfaces;
using LiteNetLib;
using LiteNetLib.Utils;
using System;

namespace LabApi.Events.Arguments.PlayerEvents;

/// <summary>
/// Represents the arguments for the <see cref="Handlers.PlayerEvents.PreAuthenticating"/> event.
/// <b>DO NOT call Unity APIs in handlers of this event; doing so may cause the server to crash.</b>
/// </summary>
public class PlayerPreAuthenticatingEventArgs : EventArgs, ICancellableEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PlayerPreAuthenticatingEventArgs"/> class.
    /// </summary>
    /// <param name="canJoin">Whether the player can join.</param>
    /// <param name="userId">User ID of the player.</param>
    /// <param name="ipAddress">IP Address the of player.</param>
    /// <param name="connectionRequest">Connection request to server.</param>
    /// <param name="readerStartPosition">Start position of stream.</param>
    public PlayerPreAuthenticatingEventArgs(bool canJoin, string userId, string ipAddress, ConnectionRequest connectionRequest, int readerStartPosition)
    {
        IsAllowed = true;
        CanJoin = canJoin;
        UserId = userId;
        IpAddress = ipAddress;
        ConnectionRequest = connectionRequest;
        ReaderStartPosition = readerStartPosition;
    }

    /// <summary>
    /// Gets or sets whether the player should be able to join server.
    /// </summary>
    /// <remarks>
    /// The Carl Mod server does not check slots during pre-authentication, so this starts as <see langword="true"/>.
    /// Setting it to <see langword="false"/> rejects the connection as "server full".
    /// </remarks>
    public bool CanJoin { get; set; }

    /// <summary>
    /// Gets the user ID of the player.
    /// </summary>
    public string UserId { get; }

    /// <summary>
    /// Gets the IP Address the of player.
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

    /// <inheritdoc />
    public bool IsAllowed { get; set; }

    /// <summary>
    /// Gets or sets whether the connection should be force rejected.
    /// </summary>
    /// <remarks>
    /// The Carl Mod LiteNetLib has no force rejection, so every rejection is sent as a normal rejection.
    /// </remarks>
    public bool ForceReject { get; set; }

    /// <summary>
    /// Gets or sets the custom rejection writer.
    /// </summary>
    // TODO: Write what exactly this does and how does it work
    public NetDataWriter? CustomReject { get; set; }

    /// <summary>
    /// Rejects connection.
    /// </summary>
    /// <param name="writer">The writer.</param>
    /// <param name="isForce">If connection is force rejected.</param>
    public void Reject(NetDataWriter writer, bool isForce)
    {
        CustomReject = writer;
        ForceReject = isForce;
    }

    /// <summary>
    /// Rejects connection with banned reason.
    /// </summary>
    /// <param name="reason">The reason of ban.</param>
    /// <param name="expires">The time when ban expires.</param>
    public void RejectBanned(string reason, long expires)
    {
        CustomReject = new NetDataWriter();

        CustomReject.Put((byte)RejectionReason.Banned);
        CustomReject.Put(expires);
        CustomReject.Put(reason ?? string.Empty);

        IsAllowed = false;
    }

    /// <summary>
    /// Rejects connection with custom reason.
    /// </summary>
    /// <param name="reason">The reason.</param>
    public void RejectCustom(string reason)
    {
        CustomReject = new NetDataWriter();

        CustomReject.Put((byte)RejectionReason.Custom);
        CustomReject.Put(reason ?? string.Empty);

        IsAllowed = false;
    }

    /// <summary>
    /// Rejects connection with delayed reconnection attempt.
    /// </summary>
    /// <param name="seconds">The time in seconds when reconnection attempt happens.</param>
    public void RejectDelay(byte seconds)
    {
        CustomReject = new NetDataWriter();

        CustomReject.Put((byte)RejectionReason.Delay);
        CustomReject.Put(seconds);

        IsAllowed = false;
    }

    /// <summary>
    /// Rejects connection with reconnection attempt to specific server port.
    /// </summary>
    /// <param name="port">The server port of target server.</param>
    public void RejectRedirect(ushort port)
    {
        CustomReject = new NetDataWriter();

        CustomReject.Put((byte)RejectionReason.Redirect);
        CustomReject.Put(port);

        IsAllowed = false;
    }
}
