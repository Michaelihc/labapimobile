using System;
using System.Globalization;
using CommandSystem;
using LabApi.Features.Wrappers;
using UnityEngine;

namespace EventProbe;

/// <summary>
/// <c>probe cancel|allow &lt;Class.Event&gt;</c>, <c>probe cancelled</c>, <c>probe counts</c>,
/// <c>probe where &lt;playerId&gt;</c>, <c>probe tp &lt;playerId&gt; x y z</c>, <c>probe goto &lt;playerId&gt; &lt;GameType&gt; [index] [distance]</c>
/// (moves a test client in front of the n-th scene object of a game component type, for example Scp079Generator or TeslaGate,
/// so features can be reached without driving the touch UI across the map).
/// Registered for the server console and Remote Admin.
/// </summary>
[CommandHandler(typeof(GameConsoleCommandHandler))]
[CommandHandler(typeof(RemoteAdminCommandHandler))]
public sealed class ProbeCommand : ICommand
{
    public string Command => "probe";

    public string[] Aliases => [];

    public string Description => "EventProbe: probe cancel|allow <Class.Event>, probe cancelled, probe counts, probe where <id>, probe tp <id> <x> <y> <z>, probe goto <id> <GameType> [index] [distance]";

    public bool Execute(ArraySegment<string> arguments, ICommandSender sender, out string response)
    {
        EventProbePlugin? plugin = EventProbePlugin.Instance;
        if (plugin == null)
        {
            response = "EventProbe is not enabled.";
            return false;
        }

        string verb = arguments.Count > 0 ? arguments.At(0).ToLowerInvariant() : string.Empty;
        switch (verb)
        {
            case "cancel" or "allow" when arguments.Count > 1:
                return plugin.SetCancel(arguments.At(1), verb == "cancel", out response);
            case "cancelled":
                response = plugin.Describe(countsOnly: false);
                LabApi.Features.Console.Logger.Raw("[PROBE] cancelled:\n" + response, ConsoleColor.Cyan);
                return true;
            case "counts":
                response = plugin.Describe(countsOnly: true);
                LabApi.Features.Console.Logger.Raw("[PROBE] counts:\n" + response, ConsoleColor.Cyan);
                return true;
            case "where" when arguments.Count > 1 && TryGetPlayer(arguments.At(1), out Player? player):
                response = $"{player!.Nickname}: position {player.Position} room {player.Room?.Name.ToString() ?? "none"}";
                LabApi.Features.Console.Logger.Raw("[PROBE] " + response, ConsoleColor.Cyan);
                return true;
            case "tp" when arguments.Count > 4 && TryGetPlayer(arguments.At(1), out Player? player)
                && TryParse(arguments.At(2), out float x) && TryParse(arguments.At(3), out float y) && TryParse(arguments.At(4), out float z):
                player!.Position = new Vector3(x, y, z);
                response = $"Moved {player.Nickname} to {player.Position}";
                return true;
            case "goto" when arguments.Count > 2 && TryGetPlayer(arguments.At(1), out Player? player):
                return Goto(player!, arguments, out response);
            default:
                response = "Usage: " + Description;
                return false;
        }
    }

    private static bool TryGetPlayer(string id, out Player? player)
    {
        player = int.TryParse(id, out int playerId) ? Player.Get(playerId) : null;
        return player != null;
    }

    private static bool TryParse(string text, out float value) =>
        float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value);

    private static bool Goto(Player player, ArraySegment<string> arguments, out string response)
    {
        Type? type = typeof(ReferenceHub).Assembly.GetType(arguments.At(2), false, true);
        if (type == null)
        {
            foreach (Type candidate in typeof(ReferenceHub).Assembly.GetTypes())
            {
                if (string.Equals(candidate.Name, arguments.At(2), StringComparison.OrdinalIgnoreCase) && typeof(Component).IsAssignableFrom(candidate))
                {
                    type = candidate;
                    break;
                }
            }
        }

        if (type == null || !typeof(Component).IsAssignableFrom(type))
        {
            response = $"No component type '{arguments.At(2)}' in Assembly-CSharp.";
            return false;
        }

        UnityEngine.Object[] found = UnityEngine.Object.FindObjectsByType(type, FindObjectsSortMode.InstanceID);
        int index = arguments.Count > 3 && int.TryParse(arguments.At(3), out int i) ? i : 0;
        float distance = arguments.Count > 4 && TryParse(arguments.At(4), out float d) ? d : 1.5f;
        if (index < 0 || index >= found.Length)
        {
            response = $"{found.Length} objects of {type.Name}; index {index} is out of range.";
            return false;
        }

        Transform target = ((Component)found[index]).transform;
        player.Position = target.position + (target.forward * distance) + Vector3.up;
        response = $"Moved {player.Nickname} to {type.Name} #{index} of {found.Length} at {target.position}";
        LabApi.Features.Console.Logger.Raw("[PROBE] " + response, ConsoleColor.Cyan);
        return true;
    }
}
