using AdminToys;
using Generators;
using Mirror;
using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;

namespace LabApi.Features.Wrappers;

/// <summary>
/// Wrapper for the <see cref="AdminToyBase"/> class.
/// </summary>
public class AdminToy
{
    /// <summary>
    /// Contains all the handlers for constructing wrappers for the associated base game types.
    /// </summary>
    private static readonly Dictionary<Type, Func<AdminToyBase, AdminToy?>> _typeWrappers = [];

    /// <summary>
    /// Contains all the cached admin toys, accessible through their <see cref="Base"/>.
    /// </summary>
    public static Dictionary<AdminToyBase, AdminToy> Dictionary { get; } = [];

    /// <summary>
    /// A reference to all instances of <see cref="AdminToy"/>.
    /// </summary>
    public static IReadOnlyCollection<AdminToy> List => Dictionary.Values;

    /// <summary>
    /// Gets the admin toy wrapper from the <see cref="Dictionary"/> or creates a new one if it doesn't exist and the provided <see cref="AdminToyBase"/> was not <see langword="null"/>.
    /// </summary>
    /// <param name="adminToyBase">The <see cref="Base"/> of the admin toy.</param>
    /// <returns>The requested admin toy or <see langword="null"/>.</returns>
    [return: NotNullIfNotNull(nameof(adminToyBase))]
    public static AdminToy? Get(AdminToyBase? adminToyBase)
    {
        if (adminToyBase == null)
        {
            return null;
        }

        return Dictionary.TryGetValue(adminToyBase, out AdminToy item) ? item : CreateAdminToyWrapper(adminToyBase);
    }

    /// <summary>
    /// Tries to get the admin toy wrapper from the <see cref="Dictionary"/>.
    /// </summary>
    /// <param name="adminToyBase">The <see cref="Base"/> of the admin toy.</param>
    /// <param name="adminToy">The requested admin toy.</param>
    /// <returns>True if the admin toy exists, otherwise <see langword="false"/>.</returns>
    public static bool TryGet(AdminToyBase? adminToyBase, [NotNullWhen(true)] out AdminToy? adminToy)
    {
        adminToy = Get(adminToyBase);
        return adminToy != null;
    }

    /// <summary>
    /// Initializes the <see cref="AdminToy"/> class.
    /// </summary>
    /// <remarks>
    /// Carl Mod has no <c>AdminToyBase.OnAdded</c>/<c>OnRemoved</c>; <c>Events/Patches/Internal/CoreAdminToyLifecycle.cs</c>
    /// calls <see cref="AddAdminToy"/> when a toy is network spawned and <see cref="RemoveAdminToy"/> when it is destroyed.
    /// </remarks>
    [InitializeWrapper]
    internal static void Initialize()
    {
        Register<AdminToys.PrimitiveObjectToy>(static x => new PrimitiveObjectToy(x));
        Register<AdminToys.LightSourceToy>(static x => new LightSourceToy(x));
        Register<ShootingTarget>(static x => new ShootingTargetToy(x));
    }

    /// <summary>
    /// Instantiates a new base game admin toy object.
    /// </summary>
    /// <typeparam name="T">The base game admin toy type.</typeparam>
    /// <param name="position">The initial local position.</param>
    /// <param name="rotation">The initial local rotation.</param>
    /// <param name="scale">The initial local scale.</param>
    /// <param name="parent">The parent transform.</param>
    /// <returns>The instantiated admin toy.</returns>
    /// <remarks>
    /// The toy starts with <see cref="IsStatic"/> enabled; the first <see cref="Position"/>, <see cref="Rotation"/>,
    /// <see cref="Scale"/> or <see cref="Parent"/> change after it is spawned switches it to dynamic unless
    /// <see cref="IsStatic"/> was set explicitly.
    /// </remarks>
    protected static T Create<T>(Vector3 position, Quaternion rotation, Vector3 scale, Transform? parent)
        where T : AdminToyBase
    {
        if (PrefabCache<T>.Prefab == null)
        {
            T? found = null;
            foreach (GameObject prefab in NetworkClient.prefabs.Values)
            {
                if (prefab.TryGetComponent(out found))
                {
                    break;
                }
            }

            if (found == null)
            {
                throw new InvalidOperationException($"No prefab in NetworkClient.prefabs has component type {typeof(T)}");
            }

            PrefabCache<T>.Prefab = found;
        }

        T instance = UnityEngine.Object.Instantiate(PrefabCache<T>.Prefab, parent);
        Transform transform = instance.transform;
        transform.SetLocalPositionAndRotation(position, rotation);
        transform.localScale = scale;

        // Static toys cost nothing per frame on the server or on the phone; the wrapper reverts to dynamic on the first move.
        instance.NetworkIsStatic = true;
        PendingAutoStatic = instance;
        SyncTransform(instance);
        return instance;
    }

