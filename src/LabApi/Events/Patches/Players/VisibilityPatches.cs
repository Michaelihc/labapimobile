using HarmonyLib;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using Mirror;
using PlayerRoles.FirstPersonControl;
using PlayerRoles.FirstPersonControl.NetworkMessages;
using PlayerRoles.Visibility;

namespace LabApi.Events.Patches.Players;

// Official: PlayerRoles/FirstPersonControl/NetworkMessages/FpcServerPositionDistributor.cs WriteAll
// Runs the original untouched unless ValidatedVisibility has subscribers; otherwise replays the Carl Mod body with the event.
[HarmonyPatch(typeof(FpcServerPositionDistributor), nameof(FpcServerPositionDistributor.WriteAll))]
internal static class PlayerValidatedVisibilityPatch
{
    private static bool Prefix(ReferenceHub receiver, NetworkWriter writer)
    {
        if (!PlayerEvents.HasValidatedVisibility)
        {
            return true;
        }

        ushort count = 0;
        VisibilityController? visibilityController = receiver.roleManager.CurrentRole is ICustomVisibilityRole customVisibilityRole ? customVisibilityRole.VisibilityController : null;
        foreach (ReferenceHub hub in ReferenceHub.AllHubs)
        {
            if (hub.netId == receiver.netId || hub.roleManager.CurrentRole is not IFpcRole fpcRole)
            {
                continue;
            }

            bool invisible = visibilityController != null && !visibilityController.ValidateVisibility(hub);
            if (!hub.isLocalPlayer)
            {
                PlayerValidatedVisibilityEventArgs e = new(receiver, hub, !invisible);
                PlayerEvents.OnValidatedVisibility(e);
                invisible = !e.IsVisible;
            }

            FpcSyncData syncData = FpcServerPositionDistributor.GetNewSyncData(receiver, hub, fpcRole.FpcModule, invisible);
            if (!invisible)
            {
                FpcServerPositionDistributor._bufferPlayerIDs[count] = hub.PlayerId;
                FpcServerPositionDistributor._bufferSyncData[count] = syncData;
                count++;
            }
        }

        writer.WriteUShort(count);
        for (int i = 0; i < count; i++)
        {
            writer.WriteRecyclablePlayerId(new RecyclablePlayerId(FpcServerPositionDistributor._bufferPlayerIDs[i]));
            FpcServerPositionDistributor._bufferSyncData[i].Write(writer);
        }

        return false;
    }
}
