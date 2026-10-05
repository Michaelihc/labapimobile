using HarmonyLib;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using Mirror;

namespace LabApi.Events.Patches.Admin;

/// <summary>
/// Raises GroupChanging / GroupChanged when a player's permission group is set.
/// </summary>
// Official: ServerRoles.cs SetGroup
[HarmonyPatch(typeof(ServerRoles), nameof(ServerRoles.SetGroup))]
internal static class GroupPatch
{
    private static bool Prefix(ServerRoles __instance, ref UserGroup group, out bool __state)
    {
        __state = false;
        if ((!PlayerEvents.HasGroupChanging && !PlayerEvents.HasGroupChanged) || !NetworkServer.active)
        {
            return true;
        }

        if (PlayerEvents.HasGroupChanging)
        {
            PlayerGroupChangingEventArgs e = new(__instance._hub, group);
            PlayerEvents.OnGroupChanging(e);
            if (!e.IsAllowed)
            {
                return false;
            }

            group = e.Group!;
        }

        __state = true;
        return true;
    }

    private static void Postfix(ServerRoles __instance, bool __state)
    {
        if (!__state || !PlayerEvents.HasGroupChanged)
        {
            return;
        }

        PlayerEvents.OnGroupChanged(new PlayerGroupChangedEventArgs(__instance._hub, __instance.Group));
    }
}
