using CustomPlayerEffects;
using HarmonyLib;
using InventorySystem.Items.MicroHID;
using LabApi.Events.Arguments.Scp106Events;
using LabApi.Events.Handlers;
using Logger = LabApi.Features.Console.Logger;
using MapGeneration;
using Mirror;
using PlayerRoles;
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
// In the fork a single hit damages and captures the target; the event gates the whole hit. Carl Mod 0.0.5 checks the
// backtracked live positions instead of the client-claimed ones, sends a miss cooldown when the damage is refused, and sends
// the hit cooldown after the sinkhole cooldown change (its cooldown message now carries both). Applied only when the native
// body is one of the known Carl Mod bodies.
[HarmonyPatch(typeof(Scp106Attack), nameof(Scp106Attack.ServerShoot))]
internal static class Scp106TeleportingPlayerPatch
{
    // Scp106Attack.ServerShoot of both Carl Mod 0.0.4 builds, and of 0.0.5.
    private const string CarlMod004Body = "6025b65fae87c1e0";
    private const string Version005Body = "65544d2b24f96ab4";

    private static readonly AccessTools.FieldRef<Action<ReferenceHub>> OnPlayerTeleported =
        AccessTools.StaticFieldRefAccess<Action<ReferenceHub>>(AccessTools.Field(typeof(Scp106Attack), "OnPlayerTeleported"));

    private static readonly MethodInfo? Target = AccessTools.DeclaredMethod(typeof(Scp106Attack), nameof(Scp106Attack.ServerShoot));

    private static readonly BodyVariant Variant = NativeBody.Identify(Target, CarlMod004Body, Version005Body, out Fingerprint);

    private static readonly string? Fingerprint;

    private static bool Prepare()
    {
        if (Variant != BodyVariant.Unknown)
        {
            return true;
        }

        PatchManager.Skip(typeof(Scp106TeleportingPlayerPatch), NativeBody.UnknownBody(Target, Fingerprint, "Scp106 TeleportingPlayer / TeleportedPlayer are not raised."));
        return false;
    }

