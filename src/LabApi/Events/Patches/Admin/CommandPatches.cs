using CommandSystem;
using HarmonyLib;
using LabApi.Events.Arguments.ServerEvents;
using LabApi.Events.Handlers;
using LabApi.Features.Enums;
using Mirror;
using RemoteAdmin;
using RemoteAdmin.Communication;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace LabApi.Events.Patches.Admin;

/// <summary>
/// Raises CommandExecuting / CommandExecuted for Remote Admin commands and SendingAdminChat / SentAdminChat for
/// admin chat, which the fork routes through the same query handler (queries starting with "@").
/// </summary>
/// <remarks>
/// With subscribers the fork query handler runs here with the events inserted where official SL raises them;
/// without subscribers the fork handler runs untouched. On the build with the deathmatch module, its "help" chat and
/// "wiki" queries are handled first, as in that build, and never reach the events. Applied only when the native body
/// is one of the known Carl Mod 0.0.4 bodies.
/// </remarks>
// Official: RemoteAdmin/CommandProcessor.cs ProcessQuery and ProcessAdminChat
[HarmonyPatch(typeof(CommandProcessor), nameof(CommandProcessor.ProcessQuery))]
internal static class RemoteAdminQueryPatch
{
    // CommandProcessor.ProcessQuery without and with the deathmatch module's DmFun.HandleHelpChat / HandleWikiGrant calls.
    private const string StandardBody = "f438eb1072140469";
    private const string DeathmatchBody = "34dd4e9fe7cb467f";

    private const string AdminChatCancelled = "A server plugin cancelled the message.";

    private static readonly MethodInfo? Target = AccessTools.DeclaredMethod(typeof(CommandProcessor), nameof(CommandProcessor.ProcessQuery));

    private static readonly BodyVariant Variant = NativeBody.Identify(Target, StandardBody, DeathmatchBody, out Fingerprint);

    private static readonly string? Fingerprint;

    private static bool Prepare()
    {
        if (Variant == BodyVariant.Standard || (Variant == BodyVariant.Deathmatch && CarlModDeathmatch.HasCommandHooks))
        {
            return true;
        }

        PatchManager.Skip(typeof(RemoteAdminQueryPatch), NativeBody.UnknownBody(Target, Fingerprint, "Remote Admin CommandExecuting / CommandExecuted and SendingAdminChat / SentAdminChat are not raised."));
        return false;
    }

    private static bool Prefix(string q, CommandSender sender, ref string __result)
    {
        if (!ServerEvents.HasCommandExecuting && !ServerEvents.HasCommandExecuted && !ServerEvents.HasSendingAdminChat && !ServerEvents.HasSentAdminChat)
        {
            return true;
        }

        // Communication queries ("$") and empty queries keep the fork code path.
        if (q.StartsWith("$", StringComparison.Ordinal))
        {
            return true;
        }

        if (Variant == BodyVariant.Deathmatch && CarlModDeathmatch.HandleRemoteAdminQuery(q, sender))
        {
            __result = null!;
            return false;
        }

        if (q.StartsWith("@", StringComparison.Ordinal))
        {
            __result = ProcessAdminChat(q, sender)!;
            return false;
        }

        string[] query = q.Trim().Split(QueryProcessor.SpaceArray, 512, StringSplitOptions.RemoveEmptyEntries);
        if (query.Length == 0)
        {
            return true;
        }

        __result = ProcessCommand(query, sender)!;
        return false;
    }

    private static string? ProcessAdminChat(string q, CommandSender sender)
    {
        PlayerCommandSender? playerSender = sender as PlayerCommandSender;
        if (!CommandProcessor.CheckPermissions(sender, "Admin Chat", PlayerPermissions.AdminChat, string.Empty))
        {
            playerSender?.ReferenceHub.queryProcessor.TargetAdminChatAccessDenied(playerSender.ReferenceHub.queryProcessor.connectionToClient);
            return "You don't have permissions to access Admin Chat!";
        }

        string message = q.Substring(1);
        if (ServerEvents.HasSendingAdminChat)
        {
            SendingAdminChatEventArgs e = new(sender, message);
            ServerEvents.OnSendingAdminChat(e);
            if (!e.IsAllowed)
            {
                if (playerSender != null)
                {
                    playerSender.ReferenceHub.gameConsoleTransmission.SendToClient(AdminChatCancelled, "red");
                    playerSender.RaReply(AdminChatCancelled, false, true, string.Empty);
                }

                return null;
            }

            message = e.Message;
        }

        string content = "@" + message + " ~" + sender.Nickname;
        foreach (ReferenceHub hub in ReferenceHub.AllHubs)
        {
            if ((hub.serverRoles.AdminChatPerms || hub.serverRoles.RaEverywhere) && hub.Mode != ClientInstanceMode.Unverified)
            {
                hub.queryProcessor.TargetReply(hub.queryProcessor.connectionToClient, content, true, false, string.Empty);
            }
        }

        if (ServerEvents.HasSentAdminChat)
        {
            ServerEvents.OnSentAdminChat(new SentAdminChatEventArgs(sender, message));
        }

        return null;
    }

