using HarmonyLib;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using Mirror;
using NorthwoodLib.Pools;
using PlayerRoles;
using PlayerRoles.FirstPersonControl;
using PlayerStatsSystem;
using RemoteAdmin;
using RemoteAdmin.Communication;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Utils;
using VoiceChat;

namespace LabApi.Events.Patches.Admin;

/// <summary>
/// Raises RequestedCustomRaInfo, RequestingRaPlayersInfo / RequestedRaPlayersInfo and RequestingRaPlayerInfo /
/// RequestedRaPlayerInfo while the Remote Admin player info panel is built.
/// </summary>
/// <remarks>
/// With subscribers the fork info builder runs here with the events inserted as in official SL (the fork's text and
/// clipboard format are kept); without subscribers the fork builder runs untouched. As in official SL the
/// sensitive-data permission is checked after the Requesting events, so handlers can grant or revoke it. Carl Mod 0.0.5
/// labels the player ID "Device ID" and sends it as the <c>DeviceId</c> clipboard entry (<c>CP_DEVICEID</c>); 0.0.4 labels
/// it "User ID / Device ID" (<c>CP_USERID</c>). Applied only when the native body is one of the known Carl Mod bodies.
/// </remarks>
// Official: RemoteAdmin/Communication/RaPlayer.cs ReceiveData(CommandSender, string)
[HarmonyPatch(typeof(RaPlayer), nameof(RaPlayer.ReceiveData), typeof(CommandSender), typeof(string))]
internal static class RaPlayerPatch
{
    private const string CopyId = "<color=green><link=CP_ID></link></color>";

    private const string CopyIp = "<color=green><link=CP_IP></link></color>";

    private const string CopyUserId = "<color=green><link=CP_USERID></link></color>";

    private const string CopyDeviceId = "<color=green><link=CP_DEVICEID></link></color>";

    private const ulong UserIdPermissions = 18007046uL;

    // RaPlayer.ReceiveData of both Carl Mod 0.0.4 builds, and of 0.0.5 (device ID labels and clipboard entry).
    private const string CarlMod004Body = "9210d010dad2996c";
    private const string Version005Body = "21694c845e32a8eb";

    private static readonly MethodInfo? Target = AccessTools.DeclaredMethod(typeof(RaPlayer), nameof(RaPlayer.ReceiveData), [typeof(CommandSender), typeof(string)]);

    private static readonly BodyVariant Variant = NativeBody.Identify(Target, CarlMod004Body, Version005Body, out Fingerprint);

    private static readonly string? Fingerprint;

    private static bool Version005 => Variant == BodyVariant.Version005;

    // The ID line label, its copy link and the QR code text without an ID.
    private static string IdLabel => Version005 ? "\nDevice ID: " : "\nUser ID / Device ID: ";

    private static string CopyPlayerId => Version005 ? CopyDeviceId : CopyUserId;

    private static string NoIdQr => Version005 ? "(no Device ID)" : "(no User ID)";

    private static bool Prepare()
    {
        if (Variant != BodyVariant.Unknown)
        {
            return true;
        }

        PatchManager.Skip(typeof(RaPlayerPatch), NativeBody.UnknownBody(Target, Fingerprint, "RequestedCustomRaInfo, RequestingRaPlayersInfo / RequestedRaPlayersInfo and RequestingRaPlayerInfo / RequestedRaPlayerInfo are not raised."));
        return false;
    }

    private static bool Prefix(RaPlayer __instance, CommandSender sender, string data)
    {
        if (!PlayerEvents.HasRequestedCustomRaInfo && !PlayerEvents.HasRequestingRaPlayersInfo && !PlayerEvents.HasRequestedRaPlayersInfo
            && !PlayerEvents.HasRequestingRaPlayerInfo && !PlayerEvents.HasRequestedRaPlayerInfo)
        {
            return true;
        }

        string[] args = data.Split(' ');
        if (args.Length != 2 || !int.TryParse(args[0], out int shortInfo))
        {
            return false;
        }

        bool isShort = shortInfo == 1;
        PlayerCommandSender? playerSender = sender as PlayerCommandSender;
        bool hasSensitiveInfoPerms = playerSender == null || playerSender.ServerRoles.Staff || CommandProcessor.CheckPermissions(sender, PlayerPermissions.PlayerSensitiveDataAccess);
        ArraySegment<string> selection = new(args, 1, 1);
        List<ReferenceHub> targets = RAUtils.ProcessPlayerIdOrNamesList(selection, 0, out _);
        if (targets == null || targets.Count == 0)
        {
            SendCustomInfo(__instance, sender, selection, isShort);
            return false;
        }

        bool hasUserIdPerms = PermissionsHandler.IsPermitted(sender.Permissions, UserIdPermissions)
            || (playerSender != null && (playerSender.ServerRoles.Staff || playerSender.ServerRoles.RaEverywhere));
        if (targets.Count > 1)
        {
            SendMultipleInfo(__instance, sender, targets, isShort, hasSensitiveInfoPerms, hasUserIdPerms);
        }
        else
        {
            SendSingleInfo(__instance, sender, playerSender, targets[0], isShort, hasSensitiveInfoPerms, hasUserIdPerms);
        }

        return false;
    }

