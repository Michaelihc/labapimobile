using GameCore;
using HarmonyLib;
using InventorySystem;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using PlayerRoles;
using PlayerRoles.FirstPersonControl;
using PlayerRoles.Ragdolls;
using PlayerRoles.Spectating;
using PlayerStatsSystem;
using System;
using System.Reflection;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;

namespace LabApi.Events.Patches.Players;

/// <summary>
/// Decides how the damage events are raised on this server build.
/// </summary>
/// <remarks>
/// <list type="bullet">
/// <item><c>DealDamage</c> is the known Carl Mod body (the same in both 0.0.4 builds and 0.0.5): <see cref="PlayerDamagePatch"/>
/// replaces it with that body plus the events, at the official points.</item>
/// <item><c>KillPlayer</c> is clean (the official 0.0.4 distribution and 0.0.5): the replacement calls the native method, and
/// <c>DealDamage</c> runs untouched while no damage event has subscribers.</item>
/// <item><c>KillPlayer</c> cannot be read (the 0.0.4 deathmatch build: its <c>brfalse.s</c> at IL_00a9 jumps into the middle of
/// the call at IL_002d when the player is not a spectator after <c>ServerSetRole(Spectator, Died)</c>, which a plugin can
/// cause from ChangingRole): <c>DealDamage</c> is always replaced and runs <see cref="PlayerDamagePatch.CorrectedKillPlayer"/>.
/// Harmony cannot patch that <c>KillPlayer</c>, and <c>DealDamage</c> is its only caller.</item>
/// <item>Any other <c>DealDamage</c> (an unknown build): the game code is not replaced; <see cref="PlayerDamageFallbackPatch"/>
/// and <see cref="PlayerDeathFallbackPatch"/> raise the events around it.</item>
/// </list>
/// </remarks>
internal static class DamagePatchMode
{
    // PlayerStats.DealDamage of the Carl Mod builds (both 0.0.4 builds and 0.0.5).
    private const string KnownDealDamage = "105e09d96882658b";

    // PlayerStats.KillPlayer of the 0.0.4 deathmatch build, raw IL bytes (Harmony cannot read this body).
    private const string DeathmatchKillPlayerRaw = "dd877e4af482a1a5";

    static DamagePatchMode()
    {
        DealDamageMethod = AccessTools.DeclaredMethod(typeof(PlayerStats), nameof(PlayerStats.DealDamage));
        KillPlayerMethod = AccessTools.DeclaredMethod(typeof(PlayerStats), nameof(PlayerStats.KillPlayer), [typeof(DamageHandlerBase)]);
        DealDamageFingerprint = NativeBody.Fingerprint(DealDamageMethod);
        ReplaceDealDamage = DealDamageFingerprint == KnownDealDamage;

        if (KillPlayerMethod != null && NativeBody.Fingerprint(KillPlayerMethod) != null)
        {
            KillPlayerPatchable = true;
            NativeKillPlayer = AccessTools.MethodDelegate<Action<PlayerStats, DamageHandlerBase>>(KillPlayerMethod);
        }
        else if (ReplaceDealDamage && NativeBody.RawFingerprint(KillPlayerMethod) is string raw && raw != DeathmatchKillPlayerRaw)
        {
            Logger.Warn($"[PATCHES] PlayerStats.KillPlayer cannot be read and is not the deathmatch build's body (raw IL {raw}); deaths use LabAPI-Mobile's corrected copy of that body.");
        }
    }

    /// <summary>Gets <c>PlayerStats.DealDamage</c>.</summary>
    internal static MethodInfo? DealDamageMethod { get; }

    /// <summary>Gets <c>PlayerStats.KillPlayer</c>.</summary>
    internal static MethodInfo? KillPlayerMethod { get; }

    /// <summary>Gets the IL fingerprint of <c>PlayerStats.DealDamage</c>.</summary>
    internal static string? DealDamageFingerprint { get; }

    /// <summary>Gets whether <c>DealDamage</c> is the known body that <see cref="PlayerDamagePatch"/> replaces.</summary>
    internal static bool ReplaceDealDamage { get; }

    /// <summary>Gets whether Harmony can read (and patch) <c>KillPlayer</c>.</summary>
    internal static bool KillPlayerPatchable { get; }

    /// <summary>Gets the native <c>KillPlayer</c> when its body is clean; otherwise <see langword="null"/>.</summary>
    internal static Action<PlayerStats, DamageHandlerBase>? NativeKillPlayer { get; }

    /// <summary>Gets whether any damage event has subscribers.</summary>
    internal static bool HasSubscribers => PlayerEvents.HasHurting || PlayerEvents.HasHurt || PlayerEvents.HasDying || PlayerEvents.HasDeath;
}

