using Generators;
using Mirror;
using PlayerRoles;
using PlayerRoles.PlayableScps.Scp049;
using PlayerRoles.PlayableScps.Scp049.Zombies;
using PlayerRoles.Ragdolls;
using PlayerStatsSystem;
using System.Collections.Generic;
using UnityEngine;

namespace LabApi.Features.Wrappers;

/// <summary>
/// The wrapper representing <see cref="BasicRagdoll">basic ragdolls</see>.
/// </summary>
public class Ragdoll
{
    /// <summary>
    /// Contains all the cached ragdolls in the game, accessible through their <see cref="BasicRagdoll"/>.
    /// </summary>
    public static Dictionary<BasicRagdoll, Ragdoll> Dictionary { get; } = [];

    /// <summary>
    /// A reference to all <see cref="Ragdoll"/> instances currently in the game.
    /// </summary>
    public static IReadOnlyCollection<Ragdoll> List => Dictionary.Values;

    /// <summary>
    /// Spawns a new ragdoll based on a specified player and damage handler.
    /// </summary>
    /// <param name="player">Player for ragdoll template.</param>
    /// <param name="handler">Handler that is shown as a death cause.</param>
    /// <returns>New ragdoll.</returns>
    public static Ragdoll? SpawnRagdoll(Player player, DamageHandlerBase handler) => SpawnRagdoll(player.Role, player.Position, player.Rotation, handler, player.DisplayName, owner: player.ReferenceHub);

    /// <summary>
    /// Attempts to spawn a ragdoll from specified role. Ragdoll is not created if specified role doesn't have any ragdoll model available.
    /// </summary>
    /// <param name="role">Target role type.</param>
    /// <param name="position">Spawn position.</param>
    /// <param name="rotation">Spawn rotation.</param>
    /// <param name="handler">Damage handler of the death cause.</param>
    /// <param name="nickname">Nickname that is visible when hovering over.</param>
    /// <param name="owner">The owner of this ragdoll.</param>
    /// <returns>Ragdoll object or <see langword="null"/>.</returns>
    /// <remarks>
    /// Carl Mod ragdolls have no synchronized scale or serial, so those official parameters are absent.
    /// </remarks>
    public static Ragdoll? SpawnRagdoll(RoleTypeId role, Vector3 position, Quaternion rotation, DamageHandlerBase handler, string nickname, ReferenceHub? owner = null)
    {
        BasicRagdoll? ragdoll = CreateRagdoll(role, position, rotation, handler, nickname, owner, NetworkTime.time);
        return ragdoll == null ? null : Get(ragdoll);
    }

    /// <summary>
    /// Gets the ragdoll wrapper from the <see cref="Dictionary"/>, or creates a new one if it doesn't exist.
    /// </summary>
    /// <param name="ragdoll">The ragdoll.</param>
    /// <returns>The requested ragdoll.</returns>
    public static Ragdoll Get(BasicRagdoll ragdoll) => Dictionary.TryGetValue(ragdoll, out Ragdoll rag) ? rag : new Ragdoll(ragdoll);

    /// <summary>
    /// Initializes the <see cref="Ragdoll"/> class to subscribe to <see cref="RagdollManager"/> events and handle the ragdoll caching.
    /// </summary>
    [InitializeWrapper]
    internal static void Initialize()
    {
        Dictionary.Clear();

        RagdollManager.OnRagdollSpawned += RagdollSpawned;
        RagdollManager.OnRagdollRemoved += RagdollRemoved;
    }

    /// <summary>
    /// Instantiates and spawns a ragdoll of the given role, mirroring <see cref="RagdollManager.ServerSpawnRagdoll"/>.
    /// </summary>
    private static BasicRagdoll? CreateRagdoll(RoleTypeId role, Vector3 position, Quaternion rotation, DamageHandlerBase handler, string nickname, ReferenceHub? owner, double creationTime)
    {
        if (!NetworkServer.active || !PlayerRoleLoader.TryGetRoleTemplate(role, out PlayerRoleBase template) || template is not IRagdollRole ragdollRole || ragdollRole.Ragdoll == null)
        {
            return null;
        }

        GameObject gameObject = Object.Instantiate(ragdollRole.Ragdoll.gameObject);
        if (!gameObject.TryGetComponent(out BasicRagdoll ragdoll))
        {
            Object.Destroy(gameObject);
            return null;
        }

        ragdoll.NetworkInfo = new RagdollData(owner!, handler, role, position, rotation, nickname, creationTime);
        gameObject.transform.SetPositionAndRotation(position, rotation);
        NetworkServer.Spawn(gameObject);
        return ragdoll;
    }

    /// <summary>
    /// Event method for <see cref="RagdollManager.OnRagdollSpawned"/>.
    /// </summary>
    /// <param name="ragdoll">New ragdoll.</param>
    private static void RagdollSpawned(BasicRagdoll ragdoll) => _ = Get(ragdoll);

    /// <summary>
    /// Event method for <see cref="RagdollManager.OnRagdollRemoved"/>.
    /// </summary>
    /// <param name="ragdoll">Destroyed ragdoll.</param>
    private static void RagdollRemoved(BasicRagdoll ragdoll) => Dictionary.Remove(ragdoll);

