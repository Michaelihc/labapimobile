using HarmonyLib;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using Mirror;
using PlayerRoles;
using PlayerRoles.FirstPersonControl;
using PlayerRoles.FirstPersonControl.Spawnpoints;
using System;
using System.Reflection;
using UnityEngine;

namespace LabApi.Events.Patches.Players;

// Official: PlayerRoles/FirstPersonControl/Spawnpoints/RoleSpawnpointManager.cs SetPosition
// The Carl Mod server positions players from an anonymous PlayerRoleManager.OnRoleChanged handler in RoleSpawnpointManager.Init,
// so the first role a player ever receives (no previous role) does not raise these events, as it is not repositioned either.
[HarmonyPatch]
internal static class PlayerSpawnPatch
{
    private static MethodBase TargetMethod()
    {
        foreach (Type nested in typeof(RoleSpawnpointManager).GetNestedTypes(BindingFlags.NonPublic | BindingFlags.Public))
        {
            foreach (MethodInfo method in nested.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly))
            {
                ParameterInfo[] parameters = method.GetParameters();
                if (method.Name.Contains("<Init>") && parameters.Length == 3 && parameters[0].ParameterType == typeof(ReferenceHub)
                    && parameters[1].ParameterType == typeof(PlayerRoleBase) && parameters[2].ParameterType == typeof(PlayerRoleBase))
                {
                    return method;
                }
            }
        }

        throw new MissingMethodException(nameof(RoleSpawnpointManager), "Init role-changed handler");
    }

    private static bool Prefix(ReferenceHub __0, PlayerRoleBase __2)
    {
        ReferenceHub hub = __0;
        PlayerRoleBase newRole = __2;
        if (!PlayerEvents.HasSpawning && !PlayerEvents.HasSpawned)
        {
            return true;
        }

        if (!NetworkServer.active || newRole is not IFpcRole { SpawnpointHandler: not null } fpcRole)
        {
            return false;
        }

        bool useSpawnPoint = fpcRole.SpawnpointHandler.TryGetSpawnpoint(out Vector3 position, out float horizontalRot);
        if (PlayerEvents.HasSpawning)
        {
            PlayerSpawningEventArgs e = new(hub, newRole, useSpawnPoint, position, horizontalRot);
            PlayerEvents.OnSpawning(e);
            if (!e.IsAllowed)
            {
                return false;
            }

            position = e.SpawnLocation;
            horizontalRot = e.HorizontalRotation;
            useSpawnPoint = e.UseSpawnPoint;
        }

        if (newRole.ServerSpawnFlags.HasFlag(RoleSpawnFlags.UseSpawnpoint) && useSpawnPoint)
        {
            hub.transform.position = position;
            if (fpcRole.FpcModule.MouseLook != null)
            {
                fpcRole.FpcModule.MouseLook.CurrentHorizontal = horizontalRot;
            }
        }

        if (PlayerEvents.HasSpawned)
        {
            PlayerEvents.OnSpawned(new PlayerSpawnedEventArgs(hub, newRole, useSpawnPoint, position, horizontalRot));
        }

        return false;
    }
}
