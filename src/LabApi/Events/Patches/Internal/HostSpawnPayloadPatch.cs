using System;
using HarmonyLib;
using Mirror;

namespace LabApi.Events.Patches.Internal;

/// <summary>
/// Keeps the server's own objects from being reverted by the spawn messages the server sends to its host client.
/// </summary>
/// <remarks>
/// <para>
/// The Carl Mod server runs as a Mirror host, and its local connection observes every object. The spawn message for that
/// connection is serialized when it is sent (<c>NetworkServer.SendSpawnMessage</c>) but handled on the next frame, where
/// <c>NetworkClient.OnHostClientSpawn</c> deserializes the payload into the very same server object
/// (<c>DeserializeClient(reader, initialState: true)</c>, which assigns every SyncVar field). Every SyncVar written
/// between the spawn and that frame is therefore reverted on the server: a toy created with
/// <see cref="Features.Wrappers.AdminToy"/> <c>Create(..., networkSpawn: true)</c> and then given a colour or flags snaps
/// back to the prefab values on the server, and the next broadcast can send those stale values to clients.
/// </para>
/// <para>
/// The prefix re-serializes the object's current state into the message, so the host still runs its spawn hooks
/// (as Mirror intends) but with the values the server holds now.
/// </para>
/// </remarks>
// Official: none (Mirror host-mode behaviour of the fork's Mirror; keeps wrapper property writes after spawning intact)
[HarmonyPatch(typeof(NetworkClient), nameof(NetworkClient.OnHostClientSpawn))]
internal static class HostSpawnPayloadPatch
{
    private static void Prefix(ref SpawnMessage message, out NetworkWriterPooled? __state)
    {
        __state = null;
        if (message.payload.Count == 0
            || !NetworkServer.spawned.TryGetValue(message.netId, out NetworkIdentity identity)
            || identity == null
            || identity.NetworkBehaviours == null
            || identity.NetworkBehaviours.Length == 0)
        {
            return;
        }

        NetworkWriterPooled owner = NetworkWriterPool.Get();
        NetworkWriterPooled observers = NetworkWriterPool.Get();
        try
        {
            identity.SerializeServer_Spawn(owner, observers);
        }
        catch (Exception)
        {
            // Leave the original payload; Mirror reports serialization problems itself.
            NetworkWriterPool.Return(owner);
            NetworkWriterPool.Return(observers);
            return;
        }

        if (message.isOwner)
        {
            message.payload = owner.ToArraySegment();
            NetworkWriterPool.Return(observers);
            __state = owner;
        }
        else
        {
            message.payload = observers.ToArraySegment();
            NetworkWriterPool.Return(owner);
            __state = observers;
        }
    }

    private static void Postfix(NetworkWriterPooled? __state)
    {
        if (__state != null)
        {
            NetworkWriterPool.Return(__state);
        }
    }
}
