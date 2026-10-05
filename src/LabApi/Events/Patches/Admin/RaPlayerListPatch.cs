using HarmonyLib;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using NorthwoodLib.Pools;
using PlayerRoles;
using RemoteAdmin;
using RemoteAdmin.Communication;
using System;
using System.Collections.Generic;
using System.Text;
using VoiceChat;

namespace LabApi.Events.Patches.Admin;

/// <summary>
/// Raises RequestingRaPlayerList / RaPlayerListAddingPlayer / RaPlayerListAddedPlayer / RequestedRaPlayerList while the
/// Remote Admin player list is built.
/// </summary>
/// <remarks>
/// With subscribers the fork list builder runs here (sorting with pooled lists instead of LINQ) with the events
/// inserted as in official SL; without subscribers the fork builder runs untouched. The fork list has no muted badge,
/// so <see cref="PlayerRaPlayerListAddingPlayerEventArgs.IsMuted"/> reports the mute state but is not rendered, and the
/// fork has no "Class" sorting (it sorts by player ID).
/// </remarks>
// Official: RemoteAdmin/Communication/RaPlayerList.cs ReceiveData(CommandSender, string)
[HarmonyPatch(typeof(RaPlayerList), nameof(RaPlayerList.ReceiveData), typeof(CommandSender), typeof(string))]
internal static class RaPlayerListPatch
{
    private const string OverwatchBadge = "<link=RA_OverwatchEnabled><color=white>[</color><color=#03f8fc></color><color=white>]</color></link> ";

    private static readonly Comparison<ReferenceHub> ById = CompareById;

    private static readonly Comparison<ReferenceHub> ByIdDescending = (a, b) => CompareById(b, a);

    private static readonly Comparison<ReferenceHub> ByName = CompareByName;

    private static readonly Comparison<ReferenceHub> ByNameDescending = (a, b) => CompareByName(b, a);

    private static readonly Comparison<ReferenceHub> ByTeam = CompareByTeam;

    private static readonly Comparison<ReferenceHub> ByTeamDescending = (a, b) => CompareByTeam(b, a);

