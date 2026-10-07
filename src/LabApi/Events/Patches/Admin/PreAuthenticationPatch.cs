using HarmonyLib;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using LiteNetLib;
using Mirror;
using Mirror.LiteNetLib4Mirror;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;

namespace LabApi.Events.Patches.Admin;

/// <summary>
/// Raises PreAuthenticating / PreAuthenticated from the Carl Mod connection request handler.
/// </summary>
/// <remarks>
/// The fork has no central authentication: the client sends a device user ID (0.0.4) or device ID (0.0.5) and answers a
/// server challenge. When either event has subscribers this prefix runs the build's handler with the events inserted after
/// the challenge and ban checks, where official SL raises them. Without subscribers the original runs untouched. On 0.0.5
/// the handler also refuses a connection when the server is full (reserved slots count for players that have one):
/// <see cref="PlayerPreAuthenticatingEventArgs.CanJoin"/> starts with that result, as in official SL. On 0.0.4, which has no
/// such check, it starts <see langword="true"/>. Applied only when the native body is one of the known Carl Mod bodies.
/// </remarks>
// Official: CustomLiteNetLib4MirrorTransport.cs ProcessConnectionRequest
[HarmonyPatch(typeof(CustomLiteNetLib4MirrorTransport), nameof(CustomLiteNetLib4MirrorTransport.ProcessConnectionRequest))]
internal static class PreAuthenticationPatch
{
    // ProcessConnectionRequest of both Carl Mod 0.0.4 builds, and of 0.0.5 (device IDs, server-full check, failed Accept).
    private const string CarlMod004Body = "083595683b38d427";
    private const string Version005Body = "f81fad9e8714f44e";

    private static readonly MethodInfo? Target = AccessTools.DeclaredMethod(typeof(CustomLiteNetLib4MirrorTransport), nameof(CustomLiteNetLib4MirrorTransport.ProcessConnectionRequest));

    private static readonly BodyVariant Variant = NativeBody.Identify(Target, CarlMod004Body, Version005Body, out Fingerprint);

    private static readonly string? Fingerprint;

    private static bool Prepare()
    {
        if (Variant != BodyVariant.Unknown)
        {
            return true;
        }

        PatchManager.Skip(typeof(PreAuthenticationPatch), NativeBody.UnknownBody(Target, Fingerprint, "PreAuthenticating / PreAuthenticated are not raised."));
        return false;
    }

    private static bool Prefix(ConnectionRequest request)
    {
        if (!PlayerEvents.HasPreAuthenticating && !PlayerEvents.HasPreAuthenticated)
        {
            return true;
        }

        Process(request);
        return false;
    }

    private static void Reject(ConnectionRequest request, byte reason)
    {
        CustomLiteNetLib4MirrorTransport.RequestWriter.Reset();
        CustomLiteNetLib4MirrorTransport.RequestWriter.Put(reason);
        request.Reject(CustomLiteNetLib4MirrorTransport.RequestWriter);
    }

