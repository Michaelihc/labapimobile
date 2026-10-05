using Respawning;
using Respawning.NamingRules;
using System.Collections.Generic;

namespace LabApi.Features.Wrappers;

/// <summary>
/// A class representing <see cref="NineTailedFoxSpawnHandler">primary MTF spawn wave</see>.
/// </summary>
/// <remarks>
/// The Carl Mod game spawns a fixed composition (one captain, three sergeants, privates), so the official
/// sergeant and captain percentages are not available.
/// </remarks>
public class MtfWave : RespawnWave
{
    /// <inheritdoc cref="RespawnWave(SpawnableTeamHandlerBase, SpawnableTeamType)"/>
    internal MtfWave(NineTailedFoxSpawnHandler wave)
        : base(wave, SpawnableTeamType.NineTailedFox)
    {
        Base = wave;
    }

    /// <summary>
    /// The base <see cref="NineTailedFoxSpawnHandler"/> object.
    /// </summary>
    public new NineTailedFoxSpawnHandler Base { get; private set; }

    /// <inheritdoc/>
    /// <remarks>
    /// Plays the MTF entrance announcement for the most recent unit name.
    /// </remarks>
    public override void PlayAnnouncement(IEnumerable<Player> spawnedPlayers)
    {
        if (!UnitNamingRule.TryGetNamingRule(SpawnableTeamType.NineTailedFox, out UnitNamingRule rule)
            || !UnitNameMessageHandler.ReceivedNames.TryGetValue(SpawnableTeamType.NineTailedFox, out List<string> names)
            || names.Count == 0)
        {
            return;
        }

        rule.PlayEntranceAnnouncement(names[names.Count - 1]);
    }
}