    private static void SendCustomInfo(RaPlayer raPlayer, CommandSender sender, ArraySegment<string> selection, bool isShort)
    {
        // The fork ignores selections that match no player; only a handler can answer them.
        if (!PlayerEvents.HasRequestedCustomRaInfo)
        {
            return;
        }

        StringBuilder builder = StringBuilderPool.Shared.Rent();
        builder.Append('$').Append(raPlayer.DataId).Append(' ');
        PlayerRequestedCustomRaInfoEventArgs e = new(sender, selection, !isShort, builder);
        PlayerEvents.OnRequestedCustomRaInfo(e);
        if (e.TryGetClipboardText(0, out string? text))
        {
            RaClipboard.Send(sender, RaClipboard.RaClipBoardType.PlayerId, text);
        }

        if (e.TryGetClipboardText(1, out text))
        {
            RaClipboard.Send(sender, RaClipboard.RaClipBoardType.Ip, text);
        }

        if (e.TryGetClipboardText(2, out text))
        {
            RaClipboard.Send(sender, RaClipboard.RaClipBoardType.UserId, text);
        }

        sender.RaReply(StringBuilderPool.Shared.ToStringReturn(builder), true, true, string.Empty);
    }

    private static void SendMultipleInfo(RaPlayer raPlayer, CommandSender sender, List<ReferenceHub> targets, bool isShort, bool hasSensitiveInfoPerms, bool hasUserIdPerms)
    {
        StringBuilder builder = StringBuilderPool.Shared.Rent();
        if (PlayerEvents.HasRequestingRaPlayersInfo)
        {
            PlayerRequestingRaPlayersInfoEventArgs e = new(sender, targets, !isShort, hasSensitiveInfoPerms, hasUserIdPerms, builder);
            PlayerEvents.OnRequestingRaPlayersInfo(e);
            if (!e.IsAllowed)
            {
                StringBuilderPool.Shared.Return(builder);
                return;
            }

            isShort = !e.IsSensitiveInfo;
            hasSensitiveInfoPerms = e.HasSensitiveInfoPerms;
            hasUserIdPerms = e.HasUserIdPerms;
        }

        if (!isShort && !hasSensitiveInfoPerms)
        {
            StringBuilderPool.Shared.Return(builder);
            return;
        }

        builder.Append('$').Append(raPlayer.DataId).Append(' ');
        builder.Append("<color=white>Selecting multiple players:");
        builder.Append("\nPlayer ID: ").Append(CopyId);
        builder.Append("\nIP Address: ").Append(isShort ? "[REDACTED]" : CopyIp);
        builder.Append(IdLabel).Append(hasUserIdPerms ? CopyPlayerId : "[REDACTED]");
        builder.Append("</color>");

        StringBuilder idBuilder = StringBuilderPool.Shared.Rent();
        StringBuilder? ipBuilder = isShort ? null : StringBuilderPool.Shared.Rent();
        StringBuilder? userIdBuilder = hasUserIdPerms ? StringBuilderPool.Shared.Rent() : null;
        foreach (ReferenceHub target in targets)
        {
            idBuilder.Append(target.PlayerId).Append('.');
            ipBuilder?.Append(target.networkIdentity.connectionToClient?.address).Append(',');
            userIdBuilder?.Append(target.characterClassManager.UserId).Append('.');
        }

        if (PlayerEvents.HasRequestedRaPlayersInfo)
        {
            PlayerEvents.OnRequestedRaPlayersInfo(new PlayerRequestedRaPlayersInfoEventArgs(sender, targets, !isShort, hasUserIdPerms, builder, idBuilder, ipBuilder!, userIdBuilder!));
        }

        SendClipboard(sender, RaClipboard.RaClipBoardType.PlayerId, idBuilder);
        SendClipboard(sender, RaClipboard.RaClipBoardType.Ip, ipBuilder);
        SendClipboard(sender, RaClipboard.RaClipBoardType.UserId, userIdBuilder);
        sender.RaReply(StringBuilderPool.Shared.ToStringReturn(builder), true, true, string.Empty);
    }

