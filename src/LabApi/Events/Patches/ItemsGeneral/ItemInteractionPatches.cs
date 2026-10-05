using HarmonyLib;
using InventorySystem.Items.Autosync;
using InventorySystem.Items.Coin;
using InventorySystem.Items.Firearms;
using InventorySystem.Items.Firearms.BasicMessages;
using InventorySystem.Items.Flashlight;
using InventorySystem.Items.Jailbird;
using InventorySystem.Items.Radio;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using Mirror;
using PlayerRoles.Spectating;
using UnityEngine;
using Utils.Networking;
using VoiceChat.Playbacks;

namespace LabApi.Events.Patches.ItemsGeneral;

/// <summary>
/// Raises FlippingCoin / FlippedCoin when a coin flip command is processed.
/// </summary>
/// <remarks>
/// The fork coin has no interaction blockers. With subscribers the fork body runs here; without subscribers it runs untouched.
/// </remarks>
// Official: InventorySystem/Items/Coin/Coin.cs ServerProcessCmd
[HarmonyPatch(typeof(Coin), nameof(Coin.ServerProcessCmd))]
internal static class FlippingCoinPatch
{
    private static bool Prefix(Coin __instance)
    {
        if (!PlayerEvents.HasFlippingCoin && !PlayerEvents.HasFlippedCoin)
        {
            return true;
        }

        // AutosyncItem.ServerProcessCmd is empty in the fork, so skipping the base call changes nothing.
        if (!__instance.Owner.isLocalPlayer && __instance._lastUseSw.Elapsed.TotalSeconds < 0.6000000238418579)
        {
            return false;
        }

        bool isTails = Random.value >= 0.5f;
        if (PlayerEvents.HasFlippingCoin)
        {
            PlayerFlippingCoinEventArgs e = new(__instance.Owner, __instance, isTails);
            PlayerEvents.OnFlippingCoin(e);
            if (!e.IsAllowed)
            {
                return false;
            }

            isTails = e.IsTails;
        }

        __instance._lastUseSw.Restart();
        using (new AutosyncRpc(__instance, toAll: true, out NetworkWriter writer))
        {
            writer.WriteBool(isTails);
        }

        if (PlayerEvents.HasFlippedCoin)
        {
            PlayerEvents.OnFlippedCoin(new PlayerFlippedCoinEventArgs(__instance.Owner, __instance, isTails));
        }

        return false;
    }
}

/// <summary>
/// Raises TogglingRadio / ToggledRadio and ChangingRadioRange / ChangedRadioRange for radio commands.
/// </summary>
/// <remarks>
/// The fork radio also accepts the mobile IncreaseRange / DecreaseRange commands; they raise the range events when the
/// range actually changes. With subscribers the fork body runs here; without subscribers it runs untouched.
/// </remarks>
// Official: InventorySystem/Items/Radio/RadioItem.cs ServerProcessCmd
[HarmonyPatch(typeof(RadioItem), nameof(RadioItem.ServerProcessCmd))]
internal static class RadioCommandPatch
{
    private static bool Prefix(RadioItem __instance, RadioMessages.RadioCommand command)
    {
        if (!PlayerEvents.HasTogglingRadio && !PlayerEvents.HasToggledRadio && !PlayerEvents.HasChangingRadioRange && !PlayerEvents.HasChangedRadioRange)
        {
            return true;
        }

        ReferenceHub owner = __instance.Owner;
        switch (command)
        {
            case RadioMessages.RadioCommand.Enable:
                if (__instance._enabled || __instance._battery <= 0f || !Toggling(owner, __instance, true))
                {
                    return false;
                }

                __instance._enabled = true;
                Toggled(owner, __instance, true);
                break;
            case RadioMessages.RadioCommand.Disable:
                if (!__instance._enabled || !Toggling(owner, __instance, false))
                {
                    return false;
                }

                __instance._enabled = false;
                Toggled(owner, __instance, false);
                break;
            case RadioMessages.RadioCommand.ChangeRange:
            case RadioMessages.RadioCommand.IncreaseRange:
            case RadioMessages.RadioCommand.DecreaseRange:
            {
                int count = __instance.Ranges.Length;
                byte range = command switch
                {
                    RadioMessages.RadioCommand.ChangeRange => (byte)((__instance._rangeId + 1) % count),
                    RadioMessages.RadioCommand.IncreaseRange => (byte)Mathf.Min(__instance._rangeId + 1, count - 1),
                    _ => (byte)Mathf.Max(__instance._rangeId - 1, 0),
                };

                if (range == __instance._rangeId && command != RadioMessages.RadioCommand.ChangeRange)
                {
                    break;
                }

                if (PlayerEvents.HasChangingRadioRange)
                {
                    PlayerChangingRadioRangeEventArgs e = new(owner, __instance, (RadioMessages.RadioRangeLevel)range);
                    PlayerEvents.OnChangingRadioRange(e);
                    if (!e.IsAllowed)
                    {
                        return false;
                    }

                    range = (byte)e.Range;
                }

                __instance._rangeId = range;
                if (PlayerEvents.HasChangedRadioRange)
                {
                    PlayerEvents.OnChangedRadioRange(new PlayerChangedRadioRangeEventArgs(owner, __instance, (RadioMessages.RadioRangeLevel)range));
                }

                break;
            }
        }

        __instance.SendStatusMessage();
        return false;
    }

