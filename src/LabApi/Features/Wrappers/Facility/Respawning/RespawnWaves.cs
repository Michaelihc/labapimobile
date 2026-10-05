using Generators;
using Respawning;

namespace LabApi.Features.Wrappers;

/// <summary>
/// A static class holding references to the wrapping <see cref="RespawnWave"/>s.
/// </summary>
/// <remarks>
/// The Carl Mod game code uses the SL 13.x <see cref="RespawnManager"/>: one MTF and one Chaos Insurgency wave
/// (<see cref="SpawnableTeamType"/>) that share a single respawn timer. Mini waves do not exist.
/// </remarks>
public static class RespawnWaves
{
    /// <summary>
    /// Gets the primary MTF respawn wave.
    /// </summary>
    public static MtfWave? PrimaryMtfWave { get; private set; }

    /// <summary>
    /// Gets the primary Chaos Insurgency respawn wave.
    /// </summary>
    public static ChaosWave? PrimaryChaosWave { get; private set; }

    /// <summary>
    /// Gets the respawn wave wrapper from the static references or creates a new one if it doesn't exist and the provided <see cref="SpawnableTeamHandlerBase"/> was not <see langword="null"/> or not valid subclass.
    /// </summary>
    /// <param name="baseWave">The <see cref="RespawnWave.Base"/> of the respawn wave.</param>
    /// <returns>The requested respawn wave or <see langword="null"/>.</returns>
    public static RespawnWave? Get(SpawnableTeamHandlerBase? baseWave)
    {
        return baseWave switch
        {
            NineTailedFoxSpawnHandler ntf => PrimaryMtfWave ??= new MtfWave(ntf),
            ChaosInsurgencySpawnHandler ci => PrimaryChaosWave ??= new ChaosWave(ci),
            _ => null,
        };
    }

    /// <summary>
    /// Gets the respawn wave wrapper for the provided <see cref="SpawnableTeamType"/>.
    /// </summary>
    /// <param name="team">The spawnable team of the respawn wave.</param>
    /// <returns>The requested respawn wave or <see langword="null"/> for <see cref="SpawnableTeamType.None"/>.</returns>
    public static RespawnWave? Get(SpawnableTeamType team)
    {
        return RespawnManager.SpawnableTeams.TryGetValue(team, out SpawnableTeamHandlerBase handler) ? Get(handler) : null;
    }

    /// <summary>
    /// Initializes the <see cref="RespawnWaves"/> wrapper and its wave wrapper instances.
    /// </summary>
    [InitializeWrapper]
    internal static void Initialize()
    {
        foreach (SpawnableTeamHandlerBase handler in RespawnManager.SpawnableTeams.Values)
        {
            Get(handler);
        }
    }
}
