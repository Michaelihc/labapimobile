using HarmonyLib;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;

namespace LabApi.Events.Patches.Admin;

/// <summary>
/// Raises ChangingNickname / ChangedNickname when a player's display name is set.
/// </summary>
// Official: NicknameSync.cs DisplayName (setter)
[HarmonyPatch(typeof(NicknameSync), nameof(NicknameSync.DisplayName), MethodType.Setter)]
internal static class NicknamePatch
{
    private static bool Prefix(NicknameSync __instance, ref string value, out NicknameState __state)
    {
        __state = default;
        if (!PlayerEvents.HasChangingNickname && !PlayerEvents.HasChangedNickname)
        {
            return true;
        }

        ReferenceHub hub = GetHub(__instance);
        if (PlayerEvents.HasChangingNickname)
        {
            PlayerChangingNicknameEventArgs e = new(hub, __instance._displayName, value);
            PlayerEvents.OnChangingNickname(e);
            if (!e.IsAllowed)
            {
                return false;
            }

            value = e.NewNickname!;
        }

        __state = new NicknameState(__instance._cleanDisplayName);
        return true;
    }

    private static void Postfix(NicknameSync __instance, NicknameState __state)
    {
        if (!__state.Fired || !PlayerEvents.HasChangedNickname)
        {
            return;
        }

        PlayerEvents.OnChangedNickname(new PlayerChangedNicknameEventArgs(GetHub(__instance), __state.OldCleanName, __instance._displayName));
    }

    private static ReferenceHub GetHub(NicknameSync nicknameSync) => nicknameSync._hub != null ? nicknameSync._hub : ReferenceHub.GetHub(nicknameSync.gameObject);

    internal readonly struct NicknameState(string? oldCleanName)
    {
        public readonly bool Fired = true;

        public readonly string? OldCleanName = oldCleanName;
    }
}
