using Achievements;
using CustomPlayerEffects;
using HarmonyLib;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using MapGeneration;
using Mirror;
using PlayerRoles.FirstPersonControl;
using PlayerRoles.PlayableScps.Scp106;
using PlayerStatsSystem;
using RelativePositioning;
using UnityEngine;

namespace LabApi.Events.Patches.Players;

// Official: CustomPlayerEffects/PocketCorroding.cs Enabled
// Carl Mod has no PocketCorroding: Corroding.Enabled captures the position and moves the player into the pocket dimension.
[HarmonyPatch(typeof(Corroding), nameof(Corroding.Enabled))]
internal static class PlayerEnteringPocketDimensionPatch
{
    private static bool Prefix(Corroding __instance)
    {
        if ((!PlayerEvents.HasEnteringPocketDimension && !PlayerEvents.HasEnteredPocketDimension) || !NetworkServer.active)
        {
            return true;
        }

        __instance._damagePerTick = __instance._startingDamage;
        ReferenceHub hub = __instance.Hub;
        if (hub.roleManager.CurrentRole is not IFpcRole fpcRole)
        {
            return false;
        }

        if (PlayerEvents.HasEnteringPocketDimension)
        {
            PlayerEnteringPocketDimensionEventArgs e = new(hub);
            PlayerEvents.OnEnteringPocketDimension(e);
            if (!e.IsAllowed)
            {
                return false;
            }
        }

        __instance.CapturePosition = new RelativePosition(fpcRole.FpcModule.Position);
        fpcRole.FpcModule.ServerOverridePosition(Vector3.up * -1998.5f, Vector3.zero);

        if (PlayerEvents.HasEnteredPocketDimension)
        {
            PlayerEvents.OnEnteredPocketDimension(new PlayerEnteredPocketDimensionEventArgs(hub));
        }

        return false;
    }
}

// Official: PocketDimensionTeleport.cs OnTriggerEnter (TryExit/TryKill, Exit/Kill)
// Runs the original untouched unless a leaving event has subscribers; otherwise replays the Carl Mod body with the events.
[HarmonyPatch(typeof(PocketDimensionTeleport), nameof(PocketDimensionTeleport.OnTriggerEnter))]
internal static class PlayerLeavingPocketDimensionPatch
{
    private static bool Prefix(PocketDimensionTeleport __instance, Collider other)
    {
        if (!PlayerEvents.HasLeavingPocketDimension && !PlayerEvents.HasLeftPocketDimension)
        {
            return true;
        }

        if (!NetworkServer.active)
        {
            return false;
        }

        NetworkIdentity identity = other.GetComponent<NetworkIdentity>();
        if (identity == null || !ReferenceHub.TryGetHubNetID(identity.netId, out ReferenceHub hub) || hub.roleManager.CurrentRole.ActiveTime < 1f)
        {
            return false;
        }

        bool killer = (__instance._type == PocketDimensionTeleport.PDTeleportType.Killer || AlphaWarheadController.Detonated) && !PocketDimensionTeleport.DebugBool;
        if (!killer && __instance._type != PocketDimensionTeleport.PDTeleportType.Exit && !PocketDimensionTeleport.DebugBool)
        {
            return false;
        }

        if (!killer && hub.roleManager.CurrentRole is not IFpcRole)
        {
            return false;
        }

        bool successful = !killer;
        if (PlayerEvents.HasLeavingPocketDimension)
        {
            PlayerLeavingPocketDimensionEventArgs e = new(hub, __instance, successful);
            PlayerEvents.OnLeavingPocketDimension(e);
            if (!e.IsAllowed)
            {
                return false;
            }

            successful = e.IsSuccessful;
        }

        if (successful && hub.roleManager.CurrentRole is IFpcRole fpcRole)
        {
            fpcRole.FpcModule.ServerOverridePosition(Scp106PocketExitFinder.GetBestExitPosition(fpcRole), Vector3.zero);
            hub.playerEffectsController.EnableEffect<Disabled>(10f, addDuration: true);
            hub.playerEffectsController.DisableEffect<Corroding>();
            AchievementHandlerBase.ServerAchieve(identity.connectionToClient, AchievementName.LarryFriend);
            ImageGenerator.pocketDimensionGenerator.GenerateRandom();
        }
        else if (!successful)
        {
            hub.playerStats.DealDamage(new UniversalDamageHandler(-1f, DeathTranslations.PocketDecay));
        }
        else
        {
            return false;
        }

        if (PlayerEvents.HasLeftPocketDimension)
        {
            PlayerEvents.OnLeftPocketDimension(new PlayerLeftPocketDimensionEventArgs(hub, __instance, successful));
        }

        return false;
    }
}
