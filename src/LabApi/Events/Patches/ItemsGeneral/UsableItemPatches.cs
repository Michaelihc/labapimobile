using CustomPlayerEffects;
using HarmonyLib;
using InventorySystem.Items.Usables;
using InventorySystem.Items.Usables.Scp330;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using Mirror;
using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Utils.Networking;

namespace LabApi.Events.Patches.ItemsGeneral;

/// <summary>
/// Raises ItemUsageEffectsApplying / UsedItem when an item finishes being used.
/// </summary>
/// <remarks>
/// With subscribers the fork's per-frame body runs here (reusing one snapshot list instead of allocating one per frame);
/// without subscribers it runs untouched.
/// </remarks>
// Official: InventorySystem/Items/Usables/UsableItemsController.cs Update
[HarmonyPatch(typeof(UsableItemsController), nameof(UsableItemsController.Update))]
internal static class UsedItemPatch
{
    private static readonly List<KeyValuePair<ReferenceHub, PlayerHandler>> Snapshot = [];

    // The event's backing delegate; C# only allows invoking it from inside UsableItemsController.
    private static readonly FieldInfo CompletedEventField = AccessTools.Field(typeof(UsableItemsController), nameof(UsableItemsController.ServerOnUsingCompleted));

    private static bool Prefix()
    {
        if (!PlayerEvents.HasItemUsageEffectsApplying && !PlayerEvents.HasUsedItem)
        {
            return true;
        }

        if (!StaticUnityMethods.IsPlaying || !NetworkServer.active)
        {
            return false;
        }

        Snapshot.Clear();
        Snapshot.AddRange(UsableItemsController.Handlers);
        try
        {
            for (int i = 0; i < Snapshot.Count; i++)
            {
                ReferenceHub hub = Snapshot[i].Key;
                PlayerHandler handler = Snapshot[i].Value;
                if (hub == null || hub.inventory == null)
                {
                    continue;
                }

                handler.DoUpdate(hub);
                CurrentlyUsedItem current = handler.CurrentUsable;
                if (current.ItemSerial == 0 || current.Item == null)
                {
                    continue;
                }

                float speedMultiplier = current.Item.ItemTypeId.GetSpeedMultiplier(hub);
                if (current.ItemSerial != hub.inventory.CurItem.SerialNumber)
                {
                    current.Item.OnUsingCancelled();
                    handler.CurrentUsable = CurrentlyUsedItem.None;
                    hub.inventory.connectionToClient?.Send(new StatusMessage(StatusMessage.StatusType.Cancel, current.ItemSerial));
                    continue;
                }

                if (Time.timeSinceLevelLoad < current.StartTime + current.Item.UseTime / speedMultiplier)
                {
                    continue;
                }

                if (PlayerEvents.HasItemUsageEffectsApplying)
                {
                    PlayerItemUsageEffectsApplyingEventArgs e = new(hub, current.Item);
                    PlayerEvents.OnItemUsageEffectsApplying(e);
                    if (!e.IsAllowed)
                    {
                        if (!e.ContinueProcess)
                        {
                            handler.CurrentUsable = CurrentlyUsedItem.None;
                        }

                        continue;
                    }
                }

                current.Item.ServerOnUsingCompleted();
                ((Action<ReferenceHub, UsableItem>?)CompletedEventField.GetValue(null))?.Invoke(hub, current.Item);
                handler.CurrentUsable = CurrentlyUsedItem.None;
                if (PlayerEvents.HasUsedItem)
                {
                    PlayerEvents.OnUsedItem(new PlayerUsedItemEventArgs(hub, current.Item));
                }
            }
        }
        finally
        {
            Snapshot.Clear();
        }

        return false;
    }
}

