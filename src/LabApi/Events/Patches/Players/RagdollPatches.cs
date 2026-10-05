using HarmonyLib;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using Mirror;
using PlayerRoles;
using PlayerRoles.Ragdolls;
using PlayerStatsSystem;
using UnityEngine;

namespace LabApi.Events.Patches.Players;

// Official: PlayerRoles/Ragdolls/RagdollManager.cs ServerSpawnRagdoll
// Runs the original untouched unless a ragdoll event has subscribers; otherwise replays the Carl Mod body with the events.
[HarmonyPatch(typeof(RagdollManager), nameof(RagdollManager.ServerSpawnRagdoll))]
internal static class PlayerSpawnRagdollPatch
{
    private static bool Prefix(ReferenceHub owner, DamageHandlerBase handler, ref BasicRagdoll? __result)
    {
        if (!PlayerEvents.HasSpawningRagdoll && !PlayerEvents.HasSpawnedRagdoll)
        {
            return true;
        }

        __result = null;
        if (!NetworkServer.active || owner == null || owner.roleManager.CurrentRole is not IRagdollRole ragdollRole)
        {
            return false;
        }

        if (PlayerEvents.HasSpawningRagdoll)
        {
            PlayerSpawningRagdollEventArgs e = new(owner, ragdollRole.Ragdoll, handler);
            PlayerEvents.OnSpawningRagdoll(e);
            if (!e.IsAllowed)
            {
                return false;
            }
        }

        GameObject gameObject = Object.Instantiate(ragdollRole.Ragdoll.gameObject);
        if (gameObject.TryGetComponent(out BasicRagdoll ragdoll))
        {
            Transform template = ragdollRole.Ragdoll.transform;
            ragdoll.NetworkInfo = new RagdollData(owner, handler, template.localPosition, template.localRotation);

            // BasicRagdoll.Start applies the same pose next frame; setting it now lets SpawnedRagdoll handlers read it.
            gameObject.transform.SetPositionAndRotation(ragdoll.Info.StartPosition, ragdoll.Info.StartRotation);
        }
        else
        {
            ragdoll = null!;
        }

        NetworkServer.Spawn(gameObject);
        __result = ragdoll;

        if (ragdoll != null && PlayerEvents.HasSpawnedRagdoll)
        {
            PlayerEvents.OnSpawnedRagdoll(new PlayerSpawnedRagdollEventArgs(owner, ragdoll, handler));
        }

        return false;
    }
}
