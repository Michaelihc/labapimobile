using HarmonyLib;
using InventorySystem.Items.Firearms;
using InventorySystem.Items.Firearms.Attachments;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using Mirror;
using PlayerRoles.Spectating;
using System.Collections.Generic;

namespace LabApi.Events.Patches.ItemsFirearms;

/// <summary>
/// Raises ChangingAttachments / ChangedAttachments for workstation (and spectator) attachment change requests.
/// </summary>
// Official: InventorySystem/Items/Firearms/Attachments/AttachmentsServerHandler.cs ServerReceiveChangeRequest
[HarmonyPatch(typeof(AttachmentsServerHandler), nameof(AttachmentsServerHandler.ServerReceiveChangeRequest))]
internal static class ChangingAttachmentsPatch
{
    private static bool Prefix(NetworkConnection conn, ref AttachmentsChangeRequest msg, out ChangeState __state)
    {
        __state = default;
        if (!PlayerEvents.HasChangingAttachments && !PlayerEvents.HasChangedAttachments)
        {
            return true;
        }

        // Same gate as the fork method: the request is ignored unless every check passes.
        if (!NetworkServer.active || conn.identity == null || !ReferenceHub.TryGetHub(conn.identity.gameObject, out ReferenceHub hub))
        {
            return true;
        }

        if (hub.inventory.CurInstance is not Firearm firearm || firearm == null || hub.inventory.CurItem.SerialNumber != msg.WeaponSerial)
        {
            return true;
        }

        if (hub.roleManager.CurrentRole is not SpectatorRole && !AnyWorkstationNearby(hub))
        {
            return true;
        }

        uint oldCode = firearm.GetCurrentAttachmentsCode();
        uint newCode = msg.AttachmentsCode;
        if (PlayerEvents.HasChangingAttachments)
        {
            PlayerChangingAttachmentsEventArgs e = new(hub, firearm, oldCode, newCode);
            PlayerEvents.OnChangingAttachments(e);
            if (!e.IsAllowed)
            {
                return false;
            }

            newCode = e.NewAttachments;
            msg.AttachmentsCode = newCode;
        }

        __state = new ChangeState(hub, firearm, oldCode, newCode);
        return true;
    }

    private static void Postfix(ChangeState __state)
    {
        if (__state.Firearm == null || !PlayerEvents.HasChangedAttachments)
        {
            return;
        }

        PlayerEvents.OnChangedAttachments(new PlayerChangedAttachmentsEventArgs(__state.Hub, __state.Firearm, __state.OldCode, __state.NewCode));
    }

    private static bool AnyWorkstationNearby(ReferenceHub hub)
    {
        foreach (WorkstationController workstation in WorkstationController.AllWorkstations)
        {
            if (workstation != null && workstation.Status == 3 && workstation.IsInRange(hub))
            {
                return true;
            }
        }

        return false;
    }

    internal readonly struct ChangeState(ReferenceHub hub, Firearm firearm, uint oldCode, uint newCode)
    {
        public readonly ReferenceHub Hub = hub;

        public readonly Firearm? Firearm = firearm;

        public readonly uint OldCode = oldCode;

        public readonly uint NewCode = newCode;
    }
}

/// <summary>
/// Raises SendingAttachmentsPrefs / SentAttachmentsPrefs when a client sends its preferred attachment setup.
/// </summary>
// Official: InventorySystem/Items/Firearms/Attachments/AttachmentsServerHandler.cs ServerReceivePreference
[HarmonyPatch(typeof(AttachmentsServerHandler), nameof(AttachmentsServerHandler.ServerReceivePreference))]
internal static class SendingAttachmentsPrefsPatch
{
    private static bool Prefix(NetworkConnection conn, ref AttachmentsSetupPreference msg, out PreferenceState __state)
    {
        __state = default;
        if (!PlayerEvents.HasSendingAttachmentsPrefs && !PlayerEvents.HasSentAttachmentsPrefs)
        {
            return true;
        }

        if (!NetworkServer.active || conn.identity == null || !ReferenceHub.TryGetHub(conn.identity.gameObject, out ReferenceHub hub))
        {
            return true;
        }

        uint oldCode = 0;
        if (AttachmentsServerHandler.PlayerPreferences.TryGetValue(hub, out Dictionary<ItemType, uint> preferences) && preferences != null)
        {
            preferences.TryGetValue(msg.Weapon, out oldCode);
        }

        uint newCode = msg.AttachmentsCode;
        if (PlayerEvents.HasSendingAttachmentsPrefs)
        {
            PlayerSendingAttachmentsPrefsEventArgs e = new(hub, msg.Weapon, oldCode, newCode);
            PlayerEvents.OnSendingAttachmentsPrefs(e);
            if (!e.IsAllowed)
            {
                return false;
            }

            newCode = e.NewAttachments;
            msg.AttachmentsCode = newCode;
        }

        __state = new PreferenceState(hub, msg.Weapon, oldCode, newCode);
        return true;
    }

    private static void Postfix(PreferenceState __state)
    {
        if (__state.Hub == null || !PlayerEvents.HasSentAttachmentsPrefs)
        {
            return;
        }

        PlayerEvents.OnSentAttachmentsPrefs(new PlayerSentAttachmentsPrefsEventArgs(__state.Hub, __state.Weapon, __state.OldCode, __state.NewCode));
    }

    internal readonly struct PreferenceState(ReferenceHub hub, ItemType weapon, uint oldCode, uint newCode)
    {
        public readonly ReferenceHub? Hub = hub;

        public readonly ItemType Weapon = weapon;

        public readonly uint OldCode = oldCode;

        public readonly uint NewCode = newCode;
    }
}
