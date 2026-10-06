using HarmonyLib;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using Mirror;
using PlayerRoles;
using PlayerRoles.FirstPersonControl;
using Respawning;
using System;
using UnityEngine;
using static Escape;

namespace LabApi.Events.Patches.Players;

// Official: Escape.cs ServerHandlePlayer
// The Carl Mod escape area is a sphere around Escape.WorldPos; EscapeZone reports its bounding box. Escape rewards
// (respawn tokens) are granted for the final scenario after the event, as the Carl Mod server does.
[HarmonyPatch(typeof(Escape), nameof(Escape.ServerHandlePlayer))]
internal static class PlayerEscapePatch
{
    private static readonly AccessTools.FieldRef<Action<ReferenceHub>> OnServerPlayerEscape =
        AccessTools.StaticFieldRefAccess<Action<ReferenceHub>>(AccessTools.Field(typeof(Escape), nameof(Escape.OnServerPlayerEscape)));

    private static readonly Bounds EscapeZone = new(Escape.WorldPos, Vector3.one * (2f * Mathf.Sqrt(Escape.RadiusSqr)));

    private static bool Prefix(ReferenceHub hub)
    {
        if (!PlayerEvents.HasEscaping && !PlayerEvents.HasEscaped)
        {
            return true;
        }

        // Role instances are pooled: until the next movement update a new role reports its previous holder's position,
        // while the spawnpoint has already moved the transform. Both must be inside, so a role assigned elsewhere never
        // raises Escaping with a stale position.
        if (hub.roleManager.CurrentRole is not IFpcRole fpcRole || (fpcRole.FpcModule.Position - Escape.WorldPos).sqrMagnitude > Escape.RadiusSqr
            || (hub.transform.position - Escape.WorldPos).sqrMagnitude > Escape.RadiusSqr)
        {
            return false;
        }

        EscapeScenarioType scenario = Escape.ServerGetScenario(hub);
        RoleTypeId newRole = scenario switch
        {
            EscapeScenarioType.ClassD or EscapeScenarioType.CuffedScientist => RoleTypeId.ChaosConscript,
            EscapeScenarioType.CuffedClassD => RoleTypeId.NtfPrivate,
            EscapeScenarioType.Scientist => RoleTypeId.NtfSpecialist,
            _ => RoleTypeId.None,
        };

        RoleTypeId oldRole = hub.roleManager.CurrentRole.RoleTypeId;
        if (PlayerEvents.HasEscaping)
        {
            PlayerEscapingEventArgs e = new(hub, oldRole, newRole, scenario, EscapeZone);
            PlayerEvents.OnEscaping(e);
            if (!e.IsAllowed)
            {
                return false;
            }

            newRole = e.NewRole;
            scenario = e.EscapeScenario;
        }

        if (scenario == EscapeScenarioType.None)
        {
            return false;
        }

        switch (scenario)
        {
            case EscapeScenarioType.ClassD:
            case EscapeScenarioType.CuffedScientist:
                RespawnTokensManager.GrantTokens(SpawnableTeamType.ChaosInsurgency, 4f);
                break;
            case EscapeScenarioType.CuffedClassD:
            case EscapeScenarioType.Scientist:
                RespawnTokensManager.GrantTokens(SpawnableTeamType.NineTailedFox, 3f);
                break;
        }

        hub.connectionToClient.Send(new EscapeMessage
        {
            ScenarioId = (byte)scenario,
            EscapeTime = (ushort)Mathf.CeilToInt(hub.roleManager.CurrentRole.ActiveTime),
        });
        OnServerPlayerEscape()?.Invoke(hub);
        hub.roleManager.ServerSetRole(newRole, RoleChangeReason.Escaped);

        if (PlayerEvents.HasEscaped)
        {
            PlayerEvents.OnEscaped(new PlayerEscapedEventArgs(hub, oldRole, newRole, scenario, EscapeZone));
        }

        return false;
    }
}
