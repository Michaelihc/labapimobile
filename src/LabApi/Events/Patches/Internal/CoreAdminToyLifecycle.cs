using AdminToys;
using HarmonyLib;
using LabApi.Features.Wrappers;
using Mirror;

namespace LabApi.Events.Patches.Internal;

/// <summary>
/// Adds the <see cref="AdminToy"/> wrapper when an admin toy is network spawned.
/// </summary>
/// <remarks>
/// Official SL raises <c>AdminToyBase.OnAdded</c> from <c>AdminToyBase.Start</c>; Carl Mod's toys have no such hook.
/// Toys created through the wrapper <c>Create</c> methods are cached immediately, so only spawns from elsewhere
/// (Remote Admin <c>spawntoy</c>, other plugins) depend on this.
/// </remarks>
// Official: AdminToys/AdminToyBase.cs AdminToyBase.Start (OnAdded)
[HarmonyPatch(typeof(NetworkIdentity), nameof(NetworkIdentity.OnStartServer))]
internal static class AdminToyAddedPatch
{
    private static void Postfix(NetworkIdentity __instance)
    {
        AdminToyBase? toy = FindToy(__instance);
        if (toy != null)
        {
            AdminToy.AddAdminToy(toy);
        }
    }

    /// <summary>
    /// Finds the admin toy behaviour of a network identity without allocating.
    /// </summary>
    /// <param name="identity">The network identity.</param>
    /// <returns>The admin toy, or <see langword="null"/>.</returns>
    internal static AdminToyBase? FindToy(NetworkIdentity identity)
    {
        NetworkBehaviour[]? behaviours = identity.NetworkBehaviours;
        if (behaviours == null)
        {
            return null;
        }

        for (int i = 0; i < behaviours.Length; i++)
        {
            if (behaviours[i] is AdminToyBase toy)
            {
                return toy;
            }
        }

        return null;
    }
}

/// <summary>
/// Removes the <see cref="AdminToy"/> wrapper when an admin toy is destroyed.
/// </summary>
/// <remarks>
/// Official SL raises <c>AdminToyBase.OnRemoved</c> from <c>AdminToyBase.OnDestroy</c>; Carl Mod's toys have no such method.
/// A prefix: the server destroy inside <c>OnDestroy</c> resets the identity, which empties <c>NetworkBehaviours</c>, so a
/// postfix never found the toy and the wrappers of destroyed toys stayed cached.
/// </remarks>
// Official: AdminToys/AdminToyBase.cs AdminToyBase.OnDestroy (OnRemoved)
[HarmonyPatch(typeof(NetworkIdentity), nameof(NetworkIdentity.OnDestroy))]
internal static class AdminToyRemovedPatch
{
    private static void Prefix(NetworkIdentity __instance)
    {
        if (AdminToy.Dictionary.Count == 0)
        {
            return;
        }

        AdminToyBase? toy = AdminToyAddedPatch.FindToy(__instance);
        if (toy != null)
        {
            AdminToy.RemoveAdminToy(toy);
        }
    }
}
