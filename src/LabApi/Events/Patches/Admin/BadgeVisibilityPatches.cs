using CommandSystem;
using HarmonyLib;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using RemoteAdmin;
using System;
using RaHideTagCommand = CommandSystem.Commands.RemoteAdmin.HideTagCommand;
using RaShowTagCommand = CommandSystem.Commands.RemoteAdmin.ShowTagCommand;

namespace LabApi.Events.Patches.Admin;

/// <summary>
/// Raises ChangingBadgeVisibility / ChangedBadgeVisibility for the game console hidetag command.
/// </summary>
/// <remarks>
/// The fork handles hidetag/showtag/globaltag in <see cref="CharacterClassManager"/> commands and RA commands instead of
/// the official ServerRoles.TryHideTag / RefreshLocalTag / RefreshGlobalTag helpers, so each of them is patched.
/// With subscribers the fork body runs here with the events around the change; without subscribers it runs untouched.
/// </remarks>
// Official: ServerRoles.cs TryHideTag
[HarmonyPatch(typeof(CharacterClassManager), nameof(CharacterClassManager.UserCode_CmdRequestHideTag))]
internal static class ClientHideTagPatch
{
    private static bool Prefix(CharacterClassManager __instance)
    {
        if (!BadgeVisibilityEvents.Any)
        {
            return true;
        }

        if (!__instance._commandRateLimit.CanExecute())
        {
            return false;
        }

        ServerRoles roles = __instance.SrvRoles;
        if (!string.IsNullOrEmpty(roles.HiddenBadge))
        {
            __instance.ConsolePrint("Your badge is already hidden.", "yellow");
            return false;
        }

        if (string.IsNullOrEmpty(roles.MyText))
        {
            __instance.ConsolePrint("You don't have a badge.", "red");
            return false;
        }

        if (!BadgeVisibilityEvents.Changing(roles._hub, false, false))
        {
            return false;
        }

        BadgeVisibilityEvents.HideLocalBadge(roles);
        __instance.ConsolePrint("Badge hidden.", "green");
        BadgeVisibilityEvents.Changed(roles._hub, false, false);
        return false;
    }
}

/// <summary>
/// Raises ChangingBadgeVisibility / ChangedBadgeVisibility for the game console showtag and globaltag commands.
/// </summary>
// Official: ServerRoles.cs RefreshLocalTag / RefreshGlobalTag
[HarmonyPatch(typeof(CharacterClassManager), nameof(CharacterClassManager.UserCode_CmdRequestShowTag__Boolean))]
internal static class ClientShowTagPatch
{
    private static bool Prefix(CharacterClassManager __instance, bool global)
    {
        if (!BadgeVisibilityEvents.Any)
        {
            return true;
        }

        if (!__instance._commandRateLimit.CanExecute())
        {
            return false;
        }

        ServerRoles roles = __instance.SrvRoles;
        if (!global)
        {
            if (!BadgeVisibilityEvents.Changing(roles._hub, false, true))
            {
                return false;
            }

            roles.NetworkGlobalBadge = null;
            roles.HiddenBadge = null;
            roles.RpcResetFixed();
            roles.RefreshPermissions(disp: true);
            __instance.ConsolePrint("Local tag refreshed.", "green");
            BadgeVisibilityEvents.Changed(roles._hub, false, true);
            return false;
        }

        if (string.IsNullOrEmpty(roles.PrevBadge))
        {
            __instance.ConsolePrint("You don't have a global tag.", "magenta");
            return false;
        }

        if (IsGlobalBadgeBlocked(roles))
        {
            __instance.ConsolePrint("You can't show this type of global badge on this server. Try joining server with global badges allowed.", "red");
            return false;
        }

        if (!BadgeVisibilityEvents.Changing(roles._hub, true, true))
        {
            return false;
        }

        roles.NetworkGlobalBadge = roles.PrevBadge;
        roles.GlobalHidden = false;
        roles.HiddenBadge = null;
        roles.RpcResetFixed();
        __instance.ConsolePrint("Global tag refreshed.", "green");
        BadgeVisibilityEvents.Changed(roles._hub, true, true);
        return false;
    }

