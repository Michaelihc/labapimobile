using HarmonyLib;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using PlayerRoles.Spectating;
using UnityEngine;

namespace LabApi.Events.Patches.ItemsFirearms;

/// <summary>
/// Raises SendingHitmarker / SentHitmarker for every hitmarker sent to a remote player.
/// </summary>
/// <remarks>
/// Mirrors the fork method body. The fork message has no audio flag or hitmarker type.
/// </remarks>
// Official: Hitmarker.cs SendHitmarkerDirectly(ReferenceHub, float, bool, HitmarkerType)
[HarmonyPatch(typeof(Hitmarker), nameof(Hitmarker.SendHitmarker), typeof(ReferenceHub), typeof(float))]
internal static class HitmarkerPatch
{
    private const float MaxSize = 2.55f;

    private static bool Prefix(ReferenceHub hub, float size)
    {
        if (!PlayerEvents.HasSendingHitmarker && !PlayerEvents.HasSentHitmarker)
        {
            return true;
        }

        if (hub == null || hub.isLocalPlayer)
        {
            return true;
        }

        size = Mathf.Clamp(size, 0f, MaxSize);
        if (PlayerEvents.HasSendingHitmarker)
        {
            PlayerSendingHitmarkerEventArgs e = new(hub, size);
            PlayerEvents.OnSendingHitmarker(e);
            if (!e.IsAllowed)
            {
                return false;
            }

            size = e.Size;
            hub = e.Player.ReferenceHub;
        }

        new Hitmarker.HitmarkerMessage((byte)Mathf.RoundToInt(size / MaxSize * 255f)).SendToSpectatorsOf(hub, includeTarget: true);

        if (PlayerEvents.HasSentHitmarker)
        {
            PlayerEvents.OnSentHitmarker(new PlayerSentHitmarkerEventArgs(hub, size));
        }

        return false;
    }
}
