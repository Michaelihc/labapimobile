using CustomPlayerEffects;
using HarmonyLib;
using InventorySystem.Items.MicroHID;
using LabApi.Events.Arguments.Scp106Events;
using LabApi.Events.Handlers;
using Logger = LabApi.Features.Console.Logger;
using MapGeneration;
using Mirror;
using PlayerRoles.FirstPersonControl;
using PlayerRoles.PlayableScps.Scp106;
using PlayerStatsSystem;
using RelativePositioning;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using UnityEngine;

namespace LabApi.Events.Patches.Scps;

// Official: PlayerRoles/PlayableScps/Scp106/Scp106Attack.cs ServerShoot
// In the fork a single hit damages and captures the target; the event gates the whole hit.
[HarmonyPatch(typeof(Scp106Attack), nameof(Scp106Attack.ServerShoot))]
internal static class Scp106TeleportingPlayerPatch
{
    private static readonly AccessTools.FieldRef<Action<ReferenceHub>> OnPlayerTeleported =
        AccessTools.StaticFieldRefAccess<Action<ReferenceHub>>(AccessTools.Field(typeof(Scp106Attack), "OnPlayerTeleported"));

    private static bool Prefix(Scp106Attack __instance)
    {
        if (!Scp106Events.HasTeleportingPlayer && !Scp106Events.HasTeleportedPlayer)
        {
            return true;
        }

        ReferenceHub target = __instance._targetHub;
        using (new FpcBacktracker(target, __instance._targetPosition, 0.35f))
        {
            Vector3 vector = __instance._targetPosition - __instance._ownerPosition;
            float sqrMagnitude = vector.sqrMagnitude;
            if (sqrMagnitude > __instance._maxRangeSqr)
            {
                __instance.SendCooldown(__instance._missCooldown);
                return false;
            }

            Vector3 forward = __instance.OwnerCam.forward;
            forward.y = 0f;
            vector.y = 0f;
            if (Physics.Linecast(__instance._ownerPosition, __instance._targetPosition, MicroHIDItem.WallMask))
            {
                __instance.SendCooldown(__instance._missCooldown);
                return false;
            }

            if (__instance._dotOverDistance.Evaluate(sqrMagnitude) > Vector3.Dot(vector.normalized, forward.normalized))
            {
                __instance.SendCooldown(__instance._missCooldown);
                return false;
            }

            if (Scp106Events.HasTeleportingPlayer)
            {
                Scp106TeleportingPlayerEvent e = new(__instance.Owner, target);
                Scp106Events.OnTeleportingPlayer(e);
                if (!e.IsAllowed)
                {
                    return false;
                }
            }

            ScpDamageHandler handler = new(__instance.Owner, __instance._damage, DeathTranslations.PocketDecay);
            if (!target.playerStats.DealDamage(handler))
            {
                return false;
            }
        }

        __instance.SendCooldown(__instance._hitCooldown);
        Scp106VigorChangePatch.SetVigor(__instance.Vigor, __instance.Vigor.VigorAmount + 0.3f);
        __instance.ReduceSinkholeCooldown();
        Hitmarker.SendHitmarker(__instance.Owner, 1f);
        OnPlayerTeleported()?.Invoke(target);
        PlayerEffectsController effects = target.playerEffectsController;
        effects.EnableEffect<Traumatized>(180f);
        effects.EnableEffect<Corroding>();

        if (Scp106Events.HasTeleportedPlayer)
        {
            Scp106Events.OnTeleportedPlayer(new Scp106TeleportedPlayerEvent(__instance.Owner, target));
        }

        return false;
    }
}

