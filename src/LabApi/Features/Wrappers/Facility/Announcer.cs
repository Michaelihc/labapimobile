using LabApi.Events.Patches.Rounds;
using PlayerRoles;
using PlayerStatsSystem;
using Respawning;
using Respawning.NamingRules;
using System;
using System.Collections.Generic;
using System.Text;
using VoiceLine = NineTailedFoxAnnouncer.VoiceLine;

namespace LabApi.Features.Wrappers;

/// <summary>
/// The wrapper for in game CASSIE announcer.
/// </summary>
/// <remarks>
/// The Carl Mod game code uses the SL 13.x <see cref="NineTailedFoxAnnouncer"/>: announcements are sent to clients
/// with <see cref="RespawnEffectsController.PlayCassieAnnouncement"/> and queued on each client in arrival order.
/// </remarks>
public static class Announcer
{
    private static HashSet<string>? _validWords;

    private static NineTailedFoxAnnouncer? _wordsSource;

    /// <summary>
    /// Gets whether CASSIE is currently speaking.
    /// </summary>
    /// <remarks>
    /// Reads the host's local announcement queue, which receives every announcement the server sends.
    /// </remarks>
    public static bool IsSpeaking
    {
        get
        {
            NineTailedFoxAnnouncer announcer = NineTailedFoxAnnouncer.singleton;
            return announcer != null && announcer.queue.Count != 0;
        }
    }

    /// <summary>
    /// Gets all available voice lines for CASSIE.
    /// </summary>
    public static VoiceLine[] AllLines
    {
        get
        {
            NineTailedFoxAnnouncer announcer = NineTailedFoxAnnouncer.singleton;
            return announcer != null && announcer.voiceLines != null ? announcer.voiceLines : [];
        }
    }

    /// <summary>
    /// Gets all collection names in which voice lines are in.
    /// </summary>
    public static string[] CollectionNames
    {
        get
        {
            HashSet<string> words = ValidWords;
            string[] result = new string[words.Count];
            words.CopyTo(result);
            return result;
        }
    }

    private static HashSet<string> ValidWords
    {
        get
        {
            NineTailedFoxAnnouncer announcer = NineTailedFoxAnnouncer.singleton;
            if (_validWords != null && _wordsSource == announcer)
            {
                return _validWords;
            }

            HashSet<string> words = new(StringComparer.InvariantCultureIgnoreCase);
            foreach (VoiceLine line in AllLines)
            {
                if (!string.IsNullOrEmpty(line.apiName))
                {
                    words.Add(line.apiName);
                }
            }

            _validWords = words;
            _wordsSource = announcer;
            return words;
        }
    }

    /// <summary>
    /// Checks whether a specified word is valid for CASSIE.
    /// <note>String comparison is case-insensitive.</note>
    /// </summary>
    /// <param name="word">The word to check.</param>
    /// <returns>Whether the word is valid.</returns>
    public static bool IsValid(string word)
        => word != null && ValidWords.Contains(word);

    /// <summary>
    /// Calculates duration of specific message.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="rawNumber">Raw numbers.</param>
    /// <param name="speed">The speed of the cassie talking.</param>
    /// <returns>Duration of the specific message in seconds.</returns>
    public static float CalculateDuration(string message, bool rawNumber = false, float speed = 1f)
    {
        NineTailedFoxAnnouncer announcer = NineTailedFoxAnnouncer.singleton;
        return announcer == null ? 0f : announcer.CalculateDuration(message, rawNumber, speed);
    }

    /// <summary>
    /// Queues a custom announcement.
    /// </summary>
    /// <param name="message">The sentence CASSIE is supposed to say.</param>
    /// <param name="isHeld">Sets a minimal 3-second moment of silence before the announcement. For most cases you wanna keep it true.</param>
    /// <param name="isNoisy">Whether the background noises play.</param>
    /// <param name="isSubtitles">Show subtitles.</param>
    /// <param name="customSubtitles">Custom subtitles to appear instead of the actual message.</param>
    [Obsolete("Use Message(string message, string customSubtitles = \"\", bool playBackground = true, float priority = 0f, float glitchScale = 1f) instead.", true)]
    public static void Message(string message, bool isHeld = false, bool isNoisy = true, bool isSubtitles = true, string customSubtitles = "")
        => Message(message, customSubtitles);

    /// <summary>
    /// Queues a custom announcement.
    /// </summary>
    /// <param name="message">The sentence CASSIE is supposed to say.</param>
    /// <param name="customSubtitles">Custom subtitles to play.</param>
    /// <param name="playBackground">Should play the background track (bells, noise).</param>
    /// <param name="priority">Ignored: Carl Mod clients play announcements in arrival order.</param>
    /// <param name="glitchScale">Intensity of glitches and stutters added before sending to clients.</param>
    public static void Message(string message, string customSubtitles = "", bool playBackground = true, float priority = 0f, float glitchScale = 1f)
    {
        if (string.IsNullOrEmpty(message))
        {
            return;
        }

        message = Glitchify(message, glitchScale);
        CassieAnnouncementPatch.PendingCustomSubtitles = customSubtitles;
        try
        {
            RespawnEffectsController.PlayCassieAnnouncement(message, false, playBackground, !string.IsNullOrEmpty(customSubtitles));
        }
        finally
        {
            CassieAnnouncementPatch.PendingCustomSubtitles = null;
        }
    }