/// <summary>
/// Replaces <see cref="PlayerStats.DealDamage"/> with the Carl Mod body plus the Hurting / Hurt / Dying / Death events.
/// See <see cref="DamagePatchMode"/> for when it is applied and how <c>KillPlayer</c> runs.
/// </summary>
// Official: PlayerStatsSystem/PlayerStats.cs DealDamage
[HarmonyPatch(typeof(PlayerStats), nameof(PlayerStats.DealDamage))]
internal static class PlayerDamagePatch
{
    // The deathmatch module's broadcasts: "You died, auto-respawning in 5 seconds" and "You killed ".
    private const string DmDiedBroadcast = "你已阵亡，将在 5 秒后自动复活";

    private const string DmKilledBroadcast = "你击杀了 ";

    private static readonly AccessTools.FieldRef<Action<ReferenceHub, DamageHandlerBase>> OnAnyPlayerDamaged =
        AccessTools.StaticFieldRefAccess<Action<ReferenceHub, DamageHandlerBase>>(AccessTools.Field(typeof(PlayerStats), nameof(PlayerStats.OnAnyPlayerDamaged)));

    private static readonly AccessTools.FieldRef<Action<ReferenceHub, DamageHandlerBase>> OnAnyPlayerDied =
        AccessTools.StaticFieldRefAccess<Action<ReferenceHub, DamageHandlerBase>>(AccessTools.Field(typeof(PlayerStats), nameof(PlayerStats.OnAnyPlayerDied)));

    private static readonly AccessTools.FieldRef<PlayerStats, Action<DamageHandlerBase>> OnThisPlayerDamaged =
        AccessTools.FieldRefAccess<PlayerStats, Action<DamageHandlerBase>>(nameof(PlayerStats.OnThisPlayerDamaged));

    private static readonly AccessTools.FieldRef<PlayerStats, Action<DamageHandlerBase>> OnThisPlayerDied =
        AccessTools.FieldRefAccess<PlayerStats, Action<DamageHandlerBase>>(nameof(PlayerStats.OnThisPlayerDied));

    private static bool Prepare()
    {
        if (DamagePatchMode.ReplaceDealDamage)
        {
            return true;
        }

        PatchManager.Skip(typeof(PlayerDamagePatch), $"{NativeBody.Describe(DamagePatchMode.DealDamageMethod)} differs from the Carl Mod builds LabAPI-Mobile knows (0.0.4, 0.0.5) "
            + $"(IL {DamagePatchMode.DealDamageFingerprint ?? "unreadable"}); the game's damage code runs unchanged and Hurting / Hurt / Dying / Death "
            + "are raised around it (Dying after the game's own death callbacks).");
        return false;
    }

    private static bool Prefix(PlayerStats __instance, DamageHandlerBase handler, ref bool __result)
    {
        // The native KillPlayer is clean: nothing to correct, so the game runs untouched while nobody listens.
        if (DamagePatchMode.NativeKillPlayer != null && !DamagePatchMode.HasSubscribers)
        {
            return true;
        }

        __result = DealDamage(__instance, handler);
        return false;
    }

    /// <summary>
    /// The deathmatch build's <c>PlayerStats.KillPlayer</c> with its branch fixed: ragdoll, item drop unless
    /// <c>death_no_drop</c>, one <c>ServerSetRole(Spectator, Died)</c>, the death console message, the deathmatch
    /// broadcasts, <see cref="SpectatorRole.ServerSetData"/> only when the player is a spectator afterwards, and
    /// <c>PlayerStats.DmCleanup</c>.
    /// </summary>
    /// <param name="stats">The dying player's stats.</param>
    /// <param name="handler">The damage handler that killed the player.</param>
    internal static void CorrectedKillPlayer(PlayerStats stats, DamageHandlerBase handler)
    {
        ReferenceHub hub = stats._hub;
        RagdollManager.ServerSpawnRagdoll(hub, handler);
        if (!ConfigFile.ServerConfig.GetBool("death_no_drop"))
        {
            hub.inventory.ServerDropEverything();
        }

        hub.roleManager.ServerSetRole(RoleTypeId.Spectator, RoleChangeReason.Died);
        hub.gameConsoleTransmission.SendToClient("You died. Reason: " + handler.ServerLogsText, "yellow");
        bool deathmatch = CarlModDeathmatch.IsEnabled;
        if (deathmatch)
        {
            Broadcast.Singleton.TargetAddElement(hub.connectionToClient, DmDiedBroadcast, 5, Broadcast.BroadcastFlags.Normal);
        }

        if (hub.roleManager.CurrentRole is SpectatorRole spectator)
        {
            spectator.ServerSetData(handler);
        }

        if (deathmatch && handler is AttackerDamageHandler attackerHandler)
        {
            ReferenceHub attacker = attackerHandler.Attacker.Hub;
            if ((object)attacker != null && (object)attacker != hub)
            {
                Broadcast.Singleton.TargetAddElement(attacker.connectionToClient, DmKilledBroadcast + hub.nicknameSync.MyNick, 2, Broadcast.BroadcastFlags.Normal);
            }
        }

        CarlModDeathmatch.Cleanup();
    }