    private static string? ProcessCommand(string[] query, CommandSender sender)
    {
        bool found = CommandProcessor.RemoteAdminCommandHandler.TryGetCommand(query[0], out ICommand command);
        ArraySegment<string> arguments = new(query, 1, query.Length - 1);
        if (ServerEvents.HasCommandExecuting)
        {
            CommandExecutingEventArgs e = new(sender, CommandType.RemoteAdmin, found, command, arguments);
            ServerEvents.OnCommandExecuting(e);
            if (!e.IsAllowed)
            {
                return null;
            }

            arguments = e.Arguments;
            sender = e.Sender;
            command = e.Command;
        }

        if (!found)
        {
            sender.RaReply("SYSTEM#Unknown command!", false, true, string.Empty);
            return "Unknown command!";
        }

        try
        {
            bool success = command.Execute(arguments, sender, out string response);
            if (ServerEvents.HasCommandExecuted)
            {
                CommandExecutedEventArgs e = new(sender, CommandType.RemoteAdmin, command, arguments, success, response);
                ServerEvents.OnCommandExecuted(e);
                response = e.Response;
                success = e.ExecutedSuccessfully;
            }

            if (!string.IsNullOrEmpty(response))
            {
                sender.RaReply(query[0].ToUpperInvariant() + "#" + response, success, true, string.Empty);
            }

            return response;
        }
        catch (Exception ex)
        {
            string response = "Command execution failed! Error: " + Misc.RemoveStacktraceZeroes(ex.ToString());
            if (ServerEvents.HasCommandExecuted)
            {
                CommandExecutedEventArgs e = new(sender, CommandType.RemoteAdmin, command, arguments, false, response);
                ServerEvents.OnCommandExecuted(e);
                response = e.Response;
            }

            sender.RaReply(response, false, true, query[0].ToUpperInvariant() + "#" + response);
            return response;
        }
    }
}

/// <summary>
/// Raises CommandExecuting / CommandExecuted for server console commands.
/// </summary>
/// <remarks>
/// RA ("/") and admin chat ("@") input is forwarded to <see cref="CommandProcessor.ProcessQuery"/> by the fork and is
/// covered by <see cref="RemoteAdminQueryPatch"/>. The fork's built-in ServerConsole keywords (FORCESTART, STOPNEXTROUND,
/// RESTARTNEXTROUND, CONFIG, IDLE...) are handled before this method and never raise the events.
/// </remarks>
// Official: GameCore/Console.cs TypeCommand
[HarmonyPatch(typeof(GameCore.Console), nameof(GameCore.Console.TypeCommand))]
internal static class ConsoleCommandPatch
{
    private static bool Prefix(GameCore.Console __instance, string cmd, CommandSender sender, ref string __result)
    {
        if (!ServerEvents.HasCommandExecuting && !ServerEvents.HasCommandExecuted)
        {
            return true;
        }

        if (cmd.StartsWith(".", StringComparison.Ordinal) && cmd.Length > 1)
        {
            return true;
        }

        bool isAdminChat = cmd.StartsWith("@", StringComparison.Ordinal);
        if ((isAdminChat || cmd.StartsWith("/", StringComparison.Ordinal)) && cmd.Length > 1
            && (NetworkServer.active || NetworkClient.active || (!isAdminChat && cmd.Substring(1).TrimStart('$').Length == 0)))
        {
            return true;
        }

        string[] query = cmd.Trim().Split(QueryProcessor.SpaceArray, 512, StringSplitOptions.RemoveEmptyEntries);
        if (query.Length == 0)
        {
            return true;
        }

        sender ??= GameCore.Console.Ccs;
        List<string> logs = __instance._clientCommandLogs;
        logs?.Add(cmd);
        if (logs != null && logs.Count > 100)
        {
            logs.RemoveAt(0);
        }

        __result = Execute(__instance, query, sender)!;
        return false;
    }