    /// <summary>
    /// Plays the custom announcement with chance of 0f to 1f of adding a glitch or jam before each word. Values closer to 1f are higher chances.
    /// </summary>
    /// <param name="message">The sentence CASSIE is supposed to say.</param>
    /// <param name="glitchChance">The chance for glitch sound to be added before each word. Range from 0f to 1f.</param>
    /// <param name="jamChance">The chance for jam sound to be added before each word. Range from 0f to 1f.</param>
    public static void GlitchyMessage(string message, float glitchChance, float jamChance)
    {
        NineTailedFoxAnnouncer announcer = NineTailedFoxAnnouncer.singleton;
        if (announcer != null)
        {
            announcer.ServerOnlyAddGlitchyPhrase(message, glitchChance, jamChance);
        }
    }

    /// <summary>
    /// Plays the termination announcement of a SCP player. If the specified player does not have an SCP role then nothing is played.
    /// </summary>
    /// <param name="player">The player who is being terminated as an SCP.</param>
    /// <param name="info">Damage handler causing the death of the player.</param>
    public static void ScpTermination(Player player, DamageHandlerBase info)
        => NineTailedFoxAnnouncer.AnnounceScpTermination(player.ReferenceHub, info);

    /// <summary>
    /// Clears the CASSIE announcements queue.
    /// </summary>
    public static void Clear()
        => RespawnEffectsController.ClearQueue();

    /// <summary>
    /// Converts player's team into CASSIE-able word. Unit names are converted into NATO_X words, followed by a number. For example "Alpha-5" is converted to "NATO_A 5".
    /// </summary>
    /// <param name="team">Target team.</param>
    /// <param name="unitName">MTF Unit name (for team <see cref="Team.FoundationForces"/>).</param>
    /// <returns>Converted name.</returns>
    public static string ConvertTeam(Team team, string unitName)
    {
        string text = "CONTAINMENTUNIT UNKNOWN";
        switch (team)
        {
            case Team.FoundationForces:
                {
                    if (!UnitNamingRule.TryGetNamingRule(SpawnableTeamType.NineTailedFox, out UnitNamingRule unitNamingRule))
                    {
                        return text;
                    }

                    return "CONTAINMENTUNIT " + unitNamingRule.GetCassieUnitName(unitName);
                }

            case Team.ChaosInsurgency:
                return "CHAOSINSURGENCY";
            case Team.Scientists:
                return "SCIENCE PERSONNEL";
            case Team.ClassD:
                return "CLASSD PERSONNEL";
            default:
                return text;
        }
    }

    /// <summary>
    /// Converts number into string.
    /// </summary>
    /// <param name="num">The number.</param>
    /// <returns>Number converted to string.</returns>
    public static string ConvertNumber(int num)
        => NineTailedFoxAnnouncer.ConvertNumber(num);

    /// <summary>
    /// Converts player's <see cref="RoleTypeId"/> into an SCP <b>number</b> identifier.
    /// </summary>
    /// <param name="role">The target <see cref="RoleTypeId"/>.</param>
    /// <param name="withoutSpace">The SCP number without spaces between. Used by CASSIE.</param>
    /// <param name="withSpace">The SCP number with spaces between. Used by Subtitles.</param>
    public static void ConvertScp(RoleTypeId role, out string withoutSpace, out string withSpace)
        => NineTailedFoxAnnouncer.ConvertSCP(role, out withoutSpace, out withSpace);

    /// <summary>
    /// Converts player's role name into an SCP <b>number</b> identifier.
    /// </summary>
    /// <param name="roleName">The targets role name.</param>
    /// <param name="withoutSpace">The SCP number without spaces between. Used by CASSIE.</param>
    /// <param name="withSpace">The SCP number with spaces between. Used by Subtitles.</param>
    public static void ConvertScp(string roleName, out string withoutSpace, out string withSpace)
        => NineTailedFoxAnnouncer.ConvertSCP(roleName, out withoutSpace, out withSpace);

    /// <summary>
    /// Adds glitch (<c>.G1</c>-<c>.G6</c>) and jam (<c>JAM_xxx_y</c>) words between the words of a message,
    /// matching the official intensity scale (doubled after detonation) with the Carl Mod CASSIE syntax.
    /// </summary>
    /// <param name="message">The message.</param>
    /// <param name="intensityScale">The glitch intensity scale. Zero or less returns the message unchanged.</param>
    /// <returns>The glitched message.</returns>
    private static string Glitchify(string message, float intensityScale)
    {
        if (intensityScale <= 0f)
        {
            return message;
        }

        if (AlphaWarheadController.Detonated)
        {
            intensityScale *= 2f;
        }

        float glitchChance = UnityEngine.Random.Range(0.1f, 0.14f) * intensityScale;
        float jamChance = UnityEngine.Random.Range(0.07f, 0.09f) * intensityScale;
        string[] words = message.Split(' ');
        StringBuilder sb = NorthwoodLib.Pools.StringBuilderPool.Shared.Rent(message.Length + 16);
        for (int i = 0; i < words.Length; i++)
        {
            sb.Append(words[i]);
            sb.Append(' ');
            if (i >= words.Length - 1)
            {
                continue;
            }

            if (UnityEngine.Random.value < glitchChance)
            {
                sb.Append(".G");
                sb.Append(UnityEngine.Random.Range(1, 7));
                sb.Append(' ');
            }

            if (UnityEngine.Random.value < jamChance)
            {
                sb.Append("JAM_");
                sb.Append(UnityEngine.Random.Range(0, 70).ToString("000"));
                sb.Append('_');
                sb.Append(UnityEngine.Random.Range(2, 6));
                sb.Append(' ');
            }
        }

        return NorthwoodLib.Pools.StringBuilderPool.Shared.ToStringReturn(sb);
    }
}
