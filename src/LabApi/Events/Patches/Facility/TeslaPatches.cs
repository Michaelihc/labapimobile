using HarmonyLib;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using Mirror;
using PlayerRoles;
using UnityEngine;

namespace LabApi.Events.Patches.Facility;

// Official: TeslaGateController.cs FixedUpdate
// The fork loop is reproduced with the official events only while a tesla event has subscribers.
[HarmonyPatch(typeof(TeslaGateController), nameof(TeslaGateController.FixedUpdate))]
internal static class TeslaUpdatePatch
{
    private static bool Prefix(TeslaGateController __instance)
    {
        if (!NetworkServer.active
            || (!PlayerEvents.HasIdlingTesla && !PlayerEvents.HasIdledTesla && !PlayerEvents.HasTriggeringTesla && !PlayerEvents.HasTriggeredTesla))
        {
            return true;
        }

        foreach (TeslaGate gate in __instance.TeslaGates)
        {
            if (!gate.isActiveAndEnabled)
            {
                continue;
            }

            if (gate.InactiveTime > 0f)
            {
                gate.NetworkInactiveTime = Mathf.Max(0f, gate.InactiveTime - Time.fixedDeltaTime);
                continue;
            }

            bool idle = false;
            bool trigger = false;
            ReferenceHub? idler = null;
            ReferenceHub? triggerer = null;
            foreach (ReferenceHub hub in ReferenceHub.AllHubs)
            {
                if (!hub.IsAlive())
                {
                    continue;
                }

                if (!idle && gate.PlayerInIdleRange(hub))
                {
                    idle = true;
                    if (PlayerEvents.HasIdlingTesla)
                    {
                        PlayerIdlingTeslaEventArgs e = new(hub, gate);
                        PlayerEvents.OnIdlingTesla(e);
                        idle = e.IsAllowed;
                    }

                    if (idle)
                    {
                        idler = hub;
                    }
                }

                if (!trigger && gate.PlayerInRange(hub) && !gate.InProgress)
                {
                    trigger = true;
                    if (PlayerEvents.HasTriggeringTesla)
                    {
                        PlayerTriggeringTeslaEventArgs e = new(hub, gate);
                        PlayerEvents.OnTriggeringTesla(e);
                        trigger = e.IsAllowed;
                    }

                    if (trigger)
                    {
                        triggerer = hub;
                    }
                }
            }

            if (trigger)
            {
                gate.ServerSideCode();
                if (PlayerEvents.HasTriggeredTesla)
                {
                    PlayerEvents.OnTriggeredTesla(new PlayerTriggeredTeslaEventArgs(triggerer!, gate));
                }
            }

            if (idle != gate.isIdling)
            {
                gate.ServerSideIdle(idle);
                if (idle && PlayerEvents.HasIdledTesla)
                {
                    PlayerEvents.OnIdledTesla(new PlayerIdledTeslaEventArgs(idler!, gate));
                }
            }
        }

        return false;
    }
}
