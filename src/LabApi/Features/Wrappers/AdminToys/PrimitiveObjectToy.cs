using AdminToys;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;
using BasePrimitiveObjectToy = AdminToys.PrimitiveObjectToy;

namespace LabApi.Features.Wrappers;

/// <summary>
/// Wrapper for the <see cref="BasePrimitiveObjectToy"/> class.
/// </summary>
/// <remarks>
/// Carl Mod has no primitive flags SyncVar. Its client adds a collider only when a component of the <c>Scale</c> SyncVar is
/// positive, and static toys keep the transform from their spawn message. The wrapper therefore encodes
/// <see cref="Flags"/> in the transform and the <c>Scale</c> SyncVar:
/// <list type="bullet">
/// <item>Static toys get a positive transform scale. The <c>Scale</c> SyncVar is the positive scale when
/// <see cref="PrimitiveFlags.Collidable"/> is set and the negated scale when it is not.</item>
/// <item>Dynamic toys render the SyncVar scale, so a collidable toy gets a positive scale and a non-collidable one an
/// all-negative scale with a 180° rotation about local X that makes it look the same.</item>
/// <item>Negative (mirrored) scale components are kept exactly: they become a rotation, plus a mirror on local X that
/// none of the Unity primitives can show.</item>
/// </list>
/// <see cref="Rotation"/> and <see cref="Scale"/> report the requested values; the toy's <see cref="AdminToy.Transform"/>
/// holds the encoded ones, so set the transform of such toys through the wrapper.
/// </remarks>
public class PrimitiveObjectToy : AdminToy
{
    /// <summary>
    /// Contains all the primitive object toys, accessible through their <see cref="Base"/>.
    /// </summary>
    public static new Dictionary<BasePrimitiveObjectToy, PrimitiveObjectToy> Dictionary { get; } = [];

    /// <summary>
    /// A reference to all instances of <see cref="PrimitiveObjectToy"/>.
    /// </summary>
    public static new IReadOnlyCollection<PrimitiveObjectToy> List => Dictionary.Values;

    /// <inheritdoc cref="Create(Vector3, Quaternion, Vector3, Transform?, bool)"/>
    public static PrimitiveObjectToy Create(Transform? parent = null, bool networkSpawn = true)
        => Create(Vector3.zero, parent, networkSpawn);

    /// <inheritdoc cref="Create(Vector3, Quaternion, Vector3, Transform?, bool)"/>
    public static PrimitiveObjectToy Create(Vector3 position, Transform? parent = null, bool networkSpawn = true)
        => Create(position, Quaternion.identity, parent, networkSpawn);

    /// <inheritdoc cref="Create(Vector3, Quaternion, Vector3, Transform?, bool)"/>
    public static PrimitiveObjectToy Create(Vector3 position, Quaternion rotation, Transform? parent = null, bool networkSpawn = true)
        => Create(position, rotation, Vector3.one, parent, networkSpawn);

    /// <summary>
    /// Creates a new primitive object toy.
    /// </summary>
    /// <param name="position">The initial local position.</param>
    /// <param name="rotation">The initial local rotation.</param>
    /// <param name="scale">The initial local scale. Negative components are kept (see <see cref="Scale"/>).</param>
    /// <param name="parent">The parent transform.</param>
    /// <param name="networkSpawn">Whether to spawn the toy on the client.</param>
    /// <returns>The created primitive object toy.</returns>
    /// <remarks>
    /// The toy starts static and with <see cref="PrimitiveFlags.Visible"/> | <see cref="PrimitiveFlags.Collidable"/>.
    /// Set <see cref="Flags"/> and <see cref="AdminToy.IsStatic"/> before spawning (<paramref name="networkSpawn"/> =
    /// <see langword="false"/>) to avoid a respawn.
    /// </remarks>
    public static PrimitiveObjectToy Create(Vector3 position, Quaternion rotation, Vector3 scale, Transform? parent = null, bool networkSpawn = true)
    {
        PrimitiveObjectToy toy = Get(Create<BasePrimitiveObjectToy>(position, rotation, Abs(scale), parent));
        toy._signs = Signs(scale);
        toy.ApplyEncoding(rotation);
        toy.WriteTransformSyncVars();

        if (networkSpawn)
        {
            toy.Spawn();
        }

        return toy;
    }

    /// <summary>
    /// Gets the primitive object toy wrapper from the <see cref="Dictionary"/> or creates a new one if it doesn't exist and the provided <see cref="BasePrimitiveObjectToy"/> was not <see langword="null"/>.
    /// </summary>
    /// <param name="primitiveObjectToy">The <see cref="Base"/> of the primitive object toy.</param>
    /// <returns>The requested primitive object toy or <see langword="null"/>.</returns>
    [return: NotNullIfNotNull(nameof(primitiveObjectToy))]
    public static PrimitiveObjectToy? Get(BasePrimitiveObjectToy? primitiveObjectToy)
    {
        if (primitiveObjectToy == null)
        {
            return null;
        }

        return Dictionary.TryGetValue(primitiveObjectToy, out PrimitiveObjectToy item) ? item : (PrimitiveObjectToy)CreateAdminToyWrapper(primitiveObjectToy);
    }