    /// <summary>
    /// Creates a new wrapper from the base admin toy object.
    /// </summary>
    /// <param name="adminToyBase">The base object.</param>
    /// <returns>The newly created wrapper.</returns>
    protected static AdminToy CreateAdminToyWrapper(AdminToyBase adminToyBase)
    {
        if (!_typeWrappers.TryGetValue(adminToyBase.GetType(), out Func<AdminToyBase, AdminToy?> handler))
        {
            Console.Logger.InternalWarn($"Backing up to the default AdminToy constructor. Missing constructor handler for type {adminToyBase.GetType()}");
            return new AdminToy(adminToyBase);
        }

        AdminToy? wrapper = handler.Invoke(adminToyBase);
        if (wrapper == null)
        {
            Console.Logger.InternalWarn($"Backing up to the default AdminToy constructor. A handler returned null for type {adminToyBase.GetType()}");
            return new AdminToy(adminToyBase);
        }

        return wrapper;
    }

    /// <summary>
    /// A private method to handle the creation of new admin toys in the server.
    /// </summary>
    /// <param name="adminToyBase">The created <see cref="AdminToyBase"/> instance.</param>
    internal static void AddAdminToy(AdminToyBase adminToyBase)
    {
        try
        {
            if (!Dictionary.ContainsKey(adminToyBase))
            {
                _ = CreateAdminToyWrapper(adminToyBase);
            }
        }
        catch (Exception e)
        {
            Console.Logger.InternalError($"Failed to handle admin toy creation with error: {e}");
        }
    }

    /// <summary>
    /// A private method to handle the removal of admin toys from the server.
    /// </summary>
    /// <param name="adminToyBase">The to be destroyed <see cref="AdminToyBase"/> instance.</param>
    internal static void RemoveAdminToy(AdminToyBase adminToyBase)
    {
        try
        {
            if (Dictionary.Remove(adminToyBase, out AdminToy adminToy))
            {
                adminToy.OnRemove();
            }
        }
        catch (Exception e)
        {
            Console.Logger.InternalError($"Failed to handle admin toy destruction with error: {e}");
        }
    }

    /// <summary>
    /// A private method to handle the addition of wrapper handlers.
    /// </summary>
    /// <typeparam name="T">The derived base game type to handle.</typeparam>
    /// <param name="constructor">A handler to construct the wrapper with the base game instance.</param>
    private static void Register<T>(Func<T, AdminToy?> constructor)
        where T : AdminToyBase
    {
        _typeWrappers.Add(typeof(T), x => constructor((T)x));
    }

    /// <summary>
    /// A protected constructor to prevent external instantiation.
    /// </summary>
    /// <param name="adminToyBase">The base object.</param>
    protected AdminToy(AdminToyBase adminToyBase)
    {
        Base = adminToyBase;

        if (ReferenceEquals(PendingAutoStatic, adminToyBase))
        {
            _autoStatic = true;
            PendingAutoStatic = null;
        }

        if (CanCache)
        {
            Dictionary.Add(adminToyBase, this);
        }
    }

    /// <summary>
    /// The <see cref="AdminToyBase">base</see> object.
    /// </summary>
    public AdminToyBase Base { get; }

    /// <summary>
    /// The <see cref="UnityEngine.GameObject"/>.
    /// </summary>
    public GameObject GameObject => Base.gameObject;

    /// <summary>
    /// The admin toys <see cref="UnityEngine.Transform"/>.
    /// </summary>
    public Transform Transform => Base.transform;

    /// <summary>
    /// Whether the <see cref="Base"/> was destroyed.
    /// </summary>
    /// <remarks>
    /// A destroyed object may not be used.
    /// </remarks>
    public bool IsDestroyed => Base == null;