/// <summary>
/// Raises UsingItem and CancellingUsingItem / CancelledUsingItem for item usage status requests.
/// </summary>
/// <remarks>
/// With subscribers the fork body runs here; without subscribers it runs untouched.
/// </remarks>
// Official: InventorySystem/Items/Usables/UsableItemsController.cs ServerReceivedStatus
[HarmonyPatch(typeof(UsableItemsController), nameof(UsableItemsController.ServerReceivedStatus))]
internal static class UsingItemPatch
{
    private static bool Prefix(NetworkConnection conn, StatusMessage msg)
    {
        if (!PlayerEvents.HasUsingItem && !PlayerEvents.HasCancellingUsingItem && !PlayerEvents.HasCancelledUsingItem)
        {
            return true;
        }

        if (conn.identity == null || !ReferenceHub.TryGetHub(conn.identity.gameObject, out ReferenceHub hub)
            || hub.inventory.CurInstance is not UsableItem usableItem || usableItem.ItemSerial != msg.ItemSerial)
        {
            return false;
        }

        PlayerHandler handler = UsableItemsController.GetHandler(hub);
        switch (msg.Status)
        {
            case StatusMessage.StatusType.Start:
            {
                if (!usableItem.ServerValidateStartRequest(handler) || handler.CurrentUsable.ItemSerial != 0 || !usableItem.CanStartUsing)
                {
                    break;
                }

                float cooldown = UsableItemsController.GetCooldown(msg.ItemSerial, usableItem, handler);
                if (cooldown > 0f)
                {
                    conn.Send(new ItemCooldownMessage(msg.ItemSerial, cooldown));
                    break;
                }

                if (usableItem.ItemTypeId.GetSpeedMultiplier(hub) <= 0f)
                {
                    break;
                }

                if (PlayerEvents.HasUsingItem)
                {
                    PlayerUsingItemEventArgs e = new(hub, usableItem);
                    PlayerEvents.OnUsingItem(e);
                    if (!e.IsAllowed)
                    {
                        break;
                    }
                }

                handler.CurrentUsable = new CurrentlyUsedItem(usableItem, msg.ItemSerial, Time.timeSinceLevelLoad);
                handler.CurrentUsable.Item.OnUsingStarted();
                new StatusMessage(StatusMessage.StatusType.Start, msg.ItemSerial).SendToAuthenticated();
                break;
            }

            case StatusMessage.StatusType.Cancel:
            {
                if (!usableItem.ServerValidateCancelRequest(handler) || handler.CurrentUsable.ItemSerial == 0)
                {
                    break;
                }

                float speedMultiplier = handler.CurrentUsable.Item.ItemTypeId.GetSpeedMultiplier(hub);
                if (handler.CurrentUsable.StartTime + handler.CurrentUsable.Item.MaxCancellableTime / speedMultiplier <= Time.timeSinceLevelLoad)
                {
                    break;
                }

                if (PlayerEvents.HasCancellingUsingItem)
                {
                    PlayerCancellingUsingItemEventArgs e = new(hub, usableItem);
                    PlayerEvents.OnCancellingUsingItem(e);
                    if (!e.IsAllowed)
                    {
                        break;
                    }
                }

                handler.CurrentUsable.Item.OnUsingCancelled();
                handler.CurrentUsable = CurrentlyUsedItem.None;
                new StatusMessage(StatusMessage.StatusType.Cancel, msg.ItemSerial).SendToAuthenticated();
                if (PlayerEvents.HasCancelledUsingItem)
                {
                    PlayerEvents.OnCancelledUsingItem(new PlayerCancelledUsingItemEventArgs(hub, usableItem));
                }

                break;
            }
        }

        return false;
    }
}

/// <summary>
/// Raises UsingItem when a player selects a candy to eat from the SCP-330 bag.
/// </summary>
/// <remarks>
/// A denied selection is dropped before the bag starts the usage.
/// </remarks>
// Official: InventorySystem/Items/Usables/Scp330/Scp330NetworkHandler.cs ServerSelectCandy
[HarmonyPatch(typeof(Scp330NetworkHandler), nameof(Scp330NetworkHandler.ServerSelectMessageReceived))]
internal static class UsingScp330Patch
{
    private static bool Prefix(NetworkConnection conn, SelectScp330Message msg)
    {
        if (!PlayerEvents.HasUsingItem || msg.Drop)
        {
            return true;
        }

        if (conn?.identity == null || !ReferenceHub.TryGetHubNetID(conn.identity.netId, out ReferenceHub hub)
            || hub.inventory.CurInstance is not Scp330Bag bag || bag == null || bag.ItemSerial != msg.Serial
            || bag.Candies == null || msg.CandyID < 0 || msg.CandyID >= bag.Candies.Count)
        {
            return true;
        }

        PlayerUsingItemEventArgs e = new(hub, bag);
        PlayerEvents.OnUsingItem(e);
        return e.IsAllowed;
    }
}
