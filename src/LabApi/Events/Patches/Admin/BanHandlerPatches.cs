using HarmonyLib;
using LabApi.Events.Arguments.ServerEvents;
using LabApi.Events.Handlers;
using System;

namespace LabApi.Events.Patches.Admin;

/// <summary>
/// Raises BanIssuing / BanIssued for new bans and BanUpdating / BanUpdated when a ban with the same ID exists.
/// </summary>
/// <remarks>
/// The fork rewrites the ban file in one step instead of removing and re-issuing, so an update raises only the
/// update events (plus BanRevoking / BanRevoked for the old entry when a handler changes the ban type).
/// </remarks>
// Official: BanHandler.cs IssueBan
[HarmonyPatch(typeof(BanHandler), nameof(BanHandler.IssueBan))]
internal static class BanIssuingPatch
{
    private static readonly char[] InvalidIdChars = [';', '\r', '\n'];

    private static bool Prefix(ref BanDetails ban, ref BanHandler.BanType banType, bool forced, ref bool __result, out IssueState __state)
    {
        __state = default;
        if (!ServerEvents.HasBanIssuing && !ServerEvents.HasBanIssued && !ServerEvents.HasBanUpdating && !ServerEvents.HasBanUpdated)
        {
            return true;
        }

        // Bans the fork method rejects never reach the events.
        if (ban == null || string.IsNullOrWhiteSpace(ban.Id) || ban.Id.IndexOfAny(InvalidIdChars) >= 0 || ban.Expires < 0 || ban.Expires > DateTime.MaxValue.Ticks)
        {
            return true;
        }

        string id = ban.Id.Trim();
        if (banType == BanHandler.BanType.IP && (id.Equals("localClient", StringComparison.OrdinalIgnoreCase) || id == "127.0.0.1"))
        {
            return true;
        }

        BanDetails oldBan = BanHandler.GetBan(id, banType);
        if (oldBan == null)
        {
            if (ServerEvents.HasBanIssuing)
            {
                BanIssuingEventArgs e = new(banType, ban);
                ServerEvents.OnBanIssuing(e);
                if (!forced && !e.IsAllowed)
                {
                    __result = false;
                    return false;
                }

                banType = e.BanType;
                ban = e.BanDetails;
            }

            __state = new IssueState(null);
            return true;
        }

        if (ServerEvents.HasBanUpdating)
        {
            BanUpdatingEventArgs e = new(banType, ban, oldBan);
            ServerEvents.OnBanUpdating(e);
            if (!forced && !e.IsAllowed)
            {
                __result = false;
                return false;
            }

            if (e.BanType != banType)
            {
                BanHandler.RemoveBan(oldBan.Id, banType, true);
                banType = e.BanType;
            }

            ban = e.BanDetails;
        }

        __state = new IssueState(oldBan);
        return true;
    }

    private static void Postfix(BanDetails ban, BanHandler.BanType banType, bool __result, IssueState __state)
    {
        if (!__state.Fired || !__result)
        {
            return;
        }

        if (__state.OldBan == null)
        {
            if (ServerEvents.HasBanIssued)
            {
                ServerEvents.OnBanIssued(new BanIssuedEventArgs(banType, ban));
            }

            return;
        }

        if (ServerEvents.HasBanUpdated)
        {
            ServerEvents.OnBanUpdated(new BanUpdatedEventArgs(banType, ban, __state.OldBan));
        }
    }

    internal readonly struct IssueState(BanDetails? oldBan)
    {
        public readonly bool Fired = true;

        public readonly BanDetails? OldBan = oldBan;
    }
}

/// <summary>
/// Raises BanRevoking / BanRevoked when a ban is removed.
/// </summary>
// Official: BanHandler.cs RemoveBan
[HarmonyPatch(typeof(BanHandler), nameof(BanHandler.RemoveBan))]
internal static class BanRevokingPatch
{
    private static bool Prefix(string id, BanHandler.BanType banType, bool forced, out RevokeState __state)
    {
        __state = default;
        if ((!ServerEvents.HasBanRevoking && !ServerEvents.HasBanRevoked) || id == null)
        {
            return true;
        }

        BanDetails banDetails = BanHandler.GetBan(id.Trim(), banType);
        if (ServerEvents.HasBanRevoking)
        {
            BanRevokingEventArgs e = new(banType, banDetails);
            ServerEvents.OnBanRevoking(e);
            if (!forced && !e.IsAllowed)
            {
                return false;
            }
        }

        __state = new RevokeState(banDetails);
        return true;
    }

    private static void Postfix(BanHandler.BanType banType, RevokeState __state)
    {
        if (!__state.Fired || !ServerEvents.HasBanRevoked)
        {
            return;
        }

        ServerEvents.OnBanRevoked(new BanRevokedEventArgs(banType, __state.BanDetails));
    }

    internal readonly struct RevokeState(BanDetails banDetails)
    {
        public readonly bool Fired = true;

        public readonly BanDetails BanDetails = banDetails;
    }
}
