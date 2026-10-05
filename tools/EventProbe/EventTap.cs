using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using LabApi.Events;
using LabApi.Events.Arguments.Interfaces;
using LabApi.Features.Wrappers;
using UnityEngine;

namespace EventProbe;

/// <summary>
/// One subscription: formats the event arguments, throttles per-frame events and optionally cancels.
/// </summary>
public sealed class EventTap
{
    private static readonly Dictionary<Type, PropertyInfo[]> PropertyCache = new();
    private static readonly MethodInfo GenericHandler = typeof(EventTap).GetMethod(nameof(OnEvent), BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly MethodInfo PlainHandler = typeof(EventTap).GetMethod(nameof(OnPlainEvent), BindingFlags.Instance | BindingFlags.NonPublic)!;

    private readonly EventProbePlugin _plugin;
    private bool _autoThrottled;
    private int _windowCount;
    private int _windowLines;
    private int _previousWindowCount = -1;

    public EventTap(EventProbePlugin plugin, string name, bool throttled)
    {
        _plugin = plugin;
        Name = name;
        Throttled = throttled;
    }

    public string Name { get; }

    public bool Throttled { get; private set; }

    public bool IsCancellable { get; private set; }

    public bool Cancel { get; set; }

    public int Total { get; private set; }

    public (EventInfo? Event, Delegate? Handler) Subscription { get; set; }

    public Delegate? CreateHandler(Type handlerType)
    {
        if (handlerType == typeof(LabEventHandler))
            return Delegate.CreateDelegate(handlerType, this, PlainHandler);

        if (!handlerType.IsGenericType || handlerType.GetGenericTypeDefinition() != typeof(LabEventHandler<>))
            return null;

        Type argsType = handlerType.GetGenericArguments()[0];
        IsCancellable = typeof(ICancellableEvent).IsAssignableFrom(argsType);
        return Delegate.CreateDelegate(handlerType, this, GenericHandler.MakeGenericMethod(argsType));
    }

    /// <summary>Returns and resets the number of calls in the current report window.</summary>
    public int TakeWindowCount(int autoThrottleLines)
    {
        int count = _windowCount;
        if (!Throttled && autoThrottleLines > 0 && _windowLines > autoThrottleLines)
        {
            Throttled = true;
            _autoThrottled = true;
            _plugin.Write($"{_plugin.Stamp()} {Name} logged {_windowLines} lines in one window; throttling it until it is quiet for a window");
        }
        else if (_autoThrottled && count == 0)
        {
            // A burst (for example every effect of a joining player) should not hide later single events.
            Throttled = false;
            _autoThrottled = false;
        }

        _previousWindowCount = count;
        _windowCount = 0;
        _windowLines = 0;
        return count;
    }

    private void OnPlainEvent()
    {
        if (ShouldLog())
            _plugin.Write(_plugin.Stamp() + " " + Name);
    }

    private void OnEvent<T>(T ev)
        where T : EventArgs
    {
        bool cancelled = false;
        if (Cancel && ev is ICancellableEvent cancellable)
        {
            cancellable.IsAllowed = false;
            cancelled = true;
        }

        if (!ShouldLog())
            return;

        StringBuilder sb = new();
        sb.Append(_plugin.Stamp()).Append(' ').Append(Name);
        AppendArgs(sb, ev);
        if (cancelled)
            sb.Append(" [probe cancelled]");
        if (Throttled)
            sb.Append(" [throttled: first in window]");

        int max = _plugin.Config.MaxLineLength;
        if (max > 0 && sb.Length > max)
        {
            sb.Length = max;
            sb.Append("...");
        }

        _plugin.Write(sb.ToString());
    }

    private bool ShouldLog()
    {
        Total++;
        _windowCount++;
        if (!Throttled)
        {
            _windowLines++;
            return true;
        }

        // Throttled: log the first call, and the first call after a silent window.
        return _windowCount == 1 && _previousWindowCount <= 0;
    }

    private static void AppendArgs(StringBuilder sb, object ev)
    {
        Type type = ev.GetType();
        if (!PropertyCache.TryGetValue(type, out PropertyInfo[] properties))
        {
            List<PropertyInfo> list = [];
            foreach (PropertyInfo property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.CanRead && property.GetIndexParameters().Length == 0)
                    list.Add(property);
            }

            properties = list.ToArray();
            PropertyCache[type] = properties;
        }

        foreach (PropertyInfo property in properties)
        {
            sb.Append(' ').Append(property.Name).Append('=');
            object? value;
            try
            {
                value = property.GetValue(ev);
            }
            catch (TargetInvocationException e)
            {
                sb.Append("<").Append(e.InnerException?.GetType().Name ?? "error").Append('>');
                continue;
            }

            AppendValue(sb, value);
        }
    }

    private static void AppendValue(StringBuilder sb, object? value)
    {
        switch (value)
        {
            case null:
                sb.Append("null");
                return;
            case string s:
                sb.Append('"').Append(s.Length > 80 ? s.Substring(0, 80) + "..." : s).Append('"');
                return;
            case Player player:
                sb.Append(player.IsHost ? "Host" : player.Nickname).Append("(#").Append(player.PlayerId).Append(',').Append(player.Role).Append(')');
                return;
            case Item item:
                sb.Append(item.Type).Append('#').Append(item.Serial);
                return;
            case Pickup pickup:
                sb.Append("Pickup:").Append(pickup.Type).Append('#').Append(pickup.Serial);
                return;
            case Door door:
                sb.Append(door.GetType().Name).Append(':').Append(door.DoorName).Append('/').Append(door.NameTag ?? "-");
                return;
            case Room room:
                sb.Append("Room:").Append(room.Name).Append('/').Append(room.Zone);
                return;
            case Vector3 v:
                sb.Append('(').Append(v.x.ToString("0.0")).Append(',').Append(v.y.ToString("0.0")).Append(',').Append(v.z.ToString("0.0")).Append(')');
                return;
            case float f:
                sb.Append(f.ToString("0.###"));
                return;
            case ArraySegment<string> segment:
                sb.Append('"');
                for (int i = 0; i < segment.Count; i++)
                {
                    if (i > 0)
                        sb.Append(' ');
                    sb.Append(segment.Array![segment.Offset + i]);
                }

                sb.Append('"');
                return;
            case ICollection collection:
                sb.Append('[').Append(collection.Count).Append(']');
                return;
            case Enum or ValueType:
                sb.Append(value);
                return;
        }

        // Other wrappers and game objects: short ToString when the type provides one, else the type name.
        Type type = value.GetType();
        MethodInfo? toString = type.GetMethod(nameof(ToString), Type.EmptyTypes);
        if (toString != null && toString.DeclaringType != typeof(object))
        {
            string text = value.ToString();
            if (text.Length <= 100)
            {
                sb.Append(text);
                return;
            }
        }

        sb.Append(type.Name);
    }
}