    private static void KillPlayer(PlayerStats stats, DamageHandlerBase handler)
    {
        Action<PlayerStats, DamageHandlerBase>? native = DamagePatchMode.NativeKillPlayer;
        if (native != null)
        {
            native(stats, handler);
        }
        else
        {
            CorrectedKillPlayer(stats, handler);
        }
    }

    private static bool DealDamage(PlayerStats stats, DamageHandlerBase handler)
    {
        ReferenceHub hub = stats._hub;
        if (hub.characterClassManager.GodMode)
        {
            return false;
        }

        if (hub.roleManager.CurrentRole is IDamageHandlerProcessingRole processingRole)
        {
            handler = processingRole.ProcessDamageHandler(handler);
        }

        ReferenceHub? attacker = handler is AttackerDamageHandler attackerHandler ? attackerHandler.Attacker.Hub : null;

        if (PlayerEvents.HasHurting)
        {
            PlayerHurtingEventArgs hurting = new(attacker, hub, handler);
            PlayerEvents.OnHurting(hurting);
            if (!hurting.IsAllowed)
            {
                return false;
            }
        }

        DamageHandlerBase.HandlerOutput output = handler.ApplyDamage(hub);
        if (PlayerEvents.HasHurt)
        {
            PlayerEvents.OnHurt(new PlayerHurtEventArgs(attacker, hub, handler));
        }

        if (output == DamageHandlerBase.HandlerOutput.Nothing)
        {
            return false;
        }

        OnAnyPlayerDamaged()?.Invoke(hub, handler);
        OnThisPlayerDamaged(stats)?.Invoke(handler);
        if (output != DamageHandlerBase.HandlerOutput.Death)
        {
            return true;
        }

        if (PlayerEvents.HasDying)
        {
            PlayerDyingEventArgs dying = new(hub, attacker, handler);
            PlayerEvents.OnDying(dying);
            if (!dying.IsAllowed)
            {
                return false;
            }
        }

        RoleTypeId oldRole = hub.GetRoleId();
        Vector3 oldPosition = hub.transform.position;
        Vector3 oldVelocity = hub.GetVelocity();
        Quaternion oldCameraRotation = hub.PlayerCameraReference.rotation;

        OnAnyPlayerDied()?.Invoke(hub, handler);
        OnThisPlayerDied(stats)?.Invoke(handler);
        KillPlayer(stats, handler);

        if (PlayerEvents.HasDeath)
        {
            PlayerEvents.OnDeath(new PlayerDeathEventArgs(hub, attacker, handler, oldRole, oldPosition, oldVelocity, oldCameraRotation));
        }

        return true;
    }
}

/// <summary>
/// On a build with an unknown <see cref="PlayerStats.DealDamage"/>: raises Hurting before the unchanged game method and
/// Hurt after it (or, for a lethal hit, just before Dying).
/// </summary>
/// <remarks>
/// Hurting runs before the role's damage processing (SCP-096 and similar roles may still change the handler), and a
/// changed handler is not fed back.
/// </remarks>
// Official: PlayerStatsSystem/PlayerStats.cs DealDamage
[HarmonyPatch(typeof(PlayerStats), nameof(PlayerStats.DealDamage))]
internal static class PlayerDamageFallbackPatch
{
    private static PlayerStats? _pendingHurt;
    private static DamageHandlerBase? _pendingHandler;

    /// <summary>
    /// Raises the Hurt event still owed for <paramref name="stats"/>, if any.
    /// </summary>
    /// <param name="stats">The player's stats.</param>
    internal static void RaisePendingHurt(PlayerStats stats)
    {
        if ((object?)_pendingHurt != stats || _pendingHandler == null)
        {
            return;
        }

        DamageHandlerBase handler = _pendingHandler;
        _pendingHurt = null;
        _pendingHandler = null;
        PlayerEvents.OnHurt(new PlayerHurtEventArgs(Attacker(handler), stats._hub, handler));
    }

    private static bool Prepare() => DamagePatchMode.DealDamageMethod != null && !DamagePatchMode.ReplaceDealDamage;

