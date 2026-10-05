using HarmonyLib;
using LabApi.Features.Wrappers;
using PlayerRoles;
using PlayerStatsSystem;
using System.Collections.Generic;

namespace LabApi.Events.Patches.Internal;

/// <summary>
/// Gives every role initialization a new <see cref="Player.LifeId"/> and clears the <see cref="Player.MaxHealth"/> override.
/// </summary>
/// <remarks>
/// Official SL stores <c>PlayerRoleBase.UniqueLifeIdentifier</c> on the role and resets <c>HealthStat.MaxValue</c> on class change.
/// Carl Mod has neither, so both live in LabAPI and are refreshed here.
/// </remarks>
// Official: PlayerRoles/PlayerRoleBase.cs Init (UniqueLifeIdentifier) and PlayerStatsSystem/HealthStat.cs ClassChanged
[HarmonyPatch(typeof(PlayerRoleManager), nameof(PlayerRoleManager.InitializeNewRole))]
internal static class PlayerRoleInitializedPatch
{
    private static int _lifeCounter;

    private static void Postfix(PlayerRoleManager __instance)
    {
        ReferenceHub hub = __instance.Hub;
        if (hub == null)
        {
            return;
        }

        HealthStatMaxValuePatch.Overrides.Remove(hub);
        if (Player.Dictionary.TryGetValue(hub, out Player player))
        {
            player.LifeId = ++_lifeCounter;
        }
    }
}

/// <summary>
/// Applies <see cref="Player.MaxHealth"/> overrides to <see cref="HealthStat.MaxValue"/>.
/// </summary>
/// <remarks>
/// Official SL has a settable <c>HealthStat.MaxValue</c>; Carl Mod derives it from the role, so the setter is emulated here.
/// </remarks>
// Official: PlayerStatsSystem/HealthStat.cs MaxValue
[HarmonyPatch(typeof(HealthStat), nameof(HealthStat.MaxValue), MethodType.Getter)]
internal static class HealthStatMaxValuePatch
{
    /// <summary>
    /// Gets the maximum health overrides by player.
    /// </summary>
    internal static Dictionary<ReferenceHub, float> Overrides { get; } = [];

    private static void Postfix(HealthStat __instance, ref float __result)
    {
        if (Overrides.Count != 0 && Overrides.TryGetValue(__instance.Hub, out float value))
        {
            __result = value;
        }
    }
}
