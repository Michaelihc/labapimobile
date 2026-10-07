using System;
using static LightContainmentZoneDecontamination.DecontaminationController;

namespace LabApi.Features.Wrappers;

/// <summary>
/// Static wrapper for LCZ decontamination.
/// </summary>
public static class Decontamination
{
    /// <summary>
    /// Smallest synchronized round start time the offset may produce. The fork's <c>UpdateTime</c> (server and clients)
    /// stops the timer while <c>RoundStartTime</c> is 0 or below.
    /// </summary>
    private const double MinRoundStartTime = 0.001;

    /// <summary>
    /// Offset that has not been applied yet, because the decontamination timer had not started when it was set.
    /// Applied by the decontamination patch once the timer starts.
    /// </summary>
    internal static float PendingOffset;

    /// <summary>
    /// Offset folded into the synchronized round start time (seen by the server and clients).
    /// </summary>
    private static float _shiftedOffset;

    /// <summary>
    /// Offset kept in the controller's server-only <c>TimeOffset</c>, because the round start time cannot move further.
    /// </summary>
    private static float _serverOnlyOffset;

    /// <summary>
    /// Gets or sets the decontamination status.
    /// </summary>
    public static DecontaminationStatus Status
    {
        get => Singleton.DecontaminationOverride;
        set => Singleton.NetworkDecontaminationOverride = value;
    }

    /// <summary>
    /// Gets whether the LCZ is currently being decontaminated.
    /// </summary>
    public static bool IsDecontaminating => Singleton.IsDecontaminating;

    /// <summary>
    /// Gets the current server time since round has started plus the <see cref="Offset"/>.
    /// </summary>
    public static double ServerTime => GetServerTime;

    /// <summary>
    /// Gets the network time at which round has started. Value of -1 means the round hasnt started yet.
    /// </summary>
    public static double RoundStartTime => Singleton.RoundStartTime;

    /// <summary>
    /// Gets or sets the offset of the decontamination timer in seconds.
    /// Positive values decrease the timer and negative extend it.
    /// </summary>
    /// <remarks>
    /// The Carl Mod controller does not synchronize its own time offset, so the offset is applied by shifting the
    /// synchronized round start time, which keeps the client timers and announcements in step with the server.
    /// An offset set before the decontamination timer starts is applied when it starts. The start time is network time
    /// since the server started and must stay above 0, so a positive offset larger than that (early in the server's life)
    /// is applied up to that limit and the rest goes to the controller's server-only <c>TimeOffset</c>: server phases follow
    /// the full offset, while client timers and announcement audio lag by the rest.
    /// </remarks>
    public static float Offset
    {
        get => _shiftedOffset + _serverOnlyOffset + PendingOffset;
        set
        {
            float delta = value - Offset;
            if (delta == 0f)
            {
                return;
            }

            PendingOffset += delta;
            ApplyPendingOffset(Singleton);
        }
    }

    /// <summary>
    /// Applies the pending offset once the timer has started, splitting it between the synchronized round start time and
    /// the controller's server-only time offset.
    /// </summary>
    /// <param name="controller">The decontamination controller.</param>
    internal static void ApplyPendingOffset(LightContainmentZoneDecontamination.DecontaminationController controller)
    {
        if (controller == null || controller.RoundStartTime <= 0.0)
        {
            return;
        }

        float total = _shiftedOffset + _serverOnlyOffset + PendingOffset;
        double unshiftedStart = controller.RoundStartTime + _shiftedOffset;
        float shift = (float)Math.Min(total, unshiftedStart - MinRoundStartTime);
        float serverOnly = total - shift;

        controller.NetworkRoundStartTime = unshiftedStart - shift;
        controller.TimeOffset += serverOnly - _serverOnlyOffset;
        _shiftedOffset = shift;
        _serverOnlyOffset = serverOnly;
        PendingOffset = 0f;
    }

    /// <summary>
    /// Applies the offset again after something other than LabAPI restarted the timer by setting the synchronized round start
    /// time (the Carl Mod 0.0.5 CarlModExtras module does at round start), which drops the part of the offset folded into it.
    /// </summary>
    /// <param name="controller">The decontamination controller.</param>
    internal static void OnTimerRestarted(LightContainmentZoneDecontamination.DecontaminationController controller)
    {
        PendingOffset += _shiftedOffset;
        _shiftedOffset = 0f;
        if (PendingOffset != 0f)
        {
            ApplyPendingOffset(controller);
        }
    }

    /// <summary>
    /// Resets the offset bookkeeping for a new round.
    /// </summary>
    internal static void ResetOffset()
    {
        PendingOffset = 0f;
        _shiftedOffset = 0f;
        _serverOnlyOffset = 0f;
    }
}