    private static void Process(ConnectionRequest request)
    {
        try
        {
            if (!request.Data.TryGetByte(out byte result) || result != 0)
            {
                Reject(request, 2);
                return;
            }

            byte backwardRevision = 0;
            if (!request.Data.TryGetByte(out byte major) || !request.Data.TryGetByte(out byte minor) || !request.Data.TryGetByte(out byte revision)
                || !request.Data.TryGetBool(out bool backwardCompatibility) || (backwardCompatibility && !request.Data.TryGetByte(out backwardRevision)))
            {
                Reject(request, 3);
                return;
            }

            if (!GameCore.Version.CompatibilityCheck(GameCore.Version.Major, GameCore.Version.Minor, GameCore.Version.Revision, major, minor, revision, backwardCompatibility, backwardRevision))
            {
                Reject(request, 3);
                return;
            }

            if (!request.Data.TryGetInt(out int challengeId) || !request.Data.TryGetBytesWithLength(out byte[] challengeResponse) || !request.Data.TryGetBytesWithLength(out byte[] userIdBytes))
            {
                Reject(request, 15);
                return;
            }

            string userId = Encoding.UTF8.GetString(userIdBytes);
            if (!CarlModBuild.IsValidPlayerId(userId))
            {
                Reject(request, 15);
                return;
            }

            if (CustomLiteNetLib4MirrorTransport.DelayConnections)
            {
                CustomLiteNetLib4MirrorTransport.PreauthDisableIdleMode();
                CustomLiteNetLib4MirrorTransport.RequestWriter.Reset();
                CustomLiteNetLib4MirrorTransport.RequestWriter.Put((byte)17);
                CustomLiteNetLib4MirrorTransport.RequestWriter.Put(CustomLiteNetLib4MirrorTransport.DelayTime);
                if (CustomLiteNetLib4MirrorTransport.DelayVolume < byte.MaxValue)
                {
                    CustomLiteNetLib4MirrorTransport.DelayVolume++;
                }

                if (CustomLiteNetLib4MirrorTransport.DisplayPreauthLogs)
                {
                    ServerConsole.AddLog((CustomLiteNetLib4MirrorTransport.DelayVolume < CustomLiteNetLib4MirrorTransport.DelayVolumeThreshold ? "Delayed" : "Force delayed")
                        + $" connection incoming from endpoint {request.RemoteEndPoint} by {CustomLiteNetLib4MirrorTransport.DelayTime} seconds.");
                }

                request.Reject(CustomLiteNetLib4MirrorTransport.RequestWriter);
                return;
            }

            if (!CustomLiteNetLib4MirrorTransport.ValidateChallenge(request.RemoteEndPoint.Address, challengeId, challengeResponse))
            {
                CustomLiteNetLib4MirrorTransport.IssueChallenge(request);
                return;
            }

            string ipAddress = request.RemoteEndPoint.Address.ToString();
            KeyValuePair<BanDetails, BanDetails> bans = BanHandler.QueryBan(userId, ipAddress);
            BanDetails banDetails = bans.Key ?? bans.Value;
            if (banDetails != null)
            {
                CustomLiteNetLib4MirrorTransport.RequestWriter.Reset();
                CustomLiteNetLib4MirrorTransport.RequestWriter.Put((byte)6);
                CustomLiteNetLib4MirrorTransport.RequestWriter.Put(banDetails.Expires == 0L ? 0 : new DateTime(banDetails.Expires, DateTimeKind.Utc).ToBinary());
                string reason = banDetails.Reason ?? "No reason";
                CustomLiteNetLib4MirrorTransport.RequestWriter.Put(reason.Length > 400 ? reason.Substring(0, 400) : reason);
                request.Reject(CustomLiteNetLib4MirrorTransport.RequestWriter);
                return;
            }

            int position = request.Data.Position;
            bool version005 = Variant == BodyVariant.Version005;
            bool canJoin = !version005 || !IsServerFull(userId);
            if (PlayerEvents.HasPreAuthenticating)
            {
                PlayerPreAuthenticatingEventArgs e = new(canJoin, userId, ipAddress, request, position);
                PlayerEvents.OnPreAuthenticating(e);
                if (!e.IsAllowed)
                {
                    if (e.CustomReject != null)
                    {
                        request.Reject(e.CustomReject);
                    }
                    else
                    {
                        Reject(request, 4);
                    }

                    return;
                }

                if (!e.CanJoin)
                {
                    Reject(request, 1);
                    CustomLiteNetLib4MirrorTransport.ResetIdleMode();
                    return;
                }
            }
            else if (!canJoin)
            {
                // The 0.0.5 server-full refusal.
                Reject(request, 1);
                return;
            }

            if (version005)
            {
                // 0.0.5 forgets the requested ID when LiteNetLib refuses the connection.
                string key = request.RemoteEndPoint.ToString();
                lock (CustomLiteNetLib4MirrorTransport.UserIdLock)
                {
                    CustomLiteNetLib4MirrorTransport.RequestedUserIds[key] = userId;
                    if (request.Accept() == null)
                    {
                        CustomLiteNetLib4MirrorTransport.RequestedUserIds.Remove(key);
                    }
                }
            }
            else
            {
                lock (CustomLiteNetLib4MirrorTransport.UserIdLock)
                {
                    CustomLiteNetLib4MirrorTransport.RequestedUserIds[request.RemoteEndPoint.ToString()] = userId;
                }

                request.Accept();
            }

            CustomLiteNetLib4MirrorTransport.PreauthDisableIdleMode();
            if (PlayerEvents.HasPreAuthenticated)
            {
                PlayerEvents.OnPreAuthenticated(new PlayerPreAuthenticatedEventArgs(userId, ipAddress, request, position));
            }
        }
        catch (Exception ex)
        {
            CustomLiteNetLib4MirrorTransport.Rejected++;
            if (CustomLiteNetLib4MirrorTransport.Rejected > CustomLiteNetLib4MirrorTransport.RejectionThreshold)
            {
                CustomLiteNetLib4MirrorTransport.SuppressRejections = true;
            }

            if (!CustomLiteNetLib4MirrorTransport.SuppressRejections)
            {
                ServerConsole.AddLog($"Player from endpoint {request.RemoteEndPoint} failed to preauthenticate: {ex.Message}");
            }

            Reject(request, 4);
        }
    }

    /// <summary>
    /// The Carl Mod 0.0.5 server-full check: connected peers (plus the host's own client on a non-dedicated server) against
    /// the slots, with the reserved slots added for a player that has one.
    /// </summary>
    private static bool IsServerFull(string userId)
    {
        lock (CustomLiteNetLib4MirrorTransport.UserIdLock)
        {
            int players = LiteNetLib4MirrorCore.Host.PeersCount;
            if (!ServerStatic.IsDedicated && NetworkServer.localConnection != null)
            {
                players++;
            }

            int slots = CustomNetworkManager.slots;
            if (ReservedSlot.HasReservedSlot(userId, out _))
            {
                slots += CustomNetworkManager.reservedSlots;
            }

            return players >= slots;
        }
    }
}
