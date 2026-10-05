using LabApi.Events.Patches.Rounds;
using PlayerRoles;
using Respawning;
using System;
using System.Collections.Generic;

namespace LabApi.Features.Wrappers;

/// <summary>
/// A class representing a <see cref="SpawnableTeamHandlerBase">respawn wave</see> of the SL 13.x respawn system.
/// </summary>
/// <remarks>
/// Both waves share the single <see cref="RespawnManager"/> timer, so the timer members of one wave affect the other.
/// </remarks>
public abstract class RespawnWave
{
    /// <summary>
    /// Internal constructor preventing external instantiation.
    /// </summary>
    /// <param name="wave">The base game object.</param>
    /// <param name="team">The spawnable team of the wave.</param>
    internal RespawnWave(SpawnableTeamHandlerBase wave, SpawnableTeamType team)
    {
        Base = wave;
        TeamType = team;
    }

    /// <summary>
    /// The base <see cref="SpawnableTeamHandlerBase"/> object.
    /// </summary>
    public SpawnableTeamHandlerBase Base { get; private set; }

    /// <summary>
    /// Gets the faction this respawn wave belong to.
    /// </summary>
    public Faction Faction => TeamType == SpawnableTeamType.ChaosInsurgency ? Faction.FoundationEnemy : Faction.FoundationStaff;

    /// <summary>
    /// Gets or sets the maximum amount of <see cref="Player"/>s that are going to spawn with the wave.
    /// </summary>
    /// <remarks>
    /// The Carl Mod game uses an absolute player count (<c>maximum_MTF_respawn_amount</c> / <c>maximum_CI_respawn_amount</c>).
    /// A value set here lasts until the server config is reloaded.
    /// </remarks>
    public int MaxWaveSize
    {
        get => Base.MaxWaveSize;
        set
        {
            if (Base is ConfigBasedTeamSpawnHandler handler)
            {
                handler._maxWaveSize = value;
            }
        }
    }

    /// <summary>
    /// Gets the time the spawn animations takes in seconds.
    /// </summary>
    /// <remarks>
    /// The Carl Mod respawn manager spawns the wave in the same frame its team is selected, so this is the length of
    /// the arrival effects rather than a delay before the spawn.
    /// </remarks>
    public float AnimationTime => Base.EffectTime;

    /// <summary>
    /// Gets or sets the respawn token share of this wave's <see cref="Faction"/> (0-100).
    /// </summary>
    /// <remarks>
    /// The SL 13.x respawn tokens are a zero-sum share between both waves that the game raises for kills, escapes and
    /// SCP damage; the wave with the larger share is selected for the next respawn. Setting the value rebalances the
    /// other wave's share.
    /// </remarks>
    public float Influence
    {
        get
        {
            foreach (RespawnTokensManager.TokenCounter counter in RespawnTokensManager.Counters)
            {
                if (counter.Team == TeamType)
                {
                    return counter.Amount;
                }
            }

            return 0f;
        }

        set
        {
            float total = RespawnTokensManager.TotalAssigned;
            if (total <= 0f)
            {
                return;
            }

            RespawnTokensManager.ForceTeamDominance(TeamType, value / total);
        }
    }

    /// <summary>
    /// Gets or sets the time in seconds until the next respawn wave is selected.
    /// </summary>
    /// <remarks>
    /// Shared by both waves.
    /// </remarks>
    public float TimeLeft
    {
        get
        {
            RespawnManager manager = RespawnManager.Singleton;
            return manager == null ? 0f : Math.Max(0f, manager._timeForNextSequence - (float)manager._stopwatch.Elapsed.TotalSeconds);
        }

        set
        {
            RespawnManager manager = RespawnManager.Singleton;
            if (manager == null)
            {
                return;
            }

            manager._timeForNextSequence = value;
            manager._stopwatch.Restart();
        }
    }

    /// <summary>
    /// Gets the time that has passed since last wave respawn.
    /// </summary>
    /// <remarks>
    /// Shared by both waves.
    /// </remarks>
    public float TimePassed
    {
        get
        {
            RespawnManager manager = RespawnManager.Singleton;
            return manager == null ? 0f : (float)manager._stopwatch.Elapsed.TotalSeconds;
        }
    }

    /// <summary>
    /// Gets the spawnable team of this wave.
    /// </summary>
    internal SpawnableTeamType TeamType { get; }

    /// <summary>
    /// Initiates the respawn with animation.
    /// </summary>
    /// <remarks>
    /// Raises the wave team selection events, plays the arrival effects and spawns the wave, like the natural
    /// Carl Mod team selection.
    /// </remarks>
    public virtual void InitiateRespawn() => RespawnPatches.InitiateRespawn(TeamType);

    /// <summary>
    /// Instantly respawns this wave.
    /// </summary>
    public void InstantRespawn()
    {
        RespawnManager manager = RespawnManager.Singleton;
        if (manager != null)
        {
            manager.ForceSpawnTeam(TeamType);
        }
    }

    /// <summary>
    /// Plays the respawn announcement.
    /// </summary>
    [Obsolete("Use PlayAnnouncement(IEnumerable<Player>) instead.", true)]
    public void PlayAnnouncement()
    {
        PlayAnnouncement([]);
    }

    /// <summary>
    /// Plays the respawn announcement.
    /// </summary>
    /// <param name="spawnedPlayers">The players that have spawned to take into account for the announcement.</param>
    /// <remarks>
    /// Only the MTF wave has a CASSIE announcement in the Carl Mod game; it counts the living SCPs itself.
    /// </remarks>
    public virtual void PlayAnnouncement(IEnumerable<Player> spawnedPlayers)
    {
    }

    /// <summary>
    /// Plays the respawn animation without spawning the wave.
    /// </summary>
    public void PlayRespawnEffect()
    {
        RespawnEffectsController.ExecuteAllEffects(RespawnEffectsController.EffectType.Selection, TeamType);
    }
}
