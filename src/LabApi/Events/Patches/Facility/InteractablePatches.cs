using CustomPlayerEffects;
using Footprinting;
using HarmonyLib;
using Interactables.Interobjects;
using InventorySystem;
using InventorySystem.Items.Keycards;
using InventorySystem.Items.Usables.Scp330;
using InventorySystem.Searching;
using Interactables.Interobjects.DoorUtils;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using MapGeneration.Distributors;
using PlayerRoles;
using PlayerStatsSystem;
using Respawning;
using System.Collections.Generic;
using UnityEngine;

namespace LabApi.Events.Patches.Facility;

// Official: MapGeneration/Distributors/Locker.cs ServerInteract
[HarmonyPatch(typeof(Locker), nameof(Locker.ServerInteract))]
internal static class LockerInteractPatch
{
    private static bool Prefix(Locker __instance, ReferenceHub ply, byte colliderId)
    {
        if (!PlayerEvents.HasInteractingLocker && !PlayerEvents.HasInteractedLocker)
        {
            return true;
        }

        if (colliderId >= __instance.Chambers.Length || !__instance.Chambers[colliderId].CanInteract)
        {
            return false;
        }

        LockerChamber chamber = __instance.Chambers[colliderId];
        bool canOpen = __instance.CheckPerms(chamber.RequiredPermissions, ply) || ply.serverRoles.BypassMode;
        if (PlayerEvents.HasInteractingLocker)
        {
            PlayerInteractingLockerEventArgs e = new(ply, __instance, chamber, canOpen);
            PlayerEvents.OnInteractingLocker(e);
            if (!e.IsAllowed)
            {
                return false;
            }

            canOpen = e.CanOpen;
        }

        if (!canOpen)
        {
            __instance.RpcPlayDenied(colliderId);
        }
        else
        {
            chamber.SetDoor(!chamber.IsOpen, __instance._grantedBeep);
            __instance.RefreshOpenedSyncvar();
        }

        if (PlayerEvents.HasInteractedLocker)
        {
            PlayerEvents.OnInteractedLocker(new PlayerInteractedLockerEventArgs(ply, __instance, chamber, canOpen));
        }

        return false;
    }
}

// Official: Interactables/Interobjects/Scp330Interobject.cs ServerInteract
[HarmonyPatch(typeof(Scp330Interobject), nameof(Scp330Interobject.ServerInteract))]
internal static class Scp330InteractPatch
{
    // Carl Mod severs hands on the third candy within one life (two previous uses).
    private const int MaxAmountPerLife = 2;

    private const float TakeCooldown = 0.1f;

    private static bool Prefix(Scp330Interobject __instance, ReferenceHub ply)
    {
        if (!PlayerEvents.HasInteractingScp330 && !PlayerEvents.HasInteractedScp330)
        {
            return true;
        }

        if (!ply.IsHuman())
        {
            return false;
        }

        Footprint footprint = new(ply);
        float cooldown = TakeCooldown;
        int uses = 0;
        foreach (Footprint taken in __instance._takenCandies)
        {
            if (taken.SameLife(footprint))
            {
                cooldown = Mathf.Min(cooldown, (float)taken.Stopwatch.Elapsed.TotalSeconds);
                uses++;
            }
        }

        if (cooldown < TakeCooldown)
        {
            return false;
        }

        // Official checks Scp330Bag.CanAddCandy before the event; Carl Mod shows the overload hint instead.
        bool hasBag = Scp330Bag.TryGetBag(ply, out Scp330Bag bag);
        if (hasBag && (bag.Candies == null || bag.Candies.Count >= 6))
        {
            Scp330SearchCompletor.ShowOverloadHint(ply, true);
            return false;
        }

        bool playSound = true;
        bool allowPunishment = true;
        CandyKindID candy = Scp330Candies.GetRandom();
        if (PlayerEvents.HasInteractingScp330)
        {
            PlayerInteractingScp330EventArgs e = new(ply, uses, playSound, allowPunishment, candy);
            PlayerEvents.OnInteractingScp330(e);
            if (!e.IsAllowed)
            {
                return false;
            }

            playSound = e.PlaySound;
            allowPunishment = e.AllowPunishment;
            uses = e.Uses;
            candy = e.CandyType;
        }

        if (candy == CandyKindID.None)
        {
            return false;
        }

        if (!TryAddCandy(ply, candy))
        {
            Scp330SearchCompletor.ShowOverloadHint(ply, Scp330Bag.TryGetBag(ply, out _));
            return false;
        }

        if (playSound)
        {
            __instance.RpcMakeSound();
        }

        if (allowPunishment && uses >= MaxAmountPerLife)
        {
            ply.playerEffectsController.EnableEffect<SeveredHands>();
            while (__instance._takenCandies.Remove(footprint))
            {
            }
        }
        else
        {
            __instance._takenCandies.Add(footprint);
        }

        if (PlayerEvents.HasInteractedScp330)
        {
            PlayerEvents.OnInteractedScp330(new PlayerInteractedScp330EventArgs(ply, uses, playSound, allowPunishment, candy));
        }

        return false;
    }

    // Scp330Bag.ServerProcessPickup with a chosen candy instead of a random one (same approach as EXILED for SL 13.x).
    private static bool TryAddCandy(ReferenceHub ply, CandyKindID candy)
    {
        if (!Scp330Bag.TryGetBag(ply, out Scp330Bag bag))
        {
            if (ply.inventory.ServerAddItem(ItemType.SCP330) == null || !Scp330Bag.TryGetBag(ply, out bag))
            {
                return false;
            }

            bag.Candies = new List<CandyKindID> { candy };
            bag.ServerRefreshBag();
            return true;
        }

        bool result = bag.TryAddSpecific(candy);
        if (bag.AcquisitionAlreadyReceived)
        {
            bag.ServerRefreshBag();
        }

        return result;
    }
}

