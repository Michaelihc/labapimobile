using CustomPlayerEffects;
using HarmonyLib;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using Mirror;

namespace LabApi.Events.Patches.Players;

// Official: CustomPlayerEffects/StatusEffectBase.cs ForceIntensity
// The updated event runs after the effect's enable/disable callbacks instead of between the sync and the callbacks.
[HarmonyPatch(typeof(StatusEffectBase), nameof(StatusEffectBase.ForceIntensity))]
internal static class PlayerEffectUpdatePatch
{
    private static bool Prefix(StatusEffectBase __instance, ref byte value, out bool __state)
    {
        __state = false;
        if ((!PlayerEvents.HasUpdatingEffect && !PlayerEvents.HasUpdatedEffect) || __instance._intensity == value || !NetworkServer.active || __instance.Hub == null)
        {
            return true;
        }

        if (PlayerEvents.HasUpdatingEffect)
        {
            PlayerEffectUpdatingEventArgs e = new(__instance.Hub, __instance, value, __instance.Duration);
            PlayerEvents.OnUpdatingEffect(e);
            if (!e.IsAllowed)
            {
                return false;
            }

            value = e.Intensity;
            __instance.Duration = e.Duration;
        }

        __state = true;
        return true;
    }

    private static void Postfix(StatusEffectBase __instance, byte value, bool __state)
    {
        if (!__state || !PlayerEvents.HasUpdatedEffect)
        {
            return;
        }

        PlayerEvents.OnUpdatedEffect(new PlayerEffectUpdatedEventArgs(__instance.Hub, __instance, value, __instance.Duration));
    }
}
