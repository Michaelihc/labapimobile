using Generators;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using UnityEngine;

namespace LabApi.Features.Wrappers;

/// <summary>
/// The wrapper representing <see cref="FlickerableLightController">room light controller</see>.
/// </summary>
/// <remarks>
/// Carl Mod uses <see cref="FlickerableLightController"/> (official: <c>RoomLightController</c>). The override color is the
/// controller's warhead light color and override flag, as used by the game's own room color command.
/// </remarks>
public class LightsController
{
    /// <summary>
    /// A reference to all <see cref="LightsController"/> instances currently in the game.
    /// </summary>
    public static IReadOnlyCollection<LightsController> List => Dictionary.Values;

    /// <summary>
    /// Contains all the cached rooms in the game, accessible through their <see cref="FlickerableLightController"/>.
    /// </summary>
    private static Dictionary<FlickerableLightController, LightsController> Dictionary { get; } = [];

    /// <summary>
    /// Gets the controller wrapper from <see cref="Dictionary"/>, or creates a new one if it doesn't exists.
    /// </summary>
    /// <param name="roomLightController">The original light controller.</param>
    /// <returns>The requested light controller wrapper.</returns>
    [return: NotNullIfNotNull(nameof(roomLightController))]
    public static LightsController? Get(FlickerableLightController roomLightController)
    {
        if (roomLightController == null)
        {
            return null;
        }

        return Dictionary.TryGetValue(roomLightController, out LightsController lightController) ? lightController : new LightsController(roomLightController);
    }

    /// <summary>
    /// Initializes the Room wrapper by subscribing to the <see cref="FlickerableLightController"/> events.
    /// </summary>
    [InitializeWrapper]
    internal static void Initialize()
    {
        Dictionary.Clear();
    }

    /// <summary>
    /// Called by the lifecycle patch when a controller is enabled.
    /// </summary>
    /// <param name="controller">The enabled controller.</param>
    internal static void OnAdded(FlickerableLightController controller)
    {
        if (!Dictionary.ContainsKey(controller))
        {
            _ = new LightsController(controller);
        }
    }

    /// <summary>
    /// Called by the lifecycle patch when a controller is disabled or destroyed.
    /// </summary>
    /// <param name="controller">The disabled controller.</param>
    internal static void OnRemoved(FlickerableLightController controller) => Dictionary.Remove(controller);

    /// <summary>
    /// A private constructor to prevent external instantiation.
    /// </summary>
    /// <param name="original">The original object.</param>
    private LightsController(FlickerableLightController original)
    {
        Dictionary.Add(original, this);
        Base = original;
    }

    /// <summary>
    /// The base game object.
    /// </summary>
    public FlickerableLightController Base { get; }

    /// <summary>
    /// The room this controller is assigned to.
    /// </summary>
    /// <remarks>
    /// Carl Mod assigns the room after map generation; before that this is <see langword="null"/>.
    /// </remarks>
    public Room? Room => Room.Get(Base.Room);

    /// <summary>
    /// Gets or sets whether the lights are enabled in this room.
    /// </summary>
    public bool LightsEnabled
    {
        get => Base.LightsEnabled;
        set => Base.SetLights(value);
    }

    /// <summary>
    /// Gets or sets the overriden room light color. Set the value to <see cref="Color.clear"/> to reset override color.
    /// </summary>
    public Color OverrideLightsColor
    {
        get => Base.WarheadLightOverride ? Base.WarheadLightColor : Color.clear;
        set
        {
            if (value == Color.clear)
            {
                Base.WarheadLightOverride = false;
                Base.WarheadLightColor = FlickerableLightController.DefaultWarheadColor;
                return;
            }

            Base.WarheadLightColor = value;
            Base.WarheadLightOverride = true;
        }
    }

    /// <summary>
    /// Blackouts the room for specified duration.
    /// </summary>
    /// <param name="duration">Duration of light shutdown in seconds.</param>
    public void FlickerLights(float duration) => Base.ServerFlickerLights(duration);
}