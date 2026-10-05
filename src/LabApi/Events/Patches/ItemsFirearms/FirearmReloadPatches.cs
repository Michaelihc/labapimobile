using HarmonyLib;
using InventorySystem.Items.Firearms;
using InventorySystem.Items.Firearms.Modules;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using Mirror;
using System.Collections.Generic;
using System.Reflection;

namespace LabApi.Events.Patches.ItemsFirearms;

/// <summary>
/// Records server-approved reloads so the stop can raise ReloadedWeapon.
/// </summary>
// Official: InventorySystem/Items/Firearms/Modules/AnimatorReloaderModuleBase.cs ServerProcessCmd (IsReloading set on approval)
[HarmonyPatch]
internal static class FirearmReloadStartedPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(AutomaticAmmoManager), nameof(AutomaticAmmoManager.ServerTryReload));
        yield return AccessTools.Method(typeof(ClipLoadedInternalMagAmmoManager), nameof(ClipLoadedInternalMagAmmoManager.ServerTryReload));
        yield return AccessTools.Method(typeof(TubularMagazineAmmoManager), nameof(TubularMagazineAmmoManager.ServerTryReload));
    }

    private static void Postfix(IAmmoManagerModule __instance, bool __result)
    {
        if (__result)
        {
            FirearmReloadTracker.Begin(FirearmReloadTracker.GetFirearm(__instance)!, FirearmReloadTracker.Reloading);
        }
    }
}

/// <summary>
/// Records server-approved unloads so the stop can raise UnloadedWeapon.
/// </summary>
// Official: InventorySystem/Items/Firearms/Modules/AnimatorReloaderModuleBase.cs ServerProcessCmd (IsUnloading set on approval)
[HarmonyPatch]
internal static class FirearmUnloadStartedPatch
{
    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(AutomaticAmmoManager), nameof(AutomaticAmmoManager.ServerTryUnload));
        yield return AccessTools.Method(typeof(ClipLoadedInternalMagAmmoManager), nameof(ClipLoadedInternalMagAmmoManager.ServerTryUnload));
        yield return AccessTools.Method(typeof(TubularMagazineAmmoManager), nameof(TubularMagazineAmmoManager.ServerTryUnload));
    }

    private static void Postfix(IAmmoManagerModule __instance, bool __result)
    {
        if (__result)
        {
            FirearmReloadTracker.Begin(FirearmReloadTracker.GetFirearm(__instance)!, FirearmReloadTracker.Unloading);
        }
    }
}

/// <summary>
/// Raises ReloadedWeapon / UnloadedWeapon when the ammo manager returns to standby (the fork's ReloadStop point).
/// </summary>
// Official: InventorySystem/Items/Firearms/Modules/AnimatorReloaderModuleBase.cs StopReloadingAndUnloading
[HarmonyPatch(typeof(Firearm), nameof(Firearm.EquipUpdate))]
internal static class FirearmReloadStoppedPatch
{
    private static void Postfix(Firearm __instance)
    {
        if (FirearmReloadTracker.States.Count == 0 || !NetworkServer.active)
        {
            return;
        }

        if (!FirearmReloadTracker.States.TryGetValue(__instance, out byte state))
        {
            return;
        }

        IAmmoManagerModule module = __instance.AmmoManagerModule;
        if (module != null && !module.Standby)
        {
            return;
        }

        FirearmReloadTracker.States.Remove(__instance);
        RaiseStopped(__instance, state);
    }

    /// <summary>
    /// Raises the stop event for a finished or interrupted reload / unload.
    /// </summary>
    /// <param name="firearm">The firearm.</param>
    /// <param name="state">The tracked action.</param>
    internal static void RaiseStopped(Firearm firearm, byte state)
    {
        if (state == FirearmReloadTracker.Reloading)
        {
            if (PlayerEvents.HasReloadedWeapon)
            {
                PlayerEvents.OnReloadedWeapon(new PlayerReloadedWeaponEventArgs(firearm.Owner, firearm));
            }
        }
        else if (PlayerEvents.HasUnloadedWeapon)
        {
            PlayerEvents.OnUnloadedWeapon(new PlayerUnloadedWeaponEventArgs(firearm.Owner, firearm));
        }
    }
}

/// <summary>
/// Holstering interrupts the reload; official stops it and raises the stop event.
/// </summary>
// Official: InventorySystem/Items/Firearms/Modules/AnimatorReloaderModuleBase.cs OnHolstered
[HarmonyPatch(typeof(Firearm), nameof(Firearm.OnHolstered))]
internal static class FirearmReloadHolsteredPatch
{
    private static void Prefix(Firearm __instance)
    {
        if (FirearmReloadTracker.States.Count == 0 || !FirearmReloadTracker.States.TryGetValue(__instance, out byte state))
        {
            return;
        }

        FirearmReloadTracker.States.Remove(__instance);
        if (NetworkServer.active)
        {
            FirearmReloadStoppedPatch.RaiseStopped(__instance, state);
        }
    }
}

/// <summary>
/// Drops tracking when the firearm leaves the inventory.
/// </summary>
// Official: InventorySystem/Items/Firearms/Modules/AnimatorReloaderModuleBase.cs OnHolstered (state reset on removal)
[HarmonyPatch(typeof(Firearm), nameof(Firearm.OnRemoved))]
internal static class FirearmReloadRemovedPatch
{
    private static void Prefix(Firearm __instance) => FirearmReloadTracker.Forget(__instance);
}
