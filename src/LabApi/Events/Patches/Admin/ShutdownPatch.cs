using HarmonyLib;
using LabApi.Events.Handlers;

namespace LabApi.Events.Patches.Admin;

/// <summary>
/// Raises Shutdown once when the server starts quitting.
/// </summary>
/// <remarks>
/// Official SL raises it right after <see cref="global::Shutdown.OnQuit"/>; here it is raised just before.
/// </remarks>
// Official: Shutdown.cs Quit
[HarmonyPatch(typeof(global::Shutdown), nameof(global::Shutdown.Quit))]
internal static class ShutdownPatch
{
    private static void Prefix()
    {
        if (!global::Shutdown._quitting)
        {
            ServerEvents.OnShutdown();
        }
    }
}
