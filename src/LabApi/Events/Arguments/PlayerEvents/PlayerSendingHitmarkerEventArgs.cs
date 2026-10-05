using LabApi.Events.Arguments.Interfaces;
using LabApi.Features.Wrappers;
using System;

namespace LabApi.Events.Arguments.PlayerEvents;

/// <summary>
/// Represents the arguments for the <see cref="Handlers.PlayerEvents.SendingHitmarker"/> event.
/// </summary>
/// <remarks>
/// The Carl Mod hitmarker message carries only a size, so the official <c>PlayAudio</c> and <c>Hitmarker</c> type members are absent.
/// </remarks>
public class PlayerSendingHitmarkerEventArgs : EventArgs, IPlayerEvent, ICancellableEvent
{
    /// <summary>
    /// Initializes a new instance for the <see cref="PlayerSendingHitmarkerEventArgs"/> class.
    /// </summary>
    /// <param name="hub">The player that is sending the hitmarker.</param>
    /// <param name="size">The target size multiplier.</param>
    public PlayerSendingHitmarkerEventArgs(ReferenceHub hub, float size)
    {
        Player = Player.Get(hub);
        Size = size;

        IsAllowed = true;
    }

    /// <summary>
    /// Gets or sets the player that the hitmarker is being sent to.
    /// </summary>
    public Player Player { get; set; }

    /// <summary>
    /// Gets or sets the target size multiplier.
    /// </summary>
    public float Size { get; set; }

    /// <inheritdoc/>
    public bool IsAllowed { get; set; }
}
