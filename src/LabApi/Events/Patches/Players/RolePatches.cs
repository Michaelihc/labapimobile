using HarmonyLib;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using Mirror;
using PlayerRoles;

namespace LabApi.Events.Patches.Players;

// Official: PlayerRoles/PlayerRoleManager.cs ServerSetRole
// The Carl Mod server sends the new role to clients on the next frame, so ChangedRole runs before that sync instead of after it.
[HarmonyPatch(typeof(PlayerRoleManager), nameof(PlayerRoleManager.ServerSetRole))]
internal static class PlayerChangingRolePatch
{
    private static bool Prefix(PlayerRoleManager __instance, ref RoleTypeId newRole, ref RoleChangeReason reason, ref RoleSpawnFlags spawnFlags, out RoleTypeId? __state)
    {
        __state = null;
        if (!NetworkServer.active || (!PlayerEvents.HasChangingRole && !PlayerEvents.HasChangedRole))
        {
            return true;
        }

        ReferenceHub hub = __instance.Hub;
        PlayerRoleBase oldRole = __instance.CurrentRole;
        if (PlayerEvents.HasChangingRole)
        {
            PlayerChangingRoleEventArgs e = new(hub, oldRole, newRole, reason, spawnFlags);
            PlayerEvents.OnChangingRole(e);
            if (!e.IsAllowed)
            {
                return false;
            }

            newRole = e.NewRole;
            reason = e.ChangeReason;
            spawnFlags = e.SpawnFlags;
        }

        __state = oldRole.RoleTypeId;
        return true;
    }

    private static void Postfix(PlayerRoleManager __instance, RoleChangeReason reason, RoleSpawnFlags spawnFlags, RoleTypeId? __state)
    {
        if (!__state.HasValue || !PlayerEvents.HasChangedRole)
        {
            return;
        }

        PlayerEvents.OnChangedRole(new PlayerChangedRoleEventArgs(__instance.Hub, __state.Value, __instance.CurrentRole, reason, spawnFlags));
    }
}
