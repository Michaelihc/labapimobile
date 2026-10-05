using Achievements;
using HarmonyLib;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Arguments.ScpEvents;
using LabApi.Events.Handlers;
using Mirror;
using PlayerRoles.PlayableScps.HumeShield;
using PlayerRoles.Spectating;
using System;
using System.Reflection;

namespace LabApi.Events.Patches.Players;

// Official: Achievements/AchievementHandlerBase.cs ServerAchieve
// The Carl Mod server has no AllowAchievements switch; every achievement sent to a remote client raises the event.
[HarmonyPatch(typeof(AchievementHandlerBase), nameof(AchievementHandlerBase.ServerAchieve))]
internal static class PlayerReceivedAchievementPatch
{
    private static void Postfix(NetworkConnection conn, AchievementName targetAchievement)
    {
        if (!PlayerEvents.HasReceivedAchievement || conn.identity == null || conn.identity.isLocalPlayer)
        {
            return;
        }

        PlayerEvents.OnReceivedAchievement(new PlayerReceivedAchievementEventArgs(conn.identity, targetAchievement));
    }
}

// Official: PlayerRoles/Spectating/SpectatorNetworking.cs Init (SpectatedNetIdSyncMessage server handler)
// The Carl Mod handler accepts every target, so the event follows every accepted change.
[HarmonyPatch]
internal static class PlayerChangedSpectatorPatch
{
    private static MethodBase TargetMethod()
    {
        foreach (Type nested in typeof(SpectatorNetworking).GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public))
        {
            foreach (MethodInfo method in nested.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (parameters.Length == 2 && parameters[0].ParameterType == typeof(NetworkConnectionToClient)
                    && parameters[1].ParameterType == typeof(SpectatorNetworking.SpectatedNetIdSyncMessage))
                {
                    return method;
                }
            }
        }

        throw new MissingMethodException(nameof(SpectatorNetworking), "SpectatedNetIdSyncMessage server handler");
    }

    private static void Prefix(NetworkConnectionToClient __0, out uint? __state)
    {
        __state = null;
        if (PlayerEvents.HasChangedSpectator && __0.identity != null && ReferenceHub.TryGetHubNetID(__0.identity.netId, out ReferenceHub hub)
            && hub.roleManager.CurrentRole is SpectatorRole spectatorRole)
        {
            __state = spectatorRole.SyncedSpectatedNetId;
        }
    }

    private static void Postfix(NetworkConnectionToClient __0, SpectatorNetworking.SpectatedNetIdSyncMessage __1, uint? __state)
    {
        if (!__state.HasValue || !ReferenceHub.TryGetHubNetID(__0.identity.netId, out ReferenceHub hub))
        {
            return;
        }

        ReferenceHub.TryGetHubNetID(__state.Value, out ReferenceHub oldTarget);
        ReferenceHub.TryGetHubNetID(__1.NetId, out ReferenceHub newTarget);
        PlayerEvents.OnChangedSpectator(new PlayerChangedSpectatorEventArgs(hub, oldTarget, newTarget));
    }
}

// Official: PlayerRoles/PlayableScps/HumeShield/DynamicHumeShieldController.cs OnHsValueChanged
[HarmonyPatch(typeof(DynamicHumeShieldController), nameof(DynamicHumeShieldController.OnHsValueChanged))]
internal static class ScpHumeShieldBrokenPatch
{
    private static void Postfix(DynamicHumeShieldController __instance, float prevValue, float newValue)
    {
        if (!ScpEvents.HasHumeShieldBroken || !NetworkServer.active || newValue > 0f || prevValue <= 0f || __instance._shieldBreakSound == null)
        {
            return;
        }

        ScpEvents.OnHumeShieldBroken(new ScpHumeShieldBrokenEventArgs(__instance.Owner));
    }
}