    private static bool Toggling(ReferenceHub owner, RadioItem radio, bool newState)
    {
        if (!PlayerEvents.HasTogglingRadio)
        {
            return true;
        }

        PlayerTogglingRadioEventArgs e = new(owner, radio, newState);
        PlayerEvents.OnTogglingRadio(e);
        return e.IsAllowed;
    }

    private static void Toggled(ReferenceHub owner, RadioItem radio, bool newState)
    {
        if (PlayerEvents.HasToggledRadio)
        {
            PlayerEvents.OnToggledRadio(new PlayerToggledRadioEventArgs(owner, radio, newState));
        }
    }
}

/// <summary>
/// Raises UsingRadio / UsedRadio for the per-frame battery drain of an enabled radio.
/// </summary>
/// <remarks>
/// Uses the fork's drain formula. With subscribers the fork body runs here; without subscribers it runs untouched.
/// </remarks>
// Official: InventorySystem/Items/Radio/RadioItem.cs Update
[HarmonyPatch(typeof(RadioItem), nameof(RadioItem.Update))]
internal static class UsingRadioPatch
{
    private static bool Prefix(RadioItem __instance)
    {
        if (!PlayerEvents.HasUsingRadio && !PlayerEvents.HasUsedRadio)
        {
            return true;
        }

        if (!NetworkServer.active || !__instance.IsUsable)
        {
            return false;
        }

        RadioRangeMode mode = __instance.Ranges[__instance._rangeId];
        float cost = PersonalRadioPlayback.IsTransmitting(__instance.Owner) ? mode.MinuteCostWhenTalking : mode.MinuteCostWhenIdle;
        float drain = cost / 60f / 100f * Time.deltaTime;
        if (PlayerEvents.HasUsingRadio)
        {
            PlayerUsingRadioEventArgs e = new(__instance.Owner, __instance, drain);
            PlayerEvents.OnUsingRadio(e);
            if (!e.IsAllowed)
            {
                return false;
            }

            drain = e.Drain;
        }

        __instance._battery = Mathf.Clamp01(__instance._battery - drain);
        if (__instance._battery <= 0f)
        {
            __instance._enabled = false;
        }

        if (Mathf.Abs(__instance._lastSentBatteryLevel - __instance.BatteryPercent) >= 1 && __instance.OwnerInventory.CurItem.TypeId == ItemType.Radio)
        {
            __instance.SendStatusMessage();
        }

        if (PlayerEvents.HasUsedRadio)
        {
            PlayerEvents.OnUsedRadio(new PlayerUsedRadioEventArgs(__instance.Owner, __instance, drain));
        }

        return false;
    }
}