    /// <summary>
    /// Gets or sets the local position of the admin toy.
    /// Position is relative to its parent if it has one, otherwise its the world position.
    /// </summary>
    /// <remarks>
    /// If <see cref="IsStatic"/> is <see langword="true"/> client wont update its position.
    /// </remarks>
    public virtual Vector3 Position
    {
        get => Transform.localPosition;
        set
        {
            Transform.localPosition = value;
            OnTransformChanged();
        }
    }

    /// <summary>
    /// Gets or sets the local rotation of the admin toy.
    /// Rotation is relative to its parent if it has one, otherwise its the world rotation.
    /// </summary>
    /// <remarks>
    /// If <see cref="IsStatic"/> is <see langword="true"/> client wont update its rotation.
    /// </remarks>
    public virtual Quaternion Rotation
    {
        get => Transform.localRotation;
        set
        {
            Transform.localRotation = value;
            OnTransformChanged();
        }
    }

    /// <summary>
    /// Gets or sets the local scale of the admin toy.
    /// Scale is relative to its parent if it has one, otherwise its the world scale.
    /// </summary>
    /// <remarks>
    /// If <see cref="IsStatic"/> is <see langword="true"/> client wont update its scale.
    /// </remarks>
    public virtual Vector3 Scale
    {
        get => Transform.localScale;
        set
        {
            Transform.localScale = value;
            OnTransformChanged();
        }
    }

    /// <summary>
    /// Gets or sets the parent of the admin toy.
    /// </summary>
    /// <remarks>
    /// If the parent object contains a <see cref="NetworkIdentity"/> component and has been <see cref="NetworkServer.Spawn(GameObject, GameObject)"/> the parent is synced with the client.
    /// <para>
    /// Can be used even if <see cref="IsStatic"/> is <see langword="true"/>.
    /// When changing parent the toys relative <see cref="Position"/>, <see cref="Rotation"/> and <see cref="Scale"/> are retained.
    /// Note that if the parent has <see cref="NetworkServer.Destroy"/> called on it this object automatically has <see cref="NetworkServer.Destroy"/> called on itself.
    /// To prevent destruction make sure you unparent it before that happens.
    /// </para>
    /// <para>
    /// Carl Mod clients have no toy parenting: the parent only exists on the server and clients receive the resulting
    /// world position and rotation (and the local scale) while the toy is not static.
    /// </para>
    /// </remarks>
    public Transform? Parent
    {
        get => Transform.parent;
        set
        {
            if (Transform.parent == value)
            {
                return;
            }

            Transform.SetParent(value, false);
            OnTransformChanged();
        }
    }

    /// <summary>
    /// Gets or sets the movement smoothing value.
    /// </summary>
    /// <remarks>
    /// Smooths the transitions between positions, rotations and scales.
    /// Higher values means more smoothing.
    /// 0 means no movement smoothing so it will snap between positions, rotations and scales.
    /// Smoothing does not work with parenting changes so changing parent will always cause a teleport.
    /// </remarks>
    public byte MovementSmoothing
    {
        get => (byte)(Base.MovementSmoothing == 0 ? 0 : 256 - Base.MovementSmoothing);
        set => Base.NetworkMovementSmoothing = (byte)(value == 0 ? 0 : 256 - value);
    }

    /// <summary>
    /// Gets or sets whether the admin toy is static.
    /// This should be enabled on as many toys possible to increase performance.
    /// Static is only applies to the local transformations so parenting to something that moves will still cause it to move while retaining the performance boost.
    /// </summary>
    /// <remarks>
    /// A static admin toy will not process <see cref="Position"/>, <see cref="Rotation"/> or <see cref="Scale"/> on both server and client drastically increasing performance.
    /// <see cref="Parent"/> can still be used even if static is <see langword="true"/>.
    /// </remarks>
    public bool IsStatic
    {
        get => Base.IsStatic;
        set
        {
            _autoStatic = false;
            if (!value && Base.IsStatic)
            {
                // The Carl Mod server only refreshes the transform SyncVars of dynamic toys; send the current state with the switch.
                SyncTransform(Base);
            }

            Base.NetworkIsStatic = value;
        }
    }

