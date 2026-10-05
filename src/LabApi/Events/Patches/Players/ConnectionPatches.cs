using HarmonyLib;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;

namespace LabApi.Events.Patches.Players;

// Official: NicknameSync.cs UserCode_CmdSetNick__String (offline-mode join) and CentralAuth/PlayerAuthenticationManager.cs (online join).
// The Carl Mod server has no central authentication: a client is ready once its user id is assigned and its first nickname is accepted.
[HarmonyPatch(typeof(NicknameSync), nameof(NicknameSync.UserCode_CmdSetNick__String))]
internal static class PlayerJoinedPatch
{
    private static void Prefix(NicknameSync __instance, out bool __state)
    {
        // Only the first nickname of a remote client completes the join, as in the official offline-mode path.
        __state = PlayerEvents.HasJoined && !__instance.isLocalPlayer && !__instance.NickSet;
    }

    private static void Postfix(NicknameSync __instance, string n, bool __state)
    {
        if (!__state || n == null || n.Length > 1024)
        {
            return;
        }

        // The rejected-name paths ban or kick the client and return before the official event.
        __instance.CleanNickName(n, out bool printable);
        if (!printable)
        {
            return;
        }

        PlayerEvents.OnJoined(new PlayerJoinedEventArgs(__instance._hub));
    }
}

// Official: ReferenceHub.cs OnDestroy
[HarmonyPatch(typeof(ReferenceHub), nameof(ReferenceHub.OnDestroy))]
internal static class PlayerLeftPatch
{
    private static void Prefix(ReferenceHub __instance)
    {
        if (!PlayerEvents.HasLeft || __instance.isLocalPlayer)
        {
            return;
        }

        PlayerEvents.OnLeft(new PlayerLeftEventArgs(__instance));
    }
}
