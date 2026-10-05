using System.Collections.Generic;
using System.ComponentModel;
using PlayerRoles;

namespace EventProbe;

/// <summary>
/// EventProbe configuration (<c>LabAPI-Mobile/configs/&lt;port&gt;/EventProbe/config.yml</c>).
/// </summary>
public sealed class ProbeConfig
{
    [Description("Events that fire every frame or every packet. They log their first call (and the first call after a quiet interval), then a count per report interval. Format: Class.Event, for example PlayerEvents.StayingInHazard.")]
    public List<string> ThrottledEvents { get; set; } =
    [
        "PlayerEvents.StayingInHazard",
        "PlayerEvents.SendingVoiceMessage",
        "PlayerEvents.ReceivingVoiceMessage",
        "PlayerEvents.MovementStateChanged",
        "PlayerEvents.ValidatedVisibility",
        "PlayerEvents.UsingRadio",
        "PlayerEvents.UsedRadio",
        "PlayerEvents.Escaping",
        "PlayerEvents.IdlingTesla",
        "PlayerEvents.SendingHitmarker",
        "PlayerEvents.SentHitmarker",
        "PlayerEvents.RequestingRaPlayerList",
        "PlayerEvents.RequestedRaPlayerList",
        "PlayerEvents.RaPlayerListAddingPlayer",
        "PlayerEvents.RaPlayerListAddedPlayer",
        "Scp106Events.ChangingVigor",
        "Scp106Events.ChangedVigor",
        "Scp079Events.GainingExperience",
        "Scp079Events.GainedExperience",
    ];

    [Description("Any other event that logs more lines than this within one report interval is throttled until it is quiet for a whole interval. 0 disables automatic throttling.")]
    public int AutoThrottleLines { get; set; } = 40;

    [Description("Seconds between the counts printed for throttled events.")]
    public float ReportIntervalSeconds { get; set; } = 10f;

    [Description("Cancellable events (Class.Event) whose IsAllowed is set to false. Also settable at runtime: 'probe cancel <Class.Event>'.")]
    public List<string> CancelEvents { get; set; } = [];

    [Description("Role given instead of Spectator when a player dies (PlayerEvents.ChangingRole with ChangeReason Died). None keeps the death role. Tests role changes on death. Also settable at runtime: 'probe deathrole <RoleTypeId|None>'.")]
    public RoleTypeId DeathRole { get; set; } = RoleTypeId.None;

    [Description("Copy server console lines into the Unity log when the server runs with the file console (-key<session>), whose output text is otherwise lost.")]
    public bool MirrorFileConsoleToUnityLog { get; set; } = true;

    [Description("Maximum length of one probe line.")]
    public int MaxLineLength { get; set; } = 500;
}
