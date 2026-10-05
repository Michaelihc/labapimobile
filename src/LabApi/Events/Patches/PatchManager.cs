using HarmonyLib;
using LabApi.Features.Console;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;

namespace LabApi.Events.Patches;

/// <summary>
/// Applies the Harmony patches that raise LabAPI events and keep wrapper caches in sync.
/// </summary>
/// <remarks>
/// The official game assembly calls LabAPI directly. The Carl Mod build does not, so every event site and
/// wrapper lifecycle hook is a Harmony patch in this namespace. Each patch class is applied on its own so a
/// single incompatible target only disables that patch.
/// </remarks>
public static class PatchManager
{
    private const string LoggerPrefix = "[PATCHES]";

    /// <summary>
    /// The Harmony instance that owns every LabAPI patch.
    /// </summary>
    public static Harmony Harmony { get; } = new("labapi.mobile");

    /// <summary>
    /// Gets the patch classes that failed to apply, keyed by type, with the exception message.
    /// </summary>
    public static Dictionary<Type, string> FailedPatches { get; } = [];

    /// <summary>
    /// Gets the number of patch classes that applied successfully.
    /// </summary>
    public static int AppliedPatchCount { get; private set; }

    /// <summary>
    /// Gets whether <see cref="ApplyAll"/> has run.
    /// </summary>
    public static bool Applied { get; private set; }

    /// <summary>
    /// Applies every <see cref="HarmonyPatch"/> class in the LabAPI assembly.
    /// </summary>
    public static void ApplyAll()
    {
        if (Applied)
        {
            return;
        }

        Applied = true;
        Stopwatch stopwatch = Stopwatch.StartNew();

        foreach (Type type in AccessTools.GetTypesFromAssembly(typeof(PatchManager).Assembly))
        {
            if (!type.IsDefined(typeof(HarmonyPatch), false))
            {
                continue;
            }

            try
            {
                Harmony.CreateClassProcessor(type).Patch();
                AppliedPatchCount++;
            }
            catch (Exception e)
            {
                Exception root = e is TargetInvocationException { InnerException: not null } ? e.InnerException : e;
                FailedPatches[type] = root.Message;
                Logger.Error($"{LoggerPrefix} Failed to apply {type.FullName}: {root}");
            }
        }

        // Mono shares one native body between all reference-type instantiations of a generic method, so a patch on, say,
        // ScpAttackAbilityBase<Scp939Role> also replaces ZombieAttackAbility's method. Patch a non-generic override instead.
        foreach (MethodBase method in Harmony.GetPatchedMethods())
        {
            if (method.DeclaringType is { IsGenericType: true } || method.IsGenericMethod)
            {
                Logger.Error($"{LoggerPrefix} {method.DeclaringType}.{method.Name} is generic; its patch also affects every other reference-type instantiation on Mono.");
            }
        }

        stopwatch.Stop();
        Logger.Info($"{LoggerPrefix} Applied {AppliedPatchCount} patch classes in {stopwatch.ElapsedMilliseconds} ms ({FailedPatches.Count} failed)");
    }
}
