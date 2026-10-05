using Generators;
using HarmonyLib;
using LabApi.Events.Arguments.ServerEvents;
using LabApi.Events.Handlers;
using MapGeneration;
using Mirror;
using System;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;

namespace LabApi.Events.Patches.Facility;

// Official: RoomLightController.cs LightsEnabledHook (RoomLightChanged)
// Carl Mod's FlickerableLightController changes LightsEnabled on the server only through SetLights.
[HarmonyPatch(typeof(FlickerableLightController), nameof(FlickerableLightController.SetLights))]
internal static class RoomLightChangedPatch
{
    private static void Prefix(FlickerableLightController __instance, out bool __state) => __state = __instance.LightsEnabled;

    private static void Postfix(FlickerableLightController __instance, bool __state)
    {
        if (__state != __instance.LightsEnabled && ServerEvents.HasRoomLightChanged && NetworkServer.active)
        {
            ServerEvents.OnRoomLightChanged(new RoomLightChangedEventArgs(__instance.Room, __instance.LightsEnabled));
        }
    }
}

// Official: RoomLightController.cs OverrideColorHook (RoomColorChanged)
// Carl Mod's room color is the warhead light color while the warhead light override is on; the event reports the
// effective override color (Color.clear when the override is off).
[HarmonyPatch]
internal static class RoomColorChangedPatch
{
    private static System.Collections.Generic.IEnumerable<System.Reflection.MethodBase> TargetMethods()
    {
        yield return AccessTools.PropertySetter(typeof(FlickerableLightController), nameof(FlickerableLightController.WarheadLightColor));
        yield return AccessTools.PropertySetter(typeof(FlickerableLightController), nameof(FlickerableLightController.WarheadLightOverride));
    }

    private static void Prefix(FlickerableLightController __instance, out Color __state) => __state = EffectiveColor(__instance);

    private static void Postfix(FlickerableLightController __instance, Color __state)
    {
        if (!ServerEvents.HasRoomColorChanged || !NetworkServer.active)
        {
            return;
        }

        Color current = EffectiveColor(__instance);
        if (current != __state)
        {
            ServerEvents.OnRoomColorChanged(new RoomColorChangedEventArgs(__instance.Room, current));
        }
    }

    private static Color EffectiveColor(FlickerableLightController controller) =>
        controller._warheadLightOverride ? controller._warheadLightColor : Color.clear;
}

// Official: MapGeneration/SeedSynchronizer.cs Start (MapGenerating)
// Carl Mod clients generate the facility from the synchronized seed themselves, so a plugin can change the seed but
// cannot cancel generation; IsAllowed = false keeps the original seed.
[HarmonyPatch(typeof(SeedSynchronizer), nameof(SeedSynchronizer.Start))]
internal static class MapGeneratingPatch
{
    private static void Postfix(SeedSynchronizer __instance)
    {
        if (!ServerEvents.HasMapGenerating || !NetworkServer.active)
        {
            return;
        }

        MapGeneratingEventArgs e = new(__instance._syncSeed);
        ServerEvents.OnMapGenerating(e);
        if (e.IsAllowed && e.Seed > 0)
        {
            __instance.Network_syncSeed = e.Seed;
        }
    }
}

/// <summary>
/// Raises <see cref="ServerEvents.MapGenerated"/> from Carl Mod's <see cref="SeedSynchronizer.OnMapGenerated"/> event.
/// </summary>
/// <remarks>
/// Official: MapGeneration/SeedSynchronizer.cs GenerateLevel. The handler is subscribed during LabAPI startup, after the
/// game's own handlers, so doors and rooms are already registered when the event fires.
/// </remarks>
internal static class MapGeneratedHook
{
    [InitializeWrapper]
    internal static void Initialize()
    {
        SeedSynchronizer.OnMapGenerated += OnMapGenerated;
    }

    private static void OnMapGenerated()
    {
        if (!ServerEvents.HasMapGenerated || !NetworkServer.active)
        {
            return;
        }

        // The fork re-invokes every handler one by one if any throws, so this handler never lets an exception escape.
        try
        {
            ServerEvents.OnMapGenerated(new MapGeneratedEventArgs(SeedSynchronizer.Seed));
        }
        catch (Exception e)
        {
            Logger.Error($"[PATCHES] MapGenerated handler failed: {e}");
        }
    }
}
