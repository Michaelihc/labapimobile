using Footprinting;
using HarmonyLib;
using InventorySystem.Items.Keycards;
using Interactables.Interobjects.DoorUtils;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Arguments.ServerEvents;
using LabApi.Events.Handlers;
using MapGeneration.Distributors;
using Mirror;
using PlayerRoles;
using static MapGeneration.Distributors.Scp079Generator;

namespace LabApi.Events.Patches.Facility;

// Official: MapGeneration/Distributors/Scp079Generator.cs ServerInteract
// Replaces the fork method only while a generator interaction event has subscribers; the fork rules are kept
// (no denied cooldown, humans or an active lever can use the switch).
[HarmonyPatch(typeof(Scp079Generator), nameof(Scp079Generator.ServerInteract))]
internal static class GeneratorInteractPatch
{
    private static bool AnySubscriber =>
        PlayerEvents.HasInteractingGenerator || PlayerEvents.HasInteractedGenerator
        || PlayerEvents.HasOpeningGenerator || PlayerEvents.HasOpenedGenerator
        || PlayerEvents.HasClosingGenerator || PlayerEvents.HasClosedGenerator
        || PlayerEvents.HasUnlockingGenerator || PlayerEvents.HasUnlockedGenerator
        || PlayerEvents.HasActivatingGenerator || PlayerEvents.HasActivatedGenerator
        || PlayerEvents.HasDeactivatingGenerator || PlayerEvents.HasDeactivatedGenerator;

    private static bool Prefix(Scp079Generator __instance, ReferenceHub ply, byte colliderId)
    {
        if (!AnySubscriber)
        {
            return true;
        }

        Scp079Generator gen = __instance;
        if ((gen._cooldownStopwatch.IsRunning && gen._cooldownStopwatch.Elapsed.TotalSeconds < gen._targetCooldown) || (colliderId != 0 && !gen.IsOpen))
        {
            return false;
        }

        gen._cooldownStopwatch.Stop();
        GeneratorColliderId collider = (GeneratorColliderId)colliderId;
        if (PlayerEvents.HasInteractingGenerator)
        {
            PlayerInteractingGeneratorEventArgs e = new(ply, gen, collider);
            PlayerEvents.OnInteractingGenerator(e);
            if (!e.IsAllowed)
            {
                gen._cooldownStopwatch.Restart();
                return false;
            }
        }

        switch (collider)
        {
            case GeneratorColliderId.Door:
                InteractDoor(gen, ply);
                break;

            case GeneratorColliderId.Switch:
                if ((ply.IsHuman() || gen.Activating) && !gen.Engaged)
                {
                    bool activating = !gen.Activating;
                    if (!RaiseLeverEvent(gen, ply, activating))
                    {
                        break;
                    }

                    gen.Activating = activating;
                    if (activating)
                    {
                        gen._leverStopwatch.Restart();
                        gen._lastActivator = new Footprint(ply);
                    }
                    else
                    {
                        gen._lastActivator = default;
                    }

                    gen._targetCooldown = gen._doorToggleCooldownTime;
                    RaiseLeverDoneEvent(gen, ply, activating);
                }

                break;

            case GeneratorColliderId.CancelButton:
                if (gen.Activating && !gen.Engaged && RaiseLeverEvent(gen, ply, false))
                {
                    gen.ServerSetFlag(GeneratorFlags.Activating, false);
                    gen._targetCooldown = gen._unlockCooldownTime;
                    gen._lastActivator = default;
                    RaiseLeverDoneEvent(gen, ply, false);
                }

                break;

            default:
                gen._targetCooldown = 1f;
                break;
        }

        gen._cooldownStopwatch.Restart();
        if (PlayerEvents.HasInteractedGenerator)
        {
            PlayerEvents.OnInteractedGenerator(new PlayerInteractedGeneratorEventArgs(ply, gen, collider));
        }

        return false;
    }

