using HarmonyLib;
using LabApi.Events.Arguments.Scp939Events;
using LabApi.Events.Handlers;
using Mirror;
using PlayerRoles;
using PlayerRoles.FirstPersonControl;
using PlayerRoles.PlayableScps;
using PlayerRoles.PlayableScps.Scp939;
using PlayerRoles.PlayableScps.Scp939.Mimicry;
using PlayerStatsSystem;
using RelativePositioning;
using UnityEngine;
using Utils.Networking;

namespace LabApi.Events.Patches.Scps;

// Official: PlayerRoles/PlayableScps/Scp939/Mimicry/EnvironmentalMimicry.cs ServerProcessCmd
// The fork selects a sound by (category, option); SelectedSequence is the category-major flat index of that pair.
[HarmonyPatch(typeof(EnvironmentalMimicry), nameof(EnvironmentalMimicry.ServerProcessCmd))]
internal static class Scp939MimickingEnvironmentPatch
{
    private static bool Prefix(EnvironmentalMimicry __instance, NetworkReader reader)
    {
        if (!Scp939Events.HasMimickingEnvironment && !Scp939Events.HasMimickedEnvironment)
        {
            return true;
        }

        if (!__instance.Cooldown.IsReady)
        {
            return false;
        }

        __instance._syncCat = reader.ReadByte();
        __instance._syncSound = reader.ReadByte();
        float cooldown = __instance._activationCooldown;

        if (Scp939Events.HasMimickingEnvironment)
        {
            Scp939MimickingEnvironmentEventArgs e = new(__instance.Owner, ToFlatIndex(__instance, __instance._syncCat, __instance._syncSound), cooldown);
            Scp939Events.OnMimickingEnvironment(e);
            if (!e.IsAllowed)
            {
                return false;
            }

            FromFlatIndex(__instance, e.SelectedSequence, ref __instance._syncCat, ref __instance._syncSound);
            cooldown = e.CooldownTime;
        }

        __instance.Cooldown.Trigger(cooldown);
        __instance.ServerSendRpc(toAll: true);

        if (Scp939Events.HasMimickedEnvironment)
        {
            Scp939Events.OnMimickedEnvironment(new Scp939MimickedEnvironmentEventArgs(__instance.Owner, ToFlatIndex(__instance, __instance._syncCat, __instance._syncSound)));
        }

        return false;
    }

    private static byte ToFlatIndex(EnvironmentalMimicry mimicry, byte category, byte sound)
    {
        EnvironmentalMimicry.EnvMimicryCategory[] categories = mimicry.Categories;
        if (categories == null || categories.Length == 0)
        {
            return sound;
        }

        int cat = category % categories.Length;
        int index = 0;
        for (int i = 0; i < cat; i++)
        {
            index += categories[i].Options.Length;
        }

        int options = categories[cat].Options.Length;
        return (byte)(index + (options == 0 ? 0 : sound % options));
    }

    private static void FromFlatIndex(EnvironmentalMimicry mimicry, byte flat, ref byte category, ref byte sound)
    {
        EnvironmentalMimicry.EnvMimicryCategory[] categories = mimicry.Categories;
        if (categories == null)
        {
            return;
        }

        int remaining = flat;
        for (int i = 0; i < categories.Length; i++)
        {
            int options = categories[i].Options.Length;
            if (remaining < options)
            {
                category = (byte)i;
                sound = (byte)remaining;
                return;
            }

            remaining -= options;
        }
    }
}

// Official: PlayerRoles/PlayableScps/Scp939/Scp939AmnesticCloudAbility.cs OnStateEnabled
[HarmonyPatch(typeof(Scp939AmnesticCloudAbility), nameof(Scp939AmnesticCloudAbility.OnStateEnabled))]
internal static class Scp939CreatingAmnesticCloudPatch
{
    private static bool Prefix(Scp939AmnesticCloudAbility __instance)
    {
        if ((!Scp939Events.HasCreatingAmnesticCloud && !Scp939Events.HasCreatedAmnesticCloud) || !NetworkServer.active)
        {
            return true;
        }

        if (Scp939Events.HasCreatingAmnesticCloud)
        {
            Scp939CreatingAmnesticCloudEventArgs e = new(__instance.Owner);
            Scp939Events.OnCreatingAmnesticCloud(e);
            if (!e.IsAllowed)
            {
                return false;
            }
        }

        __instance._beginHeldSw.Restart();
        __instance.HudIndicatorMax.Clear();
        __instance.HudIndicatorMin.Trigger(__instance._instancePrefab.MinMaxTime.y);
        Scp939AmnesticCloudInstance cloud = Object.Instantiate(__instance._instancePrefab);
        cloud.ServerSetup(__instance.Owner);

        if (Scp939Events.HasCreatedAmnesticCloud)
        {
            Scp939Events.OnCreatedAmnesticCloud(new Scp939CreatedAmnesticCloudEventArgs(__instance.Owner, cloud));
        }

        return false;
    }
}

