using System;
using HarmonyLib;

namespace LabApi.Loader;

public static class PluginLoader
{
    public static void Initialize()
    {
        ServerConsole.AddLog("[LabApi] bootstrap reached PluginLoader.Initialize", ConsoleColor.Green);
        Harmony harmony = new("labapi.mobile.spike");
        harmony.Patch(AccessTools.Method(typeof(ReferenceHub), "Start"), postfix: new HarmonyMethod(typeof(PluginLoader), nameof(HubStartPostfix)));
        ServerConsole.AddLog("[LabApi] Harmony patch applied", ConsoleColor.Green);
    }

    private static void HubStartPostfix(ReferenceHub __instance)
    {
        // Private member access through the publicized reference.
        ServerConsole.AddLog($"[LabApi] ReferenceHub.Start postfix: isHost={__instance.isLocalPlayer} netId={__instance.netId}", ConsoleColor.Cyan);
    }
}
