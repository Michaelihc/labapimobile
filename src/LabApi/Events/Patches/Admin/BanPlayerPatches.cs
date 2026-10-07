using CommandSystem;
using HarmonyLib;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using RemoteAdmin;

namespace LabApi.Events.Patches.Admin;

/// <summary>
/// Raises Kicking / Kicked around every kick, which all go through this overload.
/// </summary>
// Official: BanPlayer.cs KickUser(ReferenceHub, ICommandSender, string)
[HarmonyPatch(typeof(BanPlayer), nameof(BanPlayer.KickUser), typeof(ReferenceHub), typeof(ICommandSender), typeof(string))]
internal static class KickingPatch
{
    private static bool Prefix(ReferenceHub target, ICommandSender issuer, ref string reason, ref bool __result, out bool __state)
    {
        __state = false;
        if (!PlayerEvents.HasKicking && !PlayerEvents.HasKicked)
        {
            return true;
        }

        if (PlayerEvents.HasKicking)
        {
            PlayerKickingEventArgs e = new(target, BanPlayerPatchUtils.GetIssuerHub(issuer), reason);
            PlayerEvents.OnKicking(e);
            if (!e.IsAllowed)
            {
                __result = false;
                return false;
            }

            reason = e.Reason;
        }

        __state = true;
        return true;
    }

    private static void Postfix(ReferenceHub target, ICommandSender issuer, string reason, bool __result, bool __state)
    {
        if (!__state || !__result || !PlayerEvents.HasKicked)
        {
            return;
        }

        PlayerEvents.OnKicked(new PlayerKickedEventArgs(target, BanPlayerPatchUtils.GetIssuerHub(issuer), reason));
    }
}

/// <summary>
/// Raises Banning / Banned around player bans, which all go through this overload.
/// </summary>
/// <remarks>
/// Banning fires only once the fork's own preconditions pass (valid target, non-zero duration, no staff bypass,
/// valid device user ID or device ID), matching the point after the staff-bypass check where official SL raises it.
/// Banned is raised only when the game reports the ban as issued; on 0.0.5 that includes the IP ban, and a failed IP ban
/// removes the ID ban again.
/// </remarks>
// Official: BanPlayer.cs BanUser(Footprint, ICommandSender, string, long)
[HarmonyPatch(typeof(BanPlayer), nameof(BanPlayer.BanUser), typeof(ReferenceHub), typeof(ICommandSender), typeof(string), typeof(long))]
internal static class BanningPatch
{
    private static bool Prefix(ReferenceHub target, ICommandSender issuer, ref string reason, ref long duration, ref bool __result, out BanState __state)
    {
        __state = default;
        if (!PlayerEvents.HasBanning && !PlayerEvents.HasBanned)
        {
            return true;
        }

        // Same early exits as the fork method; those paths either fail or turn into a kick.
        if (target == null || issuer == null || duration <= 0 || duration > uint.MaxValue || target.serverRoles.BypassStaff)
        {
            return true;
        }

        // CharacterClassManager.DeviceId on 0.0.5 returns this ID too.
        string userId = target.characterClassManager.UserId;
        if (!CarlModBuild.IsValidPlayerId(userId))
        {
            return true;
        }

        ReferenceHub issuerHub = BanPlayerPatchUtils.GetIssuerHub(issuer);
        if (PlayerEvents.HasBanning)
        {
            PlayerBanningEventArgs e = new(target, userId, issuerHub, reason, duration);
            PlayerEvents.OnBanning(e);
            if (!e.IsAllowed)
            {
                __result = false;
                return false;
            }

            duration = e.Duration;
            reason = e.Reason;
        }

        __state = new BanState(userId, issuerHub);
        return true;
    }

    private static void Postfix(ReferenceHub target, string reason, long duration, bool __result, BanState __state)
    {
        if (!__state.Fired || !__result || !PlayerEvents.HasBanned)
        {
            return;
        }

        PlayerEvents.OnBanned(new PlayerBannedEventArgs(target, __state.UserId, __state.IssuerHub, reason, duration));
    }

    internal readonly struct BanState(string userId, ReferenceHub issuerHub)
    {
        public readonly bool Fired = true;

        public readonly string UserId = userId;

        public readonly ReferenceHub IssuerHub = issuerHub;
    }
}

internal static class BanPlayerPatchUtils
{
    internal static ReferenceHub GetIssuerHub(ICommandSender issuer) =>
        issuer is PlayerCommandSender playerSender ? playerSender.ReferenceHub : ReferenceHub.HostHub;
}