// Official: PlayerRoles/PlayableScps/Scp106/Scp106HuntersAtlasAbility.cs ServerProcessCmd
[HarmonyPatch(typeof(Scp106HuntersAtlasAbility), nameof(Scp106HuntersAtlasAbility.ServerProcessCmd))]
internal static class Scp106UsingHunterAtlasPatch
{
    private static bool Prefix(Scp106HuntersAtlasAbility __instance, NetworkReader reader)
    {
        if (!Scp106Events.HasUsingHunterAtlas)
        {
            return true;
        }

        Scp106SinkholeController sinkhole = __instance.ScpRole.Sinkhole;
        if (sinkhole.NormalizedState > 0f || !sinkhole.Cooldown.IsReady)
        {
            return false;
        }

        Vector3 position = reader.ReadRelativePosition().Position;
        __instance._syncRoom = RoomIdUtils.RoomAtPosition(position);
        Vector3 offset = new(reader.ReadShort(), 0f, reader.ReadShort());
        __instance._syncPos = position + offset / 50f;
        if (__instance._syncRoom == null)
        {
            return false;
        }

        Vector3 ownerPosition = __instance.ScpRole.FpcModule.Position;
        if (Mathf.Abs(ownerPosition.y - __instance._syncPos.y) > 400f)
        {
            return false;
        }

        float cost = (ownerPosition - __instance._syncPos).MagnitudeIgnoreY() * 0.019f;
        if (cost > __instance.Vigor.VigorAmount)
        {
            return false;
        }

        Scp106UsingHunterAtlasEventArgs e = new(__instance.Owner, __instance._syncPos);
        Scp106Events.OnUsingHunterAtlas(e);
        if (!e.IsAllowed)
        {
            return false;
        }

        __instance._syncPos = e.DestinationPosition;
        __instance._estimatedCost = cost;
        __instance.SetSubmerged(val: true);
        return false;
    }
}

// Official: PlayerRoles/PlayableScps/Scp106/Scp106HuntersAtlasAbility.cs UpdateServerside
[HarmonyPatch(typeof(Scp106HuntersAtlasAbility), nameof(Scp106HuntersAtlasAbility.UpdateServerside))]
internal static class Scp106UsedHunterAtlasPatch
{
    private static void Prefix(Scp106HuntersAtlasAbility __instance, out Vector3? __state)
    {
        __state = null;
        if (Scp106Events.HasUsedHunterAtlas && __instance._submerged && __instance.ScpRole.Sinkhole.NormalizedState >= 1f)
        {
            __state = __instance.ScpRole.FpcModule.Position;
        }
    }

    private static void Postfix(Scp106HuntersAtlasAbility __instance, Vector3? __state)
    {
        if (__state.HasValue && !__instance._submerged)
        {
            Scp106Events.OnUsedHunterAtlas(new Scp106UsedHunterAtlasEventArgs(__instance.Owner, __state.Value));
        }
    }
}

// Official: PlayerRoles/PlayableScps/Scp106/Scp106StalkAbility.cs ServerSetStalk
// In the fork stalking is the 13.x submerged stalk; its IsActive setter is the single state change point.
[HarmonyPatch(typeof(Scp106StalkAbility), nameof(Scp106StalkAbility.IsActive), MethodType.Setter)]
internal static class Scp106ChangingStalkModePatch
{
    private static bool Prefix(Scp106StalkAbility __instance, bool value, out bool __state)
    {
        __state = false;
        if ((!Scp106Events.HasChangingStalkMode && !Scp106Events.HasChangedStalkMode) || !NetworkServer.active
            || Scp106StalkResetPatch.Resetting || __instance._isActive == value)
        {
            return true;
        }

        if (Scp106Events.HasChangingStalkMode)
        {
            Scp106ChangingStalkModeEventArgs e = new(__instance.Owner, value);
            Scp106Events.OnChangingStalkMode(e);
            if (!e.IsAllowed)
            {
                return false;
            }
        }

        __state = true;
        return true;
    }

    private static void Postfix(Scp106StalkAbility __instance, bool value, bool __state)
    {
        if (__state && Scp106Events.HasChangedStalkMode)
        {
            Scp106Events.OnChangedStalkMode(new Scp106ChangedStalkModeEventArgs(__instance.Owner, value));
        }
    }
}

// Official: PlayerRoles/PlayableScps/Scp106/Scp106StalkAbility.cs ResetObject
// The official reset clears stalk without events; keep the fork's reset silent and uncancellable.
[HarmonyPatch(typeof(Scp106StalkAbility), nameof(Scp106StalkAbility.ResetObject))]
internal static class Scp106StalkResetPatch
{
    internal static bool Resetting;

    private static void Prefix() => Resetting = true;