    private static bool Prefix(RaPlayerList __instance, CommandSender sender, string data)
    {
        if (!PlayerEvents.HasRequestingRaPlayerList && !PlayerEvents.HasRequestedRaPlayerList
            && !PlayerEvents.HasRaPlayerListAddingPlayer && !PlayerEvents.HasRaPlayerListAddedPlayer)
        {
            return true;
        }

        string[] args = data.Split(' ');
        if (args.Length != 3 || !int.TryParse(args[0], out int silent) || !int.TryParse(args[1], out int sortingId)
            || sortingId < (int)RaPlayerList.PlayerSorting.Ids || sortingId > (int)RaPlayerList.PlayerSorting.Team)
        {
            return false;
        }

        bool isSilent = silent == 1;
        bool isDescending = args[2].Equals("1", StringComparison.Ordinal);
        RaPlayerList.PlayerSorting sorting = (RaPlayerList.PlayerSorting)sortingId;
        bool viewHiddenLocalBadges = CommandProcessor.CheckPermissions(sender, PlayerPermissions.ViewHiddenBadges);
        bool viewHiddenGlobalBadges = CommandProcessor.CheckPermissions(sender, PlayerPermissions.ViewHiddenGlobalBadges);
        if (sender is PlayerCommandSender playerSender && playerSender.ServerRoles.Staff)
        {
            viewHiddenLocalBadges = true;
            viewHiddenGlobalBadges = true;
        }

        StringBuilder listBuilder = StringBuilderPool.Shared.Rent("\n");
        StringBuilder entryBuilder = StringBuilderPool.Shared.Rent();
        if (PlayerEvents.HasRequestingRaPlayerList)
        {
            PlayerRequestingRaPlayerListEventArgs e = new(sender, listBuilder, isDescending, sorting, viewHiddenLocalBadges, viewHiddenGlobalBadges);
            PlayerEvents.OnRequestingRaPlayerList(e);
            if (!e.IsAllowed)
            {
                StringBuilderPool.Shared.Return(listBuilder);
                StringBuilderPool.Shared.Return(entryBuilder);
                return false;
            }

            isDescending = e.IsDescending;
            sorting = e.Sorting;
            viewHiddenLocalBadges = e.ViewHiddenLocalBadges;
            viewHiddenGlobalBadges = e.ViewHiddenGlobalBadges;
        }

        List<ReferenceHub> hubs = ListPool<ReferenceHub>.Shared.Rent();
        foreach (ReferenceHub hub in ReferenceHub.AllHubs)
        {
            hubs.Add(hub);
        }

        hubs.Sort(GetComparison(sorting, isDescending));
        foreach (ReferenceHub hub in hubs)
        {
            if (hub.Mode == ClientInstanceMode.DedicatedServer || hub.Mode == ClientInstanceMode.Unverified)
            {
                continue;
            }

            string prefix = __instance.GetPrefix(hub, viewHiddenLocalBadges, viewHiddenGlobalBadges);
            bool inOverwatch = hub.serverRoles.IsInOverwatch;
            string body = "<color={RA_ClassColor}>(" + hub.PlayerId + ") "
                + (hub.nicknameSync.CombinedName ?? string.Empty).Replace("\n", string.Empty).Replace("RA_", string.Empty) + "</color>";
            if (PlayerEvents.HasRaPlayerListAddingPlayer)
            {
                PlayerRaPlayerListAddingPlayerEventArgs e = new(sender, hub, entryBuilder, prefix, inOverwatch, VoiceChatMutes.GetFlags(hub) != VcMuteFlags.None, body);
                PlayerEvents.OnRaPlayerListAddingPlayer(e);
                if (!e.IsAllowed)
                {
                    entryBuilder.Clear();
                    continue;
                }

                prefix = e.Prefix;
                inOverwatch = e.InOverwatch;
                body = e.Body;
            }

            entryBuilder.Append(prefix);
            if (inOverwatch)
            {
                entryBuilder.Append(OverwatchBadge);
            }

            entryBuilder.Append(body);
            entryBuilder.AppendLine();
            if (PlayerEvents.HasRaPlayerListAddedPlayer)
            {
                PlayerEvents.OnRaPlayerListAddedPlayer(new PlayerRaPlayerListAddedPlayerEventArgs(sender, hub, entryBuilder));
            }

            listBuilder.Append(entryBuilder);
            entryBuilder.Clear();
        }

        ListPool<ReferenceHub>.Shared.Return(hubs);
        StringBuilderPool.Shared.Return(entryBuilder);
        if (PlayerEvents.HasRequestedRaPlayerList)
        {
            PlayerEvents.OnRequestedRaPlayerList(new PlayerRequestedRaPlayerListEventArgs(sender, listBuilder));
        }

        sender.RaReply("$" + __instance.DataId + " " + StringBuilderPool.Shared.ToStringReturn(listBuilder), true, !isSilent, string.Empty);
        return false;
    }

    private static Comparison<ReferenceHub> GetComparison(RaPlayerList.PlayerSorting sorting, bool isDescending) => sorting switch
    {
        RaPlayerList.PlayerSorting.Team => isDescending ? ByTeamDescending : ByTeam,
        RaPlayerList.PlayerSorting.Alphabetical => isDescending ? ByNameDescending : ByName,
        _ => isDescending ? ByIdDescending : ById,
    };

    private static int CompareById(ReferenceHub a, ReferenceHub b) => a.PlayerId.CompareTo(b.PlayerId);

    private static int CompareByName(ReferenceHub a, ReferenceHub b)
    {
        int result = Comparer<string>.Default.Compare(a.nicknameSync.DisplayName ?? a.nicknameSync.MyNick, b.nicknameSync.DisplayName ?? b.nicknameSync.MyNick);
        return result != 0 ? result : CompareById(a, b);
    }

    private static int CompareByTeam(ReferenceHub a, ReferenceHub b)
    {
        int result = ((int)a.roleManager.CurrentRole.Team).CompareTo((int)b.roleManager.CurrentRole.Team);
        return result != 0 ? result : CompareById(a, b);
    }
}
