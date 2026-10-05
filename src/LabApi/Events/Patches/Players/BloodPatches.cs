using HarmonyLib;
using InventorySystem.Items.Firearms.BasicMessages;
using InventorySystem.Items.Firearms.Modules;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using PlayerRoles;
using UnityEngine;
using Utils.Networking;

namespace LabApi.Events.Patches.Players;

// Official: InventorySystem/Items/Firearms/Modules/ImpactEffectsModule.cs ServerSendPlayerHit
// Carl Mod firearms broadcast blood as a GunHitMessage from StandardHitregBase.PlaceBloodDecal, only for human targets.
[HarmonyPatch(typeof(StandardHitregBase), nameof(StandardHitregBase.PlaceBloodDecal))]
internal static class PlayerPlacingBloodPatch
{
    private static bool Prefix(StandardHitregBase __instance, Ray ray, RaycastHit hit, IDestructible target)
    {
        if (!PlayerEvents.HasPlacingBlood && !PlayerEvents.HasPlacedBlood)
        {
            return true;
        }

        if (!ReferenceHub.TryGetHubNetID(target.NetworkId, out ReferenceHub victim) || !victim.IsHuman())
        {
            return false;
        }

        ReferenceHub attacker = __instance.Hub;
        Vector3 hitPosition = hit.point;
        Vector3 raycastStart = hit.point + (ray.origin - hit.point).normalized;
        Vector3 direction = ray.direction;
        if (PlayerEvents.HasPlacingBlood)
        {
            PlayerPlacingBloodEventArgs e = new(victim, attacker, hitPosition, raycastStart);
            PlayerEvents.OnPlacingBlood(e);
            if (!e.IsAllowed)
            {
                return false;
            }

            if (e.HitPosition != hitPosition || e.RaycastStart != raycastStart)
            {
                hitPosition = e.HitPosition;
                raycastStart = e.RaycastStart;
                Vector3 delta = hitPosition - raycastStart;
                if (delta.sqrMagnitude > 0f)
                {
                    direction = delta.normalized;
                }
            }
        }

        new GunHitMessage(raycastStart, direction, isBlood: true).SendToAuthenticated();

        if (PlayerEvents.HasPlacedBlood)
        {
            PlayerEvents.OnPlacedBlood(new PlayerPlacedBloodEventArgs(victim, attacker, hitPosition, raycastStart));
        }

        return false;
    }
}
