using HarmonyLib;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using Mirror;
using PlayerRoles.FirstPersonControl;
using PlayerRoles.FirstPersonControl.NetworkMessages;
using PlayerStatsSystem;

namespace LabApi.Events.Patches.Admin;

/// <summary>
/// Raises TogglingNoclip / ToggledNoclip when a client asks to toggle noclip.
/// </summary>
/// <remarks>
/// As in official SL, TogglingNoclip also fires for players without noclip permission, with IsAllowed preset to false.
/// </remarks>
// Official: PlayerRoles/FirstPersonControl/NetworkMessages/FpcNoclipToggleMessage.cs ProcessMessage
[HarmonyPatch(typeof(FpcNoclipToggleMessage), nameof(FpcNoclipToggleMessage.ProcessMessage))]
internal static class NoclipTogglePatch
{
    private static bool Prefix(NetworkConnection sender)
    {
        if (!PlayerEvents.HasTogglingNoclip && !PlayerEvents.HasToggledNoclip)
        {
            return true;
        }

        if (!ReferenceHub.TryGetHubNetID(sender.identity.netId, out ReferenceHub hub))
        {
            return false;
        }

        bool isAllowed = FpcNoclip.IsPermitted(hub);
        AdminFlagsStat flags = hub.playerStats.GetModule<AdminFlagsStat>();
        if (PlayerEvents.HasTogglingNoclip)
        {
            PlayerTogglingNoclipEventArgs e = new(hub, !flags.HasFlag(AdminFlags.Noclip))
            {
                IsAllowed = isAllowed,
            };

            PlayerEvents.OnTogglingNoclip(e);
            isAllowed = e.IsAllowed;
        }

        if (!isAllowed)
        {
            return false;
        }

        if (hub.roleManager.CurrentRole is IFpcRole)
        {
            flags.InvertFlag(AdminFlags.Noclip);
        }
        else
        {
            hub.gameConsoleTransmission.SendToClient("Noclip is not supported for this class.", "yellow");
        }

        if (PlayerEvents.HasToggledNoclip)
        {
            PlayerEvents.OnToggledNoclip(new PlayerToggledNoclipEventArgs(hub, flags.HasFlag(AdminFlags.Noclip)));
        }

        return false;
    }
}