    /// <summary>
    /// Tries to get the primitive object toy wrapper from the <see cref="Dictionary"/>.
    /// </summary>
    /// <param name="basePrimitiveObjectToy">The <see cref="Base"/> of the primitive object toy.</param>
    /// <param name="primitiveObjectToy">The requested primitive object toy.</param>
    /// <returns><see langword="True"/> if the primitive object exists, otherwise <see langword="false"/>.</returns>
    public static bool TryGet(BasePrimitiveObjectToy? basePrimitiveObjectToy, [NotNullWhen(true)] out PrimitiveObjectToy? primitiveObjectToy)
    {
        primitiveObjectToy = Get(basePrimitiveObjectToy);
        return primitiveObjectToy != null;
    }

    /// <summary>
    /// An internal constructor to prevent external instantiation.
    /// </summary>
    /// <param name="basePrimitiveObjectToy">The base <see cref="BasePrimitiveObjectToy"/> object.</param>
    internal PrimitiveObjectToy(BasePrimitiveObjectToy basePrimitiveObjectToy)
        : base(basePrimitiveObjectToy)
    {
        Base = basePrimitiveObjectToy;
        _color = basePrimitiveObjectToy.MaterialColor;

        // A toy created elsewhere is taken as it is: the client adds a collider when a Scale component is positive.
        Vector3 syncScale = basePrimitiveObjectToy.Scale;
        _flags = syncScale.x > 0f || syncScale.y > 0f || syncScale.z > 0f || syncScale == Vector3.zero
            ? PrimitiveFlags.Collidable | PrimitiveFlags.Visible
            : PrimitiveFlags.Visible;
        _signs = Signs(basePrimitiveObjectToy.transform.localScale);
        _compensation = Quaternion.identity;

        if (CanCache)
        {
            Dictionary.Add(basePrimitiveObjectToy, this);
        }
    }

    /// <summary>
    /// The <see cref="BasePrimitiveObjectToy"/> object.
    /// </summary>
    public new BasePrimitiveObjectToy Base { get; }

    /// <summary>
    /// Gets or sets the <see cref="PrimitiveType"/>.
    /// </summary>
    public PrimitiveType Type
    {
        get => Base.PrimitiveType;
        set => Base.NetworkPrimitiveType = value;
    }

