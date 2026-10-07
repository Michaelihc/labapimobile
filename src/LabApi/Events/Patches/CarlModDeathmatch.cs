using HarmonyLib;
using PlayerStatsSystem;
using RemoteAdmin;
using System;
using System.Reflection;
using System.Text;

namespace LabApi.Events.Patches;

/// <summary>
/// Optional access to the CarlModExtras module (<c>CarlModExtras.DmFun</c>) and the deathmatch members some Carl Mod builds
/// add to the game assembly.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item>The official 0.0.4 server distribution has no CarlModExtras.</item>
/// <item>The 0.0.4 build with the deathmatch module has CarlModExtras and deathmatch members in Assembly-CSharp
/// (<c>PlayerStats.DmCleanup</c>, <c>RespawnManager.DmDirectRespawn</c>...), and the game calls the module from many places.</item>
/// <item>Carl Mod 0.0.5 has CarlModExtras but no deathmatch members in Assembly-CSharp; the game calls only the module's Remote
/// Admin, client console and tick hooks.</item>
/// </list>
/// LabAPI-Mobile does not reference CarlModExtras at compile time. Each member is looked up by reflection once, on its own, and
/// bound to a delegate, so no LabAPI method needs CarlModExtras (or a build-specific member) to be JIT-compiled and a build
/// without a member only sees <see langword="false"/> / no-op results. Whether the game actually calls a hook is decided by the
/// native body that calls it (<see cref="NativeBody"/>), not by these delegates.
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
        ModuleType = dmFun;
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
    /// Gets <c>CarlModExtras.DmFun</c>, or <see langword="null"/> on builds without the CarlModExtras module. Only for
    /// reflection: patches resolve module methods on it by name.
    /// </summary>
    internal static Type? ModuleType { get; }

    /// <summary>
    /// Gets whether the server has the CarlModExtras module (<c>CarlModExtras.DmFun</c>).
    /// </summary>
    internal static bool HasModule => ModuleType != null;

    /// <summary>
    /// Gets whether the game assembly has the 0.0.4 deathmatch build's members (<c>PlayerStats.DmCleanup</c>).
    /// </summary>
    internal static bool HasGameMembers => CleanupHandler != null;

    /// <summary>
    /// Gets whether the CarlModExtras module is present and its <c>deathmatch</c> config is enabled. What that config changes
    /// depends on the build: see <see cref="Rounds.RoundSummaryPatch.DeathmatchBlocksRoundEnd"/>.
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
    /// Runs <c>PlayerStats.DmCleanup</c>, which the 0.0.4 deathmatch build calls at the end of <c>KillPlayer</c>.
    /// </summary>
    internal static void Cleanup() => CleanupHandler?.Invoke();

    /// <summary>
    /// Describes the server build for the startup log.
    /// </summary>
    /// <returns>A short description.</returns>
    internal static string Describe()
    {
        StringBuilder text = new("Carl Mod ");
        text.Append(GameCore.Version.VersionString);
        if (!HasModule)
        {
            text.Append(", no CarlModExtras module");
        }
        else
        {
            text.Append(", CarlModExtras module");
        }

        text.Append(HasGameMembers ? ", deathmatch members in the game assembly" : ", no deathmatch members in the game assembly");
        return text.ToString();
    }

    private static Type? FindDmFun()
    {
        try
        {
            return Type.GetType(DmFunTypeName, false);
        }
        catch (Exception)
        {
            // CarlModExtras.dll is missing or cannot be loaded: a build without the module.
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