// Official: PlayerRoles/PlayableScps/Scp939/Scp939ClawAbility.cs DamagePlayer
// The fork claw damages every targeted hitbox in the shared ScpAttackAbilityBase.ServerPerformAttack; one event per player.
[HarmonyPatch(typeof(ScpAttackAbilityBase<Scp939Role>), nameof(ScpAttackAbilityBase<Scp939Role>.ServerPerformAttack))]
internal static class Scp939ClawAttackingPatch
{
    private static bool Prefix(ScpAttackAbilityBase<Scp939Role> __instance)
    {
        if ((!Scp939Events.HasAttacking && !Scp939Events.HasAttacked) || __instance is not Scp939ClawAbility)
        {
            return true;
        }

        Transform camera = __instance.PlyCam;
        int count = Physics.OverlapSphereNonAlloc(__instance.OverlapSphereOrigin, __instance._detectionRadius, ScpAttackAbilityBase<Scp939Role>.DetectionsNonAlloc, ScpAttackAbilityBase<Scp939Role>.DetectionMask);
        __instance._syncAttack = AttackResult.None;
        for (int i = 0; i < count; i++)
        {
            if (!ScpAttackAbilityBase<Scp939Role>.DetectionsNonAlloc[i].TryGetComponent(out IDestructible destructible)
                || Physics.Linecast(camera.position, destructible.CenterOfMass, ScpAttackAbilityBase<Scp939Role>.BlockerMask))
            {
                continue;
            }

            if (destructible is not HitboxIdentity hitbox)
            {
                if (!destructible.Damage(__instance.DamageAmount, __instance.DamageHandler, destructible.CenterOfMass))
                {
                    continue;
                }

                __instance.OnDestructibleDamaged(destructible);
                __instance._syncAttack |= AttackResult.AttackedObject;
                continue;
            }

            if (!ScpAttackAbilityBase<Scp939Role>.TargettedPlayers.Remove(hitbox.TargetHub))
            {
                continue;
            }

            ReferenceHub target = hitbox.TargetHub;
            float damage = __instance.DamageAmount;
            if (Scp939Events.HasAttacking)
            {
                Scp939AttackingEventArgs e = new(__instance.Owner, target, damage);
                Scp939Events.OnAttacking(e);
                if (!e.IsAllowed)
                {
                    continue;
                }

                target = e.Target.ReferenceHub;
                damage = e.Damage;
            }

            Scp939DamageHandler handler = new(__instance.ScpRole, Scp939DamageType.Claw)
            {
                Damage = damage,
            };

            bool dealt = target == hitbox.TargetHub
                ? hitbox.Damage(damage, handler, hitbox.CenterOfMass)
                : target.playerStats.DealDamage(handler);
            if (!dealt)
            {
                continue;
            }

            __instance.OnDestructibleDamaged(destructible);
            __instance._syncAttack |= AttackResult.AttackedObject | AttackResult.AttackedHuman;
            if (!(target.playerStats.GetModule<HealthStat>().CurValue > 0f))
            {
                __instance._syncAttack |= AttackResult.KilledHuman;
            }

            if (Scp939Events.HasAttacked)
            {
                Scp939Events.OnAttacked(new Scp939AttackedEventArgs(__instance.Owner, target, damage));
            }
        }

        __instance.ServerSendRpc(toAll: true);
        return false;
    }
}