    /// <summary>
    /// Time interval in seconds for sending updated values to the client.
    /// 0 means update every frame while 0.5 means update every 500ms.
    /// Lower values increase network usage but mean the client receives the most up to date state from the server.
    /// </summary>
    public float SyncInterval
    {
        get => Base.syncInterval;
        set => Base.syncInterval = value;
    }

    /// <summary>
    /// Whether to cache this wrapper.
    /// </summary>
    protected bool CanCache => !IsDestroyed && Base.isActiveAndEnabled;

    /// <summary>
    /// Spawns the toy on the client.
    /// </summary>
    /// <remarks>
    /// Spawn won't cascade to children toy objects, so if they are not spawned you have to call spawn on all of them.
    /// </remarks>
    public void Spawn()
    {
        if (IsSpawned)
        {
            return;
        }

        SyncTransform(Base);
        NetworkServer.Spawn(GameObject);

        // The spawn message already carried the full state; do not send the pre-spawn SyncVar writes again as a delta.
        Base.ClearAllDirtyBits();
    }

    /// <summary>
    /// Destroys the toy on server and client.
    /// </summary>
    /// <remarks>
    /// Cascades to all children toy objects, be sure to unparent children if you don't intend to destroy them.
    /// </remarks>
    public void Destroy() => NetworkServer.Destroy(GameObject);

    /// <summary>
    /// An internal method to remove itself from the cache when the base object is destroyed.
    /// </summary>
    internal virtual void OnRemove()
    {
    }

    /// <summary>
    /// Gets whether the toy is currently network spawned.
    /// </summary>
    protected bool IsSpawned => Base.netId != 0 && NetworkServer.spawned.ContainsKey(Base.netId);

    /// <summary>
    /// Respawns the toy on clients so they rebuild it from its current state.
    /// </summary>
    /// <remarks>
    /// Used for state the Carl Mod client only reads when the toy is created (the primitive collider).
    /// </remarks>
    protected void Respawn()
    {
        if (!IsSpawned)
        {
            return;
        }

        NetworkServer.UnSpawn(GameObject);
        Spawn();
    }

    /// <summary>
    /// Writes the transform to the toy's SyncVars. Mirror skips unchanged values, so equal state is never dirtied.
    /// </summary>
    /// <param name="toy">The toy to synchronize.</param>
    /// <remarks>
    /// Carl Mod's <see cref="AdminToyBase"/> only does this every frame for dynamic toys and has no <c>OnStartServer</c> sync,
    /// so static toys would otherwise keep stale SyncVars. The client derives primitive colliders from <c>Scale</c>.
    /// </remarks>
    private protected static void SyncTransform(AdminToyBase toy)
    {
        Transform transform = toy.transform;
        transform.GetPositionAndRotation(out Vector3 position, out Quaternion rotation);
        toy.NetworkPosition = position;
        toy.NetworkRotation = new LowPrecisionQuaternion(rotation);
        toy.NetworkScale = transform.localScale;
    }

    /// <summary>
    /// Keeps the SyncVars current after a wrapper transform change and leaves automatic static mode once spawned.
    /// </summary>
    private protected void OnTransformChanged()
    {
        if (_autoStatic && Base.IsStatic && IsSpawned)
        {
            _autoStatic = false;
            Base.NetworkIsStatic = false;
        }

        SyncTransform(Base);
    }

    /// <summary>
    /// Gets or sets the toy most recently instantiated by <see cref="Create{T}"/>, so its wrapper starts in automatic static mode.
    /// </summary>
    private static AdminToyBase? PendingAutoStatic { get; set; }

    /// <summary>
    /// Whether <see cref="IsStatic"/> was enabled by <see cref="Create{T}"/> rather than by the plugin.
    /// </summary>
    private bool _autoStatic;

#pragma warning disable SA1204 // Static elements should appear before instance elements
    /// <summary>
    /// Static prefab cache used to speed up prefab search.
    /// </summary>
    /// <typeparam name="T">The base game component type of the prefab.</typeparam>
    internal static class PrefabCache<T>
        where T : NetworkBehaviour
    {
        /// <summary>
        /// Cached prefab instance for type T.
        /// </summary>
        public static T? Prefab { get; set; } = null;
    }
#pragma warning restore SA1204 // Static elements should appear before instance elements
}
