using HarmonyLib;
using PlayerStatsSystem;
using RemoteAdmin;
using System;
using System.Reflection;

namespace LabApi.Events.Patches;

/// <summary>
/// Optional access to the deathmatch module that one Carl Mod 0.0.4 server build ships: <c>CarlModExtras.dll</c>
/// (<c>CarlModExtras.DmFun</c>) plus deathmatch members in Assembly-CSharp such as <c>PlayerStats.DmCleanup</c>. The
/// official Carl Mod server distribution has no such module.
/// </summary>
/// <remarks>
/// LabAPI-Mobile does not reference CarlModExtras at compile time. Each member is looked up by reflection once and bound
/// to a delegate, so no LabAPI method needs CarlModExtras (or a deathmatch member) to be JIT-compiled, and a build without
/// the module only sees <see langword="false"/> / no-op results. Patches choose between the deathmatch and the standard
/// copy of a native body by its IL fingerprint (<see cref="NativeBody"/>), not by these delegates.
/// </remarks>
internal static class CarlModDeathmatch
{
    private const string DmFunTypeName = "CarlModExtras.DmFun, CarlModExtras";

    private static readonly Func<bool>? EnabledGetter;
    private static readonly Func<string, CommandSender, bool>? HelpChatHandler;
    private static readonly Func<string, CommandSender, bool>? WikiGrantHandler;
    private static readonly Func<CommandSender, string, bool>? DotCommandHandler;
    private static readonly Action<ReferenceHub>? LoadoutHandler;
    private static readonly Action? CleanupHandler;

    static CarlModDeathmatch()
    {
        Type? dmFun = FindDmFun();
        if (dmFun != null)
        {
            EnabledGetter = Bind<Func<bool>>(dmFun, "DmEnabledBool");
            HelpChatHandler = Bind<Func<string, CommandSender, bool>>(dmFun, "HandleHelpChat");
            WikiGrantHandler = Bind<Func<string, CommandSender, bool>>(dmFun, "HandleWikiGrant");
            DotCommandHandler = Bind<Func<CommandSender, string, bool>>(dmFun, "HandleDotCommand");
            LoadoutHandler = Bind<Action<ReferenceHub>>(dmFun, "OnLoadout");
        }

        CleanupHandler = Bind<Action>(typeof(PlayerStats), "DmCleanup");
    }

    /// <summary>
    /// Gets whether the server has the deathmatch module: <c>CarlModExtras.DmFun</c> and the deathmatch members of the
    /// game assembly.
    /// </summary>
    internal static bool IsPresent => EnabledGetter != null && CleanupHandler != null;

    /// <summary>
    /// Gets whether the deathmatch module is present and its <c>deathmatch</c> config is enabled.
    /// </summary>
    internal static bool IsEnabled => EnabledGetter != null && EnabledGetter();

    /// <summary>
    /// Gets whether the command hooks of the module (help chat, wiki group, ".s" chat) are available.
    /// </summary>
    internal static bool HasCommandHooks => HelpChatHandler != null && WikiGrantHandler != null && DotCommandHandler != null;

    /// <summary>
    /// Gets whether the loadout hook of the module is available.
    /// </summary>
    internal static bool HasLoadoutHook => LoadoutHandler != null;

    /// <summary>
    /// Runs the module's Remote Admin query hooks ("help &lt;text&gt;" chat and the "wiki" group grant).
    /// </summary>
    /// <param name="query">The query.</param>
    /// <param name="sender">The sender.</param>
    /// <returns>Whether the module handled the query.</returns>
    internal static bool HandleRemoteAdminQuery(string query, CommandSender sender) =>
        (HelpChatHandler != null && HelpChatHandler(query, sender)) || (WikiGrantHandler != null && WikiGrantHandler(query, sender));

    /// <summary>
    /// Runs the module's client console hook (".s" chat).
    /// </summary>
    /// <param name="sender">The sender.</param>
    /// <param name="query">The query.</param>
    /// <returns>Whether the module handled the query.</returns>
    internal static bool HandleDotCommand(CommandSender sender, string query) => DotCommandHandler != null && DotCommandHandler(sender, query);

    /// <summary>
    /// Runs the module's loadout hook (the timed fun-mode loadouts).
    /// </summary>
    /// <param name="target">The player that received a loadout.</param>
    internal static void OnLoadout(ReferenceHub target) => LoadoutHandler?.Invoke(target);

    /// <summary>
    /// Runs <c>PlayerStats.DmCleanup</c>, which the deathmatch build calls at the end of <c>KillPlayer</c>.
    /// </summary>
    internal static void Cleanup() => CleanupHandler?.Invoke();

    /// <summary>
    /// Describes the module state for the startup log.
    /// </summary>
    /// <returns>A short description.</returns>
    internal static string Describe()
    {
        if (IsPresent)
        {
            return "Carl Mod deathmatch module found (CarlModExtras)";
        }

        return EnabledGetter != null
            ? "CarlModExtras found, but the game assembly has no deathmatch members; treated as a build without the deathmatch module"
            : "no Carl Mod deathmatch module (CarlModExtras)";
    }

    private static Type? FindDmFun()
    {
        try
        {
            return Type.GetType(DmFunTypeName, false);
        }
        catch (Exception)
        {
            // CarlModExtras.dll is missing or cannot be loaded: a build without the deathmatch module.
            return null;
        }
    }

    private static T? Bind<T>(Type type, string name)
        where T : Delegate
    {
        try
        {
            MethodInfo? method = AccessTools.DeclaredMethod(type, name);
            return method == null || !method.IsStatic ? null : (T?)Delegate.CreateDelegate(typeof(T), method, false);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
