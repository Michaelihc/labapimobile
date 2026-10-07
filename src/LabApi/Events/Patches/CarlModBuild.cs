using HarmonyLib;
using System;
using System.Reflection;

namespace LabApi.Events.Patches;

/// <summary>
/// Game features that differ between Carl Mod releases and are detected from the members the game assembly has.
/// </summary>
/// <remarks>
/// Carl Mod 0.0.5 replaces the 0.0.4 device user IDs with device IDs: <c>DeviceIdentity.IsValidDeviceId</c>, the
/// <c>DeviceId</c> ban type and Remote Admin clipboard entry, and "Device ID" in the Remote Admin player info. Members that
/// only one release has are bound by reflection, so no LabAPI method needs them to be JIT-compiled.
/// </remarks>
internal static class CarlModBuild
{
    private static readonly Func<string, bool> DeviceIdValidator;

    static CarlModBuild()
    {
        DeviceIdValidator = BindDeviceIdValidator(out bool hasDeviceIds);
        HasDeviceIds = hasDeviceIds;
    }

    /// <summary>
    /// Gets whether the game identifies players by device ID (Carl Mod 0.0.5) rather than by device user ID (0.0.4).
    /// </summary>
    internal static bool HasDeviceIds { get; }

    /// <summary>
    /// Gets the link ID of the Remote Admin clipboard entry for the player ID string (<c>CP_DEVICEID</c> on 0.0.5,
    /// <c>CP_USERID</c> on 0.0.4).
    /// </summary>
    internal static string IdClipboardLink => HasDeviceIds ? "CP_DEVICEID" : "CP_USERID";

    /// <summary>
    /// Validates a player ID with the game's own check: <c>DeviceIdentity.IsValidDeviceId</c> on 0.0.5,
    /// <c>DeviceIdentity.IsValidUserId</c> on 0.0.4.
    /// </summary>
    /// <param name="id">The ID.</param>
    /// <returns>Whether the game accepts the ID.</returns>
    internal static bool IsValidPlayerId(string id) => DeviceIdValidator(id);

    private static Func<string, bool> BindDeviceIdValidator(out bool hasDeviceIds)
    {
        MethodInfo? method = AccessTools.DeclaredMethod(typeof(DeviceIdentity), "IsValidDeviceId", [typeof(string)]);
        if (method != null && method.IsStatic && method.ReturnType == typeof(bool)
            && Delegate.CreateDelegate(typeof(Func<string, bool>), method, false) is Func<string, bool> validator)
        {
            hasDeviceIds = true;
            return validator;
        }

        hasDeviceIds = false;
        return DeviceIdentity.IsValidUserId;
    }
}