    private static bool IsGlobalBadgeBlocked(ServerRoles roles)
    {
        if (!string.IsNullOrEmpty(roles.MyText) && roles.RemoteAdmin)
        {
            return false;
        }

        YamlConfig config = GameCore.ConfigFile.ServerConfig;
        bool verified = ServerStatic.PermissionsHandler.IsVerified;
        return roles.GlobalBadgeType switch
        {
            3 or 4 => config.GetBool("block_gtag_banteam_badges") && !verified,
            1 => config.GetBool("block_gtag_staff_badges"),
            2 => config.GetBool("block_gtag_management_badges") && !verified,
            0 => config.GetBool("block_gtag_patreon_badges") && !verified,
            _ => false,
        };
    }
}

/// <summary>
/// Raises ChangingBadgeVisibility / ChangedBadgeVisibility for the RA hidetag command.
/// </summary>
// Official: ServerRoles.cs TryHideTag
[HarmonyPatch(typeof(RaHideTagCommand), nameof(RaHideTagCommand.Execute))]
internal static class RaHideTagPatch
{
    private static bool Prefix(ICommandSender sender, out string response, ref bool __result)
    {
        response = null!;
        if (!BadgeVisibilityEvents.Any)
        {
            return true;
        }

        if (sender is not PlayerCommandSender playerSender)
        {
            response = "You must be in-game to use this command!";
            __result = false;
            return false;
        }

        ServerRoles roles = playerSender.ReferenceHub.serverRoles;
        if (!roles.BypassStaff)
        {
            if (!string.IsNullOrEmpty(roles.HiddenBadge))
            {
                response = "Your badge is already hidden.";
                __result = false;
                return false;
            }

            if (string.IsNullOrEmpty(roles.MyText))
            {
                response = "Your don't have any badge.";
                __result = false;
                return false;
            }
        }

        if (!BadgeVisibilityEvents.Changing(roles._hub, false, false))
        {
            response = BadgeVisibilityEvents.CancelledResponse;
            __result = false;
            return false;
        }

        BadgeVisibilityEvents.HideLocalBadge(roles);
        BadgeVisibilityEvents.Changed(roles._hub, false, false);
        response = "Tag hidden!";
        __result = true;
        return false;
    }
}

/// <summary>
/// Raises ChangingBadgeVisibility / ChangedBadgeVisibility for the RA showtag command.
/// </summary>
// Official: ServerRoles.cs RefreshLocalTag
[HarmonyPatch(typeof(RaShowTagCommand), nameof(RaShowTagCommand.Execute))]
internal static class RaShowTagPatch
{
    private static bool Prefix(ICommandSender sender, out string response, ref bool __result)
    {
        response = null!;
        if (!BadgeVisibilityEvents.Any)
        {
            return true;
        }

        if (sender is not PlayerCommandSender playerSender)
        {
            response = "You must be in-game to use this command!";
            __result = false;
            return false;
        }

        ServerRoles roles = playerSender.ReferenceHub.serverRoles;
        if (!BadgeVisibilityEvents.Changing(roles._hub, false, true))
        {
            response = BadgeVisibilityEvents.CancelledResponse;
            __result = false;
            return false;
        }

        roles.HiddenBadge = null;
        roles.GlobalHidden = false;
        roles.RpcResetFixed();
        roles.RefreshPermissions(disp: true);
        BadgeVisibilityEvents.Changed(roles._hub, false, true);
        response = "Local tag refreshed!";
        __result = true;
        return false;
    }
}

internal static class BadgeVisibilityEvents
{
    internal const string CancelledResponse = "A server plugin cancelled the badge change.";

    internal static bool Any => PlayerEvents.HasChangingBadgeVisibility || PlayerEvents.HasChangedBadgeVisibility;

    internal static bool Changing(ReferenceHub hub, bool isGlobal, bool newVisibility)
    {
        if (!PlayerEvents.HasChangingBadgeVisibility)
        {
            return true;
        }

        PlayerChangingBadgeVisibilityEventArgs e = new(hub, isGlobal, newVisibility);
        PlayerEvents.OnChangingBadgeVisibility(e);
        return e.IsAllowed;
    }

    internal static void Changed(ReferenceHub hub, bool isGlobal, bool newVisibility)
    {
        if (PlayerEvents.HasChangedBadgeVisibility)
        {
            PlayerEvents.OnChangedBadgeVisibility(new PlayerChangedBadgeVisibilityEventArgs(hub, isGlobal, newVisibility));
        }
    }

    internal static void HideLocalBadge(ServerRoles roles)
    {
        roles.GlobalHidden = roles.GlobalSet;
        roles.HiddenBadge = roles.MyText;
        roles.NetworkGlobalBadge = null;
        roles.SetText(null);
        roles.SetColor(null);
        roles.RefreshHiddenTag();
    }
}