/// <summary>
/// Raises TogglingFlashlight / ToggledFlashlight for a flashlight toggle request.
/// </summary>
/// <remarks>
/// As in the official game, a denied toggle sends nothing back, so the requesting client keeps its local state.
/// With subscribers the fork body runs here; without subscribers it runs untouched.
/// </remarks>
// Official: InventorySystem/Items/ToggleableLights/FlashlightNetworkHandler.cs ServerProcessMessage
[HarmonyPatch(typeof(FlashlightNetworkHandler), nameof(FlashlightNetworkHandler.ServerProcessMessage))]
internal static class TogglingFlashlightPatch
{
    private static bool Prefix(NetworkConnection conn, FlashlightNetworkHandler.FlashlightMessage msg)
    {
        if (!PlayerEvents.HasTogglingFlashlight && !PlayerEvents.HasToggledFlashlight)
        {
            return true;
        }

        if (msg.Serial == 0 || conn.identity == null || !ReferenceHub.TryGetHub(conn.identity.gameObject, out ReferenceHub hub))
        {
            return true;
        }

        if (hub.inventory.CurInstance is not FlashlightItem flashlight || flashlight.ItemSerial != msg.Serial)
        {
            return false;
        }

        bool newState = msg.NewState;
        if (PlayerEvents.HasTogglingFlashlight)
        {
            PlayerTogglingFlashlightEventArgs e = new(hub, flashlight, newState);
            PlayerEvents.OnTogglingFlashlight(e);
            if (!e.IsAllowed)
            {
                return false;
            }

            newState = e.NewState;
        }

        flashlight.IsEmittingLight = newState;
        new FlashlightNetworkHandler.FlashlightMessage(msg.Serial, newState).SendToAuthenticated();
        if (PlayerEvents.HasToggledFlashlight)
        {
            PlayerEvents.OnToggledFlashlight(new PlayerToggledFlashlightEventArgs(hub, flashlight, newState));
        }

        return false;
    }
}

/// <summary>
/// Raises ProcessingJailbirdMessage / ProcessedJailbirdMessage and InspectingItem / InspectedItem for jailbird commands.
/// </summary>
/// <remarks>
/// The fork jailbird has no interaction blockers, so <c>AllowAttack</c> and <c>AllowInspect</c> start as true.
/// As in the official game, clearing <c>AllowInspect</c> drops the whole message.
/// With subscribers the fork body runs here; without subscribers it runs untouched.
/// </remarks>
// Official: InventorySystem/Items/Jailbird/JailbirdItem.cs ServerProcessCmd
[HarmonyPatch(typeof(JailbirdItem), nameof(JailbirdItem.ServerProcessCmd))]
internal static class JailbirdMessagePatch
{
    private static bool Prefix(JailbirdItem __instance, NetworkReader reader)
    {
        if (!PlayerEvents.HasProcessingJailbirdMessage && !PlayerEvents.HasProcessedJailbirdMessage && !PlayerEvents.HasInspectingItem && !PlayerEvents.HasInspectedItem)
        {
            return true;
        }

        // AutosyncItem.ServerProcessCmd is empty in the fork, so skipping the base call changes nothing.
        if (__instance._broken || !__instance.IsEquipped)
        {
            return false;
        }

        ReferenceHub owner = __instance.Owner;
        JailbirdMessageType msg = (JailbirdMessageType)reader.ReadByte();
        bool allowAttack = true;
        bool allowInspect = true;
        if (PlayerEvents.HasProcessingJailbirdMessage)
        {
            PlayerProcessingJailbirdMessageEventArgs e = new(owner, __instance, msg, allowAttack, allowInspect);
            PlayerEvents.OnProcessingJailbirdMessage(e);
            if (!e.IsAllowed)
            {
                return false;
            }

            msg = e.Message;
            allowAttack = e.AllowAttack;
            allowInspect = e.AllowInspect;
        }

        if (!allowInspect)
        {
            return false;
        }

        switch (msg)
        {
            case JailbirdMessageType.AttackTriggered:
                if (!allowAttack || __instance._serverAttackTriggered || !__instance._serverAttackCooldown.TolerantIsReady)
                {
                    return false;
                }

                __instance._serverAttackTriggered = true;
                __instance._serverAttackCooldown.Trigger(__instance._meleeCooldown);
                __instance.SendRpc(JailbirdMessageType.AttackTriggered);
                break;
            case JailbirdMessageType.AttackPerformed:
                if (!allowAttack)
                {
                    return false;
                }

                if (__instance._serverCharging)
                {
                    __instance.ServerAttack(reader);
                }
                else if (__instance._serverAttackTriggered)
                {
                    __instance._serverAttackTriggered = false;
                    __instance.ServerAttack(reader);
                }

                break;
            case JailbirdMessageType.Inspect:
                if (PlayerEvents.HasInspectingItem)
                {
                    PlayerInspectingItemEventArgs e = new(owner, __instance);
                    PlayerEvents.OnInspectingItem(e);
                    if (!e.IsAllowed)
                    {
                        return false;
                    }
                }

                __instance.SendRpc(msg);
                if (PlayerEvents.HasInspectedItem)
                {
                    PlayerEvents.OnInspectedItem(new PlayerInspectedItemEventArgs(owner, __instance));
                }

                break;
            case JailbirdMessageType.ChargeLoadTriggered:
                if (!allowAttack)
                {
                    return false;
                }

                __instance.SendRpc(msg);
                break;
            case JailbirdMessageType.ChargeFailed:
                __instance.SendRpc(msg);
                break;
            case JailbirdMessageType.ChargeStarted:
                if (!allowAttack)
                {
                    __instance.SendRpc(JailbirdMessageType.ChargeFailed);
                    break;
                }

                if (__instance._serverCharging)
                {
                    return false;
                }

                __instance._serverCharging = true;
                __instance._chargeResetTime = NetworkTime.time + __instance._chargeDuration;
                __instance.TotalChargesPerformed++;
                double resetTime = __instance._chargeResetTime;
                using (new AutosyncRpc(__instance, toAll: true, out NetworkWriter writer))
                {
                    writer.WriteByte((byte)JailbirdMessageType.ChargeStarted);
                    writer.WriteDouble(resetTime);
                }

                break;
        }

        if (PlayerEvents.HasProcessedJailbirdMessage)
        {
            PlayerEvents.OnProcessedJailbirdMessage(new PlayerProcessedJailbirdMessageEventArgs(owner, __instance, msg));
        }

        return false;
    }
}

