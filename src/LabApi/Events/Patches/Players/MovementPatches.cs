using HarmonyLib;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using PlayerRoles.FirstPersonControl;
using PlayerRoles.FirstPersonControl.NetworkMessages;

namespace LabApi.Events.Patches.Players;

// Official: PlayerRoles/FirstPersonControl/FpcJumpController.cs ProcessJump
// The Carl Mod motor has no jump controller or jump multiplier; the server-side grounded update performs the jump.
[HarmonyPatch(typeof(FpcMotor), nameof(FpcMotor.UpdateGrounded))]
internal static class PlayerJumpedPatch
{
    private static void Prefix(FpcMotor __instance, float jumpSpeed, out bool __state)
    {
        __state = PlayerEvents.HasJumped && jumpSpeed > 0f && __instance.WantsToJump;
    }

    private static void Postfix(FpcMotor __instance, float jumpSpeed, bool __state)
    {
        if (__state)
        {
            PlayerEvents.OnJumped(new PlayerJumpedEventArgs(__instance.Hub, jumpSpeed));
        }
    }
}

// Official: PlayerRoles/FirstPersonControl/NetworkMessages/FpcSyncData.cs TryApply
[HarmonyPatch(typeof(FpcSyncData), nameof(FpcSyncData.TryApply))]
internal static class PlayerMovementStateChangedPatch
{
    private static void Prefix(ReferenceHub hub, out int __state)
    {
        __state = -1;
        if (PlayerEvents.HasMovementStateChanged && hub.roleManager.CurrentRole is IFpcRole fpcRole && fpcRole.FpcModule.ModuleReady)
        {
            __state = (int)fpcRole.FpcModule.CurrentMovementState;
        }
    }

    private static void Postfix(ReferenceHub hub, bool __result, int __state)
    {
        if (__state < 0 || !__result || hub.roleManager.CurrentRole is not IFpcRole fpcRole)
        {
            return;
        }

        PlayerMovementState newState = fpcRole.FpcModule.CurrentMovementState;
        if ((int)newState != __state)
        {
            PlayerEvents.OnMovementStateChanged(new PlayerMovementStateChangedEventArgs(hub, (PlayerMovementState)__state, newState));
        }
    }
}