    private static void SendSingleInfo(RaPlayer raPlayer, CommandSender sender, PlayerCommandSender? playerSender, ReferenceHub target, bool isShort, bool hasSensitiveInfoPerms, bool hasUserIdPerms)
    {
        StringBuilder builder = StringBuilderPool.Shared.Rent();
        if (PlayerEvents.HasRequestingRaPlayerInfo)
        {
            PlayerRequestingRaPlayerInfoEventArgs e = new(sender, target, !isShort, hasSensitiveInfoPerms, hasUserIdPerms, builder);
            PlayerEvents.OnRequestingRaPlayerInfo(e);
            if (!e.IsAllowed)
            {
                StringBuilderPool.Shared.Return(builder);
                return;
            }

            isShort = !e.IsSensitiveInfo;
            hasSensitiveInfoPerms = e.HasSensitiveInfoPerms;
            hasUserIdPerms = e.HasUserIdPerms;
        }

        if (!isShort && !hasSensitiveInfoPerms)
        {
            StringBuilderPool.Shared.Return(builder);
            return;
        }

        ServerLogs.AddLog(ServerLogs.Modules.DataAccess, sender.LogName + " accessed IP address of player " + target.PlayerId + " (" + target.nicknameSync.MyNick + ").", ServerLogs.ServerLogType.RemoteAdminActivity_GameChanging);
        bool gameplayData = PermissionsHandler.IsPermitted(sender.Permissions, PlayerPermissions.GameplayData);
        CharacterClassManager characterClassManager = target.characterClassManager;
        NetworkConnectionToClient connection = target.networkIdentity.connectionToClient;
        ServerRoles serverRoles = target.serverRoles;
        if (playerSender != null)
        {
            playerSender.ReferenceHub.queryProcessor.GameplayData = gameplayData;
        }

        StringBuilder idBuilder = StringBuilderPool.Shared.Rent();
        StringBuilder ipBuilder = StringBuilderPool.Shared.Rent();
        StringBuilder userIdBuilder = StringBuilderPool.Shared.Rent();
        builder.Append('$').Append(raPlayer.DataId).Append(' ');
        builder.Append("<color=white>Nickname: ").Append(target.nicknameSync.CombinedName);
        builder.Append("\nPlayer ID: ").Append(target.PlayerId).Append(' ').Append(CopyId);
        idBuilder.Append(target.PlayerId);
        if (connection == null)
        {
            builder.Append("\nIP Address: null");
        }
        else if (!isShort)
        {
            builder.Append("\nIP Address: ").Append(connection.address).Append("  ").Append(CopyIp);
            ipBuilder.Append(connection.address);
        }
        else
        {
            builder.Append("\nIP Address: [REDACTED]");
        }

        builder.Append(IdLabel);
        if (!hasUserIdPerms)
        {
            builder.Append("<color=#D4AF37>INSUFFICIENT PERMISSIONS</color>");
        }
        else
        {
            if (string.IsNullOrEmpty(characterClassManager.UserId))
            {
                builder.Append("(none)");
            }
            else
            {
                builder.Append(characterClassManager.UserId).Append(' ').Append(CopyPlayerId);
            }

            userIdBuilder.Append(characterClassManager.UserId);
        }

        builder.Append("\nServer role: ").Append(serverRoles.GetColoredRoleString());
        bool viewHiddenBadges = CommandProcessor.CheckPermissions(sender, PlayerPermissions.ViewHiddenBadges);
        bool viewHiddenGlobalBadges = CommandProcessor.CheckPermissions(sender, PlayerPermissions.ViewHiddenGlobalBadges);
        if (playerSender != null && playerSender.ServerRoles.Staff)
        {
            viewHiddenBadges = true;
            viewHiddenGlobalBadges = true;
        }

        bool hasHiddenBadge = !string.IsNullOrEmpty(serverRoles.HiddenBadge);
        bool showHidden = !hasHiddenBadge || (serverRoles.GlobalHidden && viewHiddenGlobalBadges) || (!serverRoles.GlobalHidden && viewHiddenBadges);
        if (showHidden)
        {
            if (hasHiddenBadge)
            {
                builder.Append("\n<color=#DC143C>Hidden role: </color>").Append(serverRoles.HiddenBadge);
                builder.Append("\n<color=#DC143C>Hidden role type: </color>").Append(serverRoles.GlobalHidden ? "GLOBAL" : "LOCAL");
            }

            if (serverRoles.RaEverywhere)
            {
                builder.Append("\nStudio Status: <color=#BCC6CC>Studio GLOBAL Staff (management or global moderation)</color>");
            }
            else if (serverRoles.Staff)
            {
                builder.Append("\nStudio Status: <color=#94B9CF>Studio Staff</color>");
            }
        }

        VcMuteFlags muteFlags = VoiceChatMutes.GetFlags(target);
        if (muteFlags != VcMuteFlags.None)
        {
            builder.Append("\nMUTE STATUS:");
            AppendMuteFlag(builder, muteFlags, VcMuteFlags.LocalRegular, "LocalRegular");
            AppendMuteFlag(builder, muteFlags, VcMuteFlags.LocalIntercom, "LocalIntercom");
            AppendMuteFlag(builder, muteFlags, VcMuteFlags.GlobalRegular, "GlobalRegular");
            AppendMuteFlag(builder, muteFlags, VcMuteFlags.GlobalIntercom, "GlobalIntercom");
        }

        builder.Append("\nActive flag(s):");
        if (characterClassManager.GodMode)
        {
            builder.Append(" <color=#659EC7>[GOD MODE]</color>");
        }

        if (target.playerStats.GetModule<AdminFlagsStat>().HasFlag(AdminFlags.Noclip))
        {
            builder.Append(" <color=#DC143C>[NOCLIP ENABLED]</color>");
        }
        else if (FpcNoclip.IsPermitted(target))
        {
            builder.Append(" <color=#E52B50>[NOCLIP UNLOCKED]</color>");
        }

        if (serverRoles.DoNotTrack)
        {
            builder.Append(" <color=#BFFF00>[DO NOT TRACK]</color>");
        }

        if (serverRoles.BypassMode)
        {
            builder.Append(" <color=#BFFF00>[BYPASS MODE]</color>");
        }

        if (showHidden && serverRoles.RemoteAdmin)
        {
            builder.Append(" <color=#43C6DB>[RA AUTHENTICATED]</color>");
        }

        if (serverRoles.IsInOverwatch)
        {
            builder.Append(" <color=#008080>[OVERWATCH MODE]</color>");
        }

        if (gameplayData)
        {
            builder.Append("\nClass: ").Append(PlayerRoleLoader.AllRoles.TryGetValue(target.GetRoleId(), out PlayerRoleBase role) ? role.RoleName : "None");
            builder.Append(" <color=#fcff99>[HP: ").Append(CommandProcessor.GetRoundedStat<HealthStat>(target)).Append("]</color>");
            builder.Append(" <color=green>[AHP: ").Append(CommandProcessor.GetRoundedStat<AhpStat>(target)).Append("]</color>");
            builder.Append(" <color=#977dff>[HS: ").Append(CommandProcessor.GetRoundedStat<HumeShieldStat>(target)).Append("]</color>");
            builder.Append("\nPosition: ").Append(target.transform.position.ToPreciseString());
        }
        else
        {
            builder.Append("\n<color=#D4AF37>Some fields were hidden. GameplayData permission required.</color>");
        }

        builder.Append("</color>");
        if (PlayerEvents.HasRequestedRaPlayerInfo)
        {
            PlayerEvents.OnRequestedRaPlayerInfo(new PlayerRequestedRaPlayerInfoEventArgs(sender, target, !isShort, hasUserIdPerms, builder, idBuilder, ipBuilder, userIdBuilder));
        }

        SendClipboard(sender, RaClipboard.RaClipBoardType.PlayerId, idBuilder);
        SendClipboard(sender, RaClipboard.RaClipBoardType.Ip, ipBuilder);
        SendClipboard(sender, RaClipboard.RaClipBoardType.UserId, userIdBuilder);
        sender.RaReply(StringBuilderPool.Shared.ToStringReturn(builder), true, true, string.Empty);
        RaPlayerQR.Send(sender, false, string.IsNullOrEmpty(characterClassManager.UserId) ? NoIdQr : characterClassManager.UserId);
    }

    private static void AppendMuteFlag(StringBuilder builder, VcMuteFlags flags, VcMuteFlags flag, string name)
    {
        if ((flags & flag) == flag)
        {
            builder.Append(" <color=#F70D1A>").Append(name).Append("</color>");
        }
    }

    // The fork only sends clipboard entries that have content.
    private static void SendClipboard(CommandSender sender, RaClipboard.RaClipBoardType type, StringBuilder? builder)
    {
        if (builder == null)
        {
            return;
        }

        if (builder.Length > 0)
        {
            RaClipboard.Send(sender, type, builder.ToString());
        }

        StringBuilderPool.Shared.Return(builder);
    }
}