    /// <summary>
    /// A private constructor to prevent external instantiation.
    /// </summary>
    /// <param name="ragdoll">The ragdoll component.</param>
    private Ragdoll(BasicRagdoll ragdoll)
    {
        Base = ragdoll;

        if (CanCache)
        {
            Dictionary[ragdoll] = this;
        }
    }

    /// <summary>
    /// Gets the <see cref="BasicRagdoll"/> of the ragdoll.
    /// </summary>
    public BasicRagdoll Base { get; private set; }

    /// <summary>
    /// Gets whether the base room instance was destroyed.
    /// </summary>
    public bool IsDestroyed => Base == null;

    /// <summary>
    /// Gets or sets the role info of the ragdoll.
    /// <para>This does NOT change the ragdoll visually.</para>
    /// </summary>
    public RoleTypeId Role
    {
        get => Base.NetworkInfo.RoleType;
        set => Base.NetworkInfo = With(value, Base.Info.Nickname, Base.Info.Handler, Base.Info.StartPosition, Base.Info.StartRotation);
    }

    /// <summary>
    /// Gets or sets the ragdoll nickname.
    /// </summary>
    public string Nickname
    {
        get => Base.NetworkInfo.Nickname;
        set => Base.NetworkInfo = With(Base.Info.RoleType, value, Base.Info.Handler, Base.Info.StartPosition, Base.Info.StartRotation);
    }

    /// <summary>
    /// Gets or sets the ragdoll damage handler, providing death cause.
    /// </summary>
    public DamageHandlerBase DamageHandler
    {
        get => Base.NetworkInfo.Handler;
        set => Base.NetworkInfo = With(Base.Info.RoleType, Base.Info.Nickname, value, Base.Info.StartPosition, Base.Info.StartRotation);
    }

    /// <summary>
    /// Gets or sets the position of the ragdoll.
    /// </summary>
    /// <remarks>
    /// Carl Mod clients only read the ragdoll pose when it spawns, so setting this respawns the ragdoll for observers.
    /// </remarks>
    public Vector3 Position
    {
        get => Base.transform.position;
        set
        {
            Base.transform.position = value;
            Base.NetworkInfo = With(Base.Info.RoleType, Base.Info.Nickname, Base.Info.Handler, value, Base.transform.rotation);
            Resync();
        }
    }

    /// <summary>
    /// Gets or sets the rotation of the ragdoll.
    /// </summary>
    /// <remarks>
    /// Carl Mod clients only read the ragdoll pose when it spawns, so setting this respawns the ragdoll for observers.
    /// </remarks>
    public Quaternion Rotation
    {
        get => Base.transform.rotation;
        set
        {
            Base.transform.rotation = value;
            Base.NetworkInfo = With(Base.Info.RoleType, Base.Info.Nickname, Base.Info.Handler, Base.transform.position, value);
            Resync();
        }
    }

    /// <summary>
    /// Gets or sets whether the corpse is consumed.
    /// </summary>
    public bool IsConsumed
    {
        get => ZombieConsumeAbility.ConsumedRagdolls.Contains(Base);
        set
        {
            if (value)
            {
                ZombieConsumeAbility.ConsumedRagdolls.Add(Base);
                return;
            }

            ZombieConsumeAbility.ConsumedRagdolls.Remove(Base);
        }
    }

    /// <summary>
    /// Whether to cache this wrapper.
    /// </summary>
    protected bool CanCache => !IsDestroyed && Base.isActiveAndEnabled;

    /// <summary>
    /// Gets whether the ragdoll is revivable by SCP-049 player.
    /// </summary>
    /// <param name="scp049">Player who is SCP-049.</param>
    /// <returns>True if corpse is revivable. False if it isn't or specified player is not SCP-049.</returns>
    public bool IsRevivableBy(Player scp049)
    {
        if (scp049.RoleBase is not Scp049Role role)
        {
            return false;
        }

        if (!role.SubroutineModule.TryGetSubroutine(out Scp049ResurrectAbility ability))
        {
            return false;
        }

        return ability.CheckRagdoll(Base);
    }

    /// <summary>
    /// Destroys this ragdoll.
    /// </summary>
    public void Destroy()
    {
        NetworkServer.Destroy(Base.gameObject);
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"[Ragdoll: Nickname={Nickname}, Role={Role}, DamageHandler={DamageHandler}, Position={Position}, Rotation={Rotation}, IsConsumed={IsConsumed}]";
    }

    private RagdollData With(RoleTypeId role, string nickname, DamageHandlerBase handler, Vector3 position, Quaternion rotation)
        => new(Base.Info.OwnerHub, handler, role, position, rotation, nickname, Base.Info.CreationTime);

    private void Resync()
    {
        GameObject gameObject = Base.gameObject;
        if (!NetworkServer.active || !gameObject.TryGetComponent(out NetworkIdentity identity) || identity.netId == 0)
        {
            return;
        }

        NetworkServer.UnSpawn(gameObject);
        NetworkServer.Spawn(gameObject);
    }
}
