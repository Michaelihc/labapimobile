using HarmonyLib;
using InventorySystem.Disarming;
using InventorySystem.Items;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using Mirror;
using PlayerRoles;
using Utils.Networking;

namespace LabApi.Events.Patches.Players;

// Official: InventorySystem/Disarming/DisarmingHandlers.cs ServerProcessDisarmMessage
// Runs the original untouched unless a cuff event has subscribers; otherwise replays the Carl Mod body with the events.
[HarmonyPatch(typeof(DisarmingHandlers), nameof(DisarmingHandlers.ServerProcessDisarmMessage))]
internal static class PlayerCuffPatch
{
    private static readonly AccessTools.FieldRef<DisarmingHandlers.PlayerDisarmed> OnPlayerDisarmed =
        AccessTools.StaticFieldRefAccess<DisarmingHandlers.PlayerDisarmed>(AccessTools.Field(typeof(DisarmingHandlers), nameof(DisarmingHandlers.OnPlayerDisarmed)));

    private static bool Prefix(NetworkConnection conn, DisarmMessage msg)
    {
        if (!PlayerEvents.HasCuffing && !PlayerEvents.HasCuffed && !PlayerEvents.HasUncuffing && !PlayerEvents.HasUncuffed)
        {
            return true;
        }

        if (!NetworkServer.active || !DisarmingHandlers.ServerCheckCooldown(conn) || !ReferenceHub.TryGetHub(conn.identity.gameObject, out ReferenceHub hub))
        {
            return false;
        }

        ReferenceHub target = msg.PlayerToDisarm;
        if (!msg.PlayerIsNull && ((target.transform.position - hub.transform.position).sqrMagnitude > 20f || (target.inventory.CurInstance != null && target.inventory.CurInstance.TierFlags != ItemTierFlags.Common)))
        {
            return false;
        }

        bool targetDisarmed = !msg.PlayerIsNull && target.inventory.IsDisarmed();
        bool canDisarm = !msg.PlayerIsNull && hub.CanDisarm(target);
        if (targetDisarmed && !msg.Disarm)
        {
            bool isScp = hub.GetTeam() == Team.SCPs;
            if (isScp)
            {
                // Official asks plugins and then refuses SCPs; the Carl Mod server answers SCPs with the current list.
                if (PlayerEvents.HasUncuffing)
                {
                    PlayerEvents.OnUncuffing(new PlayerUncuffingEventArgs(hub, target, false) { IsAllowed = false });
                }

                hub.networkIdentity.connectionToClient.Send(DisarmingHandlers.NewDisarmedList);
                return false;
            }

            if (!hub.inventory.IsDisarmed())
            {
                if (PlayerEvents.HasUncuffing)
                {
                    PlayerUncuffingEventArgs uncuffing = new(hub, target, true);
                    PlayerEvents.OnUncuffing(uncuffing);
                    if (!uncuffing.IsAllowed)
                    {
                        return false;
                    }
                }

                target.inventory.SetDisarmedStatus(null);
                if (PlayerEvents.HasUncuffed)
                {
                    PlayerEvents.OnUncuffed(new PlayerUncuffedEventArgs(hub, target, true));
                }
            }
        }
        else
        {
            if (!(!targetDisarmed && canDisarm) || !msg.Disarm)
            {
                hub.networkIdentity.connectionToClient.Send(DisarmingHandlers.NewDisarmedList);
                return false;
            }

            if (target.inventory.CurInstance == null || target.inventory.CurInstance.CanHolster())
            {
                if (PlayerEvents.HasCuffing)
                {
                    PlayerCuffingEventArgs cuffing = new(hub, target);
                    PlayerEvents.OnCuffing(cuffing);
                    if (!cuffing.IsAllowed)
                    {
                        return false;
                    }
                }

                OnPlayerDisarmed()?.Invoke(hub, target);
                target.inventory.SetDisarmedStatus(hub.inventory);
                if (PlayerEvents.HasCuffed)
                {
                    PlayerEvents.OnCuffed(new PlayerCuffedEventArgs(hub, target));
                }
            }
        }

        DisarmingHandlers.NewDisarmedList.SendToAuthenticated();
        return false;
    }
}