    private static string? Execute(GameCore.Console console, string[] query, CommandSender sender)
    {
        string name = query[0];
        bool found = console.ConsoleCommandHandler.TryGetCommand(name, out ICommand command);
        ArraySegment<string> arguments = new(query, 1, query.Length - 1);
        if (ServerEvents.HasCommandExecuting)
        {
            CommandExecutingEventArgs e = new(sender, CommandType.Console, found, command, arguments);
            ServerEvents.OnCommandExecuting(e);
            if (!e.IsAllowed)
            {
                return null;
            }

            arguments = e.Arguments;
            sender = e.Sender;
            command = e.Command;
        }

        if (!found)
        {
            string notFound = "Command " + name + " does not exist!";
            sender?.Print(notFound, ConsoleColor.DarkYellow, new Color32(byte.MaxValue, 180, 0, byte.MaxValue));
            return notFound;
        }

        try
        {
            bool success = command.Execute(arguments, sender, out string response);
            response = Misc.CloseAllRichTextTags(response);
            if (ServerEvents.HasCommandExecuted)
            {
                CommandExecutedEventArgs e = new(sender, CommandType.Console, command, arguments, success, response);
                ServerEvents.OnCommandExecuted(e);
                response = e.Response;
                success = e.ExecutedSuccessfully;
            }

            if (string.IsNullOrWhiteSpace(response))
            {
                return null;
            }

            sender?.Print(response, success ? ConsoleColor.Green : ConsoleColor.Red);
            return response;
        }
        catch (Exception ex)
        {
            string response = "Command execution failed! Error: " + Misc.RemoveStacktraceZeroes(ex.ToString());
            if (ServerEvents.HasCommandExecuted)
            {
                CommandExecutedEventArgs e = new(sender, CommandType.Console, command, arguments, false, response);
                ServerEvents.OnCommandExecuted(e);
                response = e.Response;
            }

            sender?.Print(response, ConsoleColor.Red);
            return response;
        }
    }
}

/// <summary>
/// Raises CommandExecuting / CommandExecuted for client console (dot) commands.
/// </summary>
/// <remarks>
/// On the build with the deathmatch module, its ".s" chat command is handled first, as in that build, and never raises
/// the events. Applied only when the native body is one of the known Carl Mod 0.0.4 bodies.
/// </remarks>
// Official: RemoteAdmin/QueryProcessor.cs ProcessGameConsoleQuery
[HarmonyPatch(typeof(QueryProcessor), nameof(QueryProcessor.ProcessGameConsoleQuery))]
internal static class ClientCommandPatch
{
    // QueryProcessor.ProcessGameConsoleQuery without and with the deathmatch module's DmFun.HandleDotCommand call.
    private const string StandardBody = "195ef6a3beee3007";
    private const string DeathmatchBody = "065d8321356d6fbb";

    private static readonly MethodInfo? Target = AccessTools.DeclaredMethod(typeof(QueryProcessor), nameof(QueryProcessor.ProcessGameConsoleQuery));

    private static readonly BodyVariant Variant = NativeBody.Identify(Target, StandardBody, DeathmatchBody, out Fingerprint);

    private static readonly string? Fingerprint;

    private static bool Prepare()
    {
        if (Variant == BodyVariant.Standard || (Variant == BodyVariant.Deathmatch && CarlModDeathmatch.HasCommandHooks))
        {
            return true;
        }

        PatchManager.Skip(typeof(ClientCommandPatch), NativeBody.UnknownBody(Target, Fingerprint, "client console CommandExecuting / CommandExecuted are not raised."));
        return false;
    }

    private static bool Prefix(QueryProcessor __instance, string query)
    {
        if (!ServerEvents.HasCommandExecuting && !ServerEvents.HasCommandExecuted)
        {
            return true;
        }

        if (Variant == BodyVariant.Deathmatch && CarlModDeathmatch.HandleDotCommand(__instance._sender, query))
        {
            return false;
        }

        string[] parts = query.Trim().Split(QueryProcessor.SpaceArray, 512, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return false;
        }

        NetworkConnection connection = __instance.connectionToClient;
        GameConsoleTransmission console = __instance.GCT;
        CommandSender sender = __instance._sender;
        bool found = QueryProcessor.DotCommandHandler.TryGetCommand(parts[0], out ICommand command);
        ArraySegment<string> arguments = new(parts, 1, parts.Length - 1);
        if (ServerEvents.HasCommandExecuting)
        {
            CommandExecutingEventArgs e = new(sender, CommandType.Client, found, command, arguments);
            ServerEvents.OnCommandExecuting(e);
            if (!e.IsAllowed)
            {
                if (found)
                {
                    console.SendToClient(connection, "Command execution failed! Reason: Forcefully cancelled by a plugin.", "magenta");
                }
                else
                {
                    console.SendToClient(connection, "Command not found.", "red");
                }

                return false;
            }

            arguments = e.Arguments;
            sender = e.Sender;
            command = e.Command;
        }

        if (!found)
        {
            console.SendToClient(connection, "Command not found.", "red");
            return false;
        }

        string response;
        bool success;
        try
        {
            success = command.Execute(arguments, sender, out response);
        }
        catch (Exception ex)
        {
            response = "Command execution failed! Error: " + ex;
            success = false;
        }

        if (ServerEvents.HasCommandExecuted)
        {
            CommandExecutedEventArgs e = new(sender, CommandType.Client, command, arguments, success, response);
            ServerEvents.OnCommandExecuted(e);
            response = e.Response;
        }

        console.SendToClient(connection, parts[0].ToUpperInvariant() + "#" + response, string.Empty);
        return false;
    }
}