    private static bool Prefix(Scp106Attack __instance)
    {
        if (!Scp106Events.HasTeleportingPlayer && !Scp106Events.HasTeleportedPlayer)
        {
            return true;
        }

        if (Variant == BodyVariant.Version005)
        {
            ShootVersion005(__instance);
            return false;
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

    /// <summary>
    /// The Carl Mod 0.0.5 body with the events.
    /// </summary>
    private static void ShootVersion005(Scp106Attack attack)
    {
        ReferenceHub target = attack._targetHub;
        using (new FpcBacktracker(target, attack._targetPosition, 0.35f))
        {
            // ServerProcessCmd only shoots at a HumanRole target.
            Vector3 targetPosition = ((HumanRole)target.roleManager.CurrentRole).FpcModule.Position;
            Vector3 ownerPosition = attack.ScpRole.FpcModule.Position;
            Vector3 vector = targetPosition - ownerPosition;
            float sqrMagnitude = vector.sqrMagnitude;
            if (sqrMagnitude > attack._maxRangeSqr)
            {
                attack.SendCooldown(attack._missCooldown);
                return;
            }

            Vector3 forward = attack.OwnerCam.forward;
            forward.y = 0f;
            vector.y = 0f;
            if (Physics.Linecast(ownerPosition, targetPosition, MicroHIDItem.WallMask))
            {
                attack.SendCooldown(attack._missCooldown);
                return;
            }

            if (attack._dotOverDistance.Evaluate(sqrMagnitude) > Vector3.Dot(vector.normalized, forward.normalized))
            {
                attack.SendCooldown(attack._missCooldown);
                return;
            }

            if (Scp106Events.HasTeleportingPlayer)
            {
                Scp106TeleportingPlayerEvent e = new(attack.Owner, target);
                Scp106Events.OnTeleportingPlayer(e);
                if (!e.IsAllowed)
                {
                    return;
                }
            }

            ScpDamageHandler handler = new(attack.Owner, attack._damage, DeathTranslations.PocketDecay);
            if (!target.playerStats.DealDamage(handler))
            {
                attack.SendCooldown(attack._missCooldown);
                return;
            }
        }

        Scp106VigorChangePatch.SetVigor(attack.Vigor, attack.Vigor.VigorAmount + 0.3f);
        attack.ReduceSinkholeCooldown();
        attack.SendCooldown(attack._hitCooldown);
        Hitmarker.SendHitmarker(attack.Owner, 1f);
        OnPlayerTeleported()?.Invoke(target);
        PlayerEffectsController effects = target.playerEffectsController;
        effects.EnableEffect<Traumatized>(180f);
        effects.EnableEffect<Corroding>();

        if (Scp106Events.HasTeleportedPlayer)
        {
            Scp106Events.OnTeleportedPlayer(new Scp106TeleportedPlayerEvent(attack.Owner, target));
        }
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
// Also raises ChangingSubmersionStatus for the emerge that ends a Hunter's Atlas use. The fork teleports and emerges in the same
// step, so a cancelled emerge keeps SCP-106 submerged at its origin and is raised again next frame; the teleport happens once
// the emerge is allowed.
[HarmonyPatch(typeof(Scp106HuntersAtlasAbility), nameof(Scp106HuntersAtlasAbility.UpdateServerside))]
internal static class Scp106UsedHunterAtlasPatch
{
    private static bool Prefix(Scp106HuntersAtlasAbility __instance, out Vector3? __state)
    {
        __state = null;
        if ((!Scp106Events.HasUsedHunterAtlas && !Scp106Events.HasChangingSubmersionStatus)
            || !__instance._submerged || __instance.ScpRole.Sinkhole.NormalizedState < 1f)
        {
            return true;
        }

        if (!Scp106SubmersionEvents.TryChange(__instance.ScpRole.Sinkhole, __instance, false))
        {
            return false;
        }

        Scp106SubmersionEvents.Approved = __instance;
        if (Scp106Events.HasUsedHunterAtlas)
        {
            __state = __instance.ScpRole.FpcModule.Position;
        }

        return true;
    }

    private static void Postfix(Scp106HuntersAtlasAbility __instance, Vector3? __state)
    {
        if (__state.HasValue && !__instance._submerged)
        {
            Scp106Events.OnUsedHunterAtlas(new Scp106UsedHunterAtlasEventArgs(__instance.Owner, __state.Value));
        }
    }

    private static void Finalizer() => Scp106SubmersionEvents.Approved = null;
}

// Official: PlayerRoles/PlayableScps/Scp106/Scp106SinkholeController.cs ServerSetSubmerged
// Hunter's Atlas submerging (ServerProcessCmd) and emerging (UpdateServerside). Cancelling refuses the ability's state change,
// which clients follow, so the sinkhole stays in step with them.
[HarmonyPatch(typeof(Scp106HuntersAtlasAbility), nameof(Scp106HuntersAtlasAbility.SetSubmerged))]
internal static class Scp106AtlasSubmersionPatch
{
    private static bool Prefix(Scp106HuntersAtlasAbility __instance, bool val)
    {
        if (!Scp106Events.HasChangingSubmersionStatus || !NetworkServer.active || __instance._submerged == val
            || ReferenceEquals(Scp106SubmersionEvents.Approved, __instance))
        {
            return true;
        }

        return Scp106SubmersionEvents.TryChange(__instance.ScpRole.Sinkhole, __instance, val);
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
        if ((!Scp106Events.HasChangingStalkMode && !Scp106Events.HasChangedStalkMode && !Scp106Events.HasChangingSubmersionStatus)
            || !NetworkServer.active || Scp106StalkResetPatch.Resetting || __instance._isActive == value)
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

        // In the fork the stalk is the submersion: a refused submersion change refuses the stalk change.
        if (!Scp106SubmersionEvents.TryChange(__instance._sinkhole, __instance, value))
        {
            return false;
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
// The fork derives the sinkhole state from the vigor abilities every frame (on the server and on every client) through this
// setter, so it only reports the change; ChangingSubmersionStatus is raised where an ability decides to submerge or emerge.
[HarmonyPatch(typeof(Scp106SinkholeController), nameof(Scp106SinkholeController.State), MethodType.Setter)]
internal static class Scp106ChangedSubmersionStatusPatch
{
    private static void Prefix(Scp106SinkholeController __instance, bool value, out ReferenceHub? __state)
    {
        __state = null;
        if (Scp106Events.HasChangedSubmersionStatus && __instance._state != value && NetworkServer.active
            && __instance.Role.TryGetOwner(out ReferenceHub hub))
        {
            __state = hub;
        }
    }

    private static void Postfix(bool value, ReferenceHub? __state)
    {
        if (__state != null)
        {
            Scp106Events.OnChangedSubmersionStatus(new Scp106ChangedSubmersionStatusEventArgs(__state, value));
        }
    }
}

/// <summary>
/// Raises <see cref="Scp106Events.ChangingSubmersionStatus"/> where a vigor ability (stalk, Hunter's Atlas) changes its submerged
/// state on the server, when that change flips the sinkhole state the fork derives from all vigor abilities.
/// </summary>
internal static class Scp106SubmersionEvents
{
    /// <summary>
    /// The Hunter's Atlas whose emerge was already approved by <see cref="Scp106UsedHunterAtlasPatch"/> in this call.
    /// </summary>
    internal static Scp106HuntersAtlasAbility? Approved;

    /// <summary>
    /// Raises the event when <paramref name="ability"/> changing to <paramref name="submerged"/> flips the sinkhole state.
    /// </summary>
    /// <param name="sinkhole">The owner's sinkhole controller.</param>
    /// <param name="ability">The vigor ability that changes its submerged state.</param>
    /// <param name="submerged">The ability's new submerged state.</param>
    /// <returns>Whether the change may proceed.</returns>
    internal static bool TryChange(Scp106SinkholeController sinkhole, Scp106VigorAbilityBase ability, bool submerged)
    {
        if (!Scp106Events.HasChangingSubmersionStatus || !NetworkServer.active || !WouldFlip(sinkhole, ability, submerged)
            || !sinkhole.Role.TryGetOwner(out ReferenceHub hub))
        {
            return true;
        }

        Scp106ChangingSubmersionStatusEventArgs e = new(hub, submerged);
        Scp106Events.OnChangingSubmersionStatus(e);
        return e.IsAllowed;
    }

    private static bool WouldFlip(Scp106SinkholeController sinkhole, Scp106VigorAbilityBase ability, bool submerged)
    {
        bool current = false;
        bool next = false;
        Scp106VigorAbilityBase[] abilities = sinkhole._vigorAbilities;
        for (int i = 0; i < sinkhole._vigorAbilitiesCount; i++)
        {
            Scp106VigorAbilityBase other = abilities[i];
            bool otherSubmerged = other.IsSubmerged;
            current |= otherSubmerged;
            next |= other == ability ? submerged : otherSubmerged;
        }

        return current != next;
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