    /// <summary>
    /// Gets or sets the material <see cref="UnityEngine.Color"/>.
    /// </summary>
    /// <remarks>
    /// While <see cref="Flags"/> lacks <see cref="PrimitiveFlags.Visible"/> clients receive this color with zero alpha.
    /// </remarks>
    public Color Color
    {
        get => _color;
        set
        {
            _color = value;
            PushColor();
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Returns the requested rotation; the transform may carry an extra 180° turn that encodes <see cref="Flags"/> or a
    /// negative <see cref="Scale"/>.
    /// </remarks>
    public override Quaternion Rotation
    {
        get => Transform.localRotation * Quaternion.Inverse(_compensation);
        set
        {
            Transform.localRotation = value * _compensation;
            OnTransformChanged();
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Negative components are supported. The transform holds the encoded scale (see the class remarks), so read the
    /// scale through this property.
    /// </remarks>
    public override Vector3 Scale
    {
        get => Vector3.Scale(Abs(Transform.localScale), _signs);
        set
        {
            Quaternion rotation = Rotation;
            _signs = Signs(value);
            Transform.localScale = Abs(value);
            ApplyEncoding(rotation);
            OnTransformChanged();
        }
    }

    /// <summary>
    /// Gets or sets the <see cref="PrimitiveFlags"/>.
    /// </summary>
    /// <remarks>
    /// Setting flags to <see cref="PrimitiveFlags.None"/> is similar to having an empty object which is useful as a root object other toys parent to.
    /// <para>
    /// Carl Mod has no primitive flags SyncVar; they are emulated with what its client understands (see the class
    /// remarks). Without <see cref="PrimitiveFlags.Visible"/> the color is sent with zero alpha, which still renders as a
    /// transparent primitive. The client creates the collider only when it builds the primitive, so changing
    /// <see cref="PrimitiveFlags.Collidable"/> on a spawned toy respawns it.
    /// </para>
    /// </remarks>
    public PrimitiveFlags Flags
    {
        get => _flags;
        set
        {
            PrimitiveFlags changed = _flags ^ value;
            if (changed == PrimitiveFlags.None)
            {
                return;
            }

            Quaternion rotation = Rotation;
            _flags = value;

            if ((changed & PrimitiveFlags.Visible) != 0)
            {
                PushColor();
            }

            if ((changed & PrimitiveFlags.Collidable) != 0)
            {
                ApplyEncoding(rotation);
                WriteTransformSyncVars();

                // Rebuild the server-side primitive so server raycasts match (Start builds it if it has not run yet).
                if (Base._spawnedPrimitve != null)
                {
                    Base.SetPrimitive(Base.PrimitiveType, Base.PrimitiveType);
                }

                Respawn();
            }
        }
    }

    /// <inheritdoc />
    public override string ToString()
    {
        return $"[PrimitiveObjectToy: Type={Type}, Color={Color}, Flags={Flags}]";
    }

    /// <inheritdoc />
    private protected override void WriteTransformSyncVars()
    {
        Transform transform = Transform;
        transform.GetPositionAndRotation(out Vector3 position, out Quaternion rotation);
        Base.NetworkPosition = position;
        Base.NetworkRotation = new LowPrecisionQuaternion(rotation);

        // Static toys render the spawn message transform, so their Scale SyncVar only decides the collider.
        Vector3 scale = transform.localScale;
        Base.NetworkScale = Base.IsStatic && (_flags & PrimitiveFlags.Collidable) == 0 ? -Abs(scale) : scale;
    }

    /// <inheritdoc />
    private protected override void OnStaticChanged()
    {
        // _compensation still holds the encoding for the previous static state.
        Quaternion rotation = Transform.localRotation * Quaternion.Inverse(_compensation);
        ApplyEncoding(rotation);
        WriteTransformSyncVars();
    }

    /// <summary>
    /// The color requested through <see cref="Color"/>.
    /// </summary>
    private Color _color;

    /// <summary>
    /// The emulated <see cref="Flags"/>.
    /// </summary>
    private PrimitiveFlags _flags;

    /// <summary>
    /// The signs (+1 or -1) of the requested <see cref="Scale"/> components.
    /// </summary>
    private Vector3 _signs = Vector3.one;

    /// <summary>
    /// The rotation appended to the requested rotation in the transform.
    /// </summary>
    private Quaternion _compensation = Quaternion.identity;

    /// <summary>
    /// Returns the component-wise absolute value of a vector.
    /// </summary>
    private static Vector3 Abs(Vector3 value) => new(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));

    /// <summary>
    /// Returns +1 or -1 per component (zero counts as positive).
    /// </summary>
    private static Vector3 Signs(Vector3 value) => new(value.x < 0f ? -1f : 1f, value.y < 0f ? -1f : 1f, value.z < 0f ? -1f : 1f);

    /// <summary>
    /// Writes the encoded transform rotation and scale for the requested rotation, <see cref="Scale"/> signs,
    /// <see cref="Flags"/> and static state.
    /// </summary>
    /// <param name="rotation">The requested (logical) local rotation.</param>
    private void ApplyEncoding(Quaternion rotation)
    {
        // Dynamic toys render the Scale SyncVar, and the client only skips the collider when no component is positive.
        bool negative = !Base.IsStatic && (_flags & PrimitiveFlags.Collidable) == 0;
        float target = negative ? -1f : 1f;

        // The requested transform is R * diag(signs) * |s|; the encoded one is R * Q * (target * |s|).
        // diag(signs * target) with an even number of negative entries is a 180° rotation; with an odd number it is that
        // rotation times a mirror on local X, which every Unity primitive is symmetric under (the Plane and Quad normals
        // lie in the YZ plane, and Unity flips the winding of mirrored renderers).
        float sx = _signs.x * target;
        float sy = _signs.y * target;
        float sz = _signs.z * target;
        if (sx * sy * sz < 0f)
        {
            sx = -sx;
        }

        _compensation = sx > 0f
            ? (sy > 0f ? Quaternion.identity : RotationX180)
            : (sy > 0f ? RotationY180 : RotationZ180);

        Vector3 magnitude = Abs(Transform.localScale);
        Transform.localRotation = rotation * _compensation;
        Transform.localScale = negative ? -magnitude : magnitude;
    }

    /// <summary>
    /// Sends <see cref="Color"/>, hidden through zero alpha when <see cref="PrimitiveFlags.Visible"/> is not set.
    /// </summary>
    private void PushColor()
    {
        Color color = _color;
        if ((_flags & PrimitiveFlags.Visible) == 0)
        {
            color.a = 0f;
        }

        Base.NetworkMaterialColor = color;
    }

    /// <summary>
    /// An internal method to remove itself from the cache when the base object is destroyed.
    /// </summary>
    internal override void OnRemove()
    {
        base.OnRemove();
        Dictionary.Remove(Base);
    }

    private static readonly Quaternion RotationX180 = new(1f, 0f, 0f, 0f);

    private static readonly Quaternion RotationY180 = new(0f, 1f, 0f, 0f);

    private static readonly Quaternion RotationZ180 = new(0f, 0f, 1f, 0f);
}
