using System;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;
using Object = UnityEngine.Object;

namespace LabApi.Events.Patches.Internal;

/// <summary>
/// A server-side component that reports the destruction of a game object to a wrapper cache.
/// </summary>
/// <remarks>
/// Carl Mod structures, windows, tesla gates and pocket teleports have no <c>OnDestroy</c> method to patch and no static
/// removal event. This plain <see cref="MonoBehaviour"/> is never networked, so clients are unaffected.
/// </remarks>
internal sealed class FacilityDestroyNotifier : MonoBehaviour
{
    private Object? _key;

    private Action<Object>? _callback;

    private Action<Object>? _started;

    /// <summary>
    /// Attaches a notifier that calls <paramref name="callback"/> with <paramref name="key"/> when <paramref name="gameObject"/> is destroyed.
    /// </summary>
    /// <param name="gameObject">The object to observe.</param>
    /// <param name="key">The base game component passed back to the callback.</param>
    /// <param name="callback">A static callback that removes the wrapper.</param>
    /// <param name="started">
    /// An optional static callback run from this component's <c>Start</c>, once the object is active and enabled. Used to
    /// register wrappers of components that have only an <c>Awake</c> (where <c>isActiveAndEnabled</c> is still false).
    /// </param>
    internal static void Attach(GameObject gameObject, Object key, Action<Object> callback, Action<Object>? started = null)
    {
        FacilityDestroyNotifier notifier = gameObject.AddComponent<FacilityDestroyNotifier>();
        notifier._key = key;
        notifier._callback = callback;
        notifier._started = started;
    }

    private void Start()
    {
        if (_started == null || _key == null)
        {
            return;
        }

        try
        {
            _started(_key);
        }
        catch (Exception e)
        {
            Logger.Error($"[FacilityDestroyNotifier] Failed to add the wrapper of {_key.GetType().Name}: {e}");
        }
    }

    private void OnDestroy()
    {
        if (_callback == null || ReferenceEquals(_key, null))
        {
            return;
        }

        try
        {
            _callback(_key);
        }
        catch (Exception e)
        {
            Logger.Error($"[FacilityDestroyNotifier] Failed to remove the wrapper of {_key.GetType().Name}: {e}");
        }
    }
}
