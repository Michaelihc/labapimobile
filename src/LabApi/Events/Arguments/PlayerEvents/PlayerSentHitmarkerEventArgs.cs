using LabApi.Events.Arguments.Interfaces;
using LabApi.Features.Wrappers;
using System;

namespace LabApi.Events.Arguments.PlayerEvents;

/// <summary>
/// Represents the arguments for the <see cref="Handlers.PlayerEvents.SentHitmarker"/> event.
/// </summary>
/// <remarks>
/// The Carl Mod hitmarker message carries only a size, so the official <c>PlayedAudio</c> and <c>Hitmarker</c> type members are absent.
/// </remarks>
public class PlayerSentHitmarkerEventArgs : EventArgs, IPlayerEvent
{
    /// <summary>
    /// Initializes a new instance for the <see cref="PlayerSentHitmarkerEventArgs"/> class.
    /// </summary>
    /// <param name="hub">The player that sent the hitmarker.</param>
    /// <param name="size">The target size multiplier.</param>
    public PlayerSentHitmarkerEventArgs(ReferenceHub hub, float size)
    {
        Player = Player.Get(hub);
        Size = size;
    }

    /// <summary>
    /// Gets the player that the hitmarker was sent to.
    /// </summary>
    public Player Player { get; }

    /// <summary>
    /// Gets the target size multiplier.
    /// </summary>
    public float Size { get; }
}