    private static void Finalizer() => Resetting = false;
}

// Official: PlayerRoles/PlayableScps/Scp106/Scp106SinkholeController.cs ServerSetSubmerged
// The fork derives the sinkhole state from the vigor abilities every frame through the State setter.
[HarmonyPatch(typeof(Scp106SinkholeController), nameof(Scp106SinkholeController.State), MethodType.Setter)]
internal static class Scp106ChangingSubmersionStatusPatch
{
    private static bool Prefix(Scp106SinkholeController __instance, bool value, out ReferenceHub? __state)
    {
        __state = null;
        if ((!Scp106Events.HasChangingSubmersionStatus && !Scp106Events.HasChangedSubmersionStatus) || __instance._state == value
            || !NetworkServer.active || !__instance.Role.TryGetOwner(out ReferenceHub hub))
        {
            return true;
        }

        if (Scp106Events.HasChangingSubmersionStatus)
        {
            Scp106ChangingSubmersionStatusEventArgs e = new(hub, value);
            Scp106Events.OnChangingSubmersionStatus(e);
            if (!e.IsAllowed)
            {
                return false;
            }
        }

        __state = hub;
        return true;
    }

    private static void Postfix(bool value, ReferenceHub? __state)
    {
        if (__state != null && Scp106Events.HasChangedSubmersionStatus)
        {
            Scp106Events.OnChangedSubmersionStatus(new Scp106ChangedSubmersionStatusEventArgs(__state, value));
        }
    }
}

// Official: PlayerRoles/PlayableScps/Scp106/Scp106VigorAbilityBase.cs VigorAmount (setter)
// The fork's Scp106Vigor.VigorAmount setter is a one-line field store, so its server callers are redirected here.
[HarmonyPatch]
internal static class Scp106VigorChangePatch
{
    private static readonly MethodInfo Setter = AccessTools.PropertySetter(typeof(Scp106Vigor), nameof(Scp106Vigor.VigorAmount));

    private static readonly MethodInfo Replacement = AccessTools.Method(typeof(Scp106VigorChangePatch), nameof(SetVigor));

    /// <summary>
    /// Sets the vigor amount, raising <see cref="Scp106Events.ChangingVigor"/> and <see cref="Scp106Events.ChangedVigor"/>.
    /// </summary>
    internal static void SetVigor(Scp106Vigor vigor, float value)
    {
        if ((!Scp106Events.HasChangingVigor && !Scp106Events.HasChangedVigor) || !NetworkServer.active || !vigor.Role.TryGetOwner(out ReferenceHub hub))
        {
            vigor.VigorAmount = value;
            return;
        }

        float oldValue = vigor.VigorAmount;
        if (Scp106Events.HasChangingVigor)
        {
            Scp106ChangingVigorEventArgs e = new(hub, oldValue, value);
            Scp106Events.OnChangingVigor(e);
            if (!e.IsAllowed)
            {
                return;
            }

            value = e.Value;
        }

        vigor.VigorAmount = value;

        if (Scp106Events.HasChangedVigor)
        {
            Scp106Events.OnChangedVigor(new Scp106ChangedVigorEventArgs(hub, oldValue, value));
        }
    }

    private static IEnumerable<MethodBase> TargetMethods()
    {
        yield return AccessTools.Method(typeof(Scp106Attack), nameof(Scp106Attack.ServerShoot));
        yield return AccessTools.Method(typeof(Scp106HuntersAtlasAbility), nameof(Scp106HuntersAtlasAbility.UpdateServerside));
        yield return AccessTools.Method(typeof(Scp106StalkAbility), nameof(Scp106StalkAbility.UpdateServerside));
    }

    private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions, MethodBase original)
    {
        bool replaced = false;
        foreach (CodeInstruction instruction in instructions)
        {
            if (instruction.Calls(Setter))
            {
                instruction.opcode = OpCodes.Call;
                instruction.operand = Replacement;
                replaced = true;
            }

            yield return instruction;
        }

        if (!replaced)
        {
            Logger.Warn($"[PATCHES] {original.DeclaringType?.Name}.{original.Name}: Scp106Vigor.VigorAmount setter not found, vigor events will not fire there.");
        }
    }
}
