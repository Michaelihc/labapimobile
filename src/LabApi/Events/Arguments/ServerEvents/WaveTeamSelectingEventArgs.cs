using LabApi.Events.Arguments.Interfaces;
using Respawning;
using System;

namespace LabApi.Events.Arguments.ServerEvents;

/// <summary>
/// Represents the arguments for the <see cref="Handlers.ServerEvents.WaveTeamSelecting"/> event.
/// </summary>
public class WaveTeamSelectingEventArgs : EventArgs, ICancellableEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="WaveTeamSelectingEventArgs"/> class.
    /// </summary>
    /// <param name="wave">The wave that is about to be selected.</param>
    public WaveTeamSelectingEventArgs(SpawnableTeamHandlerBase wave)
    {
        IsAllowed = true;
        Wave = wave;
    }

    /// <summary>
    /// Gets or sets the spawnable wave. See <see cref="SpawnableTeamHandlerBase"/> and its subclasses.<br/>
    /// Use <see cref="Features.Wrappers.RespawnWave.Base"/> of a <see cref="Features.Wrappers.RespawnWaves"/> wave to set it to a different value.
    /// </summary>
    public SpawnableTeamHandlerBase Wave { get; set; }

    /// <inheritdoc />
    public bool IsAllowed { get; set; }
}