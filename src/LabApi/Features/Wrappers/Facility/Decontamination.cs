using static LightContainmentZoneDecontamination.DecontaminationController;

namespace LabApi.Features.Wrappers;

/// <summary>
/// Static wrapper for LCZ decontamination.
/// </summary>
public static class Decontamination
{
    /// <summary>
    /// Offset that has not been folded into the synchronized round start time yet, because the decontamination
    /// timer had not started when it was set. Applied by the decontamination patch once the timer starts.
    /// </summary>
    internal static float PendingOffset;

    /// <summary>
    /// Offset already folded into the synchronized round start time.
    /// </summary>
    private static float _appliedOffset;

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
    /// An offset set before the decontamination timer starts is applied when it starts.
    /// </remarks>
    public static float Offset
    {
        get => _appliedOffset + PendingOffset;
        set
        {
            float delta = value - Offset;
            if (delta == 0f)
            {
                return;
            }

            if (Singleton.RoundStartTime > 0.0)
            {
                Singleton.NetworkRoundStartTime = Singleton.RoundStartTime - delta;
                _appliedOffset += delta;
            }
            else
            {
                PendingOffset += delta;
            }
        }
    }

    /// <summary>
    /// Folds the pending offset into the synchronized round start time once the timer has started.
    /// </summary>
    /// <param name="controller">The decontamination controller.</param>
    internal static void ApplyPendingOffset(LightContainmentZoneDecontamination.DecontaminationController controller)
    {
        if (controller.RoundStartTime <= 0.0)
        {
            return;
        }

        controller.NetworkRoundStartTime = controller.RoundStartTime - PendingOffset;
        _appliedOffset += PendingOffset;
        PendingOffset = 0f;
    }

    /// <summary>
    /// Resets the offset bookkeeping for a new round.
    /// </summary>
    internal static void ResetOffset()
    {
        PendingOffset = 0f;
        _appliedOffset = 0f;
    }
}