    private static void InteractDoor(Scp079Generator gen, ReferenceHub ply)
    {
        if (gen.IsUnlocked)
        {
            bool open = gen.IsOpen;
            if (open && PlayerEvents.HasClosingGenerator)
            {
                PlayerClosingGeneratorEventArgs e = new(ply, gen);
                PlayerEvents.OnClosingGenerator(e);
                if (e.PlayDeniedAnimation)
                {
                    gen.RpcDenied();
                }

                if (!e.IsAllowed)
                {
                    return;
                }
            }
            else if (!open && PlayerEvents.HasOpeningGenerator)
            {
                PlayerOpeningGeneratorEventArgs e = new(ply, gen);
                PlayerEvents.OnOpeningGenerator(e);
                if (e.PlayDeniedAnimation)
                {
                    gen.RpcDenied();
                }

                if (!e.IsAllowed)
                {
                    return;
                }
            }

            gen.ServerSetFlag(GeneratorFlags.Open, !open);
            gen._targetCooldown = gen._doorToggleCooldownTime;
            if (gen.IsOpen)
            {
                if (PlayerEvents.HasOpenedGenerator)
                {
                    PlayerEvents.OnOpenedGenerator(new PlayerOpenedGeneratorEventArgs(ply, gen));
                }
            }
            else if (PlayerEvents.HasClosedGenerator)
            {
                PlayerEvents.OnClosedGenerator(new PlayerClosedGeneratorEventArgs(ply, gen));
            }

            return;
        }

        bool canOpen = ply.serverRoles.BypassMode || (ply.inventory.CurInstance is KeycardItem keycard && keycard.Permissions.HasFlagFast(gen._requiredPermission));
        if (PlayerEvents.HasUnlockingGenerator)
        {
            PlayerUnlockingGeneratorEventArgs e = new(ply, gen, canOpen);
            PlayerEvents.OnUnlockingGenerator(e);
            if (!e.IsAllowed)
            {
                return;
            }

            canOpen = e.CanOpen;
        }

        if (!canOpen)
        {
            gen._targetCooldown = gen._unlockCooldownTime;
            gen.RpcDenied();
            return;
        }

        gen.ServerSetFlag(GeneratorFlags.Unlocked, true);
        gen.ServerGrantTicketsConditionally(new Footprint(ply), 0.5f);
        if (PlayerEvents.HasUnlockedGenerator)
        {
            PlayerEvents.OnUnlockedGenerator(new PlayerUnlockedGeneratorEventArgs(ply, gen));
        }
    }

    private static bool RaiseLeverEvent(Scp079Generator gen, ReferenceHub ply, bool activating)
    {
        if (activating)
        {
            if (!PlayerEvents.HasActivatingGenerator)
            {
                return true;
            }

            PlayerActivatingGeneratorEventArgs e = new(ply, gen);
            PlayerEvents.OnActivatingGenerator(e);
            return e.IsAllowed;
        }

        if (!PlayerEvents.HasDeactivatingGenerator)
        {
            return true;
        }

        PlayerDeactivatingGeneratorEventArgs d = new(ply, gen);
        PlayerEvents.OnDeactivatingGenerator(d);
        return d.IsAllowed;
    }

    private static void RaiseLeverDoneEvent(Scp079Generator gen, ReferenceHub ply, bool activated)
    {
        if (activated)
        {
            if (PlayerEvents.HasActivatedGenerator)
            {
                PlayerEvents.OnActivatedGenerator(new PlayerActivatedGeneratorEventArgs(ply, gen));
            }
        }
        else if (PlayerEvents.HasDeactivatedGenerator)
        {
            PlayerEvents.OnDeactivatedGenerator(new PlayerDeactivatedGeneratorEventArgs(ply, gen));
        }
    }
}

// Official: MapGeneration/Distributors/Scp079Generator.cs ServerUpdate (GeneratorActivating / GeneratorActivated)
// The prefix only does work on the frame the countdown completes; a cancelled activation returns before the fork
// engages the generator, exactly where the official method returns.
[HarmonyPatch(typeof(Scp079Generator), nameof(Scp079Generator.ServerUpdate))]
internal static class GeneratorActivationPatch
{
    private static bool Prefix(Scp079Generator __instance, out bool __state)
    {
        __state = false;
        if (!ServerEvents.HasGeneratorActivating && !ServerEvents.HasGeneratorActivated)
        {
            return true;
        }

        if (!NetworkServer.active || __instance.Engaged || __instance._currentTime < __instance._totalActivationTime || !__instance.ActivationReady)
        {
            return true;
        }

        if (ServerEvents.HasGeneratorActivating)
        {
            GeneratorActivatingEventArgs e = new(__instance);
            ServerEvents.OnGeneratorActivating(e);
            if (!e.IsAllowed)
            {
                return false;
            }
        }

        __state = true;
        return true;
    }

    private static void Postfix(Scp079Generator __instance, bool __state)
    {
        if (__state && __instance.Engaged && ServerEvents.HasGeneratorActivated)
        {
            ServerEvents.OnGeneratorActivated(new GeneratorActivatedEventArgs(__instance));
        }
    }
}
