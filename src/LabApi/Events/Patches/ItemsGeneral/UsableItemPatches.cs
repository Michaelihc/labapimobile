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
/// without subscribers it runs untouched. Carl Mod 0.0.5 cancels a use whose item is no longer held (or no longer in the
/// inventory) through <c>UsableItemsController.ResetInvalidUse</c>, never completes a use while the speed multiplier is 0,
/// and clears the current use before completing it. Applied only when the native body is one of the known Carl Mod bodies.
/// </remarks>
// Official: InventorySystem/Items/Usables/UsableItemsController.cs Update
[HarmonyPatch(typeof(UsableItemsController), nameof(UsableItemsController.Update))]
internal static class UsedItemPatch
{
    // UsableItemsController.Update of both Carl Mod 0.0.4 builds, and of 0.0.5.
    private const string CarlMod004Body = "70dffda1b5902666";
    private const string Version005Body = "8f7f9817950916a5";

    private static readonly List<KeyValuePair<ReferenceHub, PlayerHandler>> Snapshot = [];

    // The event's backing delegate; C# only allows invoking it from inside UsableItemsController.
    private static readonly FieldInfo CompletedEventField = AccessTools.Field(typeof(UsableItemsController), nameof(UsableItemsController.ServerOnUsingCompleted));

    private static readonly MethodInfo? Target = AccessTools.DeclaredMethod(typeof(UsableItemsController), nameof(UsableItemsController.Update));

    private static readonly BodyVariant Variant = NativeBody.Identify(Target, CarlMod004Body, Version005Body, out Fingerprint);

    private static readonly string? Fingerprint;

    /// <summary>
    /// Gets Carl Mod 0.0.5's <c>UsableItemsController.ResetInvalidUse</c>, or <see langword="null"/> on builds without it.
    /// </summary>
    internal static Func<ReferenceHub, PlayerHandler, bool>? ResetInvalidUse { get; } =
        AccessTools.DeclaredMethod(typeof(UsableItemsController), "ResetInvalidUse", [typeof(ReferenceHub), typeof(PlayerHandler)]) is MethodInfo method
        && method.IsStatic && method.ReturnType == typeof(bool)
            ? AccessTools.MethodDelegate<Func<ReferenceHub, PlayerHandler, bool>>(method)
            : null;

    private static bool Prepare()
    {
        if (Variant == BodyVariant.Standard || (Variant == BodyVariant.Version005 && ResetInvalidUse != null))
        {
            return true;
        }

        PatchManager.Skip(typeof(UsedItemPatch), NativeBody.UnknownBody(Target, Fingerprint, "ItemUsageEffectsApplying / UsedItem are not raised."));
        return false;
    }

    private static bool Prefix()
    {
        if (!PlayerEvents.HasItemUsageEffectsApplying && !PlayerEvents.HasUsedItem)
        {
            return true;
        }

        if (Variant == BodyVariant.Version005)
        {
            UpdateVersion005();
            return false;
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

    /// <summary>
    /// The Carl Mod 0.0.5 body with the events.
    /// </summary>
    private static void UpdateVersion005()
    {
        if (!NetworkServer.active)
        {
            return;
        }

        Func<ReferenceHub, PlayerHandler, bool> resetInvalidUse = ResetInvalidUse!;
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
                if (resetInvalidUse(hub, handler) || current.ItemSerial == 0 || current.Item == null)
                {
                    continue;
                }

                float speedMultiplier = current.Item.ItemTypeId.GetSpeedMultiplier(hub);
                if (speedMultiplier <= 0f || Time.timeSinceLevelLoad < current.StartTime + current.Item.UseTime / speedMultiplier)
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

                handler.CurrentUsable = CurrentlyUsedItem.None;
                current.Item.ServerOnUsingCompleted();
                ((Action<ReferenceHub, UsableItem>?)CompletedEventField.GetValue(null))?.Invoke(hub, current.Item);
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
    }
}

/// <summary>
/// Raises UsingItem and CancellingUsingItem / CancelledUsingItem for item usage status requests.
/// </summary>
/// <remarks>
/// With subscribers the fork body runs here; without subscribers it runs untouched. Carl Mod 0.0.5 first cancels a current
/// use whose item is no longer held (<c>UsableItemsController.ResetInvalidUse</c>). Applied only when the native body is one
/// of the known Carl Mod bodies.
/// </remarks>
// Official: InventorySystem/Items/Usables/UsableItemsController.cs ServerReceivedStatus
[HarmonyPatch(typeof(UsableItemsController), nameof(UsableItemsController.ServerReceivedStatus))]
internal static class UsingItemPatch
{
    // UsableItemsController.ServerReceivedStatus of both Carl Mod 0.0.4 builds, and of 0.0.5.
    private const string CarlMod004Body = "40397420fa738aa3";
    private const string Version005Body = "257a4cad44f6515b";

    private static readonly MethodInfo? Target = AccessTools.DeclaredMethod(typeof(UsableItemsController), nameof(UsableItemsController.ServerReceivedStatus));

    private static readonly BodyVariant Variant = NativeBody.Identify(Target, CarlMod004Body, Version005Body, out Fingerprint);

    private static readonly string? Fingerprint;

    private static bool Prepare()
    {
        if (Variant == BodyVariant.Standard || (Variant == BodyVariant.Version005 && UsedItemPatch.ResetInvalidUse != null))
        {
            return true;
        }

        PatchManager.Skip(typeof(UsingItemPatch), NativeBody.UnknownBody(Target, Fingerprint, "UsingItem and CancellingUsingItem / CancelledUsingItem for usable items are not raised."));
        return false;
    }

    private static bool Prefix(NetworkConnection conn, StatusMessage msg)
    {
        if (!PlayerEvents.HasUsingItem && !PlayerEvents.HasCancellingUsingItem && !PlayerEvents.HasCancelledUsingItem)
        {
            return true;
        }

        if (conn == null || conn.identity == null || !ReferenceHub.TryGetHub(conn.identity.gameObject, out ReferenceHub hub)
            || hub.inventory.CurInstance is not UsableItem usableItem || usableItem.ItemSerial != msg.ItemSerial)
        {
            return false;
        }

        PlayerHandler handler = UsableItemsController.GetHandler(hub);
        if (Variant == BodyVariant.Version005)
        {
            UsedItemPatch.ResetInvalidUse!(hub, handler);
        }

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