// Official: PlayerRoles/PlayableScps/Scp939/Scp939LungeAbility.cs ServerProcessCmd
[HarmonyPatch(typeof(Scp939LungeAbility), nameof(Scp939LungeAbility.ServerProcessCmd))]
internal static class Scp939LungeAttackingPatch
{
    private static bool Prefix(Scp939LungeAbility __instance, NetworkReader reader)
    {
        if (!Scp939Events.HasAttacking && !Scp939Events.HasAttacked)
        {
            return true;
        }

        Vector3 position = reader.ReadRelativePosition().Position;
        ReferenceHub target = reader.ReadReferenceHub();
        RelativePosition targetClaim = reader.ReadRelativePosition();
        if (__instance.State != Scp939LungeState.Triggered)
        {
            if (!__instance.IsReady)
            {
                return false;
            }

            __instance.TriggerLunge();
        }

        if (target == null || target.roleManager.CurrentRole is not HumanRole { FpcModule: var targetModule })
        {
            return false;
        }

        using (new FpcBacktracker(target, targetClaim.Position))
        {
            using (new FpcBacktracker(__instance.Owner, targetModule.Position, Quaternion.identity))
            {
                Vector3 v = targetModule.Position - __instance.ScpRole.FpcModule.Position;
                if (v.SqrMagnitudeIgnoreY() > __instance._overallTolerance * __instance._overallTolerance || v.y > __instance._overallTolerance || v.y < -__instance._bottomTolerance)
                {
                    return false;
                }
            }
        }

        using (new FpcBacktracker(__instance.Owner, position, Quaternion.identity))
        {
            position = __instance.ScpRole.FpcModule.Position;
        }

        Transform targetTransform = target.transform;
        Vector3 targetPosition = targetModule.Position;
        Quaternion targetRotation = targetTransform.rotation;
        Vector3 landing = new(position.x, targetPosition.y, position.z);
        targetTransform.forward = -__instance.Owner.transform.forward;
        targetModule.Position = landing;

        float damage = Scp939DamageHandler.LungeTargetDamage;
        if (Scp939Events.HasAttacking)
        {
            Scp939AttackingEventArgs e = new(__instance.Owner, target, damage);
            Scp939Events.OnAttacking(e);
            if (!e.IsAllowed)
            {
                targetModule.Position = targetPosition;
                targetTransform.rotation = targetRotation;
                return false;
            }

            target = e.Target.ReferenceHub;
            damage = e.Damage;
        }

        bool hit = target.playerStats.DealDamage(new Scp939DamageHandler(__instance.ScpRole, Scp939DamageType.LungeTarget) { Damage = damage });
        float hitmarker = hit ? 1f : 0f;
        if (hit && Scp939Events.HasAttacked)
        {
            Scp939Events.OnAttacked(new Scp939AttackedEventArgs(__instance.Owner, target, damage));
        }

        if (!hit || target.IsAlive())
        {
            targetModule.Position = targetPosition;
            targetTransform.rotation = targetRotation;
        }

        foreach (ReferenceHub hub in ReferenceHub.AllHubs)
        {
            if (hub == target || hub.roleManager.CurrentRole is not HumanRole human || (human.FpcModule.Position - landing).sqrMagnitude > __instance._secondaryRangeSqr)
            {
                continue;
            }

            float secondaryDamage = Scp939DamageHandler.LungeSecondaryDamage;
            if (Scp939Events.HasAttacking)
            {
                Scp939AttackingEventArgs e = new(__instance.Owner, hub, secondaryDamage);
                Scp939Events.OnAttacking(e);
                if (!e.IsAllowed)
                {
                    continue;
                }

                secondaryDamage = e.Damage;
            }

            if (hub.playerStats.DealDamage(new Scp939DamageHandler(__instance.ScpRole, Scp939DamageType.LungeSecondary) { Damage = secondaryDamage }))
            {
                hit = true;
                hitmarker = Mathf.Max(hitmarker, 0.6f);
                if (Scp939Events.HasAttacked)
                {
                    Scp939Events.OnAttacked(new Scp939AttackedEventArgs(__instance.Owner, hub, secondaryDamage));
                }
            }
        }

        if (hit)
        {
            Hitmarker.SendHitmarker(__instance.Owner, hitmarker);
        }

        __instance.State = Scp939LungeState.LandHit;
        return false;
    }
}

// Official: PlayerRoles/PlayableScps/Scp939/Scp939LungeAbility.cs State (setter)
[HarmonyPatch(typeof(Scp939LungeAbility), nameof(Scp939LungeAbility.State), MethodType.Setter)]
internal static class Scp939LungingPatch
{
    private static bool Prefix(Scp939LungeAbility __instance, ref Scp939LungeState value, out bool __state)
    {
        __state = false;
        if ((!Scp939Events.HasLunging && !Scp939Events.HasLunged) || !NetworkServer.active || Scp939LungeResetPatch.Resetting
            || __instance._state == value)
        {
            return true;
        }

        if (Scp939Events.HasLunging)
        {
            Scp939LungingEventArgs e = new(__instance.Owner, value);
            Scp939Events.OnLunging(e);
            if (!e.IsAllowed || e.LungeState == __instance._state)
            {
                return false;
            }

            value = e.LungeState;
        }

        __state = true;
        return true;
    }

    private static void Postfix(Scp939LungeAbility __instance, Scp939LungeState value, bool __state)
    {
        if (__state && Scp939Events.HasLunged)
        {
            Scp939Events.OnLunged(new Scp939LungedEventArgs(__instance.Owner, value));
        }
    }
}

// Official: PlayerRoles/PlayableScps/Scp939/Scp939LungeAbility.cs ResetObject
// Pool resets must not be cancellable by Lunging handlers.
[HarmonyPatch(typeof(Scp939LungeAbility), nameof(Scp939LungeAbility.ResetObject))]
internal static class Scp939LungeResetPatch
{
    internal static bool Resetting;

    private static void Prefix() => Resetting = true;

    private static void Finalizer() => Resetting = false;
}

// Official: PlayerRoles/PlayableScps/Scp939/Scp939FocusAbility.cs TargetState (setter)
[HarmonyPatch(typeof(Scp939FocusAbility), nameof(Scp939FocusAbility.TargetState), MethodType.Setter)]
internal static class Scp939FocusedPatch
{
    private static void Prefix(Scp939FocusAbility __instance, bool value, out bool __state)
    {
        __state = Scp939Events.HasFocused && __instance._targetState != value;
    }

    private static void Postfix(Scp939FocusAbility __instance, bool value, bool __state)
    {
        if (__state)
        {
            Scp939Events.OnFocused(new Scp939FocusedEventArgs(__instance.Owner, value));
        }
    }
}
