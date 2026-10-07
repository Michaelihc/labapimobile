namespace LabApi.Events.Patches.Rounds;

using HarmonyLib;
using LabApi.Events.Arguments.ServerEvents;
using LabApi.Events.Handlers;
using Mirror;
using NorthwoodLib.Pools;
using PlayerRoles;
using PlayerStatsSystem;
using Respawning;
using Subtitles;
using System;
using System.Collections.Generic;
using System.Text;

/// <summary>
/// Raises <see cref="ServerEvents.CassieAnnouncing"/> and <see cref="ServerEvents.CassieAnnounced"/>.
/// Official (LabAPI 1.0 / SL 14.1): Respawning.RespawnEffectsController.PlayCassieAnnouncement; SL 14.2.7 no longer raises them.
/// </summary>
/// <remarks>
/// The fork's announcement has no custom subtitle parameter. Custom subtitles (from
/// <see cref="Features.Wrappers.Announcer.Message(string, string, bool, float, float)"/> or set by a handler) are sent
/// with the SL 13.x translated-message format (<c>subtitle&lt;size=0&gt; words &lt;/size&gt;&lt;split&gt;</c>) and
/// subtitles enabled.
/// </remarks>
[HarmonyPatch(typeof(RespawnEffectsController), nameof(RespawnEffectsController.PlayCassieAnnouncement))]
internal static class CassieAnnouncementPatch
{
    /// <summary>
    /// Custom subtitles for the next announcement, set by the announcer wrapper around its call.
    /// </summary>
    internal static string? PendingCustomSubtitles;

    private static string _announcedWords = string.Empty;

    private static string _announcedSubtitles = string.Empty;

    private static bool Prefix(ref string words, ref bool makeHold, ref bool makeNoise, ref bool customAnnouncement, out bool __state)
    {
        __state = false;
        string? subtitles = PendingCustomSubtitles;
        PendingCustomSubtitles = null;

        if (NetworkServer.active && ServerEvents.HasCassieAnnouncing)
        {
            CassieAnnouncingEventArgs e = new(words, makeHold, makeNoise, customAnnouncement, subtitles ?? string.Empty);
            ServerEvents.OnCassieAnnouncing(e);
            if (!e.IsAllowed)
            {
                return false;
            }

            words = e.Words;
            makeHold = e.MakeHold;
            makeNoise = e.MakeNoise;
            customAnnouncement = e.CustomAnnouncement;
            subtitles = e.CustomSubtitles;
        }

        if (NetworkServer.active && ServerEvents.HasCassieAnnounced)
        {
            __state = true;
            _announcedWords = words ?? string.Empty;
            _announcedSubtitles = subtitles ?? string.Empty;
        }

        if (!string.IsNullOrEmpty(subtitles) && !string.IsNullOrEmpty(words))
        {
            words = ComposeTranslated(words!, subtitles!);
            customAnnouncement = true;
        }

        return true;
    }

    private static void Postfix(bool makeHold, bool makeNoise, bool customAnnouncement, bool __state)
    {
        if (!__state)
        {
            return;
        }

        string words = _announcedWords;
        string subtitles = _announcedSubtitles;
        _announcedWords = string.Empty;
        _announcedSubtitles = string.Empty;
        ServerEvents.OnCassieAnnounced(new CassieAnnouncedEventArgs(words, makeHold, makeNoise, customAnnouncement, subtitles));
    }

    internal static string ComposeTranslated(string words, string subtitles)
    {
        string[] lines = words.Split('\n');
        string[] translations = subtitles.Split('\n');
        StringBuilder sb = StringBuilderPool.Shared.Rent(words.Length + subtitles.Length + 32);
        for (int i = 0; i < lines.Length; i++)
        {
            if (i < translations.Length)
            {
                sb.Append(translations[i].Replace(' ', ' '));
            }

            sb.Append("<size=0> ");
            sb.Append(lines[i]);
            sb.Append(" </size><split>");
        }

        return StringBuilderPool.Shared.ToStringReturn(sb);
    }
}

/// <summary>
/// Raises <see cref="ServerEvents.CassieQueuingScpTermination"/> and <see cref="ServerEvents.CassieQueuedScpTermination"/>.
/// Official: Cassie.CassieScpTerminationAnnouncement.AnnounceScpTermination.
/// </summary>
/// <remarks>
/// As officially, the events fire only when a new termination announcement is queued; a death whose announcement
/// matches one already waiting (the fork merges by announcement text) joins it without an event.
/// </remarks>
[HarmonyPatch(typeof(NineTailedFoxAnnouncer), nameof(NineTailedFoxAnnouncer.AnnounceScpTermination))]
internal static class CassieScpTerminationPatch
{
    private static bool Prefix(ReferenceHub scp, DamageHandlerBase hit)
    {
        if (!ServerEvents.HasCassieQueuingScpTermination && !ServerEvents.HasCassieQueuedScpTermination)
        {
            return true;
        }

        NineTailedFoxAnnouncer announcer = NineTailedFoxAnnouncer.singleton;
        if (!NetworkServer.active || announcer == null || scp == null || !scp.IsSCP(includeZombies: false))
        {
            return true;
        }

        string announcement = hit.CassieDeathAnnouncement.Announcement;
        if (string.IsNullOrEmpty(announcement))
        {
            return true;
        }

        List<NineTailedFoxAnnouncer.ScpDeath> deaths = NineTailedFoxAnnouncer.scpDeaths;
        for (int i = 0; i < deaths.Count; i++)
        {
            if (deaths[i].announcement == announcement)
            {
                // Merged into a waiting announcement: no new announcement is queued.
                return true;
            }
        }

        SubtitlePart[] subtitleParts = hit.CassieDeathAnnouncement.SubtitleParts ?? Array.Empty<SubtitlePart>();
        announcer.scpListTimer = 0f;

        if (ServerEvents.HasCassieQueuingScpTermination)
        {
            CassieQueuingScpTerminationEventArgs e = new(scp, announcement, subtitleParts, hit);
            ServerEvents.OnCassieQueuingScpTermination(e);
            if (!e.IsAllowed)
            {
                return false;
            }

            announcement = e.Announcement;
            subtitleParts = e.SubtitleParts;
        }

        deaths.Add(new NineTailedFoxAnnouncer.ScpDeath
        {
            scpSubjects = new List<RoleTypeId>(1) { scp.GetRoleId() },
            announcement = announcement,
            subtitleParts = subtitleParts,
        });

        if (ServerEvents.HasCassieQueuedScpTermination)
        {
            ServerEvents.OnCassieQueuedScpTermination(new CassieQueuedScpTerminationEventArgs(scp, announcement, subtitleParts, hit));
        }

        return false;
    }
}
