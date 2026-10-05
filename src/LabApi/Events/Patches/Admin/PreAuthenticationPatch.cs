using HarmonyLib;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using LiteNetLib;
using System;
using System.Collections.Generic;
using System.Text;

namespace LabApi.Events.Patches.Admin;

/// <summary>
/// Raises PreAuthenticating / PreAuthenticated from the Carl Mod connection request handler.
/// </summary>
/// <remarks>
/// The fork has no central authentication: the client sends a device user ID and answers a server challenge.
/// When either event has subscribers this prefix runs the fork's handler with the events inserted after the
/// challenge and ban checks, where official SL raises them. Without subscribers the original runs untouched.
/// </remarks>
// Official: CustomLiteNetLib4MirrorTransport.cs ProcessConnectionRequest
[HarmonyPatch(typeof(CustomLiteNetLib4MirrorTransport), nameof(CustomLiteNetLib4MirrorTransport.ProcessConnectionRequest))]
internal static class PreAuthenticationPatch
{
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
            if (!DeviceIdentity.IsValidUserId(userId))
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
            if (PlayerEvents.HasPreAuthenticating)
            {
                PlayerPreAuthenticatingEventArgs e = new(true, userId, ipAddress, request, position);
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

            lock (CustomLiteNetLib4MirrorTransport.UserIdLock)
            {
                CustomLiteNetLib4MirrorTransport.RequestedUserIds[request.RemoteEndPoint.ToString()] = userId;
            }

            request.Accept();
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
}
