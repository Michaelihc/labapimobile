using HarmonyLib;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using Mirror;
using PlayerRoles.Voice;

namespace LabApi.Events.Patches.Admin;

/// <summary>
/// Raises UsingIntercom when a player passes the intercom speaker checks; cancelling makes the check fail.
/// </summary>
// Official: PlayerRoles/Voice/Intercom.cs CheckPlayer
[HarmonyPatch(typeof(Intercom), nameof(Intercom.CheckPlayer))]
internal static class UsingIntercomPatch
{
    private static void Postfix(ReferenceHub hub, ref bool __result)
    {
        if (!__result || !PlayerEvents.HasUsingIntercom)
        {
            return;
        }

        PlayerUsingIntercomEventArgs e = new(hub, Intercom.State);
        PlayerEvents.OnUsingIntercom(e);
        __result = e.IsAllowed;
    }
}

/// <summary>
/// Raises UsedIntercom when the intercom leaves the in-use state for cooldown.
/// </summary>
/// <remarks>
/// Hooked on the state setter so no per-frame work is added; this also covers the RA intercom timeout command.
/// </remarks>
// Official: PlayerRoles/Voice/Intercom.cs Update
[HarmonyPatch(typeof(Intercom), nameof(Intercom.State), MethodType.Setter)]
internal static class UsedIntercomPatch
{
    private static void Prefix(IntercomState value, out SpeakerState __state)
    {
        __state = default;
        if (value != IntercomState.Cooldown || !PlayerEvents.HasUsedIntercom || !NetworkServer.active || Intercom.State != IntercomState.InUse)
        {
            return;
        }

        __state = new SpeakerState(Intercom._singleton._curSpeaker);
    }

    private static void Postfix(SpeakerState __state)
    {
        if (!__state.Fired || !PlayerEvents.HasUsedIntercom)
        {
            return;
        }

        PlayerEvents.OnUsedIntercom(new PlayerUsedIntercomEventArgs(__state.Speaker, Intercom.State));
    }

    internal readonly struct SpeakerState(ReferenceHub? speaker)
    {
        public readonly bool Fired = true;

        public readonly ReferenceHub? Speaker = speaker;
    }
}
