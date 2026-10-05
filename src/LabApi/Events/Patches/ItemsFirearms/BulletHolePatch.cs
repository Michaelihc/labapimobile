using HarmonyLib;
using InventorySystem.Items.Firearms.BasicMessages;
using InventorySystem.Items.Firearms.Modules;
using Knife.DeferredDecals;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using UnityEngine;
using Utils.Networking;

namespace LabApi.Events.Patches.ItemsFirearms;

/// <summary>
/// Raises PlacingBulletHole / PlacedBulletHole for every bullet hole decal the server broadcasts.
/// </summary>
/// <remarks>
/// The fork sends a <see cref="GunHitMessage"/> with the raycast start and direction; clients raycast to the surface.
/// A changed <see cref="PlayerPlacingBulletHoleEventArgs.HitPosition"/> becomes the direction from the raycast start.
/// The decal type is informational: the client picks the decal itself.
/// </remarks>
// Official: InventorySystem/Items/Firearms/Modules/ImpactEffectsModule.cs ServerSendImpactDecal
[HarmonyPatch(typeof(StandardHitregBase), nameof(StandardHitregBase.PlaceBulletholeDecal))]
internal static class BulletHolePatch
{
    private static bool Prefix(StandardHitregBase __instance, Ray ray, RaycastHit hit)
    {
        if (!PlayerEvents.HasPlacingBulletHole && !PlayerEvents.HasPlacedBulletHole)
        {
            return true;
        }

        ReferenceHub owner = __instance.Hub;
        DecalPoolType decalType = __instance is BuckshotHitreg ? DecalPoolType.Buckshot : DecalPoolType.Bullet;
        Vector3 point = hit.point;
        Vector3 startRaycast = point + (ray.origin - point).normalized;
        Vector3 direction = ray.direction;

        if (PlayerEvents.HasPlacingBulletHole)
        {
            PlayerPlacingBulletHoleEventArgs e = new(owner, decalType, point, startRaycast);
            PlayerEvents.OnPlacingBulletHole(e);
            if (!e.IsAllowed)
            {
                return false;
            }

            if (e.HitPosition != point || e.RaycastStart != startRaycast)
            {
                point = e.HitPosition;
                startRaycast = e.RaycastStart;
                direction = (point - startRaycast).normalized;
            }
        }

        new GunHitMessage(startRaycast, direction, isBlood: false).SendToAuthenticated();

        if (PlayerEvents.HasPlacedBulletHole)
        {
            PlayerEvents.OnPlacedBulletHole(new PlayerPlacedBulletHoleEventArgs(owner, decalType, point, startRaycast));
        }

        return false;
    }
}
