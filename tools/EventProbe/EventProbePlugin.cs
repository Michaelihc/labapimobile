using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using LabApi.Events.Handlers;
using LabApi.Features.Console;
using LabApi.Loader.Features.Plugins;
using MEC;

namespace EventProbe;

/// <summary>
/// Subscribes to every static event of every class in <c>LabApi.Events.Handlers</c> and logs
/// <c>[PROBE] &lt;Class&gt;.&lt;Event&gt; &lt;args&gt;</c> to the server log. Developer tool for verifying event coverage.
/// </summary>
public sealed class EventProbePlugin : Plugin<ProbeConfig>
{
    private readonly List<EventTap> _taps = [];
    private readonly ConcurrentQueue<string> _offThreadLines = new();
    private CoroutineHandle _reporter;
    private int _mainThreadId;
    private UnityLogMirror? _mirror;
    private readonly System.Diagnostics.Stopwatch _clock = System.Diagnostics.Stopwatch.StartNew();

    public static EventProbePlugin? Instance { get; private set; }

    public override string Name => "EventProbe";

    public override string Description => "Logs every LabAPI event to the server log (developer tool).";

    public override string Author => "LabAPIMobile";

    public override Version Version => new(1, 0, 0);

    public override Version RequiredApiVersion => new(1, 0, 0);

    /// <summary>Taps by "Class.Event" name.</summary>
    public Dictionary<string, EventTap> TapsByName { get; } = new(StringComparer.OrdinalIgnoreCase);

    public override void Enable()
    {
        Instance = this;
        _mainThreadId = Thread.CurrentThread.ManagedThreadId;
        if (Config.MirrorFileConsoleToUnityLog && ServerStatic.ServerOutput is ServerOutput.FileConsole)
            _mirror = new UnityLogMirror();

        HashSet<string> throttled = new(Config.ThrottledEvents, StringComparer.OrdinalIgnoreCase);

        foreach (Type type in typeof(PlayerEvents).Assembly.GetTypes())
        {
            if (type.Namespace != typeof(PlayerEvents).Namespace || !type.IsAbstract || !type.IsSealed)
                continue;

            foreach (EventInfo info in type.GetEvents(BindingFlags.Public | BindingFlags.Static))
            {
                string name = type.Name + "." + info.Name;
                EventTap tap = new(this, name, throttled.Contains(name));
                Delegate? handler = tap.CreateHandler(info.EventHandlerType);
                if (handler == null)
                {
                    Write($"[PROBE] cannot subscribe to {name}: unsupported handler type {info.EventHandlerType}");
                    continue;
                }

                info.AddEventHandler(null, handler);
                tap.Subscription = (info, handler);
                _taps.Add(tap);
                TapsByName[name] = tap;
            }
        }

        foreach (string name in Config.CancelEvents)
            SetCancel(name, true, out _);

        _reporter = Timing.RunCoroutine(ReportLoop());
        Write($"[PROBE] subscribed to {_taps.Count} events, {throttled.Count} throttled, {Config.CancelEvents.Count} cancelled");
    }

    public override void Disable()
    {
        Timing.KillCoroutines(_reporter);
        foreach (EventTap tap in _taps)
            tap.Subscription.Event?.RemoveEventHandler(null, tap.Subscription.Handler);

        _taps.Clear();
        TapsByName.Clear();
        if (_mirror != null)
            ServerConsole.ConsoleOutputs?.Remove(_mirror);
        _mirror = null;
        Instance = null;
    }

    /// <summary>Enables or disables forced cancellation of one event.</summary>
    public bool SetCancel(string name, bool cancel, out string response)
    {
        if (!TapsByName.TryGetValue(name, out EventTap tap))
        {
            response = $"Unknown event '{name}'. Use Class.Event, for example PlayerEvents.InteractingDoor.";
            return false;
        }

        if (!tap.IsCancellable)
        {
            response = $"{tap.Name} is not cancellable.";
            return false;
        }

        tap.Cancel = cancel;
        response = $"{tap.Name}: {(cancel ? "cancelled (IsAllowed = false)" : "allowed")}";
        Write("[PROBE] " + response);
        return true;
    }

    /// <summary>Lines of the cancellation and count summary for the <c>probe</c> command.</summary>
    public string Describe(bool countsOnly)
    {
        System.Text.StringBuilder sb = new();
        foreach (EventTap tap in _taps)
        {
            if (countsOnly && tap.Total == 0)
                continue;

            if (!countsOnly && !tap.Cancel)
                continue;

            sb.Append(tap.Name).Append(" total=").Append(tap.Total);
            if (tap.Throttled)
                sb.Append(" throttled");
            if (tap.Cancel)
                sb.Append(" CANCELLED");
            sb.Append('\n');
        }

        return sb.Length == 0 ? "(none)" : sb.ToString();
    }

    /// <summary>Line prefix with the seconds since the plugin was enabled.</summary>
    internal string Stamp() => "[PROBE] t=" + _clock.Elapsed.TotalSeconds.ToString("0.00");

    internal void Write(string line)
    {
        if (Thread.CurrentThread.ManagedThreadId != _mainThreadId)
        {
            _offThreadLines.Enqueue(line + " (off main thread)");
            return;
        }

        // Before the mirror can be attached the console line would be lost, so log it to Unity directly.
        if (_mirror != null && !TryAttachMirror())
            UnityEngine.Debug.Log(line);

        Logger.Raw(line, ConsoleColor.Cyan);
    }

    /// <summary>
    /// <c>ServerConsole.Start</c> replaces <c>ConsoleOutputs</c> after plugins are enabled, so the mirror is
    /// (re)attached lazily.
    /// </summary>
    private bool TryAttachMirror()
    {
        List<IOutput>? outputs = ServerConsole.ConsoleOutputs;
        if (outputs == null)
            return false;

        if (!outputs.Contains(_mirror!))
            outputs.Add(_mirror!);

        return true;
    }

    private IEnumerator<float> ReportLoop()
    {
        float interval = Math.Max(1f, Config.ReportIntervalSeconds);
        float elapsed = 0f;
        while (true)
        {
            // Flush off-thread lines quickly; report throttled counts on the configured interval.
            yield return Timing.WaitForSeconds(0.5f);
            if (_mirror != null)
                TryAttachMirror();

            while (_offThreadLines.TryDequeue(out string line))
                Logger.Raw(line, ConsoleColor.Cyan);

            elapsed += 0.5f;
            if (elapsed < interval)
                continue;

            elapsed = 0f;
            foreach (EventTap tap in _taps)
            {
                int count = tap.TakeWindowCount(Config.AutoThrottleLines);
                if (tap.Throttled && count > 0)
                    Write($"{Stamp()} {tap.Name} x{count} in last {interval:0} s (total {tap.Total})");
            }
        }
    }
}
