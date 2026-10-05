using CommandSystem;
using CommandSystem.Commands.RemoteAdmin.MutingAndIntercom;
using HarmonyLib;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using RemoteAdmin;
using System;
using System.Collections.Generic;
using System.Reflection;
using Utils;
using VoiceChat;

namespace LabApi.Events.Patches.Admin;

/// <summary>
/// Raises Muting / Muted and Unmuting / Unmuted for each target of the mute, imute, unmute and iunmute RA commands.
/// </summary>
/// <remarks>
/// With subscribers the command body runs here with the events around each target, as in official SL; without
/// subscribers the fork command runs untouched.
/// </remarks>
// Official: CommandSystem/Commands/RemoteAdmin/MutingAndIntercom/MuteCommand.cs (and IntercomMute, Unmute, IntercomUnmute) Execute
[HarmonyPatch]
internal static class MuteCommandPatch
{
    private static readonly PlayerPermissions[] RequiredPermissions =
    [
        PlayerPermissions.BanningUpToDay,
        PlayerPermissions.LongTermBanning,
        PlayerPermissions.PlayersManagement,
    ];

    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(MuteCommand), nameof(MuteCommand.Execute));
        yield return AccessTools.Method(typeof(IntercomMuteCommand), nameof(IntercomMuteCommand.Execute));
        yield return AccessTools.Method(typeof(UnmuteCommand), nameof(UnmuteCommand.Execute));
        yield return AccessTools.Method(typeof(IntercomUnmuteCommand), nameof(IntercomUnmuteCommand.Execute));
    }

    private static bool Prefix(ICommand __instance, ArraySegment<string> arguments, ICommandSender sender, out string response, ref bool __result)
    {
        response = null!;
        bool mute = __instance is MuteCommand or IntercomMuteCommand;
        bool intercom = __instance is IntercomMuteCommand or IntercomUnmuteCommand;
        if (mute ? !PlayerEvents.HasMuting && !PlayerEvents.HasMuted : !PlayerEvents.HasUnmuting && !PlayerEvents.HasUnmuted)
        {
            return true;
        }

        __result = Execute((IUsageProvider)__instance, arguments, sender, out response, mute, intercom);
        return false;
    }

    private static bool Execute(IUsageProvider command, ArraySegment<string> arguments, ICommandSender sender, out string response, bool mute, bool intercom)
    {
        if (!sender.CheckPermission(RequiredPermissions, out response))
        {
            return false;
        }

        if (arguments.Count < 1)
        {
            response = "To execute this command provide at least 1 argument!\nUsage: " + arguments.Array[0] + " " + command.DisplayCommandUsage();
            return false;
        }

        List<ReferenceHub> list = RAUtils.ProcessPlayerIdOrNamesList(arguments, 0, out _);
        ReferenceHub issuer = sender is PlayerCommandSender playerSender ? playerSender.ReferenceHub : ReferenceHub.HostHub;
        int count = 0;
        if (list != null)
        {
            foreach (ReferenceHub target in list)
            {
                if (mute ? Mute(target, issuer, intercom) : Unmute(target, issuer, intercom))
                {
                    ServerLogs.AddLog(ServerLogs.Modules.Administrative, sender.LogName + LogText(mute, intercom) + target.LoggedNameFromRefHub() + ".", ServerLogs.ServerLogType.RemoteAdminActivity_GameChanging);
                    count++;
                }
            }
        }

        response = string.Format("Done! The request affected {0} player{1}", count, count == 1 ? "!" : "s!");
        return true;
    }

    private static bool Mute(ReferenceHub target, ReferenceHub issuer, bool intercom)
    {
        if (PlayerEvents.HasMuting)
        {
            PlayerMutingEventArgs e = new(target, issuer, intercom);
            PlayerEvents.OnMuting(e);
            if (!e.IsAllowed)
            {
                return false;
            }
        }

        VoiceChatMutes.IssueLocalMute(target.characterClassManager.UserId, intercom);
        if (PlayerEvents.HasMuted)
        {
            PlayerEvents.OnMuted(new PlayerMutedEventArgs(target, issuer, intercom));
        }

        return true;
    }

    private static bool Unmute(ReferenceHub target, ReferenceHub issuer, bool intercom)
    {
        if (PlayerEvents.HasUnmuting)
        {
            PlayerUnmutingEventArgs e = new(target, issuer, intercom);
            PlayerEvents.OnUnmuting(e);
            if (!e.IsAllowed)
            {
                return false;
            }
        }

        VoiceChatMutes.RevokeLocalMute(target.characterClassManager.UserId, intercom);
        if (PlayerEvents.HasUnmuted)
        {
            PlayerEvents.OnUnmuted(new PlayerUnmutedEventArgs(target, issuer, intercom));
        }

        return true;
    }

    private static string LogText(bool mute, bool intercom) => mute
        ? (intercom ? " issued an intercom mute to player " : " muted player ")
        : (intercom ? " revoked an intercom mute of player " : " unmuted player ");
}
