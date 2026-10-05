using HarmonyLib;
using LabApi.Events.Arguments.Scp0492Events;
using LabApi.Events.Handlers;
using PlayerRoles;
using PlayerRoles.PlayableScps.Scp049;
using PlayerRoles.PlayableScps.Scp049.Zombies;
using PlayerStatsSystem;

namespace LabApi.Events.Patches.Scps;

// Official: PlayerRoles/PlayableScps/Scp049/Zombies/ZombieConsumeAbility.cs ServerProcessCmd
// The fork does not override ServerProcessCmd, so the shared RagdollAbilityBase method is patched and filtered by type.
[HarmonyPatch(typeof(RagdollAbilityBase<ZombieRole>), nameof(RagdollAbilityBase<ZombieRole>.ServerProcessCmd))]
internal static class Scp0492StartedConsumingCorpsePatch
{
    private static void Prefix(RagdollAbilityBase<ZombieRole> __instance, out bool __state)
    {
        __state = Scp0492Events.HasStartedConsumingCorpse && __instance is ZombieConsumeAbility && !__instance.IsInProgress;
    }

    private static void Postfix(RagdollAbilityBase<ZombieRole> __instance, bool __state)
    {
        if (__state && __instance.IsInProgress)
        {
            Scp0492Events.OnStartedConsumingCorpse(new Scp0492StartedConsumingCorpseEventArgs(__instance.Owner, __instance.CurRagdoll));
        }
    }
}

// Official: PlayerRoles/PlayableScps/Scp049/Zombies/ZombieConsumeAbility.cs ServerValidateBegin
[HarmonyPatch(typeof(ZombieConsumeAbility), nameof(ZombieConsumeAbility.ServerValidateBegin))]
internal static class Scp0492StartingConsumingCorpsePatch
{
    private static bool Prefix(ZombieConsumeAbility __instance, BasicRagdoll ragdoll, ref byte __result)
    {
        if (!Scp0492Events.HasStartingConsumingCorpse)
        {
            return true;
        }

        // Fork validation order, then the official event with the resulting error.
        ZombieConsumeAbility.ConsumeError error = ZombieConsumeAbility.ConsumeError.None;
        if (ZombieConsumeAbility.ConsumedRagdolls.Contains(ragdoll))
        {
            error = ZombieConsumeAbility.ConsumeError.AlreadyConsumed;
        }
        else if (ragdoll == null || !ragdoll.Info.RoleType.IsHuman() || !__instance.ServerValidateAny())
        {
            error = ZombieConsumeAbility.ConsumeError.TargetNotValid;
        }
        else
        {
            HealthStat health = __instance.Owner.playerStats.GetModule<HealthStat>();
            if (health != null && health.NormalizedValue >= 1f)
            {
                error = ZombieConsumeAbility.ConsumeError.FullHealth;
            }
            else
            {
                foreach (ZombieConsumeAbility ability in ZombieConsumeAbility.AllAbilities)
                {
                    if (ability != null && ability.IsInProgress && ability.CurRagdoll == ragdoll)
                    {
                        error = ZombieConsumeAbility.ConsumeError.BeingConsumed;
                        break;
                    }
                }
            }
        }

        if (ragdoll == null)
        {
            // The event args need a ragdoll; the fork rejects a null ragdoll the same way.
            __result = (byte)error;
            return false;
        }

        Scp0492StartingConsumingCorpseEventArgs e = new(__instance.Owner, ragdoll, error);
        Scp0492Events.OnStartingConsumingCorpse(e);
        __result = (byte)e.Error;
        return false;
    }
}

// Official: PlayerRoles/PlayableScps/Scp049/Zombies/ZombieConsumeAbility.cs ServerComplete
[HarmonyPatch(typeof(ZombieConsumeAbility), nameof(ZombieConsumeAbility.ServerComplete))]
internal static class Scp0492ConsumingCorpsePatch
{
    private static bool Prefix(ZombieConsumeAbility __instance)
    {
        if (!Scp0492Events.HasConsumingCorpse && !Scp0492Events.HasConsumedCorpse)
        {
            return true;
        }

        BasicRagdoll ragdoll = __instance.CurRagdoll;
        if (ragdoll == null)
        {
            // The fork only heals for a present, unconsumed ragdoll.
            return true;
        }

        float healAmount = 100f;
        bool addToList = true;
        bool healIfConsumed = false;

        if (Scp0492Events.HasConsumingCorpse)
        {
            Scp0492ConsumingCorpseEventArgs e = new(__instance.Owner, ragdoll, healAmount);
            Scp0492Events.OnConsumingCorpse(e);
            if (!e.IsAllowed)
            {
                return false;
            }

            healAmount = e.HealAmount;
            addToList = e.AddToConsumedRagdollList;
            healIfConsumed = e.HealIfAlreadyConsumed;
        }

        bool alreadyConsumed = ZombieConsumeAbility.ConsumedRagdolls.Contains(ragdoll);
        if (addToList)
        {
            ZombieConsumeAbility.ConsumedRagdolls.Add(ragdoll);
        }

        if (alreadyConsumed && !healIfConsumed)
        {
            return false;
        }

        __instance.Owner.playerStats.GetModule<HealthStat>()?.ServerHeal(healAmount);

        if (Scp0492Events.HasConsumedCorpse)
        {
            Scp0492Events.OnConsumedCorpse(new Scp0492ConsumedCorpseEventArgs(__instance.Owner, ragdoll));
        }

        return false;
    }
}