/// <summary>
/// Raises InspectingItem / InspectedItem for a firearm inspect request.
/// </summary>
/// <remarks>
/// The fork inspects firearms client-side and the server only relays the request to spectators, so a denied inspect
/// stops the relay but the inspecting player still sees the animation. Other requests pass through untouched.
/// </remarks>
// Official: InventorySystem/Items/Firearms/Modules/SimpleInspectorModule.cs ServerProcessCmd
[HarmonyPatch(typeof(FirearmBasicMessagesHandler), nameof(FirearmBasicMessagesHandler.ServerRequestReceived))]
internal static class FirearmInspectPatch
{
    private static bool Prefix(NetworkConnection conn, RequestMessage msg)
    {
        if (msg.Request != RequestType.Inspect || (!PlayerEvents.HasInspectingItem && !PlayerEvents.HasInspectedItem))
        {
            return true;
        }

        if (conn.identity == null || !ReferenceHub.TryGetHub(conn.identity.gameObject, out ReferenceHub hub))
        {
            return true;
        }

        if (msg.Serial != hub.inventory.CurItem.SerialNumber || hub.inventory.CurInstance is not Firearm firearm || firearm == null)
        {
            return true;
        }

        if (PlayerEvents.HasInspectingItem)
        {
            PlayerInspectingItemEventArgs e = new(hub, firearm);
            PlayerEvents.OnInspectingItem(e);
            if (!e.IsAllowed)
            {
                return false;
            }
        }

        msg.SendToHubsConditionally(IsSpectator);
        if (PlayerEvents.HasInspectedItem)
        {
            PlayerEvents.OnInspectedItem(new PlayerInspectedItemEventArgs(hub, firearm));
        }

        return false;
    }

    private static bool IsSpectator(ReferenceHub hub) => hub.roleManager.CurrentRole is SpectatorRole;
}