    private static bool Prefix(PlayerStats __instance, DamageHandlerBase __0, ref bool __result, out PendingHurt __state)
    {
        // Nested damage (from an event handler) keeps the outer call's pending Hurt.
        __state = new PendingHurt(_pendingHurt, _pendingHandler);
        _pendingHurt = null;
        _pendingHandler = null;
        if (!PlayerEvents.HasHurting && !PlayerEvents.HasHurt)
        {
            return true;
        }

        ReferenceHub hub = __instance._hub;
        if (hub.characterClassManager.GodMode)
        {
            return true;
        }

        if (PlayerEvents.HasHurting)
        {
            PlayerHurtingEventArgs hurting = new(Attacker(__0), hub, __0);
            PlayerEvents.OnHurting(hurting);
            if (!hurting.IsAllowed)
            {
                __result = false;
                return false;
            }
        }

        if (PlayerEvents.HasHurt)
        {
            _pendingHurt = __instance;
            _pendingHandler = __0;
        }

        return true;
    }

    private static void Postfix(PlayerStats __instance, PendingHurt __state)
    {
        RaisePendingHurt(__instance);
        _pendingHurt = __state.Stats;
        _pendingHandler = __state.Handler;
    }

    private static ReferenceHub? Attacker(DamageHandlerBase handler) => handler is AttackerDamageHandler attackerHandler ? attackerHandler.Attacker.Hub : null;

    /// <summary>
    /// The Hurt event owed by an enclosing <c>DealDamage</c> call.
    /// </summary>
    internal readonly struct PendingHurt(PlayerStats? stats, DamageHandlerBase? handler)
    {
        /// <summary>Gets the damaged player's stats.</summary>
        public PlayerStats? Stats { get; } = stats;

        /// <summary>Gets the damage handler.</summary>
        public DamageHandlerBase? Handler { get; } = handler;
    }
}

/// <summary>
/// On a build with an unknown <see cref="PlayerStats.DealDamage"/>: raises Dying before and Death after the game's
/// <c>KillPlayer</c>. Cancelling Dying skips <c>KillPlayer</c> and leaves the player alive at 0 HP, but the game's own
/// death callbacks (<c>OnAnyPlayerDied</c>) have already run.
/// </summary>
// Official: PlayerStatsSystem/PlayerStats.cs DealDamage (OnDying / OnDeath around KillPlayer)
[HarmonyPatch(typeof(PlayerStats), nameof(PlayerStats.KillPlayer))]
internal static class PlayerDeathFallbackPatch
{
    private static bool Prepare()
    {
        if (DamagePatchMode.DealDamageMethod == null || DamagePatchMode.ReplaceDealDamage)
        {
            return false;
        }

        if (DamagePatchMode.KillPlayerPatchable)
        {
            return true;
        }

        PatchManager.Skip(typeof(PlayerDeathFallbackPatch), $"{NativeBody.Describe(DamagePatchMode.KillPlayerMethod)} is missing or cannot be patched on this build; Dying and Death are not raised.");
        return false;
    }

    private static bool Prefix(PlayerStats __instance, DamageHandlerBase __0, out DeathState __state)
    {
        __state = default;
        PlayerDamageFallbackPatch.RaisePendingHurt(__instance);
        ReferenceHub hub = __instance._hub;
        ReferenceHub? attacker = __0 is AttackerDamageHandler attackerHandler ? attackerHandler.Attacker.Hub : null;
        if (PlayerEvents.HasDying)
        {
            PlayerDyingEventArgs dying = new(hub, attacker, __0);
            PlayerEvents.OnDying(dying);
            if (!dying.IsAllowed)
            {
                return false;
            }
        }

        if (PlayerEvents.HasDeath)
        {
            __state = new DeathState(true, attacker, hub.GetRoleId(), hub.transform.position, hub.GetVelocity(), hub.PlayerCameraReference.rotation);
        }

        return true;
    }

    private static void Postfix(PlayerStats __instance, DamageHandlerBase __0, DeathState __state, bool __runOriginal)
    {
        if (__runOriginal && __state.Valid && PlayerEvents.HasDeath)
        {
            PlayerEvents.OnDeath(new PlayerDeathEventArgs(__instance._hub, __state.Attacker, __0, __state.Role, __state.Position, __state.Velocity, __state.CameraRotation));
        }
    }

    /// <summary>
    /// The player state captured before <c>KillPlayer</c> for the Death event.
    /// </summary>
    internal readonly struct DeathState(bool valid, ReferenceHub? attacker, RoleTypeId role, Vector3 position, Vector3 velocity, Quaternion cameraRotation)
    {
        /// <summary>Gets whether the state was captured.</summary>
        public bool Valid { get; } = valid;

        /// <summary>Gets the attacker.</summary>
        public ReferenceHub? Attacker { get; } = attacker;

        /// <summary>Gets the role before death.</summary>
        public RoleTypeId Role { get; } = role;

        /// <summary>Gets the position before death.</summary>
        public Vector3 Position { get; } = position;

        /// <summary>Gets the velocity before death.</summary>
        public Vector3 Velocity { get; } = velocity;

        /// <summary>Gets the camera rotation before death.</summary>
        public Quaternion CameraRotation { get; } = cameraRotation;
    }
}