// Official: BreakableWindow.cs Damage
[HarmonyPatch(typeof(BreakableWindow), nameof(BreakableWindow.Damage))]
internal static class WindowDamagePatch
{
    private static bool Prefix(BreakableWindow __instance, float damage, ref DamageHandlerBase handler, ref bool __result)
    {
        if ((!PlayerEvents.HasDamagingWindow && !PlayerEvents.HasDamagedWindow) || handler is not AttackerDamageHandler attacker)
        {
            return true;
        }

        if (!__instance.CheckDamagePerms(attacker.Attacker.Role))
        {
            __result = false;
            return false;
        }

        if (PlayerEvents.HasDamagingWindow)
        {
            PlayerDamagingWindowEventArgs e = new(attacker.Attacker.Hub, __instance, handler);
            PlayerEvents.OnDamagingWindow(e);
            if (!e.IsAllowed)
            {
                __result = false;
                return false;
            }
        }

        __instance.LastAttacker = attacker.Attacker;
        __instance.ServerDamageWindow(damage);
        if (PlayerEvents.HasDamagedWindow)
        {
            PlayerEvents.OnDamagedWindow(new PlayerDamagedWindowEventArgs(attacker.Attacker.Hub, __instance, handler));
        }

        __result = true;
        return false;
    }
}

// Official: AlphaWarheadActivationPanel.cs ServerInteractKeycard
// Carl Mod unlocks the surface button cover through the PlayerInteract.CmdSwitchAWButton command.
[HarmonyPatch(typeof(PlayerInteract), nameof(PlayerInteract.UserCode_CmdSwitchAWButton))]
internal static class WarheadButtonUnlockPatch
{
    private static bool Prefix(PlayerInteract __instance)
    {
        if (!PlayerEvents.HasUnlockingWarheadButton && !PlayerEvents.HasUnlockedWarheadButton)
        {
            return true;
        }

        if (!__instance.CanInteract)
        {
            return false;
        }

        GameObject panelObject = GameObject.Find("OutsitePanelScript");
        if (panelObject == null || !__instance.ChckDis(panelObject.transform.position))
        {
            return false;
        }

        AlphaWarheadOutsitePanel panel = panelObject.GetComponentInParent<AlphaWarheadOutsitePanel>();
        if (panel == null || panel.keycardEntered)
        {
            return false;
        }

        ReferenceHub hub = __instance._hub;
        bool isAllowed = __instance._sr.BypassMode || (__instance._inv.CurInstance is KeycardItem keycard && keycard.Permissions.HasFlag(KeycardPermissions.AlphaWarhead));
        if (PlayerEvents.HasUnlockingWarheadButton)
        {
            PlayerUnlockingWarheadButtonEventArgs e = new(hub)
            {
                IsAllowed = isAllowed,
            };
            PlayerEvents.OnUnlockingWarheadButton(e);
            isAllowed = e.IsAllowed;
        }

        if (!isAllowed)
        {
            return false;
        }

        __instance.OnInteract();
        panel.NetworkkeycardEntered = true;
        if (hub.TryGetAssignedSpawnableTeam(out SpawnableTeamType team))
        {
            RespawnTokensManager.GrantTokens(team, 1f);
        }

        if (PlayerEvents.HasUnlockedWarheadButton)
        {
            PlayerEvents.OnUnlockedWarheadButton(new PlayerUnlockedWarheadButtonEventArgs(hub));
        }

        return false;
    }
}

// Official: AlphaWarheadNukesitePanel.cs ServerInteract (Lever)
// Carl Mod toggles the lever through the PlayerInteract.CmdUsePanel command; only the lever operation is handled here.
[HarmonyPatch(typeof(PlayerInteract), nameof(PlayerInteract.UserCode_CmdUsePanel__AlphaPanelOperations))]
internal static class WarheadLeverPatch
{
    private static bool Prefix(PlayerInteract __instance, PlayerInteract.AlphaPanelOperations n)
    {
        if (n != PlayerInteract.AlphaPanelOperations.Lever || (!PlayerEvents.HasInteractingWarheadLever && !PlayerEvents.HasInteractedWarheadLever))
        {
            return true;
        }

        if (!__instance.CanInteract)
        {
            return false;
        }

        AlphaWarheadNukesitePanel nukeside = AlphaWarheadOutsitePanel.nukeside;
        if (nukeside == null || !__instance.ChckDis(nukeside.transform.position) || !nukeside.AllowChangeLevelState())
        {
            return false;
        }

        bool enabled = !nukeside.nukenabled;
        if (PlayerEvents.HasInteractingWarheadLever)
        {
            PlayerInteractingWarheadLeverEventArgs e = new(__instance._hub, enabled);
            PlayerEvents.OnInteractingWarheadLever(e);
            if (!e.IsAllowed)
            {
                return false;
            }

            enabled = e.Enabled;
        }

        __instance.OnInteract();
        nukeside.Networknukenabled = enabled;
        __instance.RpcLeverSound();
        if (PlayerEvents.HasInteractedWarheadLever)
        {
            PlayerEvents.OnInteractedWarheadLever(new PlayerInteractedWarheadLeverEventArgs(__instance._hub, enabled));
        }

        return false;
    }
}
