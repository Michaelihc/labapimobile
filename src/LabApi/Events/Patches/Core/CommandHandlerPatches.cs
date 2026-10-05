using CommandSystem;
using CommandSystem.Commands.Shared;
using HarmonyLib;
using LabApi.Loader;

namespace LabApi.Events.Patches.Core;

/// <summary>
/// Restores LabAPI and plugin commands after Carl Mod's <c>refreshcommands</c> cleared and rebuilt a game command handler.
/// </summary>
// Official: none; 14.2.7 has no RefreshCommandsCommand. Fork: CommandSystem.Commands.Shared/RefreshCommandsCommand.cs Execute
[HarmonyPatch(typeof(RefreshCommandsCommand), nameof(RefreshCommandsCommand.Execute))]
internal static class RefreshCommandsPatch
{
    private static void Postfix(RefreshCommandsCommand __instance, bool __result)
    {
        if (__result && __instance._commandHandler is CommandHandler handler)
        {
            CommandLoader.ReregisterCommands(handler);
        }
    }
}

/// <summary>
/// Attaches the server console command handler to <see cref="CommandLoader"/> when <see cref="GameCore.Console"/> wakes.
/// </summary>
/// <remarks>
/// Official SL exposes the handler statically; Carl Mod keeps it on the console instance, which may be created after
/// <see cref="PluginLoader.Initialize"/>.
/// </remarks>
// Official: none; 14.2.7 GameCore/Console.cs exposes a static ConsoleCommandHandler. Fork: GameCore/Console.cs Awake
[HarmonyPatch(typeof(GameCore.Console), nameof(GameCore.Console.Awake))]
internal static class GameConsoleCommandHandlerPatch
{
    private static void Postfix(GameCore.Console __instance)
    {
        if (GameCore.Console.Singleton == __instance)
        {
            CommandLoader.AttachGameConsoleHandler(__instance.ConsoleCommandHandler);
        }
    }
}
