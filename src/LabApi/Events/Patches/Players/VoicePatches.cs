using HarmonyLib;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using Mirror;
using PlayerRoles.Voice;
using VoiceChat;
using VoiceChat.Networking;

namespace LabApi.Events.Patches.Players;

// Official: VoiceChat/Networking/VoiceTransceiver.cs ServerReceiveMessage
// Runs the original untouched unless a voice event has subscribers; otherwise replays the Carl Mod body with both events.
[HarmonyPatch(typeof(VoiceTransceiver), nameof(VoiceTransceiver.ServerReceiveMessage))]
internal static class PlayerVoiceMessagePatch
{
    private static bool Prefix(NetworkConnection conn, VoiceMessage msg)
    {
        if (!PlayerEvents.HasSendingVoiceMessage && !PlayerEvents.HasReceivingVoiceMessage)
        {
            return true;
        }

        if (msg.SpeakerNull || msg.Speaker.netId != conn.identity.netId || msg.Speaker.roleManager.CurrentRole is not IVoiceRole voiceRole || !voiceRole.VoiceModule.CheckRateLimit())
        {
            return false;
        }

        VcMuteFlags flags = VoiceChatMutes.GetFlags(msg.Speaker);
        if (flags == VcMuteFlags.GlobalRegular || flags == VcMuteFlags.LocalRegular)
        {
            return false;
        }

        VoiceChatChannel channel = voiceRole.VoiceModule.ValidateSend(msg.Channel);
        if (channel == VoiceChatChannel.None)
        {
            return false;
        }

        voiceRole.VoiceModule.CurrentChannel = channel;

        if (PlayerEvents.HasSendingVoiceMessage)
        {
            PlayerSendingVoiceMessageEventArgs sending = new(ref msg);
            PlayerEvents.OnSendingVoiceMessage(sending);
            if (!sending.IsAllowed)
            {
                return false;
            }

            msg = sending.Message;
        }

        bool hearYourself = VoiceChatMicCapture.HearYourself;
        bool receivingEvent = PlayerEvents.HasReceivingVoiceMessage;
        foreach (ReferenceHub hub in ReferenceHub.AllHubs)
        {
            if (hub.roleManager.CurrentRole is not IVoiceRole receiverRole)
            {
                continue;
            }

            VoiceChatChannel receiveChannel = receiverRole.VoiceModule.ValidateReceive(msg.Speaker, channel);
            if (receiveChannel == VoiceChatChannel.None && hearYourself && hub == msg.Speaker)
            {
                receiveChannel = channel;
            }

            msg.Channel = receiveChannel;
            if (receivingEvent)
            {
                PlayerReceivingVoiceMessageEventArgs receiving = new(hub, ref msg);
                PlayerEvents.OnReceivingVoiceMessage(receiving);
                if (!receiving.IsAllowed)
                {
                    continue;
                }

                msg = receiving.Message;
            }

            if (msg.Channel != VoiceChatChannel.None)
            {
                hub.connectionToClient.Send(msg);
            }
        }

        return false;
    }
}
