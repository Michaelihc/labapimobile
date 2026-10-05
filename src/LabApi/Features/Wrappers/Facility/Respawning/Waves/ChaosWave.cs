using Respawning;

namespace LabApi.Features.Wrappers;

/// <summary>
/// A class representing <see cref="ChaosInsurgencySpawnHandler">primary Chaos Insurgency spawn wave</see>.
/// </summary>
/// <remarks>
/// The Carl Mod game spawns a fixed composition (20% marauders, 30% of the rest repressors, riflemen), so the
/// official logicer and shotgun percentages are not available.
/// </remarks>
public class ChaosWave : RespawnWave
{
    /// <inheritdoc cref="RespawnWave(SpawnableTeamHandlerBase, SpawnableTeamType)"/>
    internal ChaosWave(ChaosInsurgencySpawnHandler wave)
        : base(wave, SpawnableTeamType.ChaosInsurgency)
    {
        Base = wave;
    }

    /// <summary>
    /// The base <see cref="ChaosInsurgencySpawnHandler"/> object.
    /// </summary>
    public new ChaosInsurgencySpawnHandler Base { get; private set; }
}
