using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using CICMessenger.Core.Chat.Transport.Messages;

namespace CICMessenger.Core.Chat.Transport.Host
{
    public static class ChatHostExtensions
    {
        public static void GetSessionInfo(this ChatHost host, Guid sessionId, ICICMessengerEndPoint sender, ICICMessengerEndPoint recipient)
        {
            host.Send(new GiveSessionInfoMessage() { SessionId = sessionId, Sender = new CICMessengerEndPoint(sender), Recipient = new CICMessengerEndPoint(recipient) });
        }

        public static void ReceiveSessionInfo(this ChatHost host, Guid sessionId, ICICMessengerEndPoint sender, ICICMessengerEndPoint recipient, ICICMessengerEndPoint[] participants)
        {
            host.Send(new SessionInfoMessage() { SessionId = sessionId, Sender = new CICMessengerEndPoint(sender), Recipient = new CICMessengerEndPoint(recipient), Participants = participants.Select(p=>new CICMessengerEndPoint(p)).ToList() });
        }

        public static void Buzz(this ChatHost host, Guid sessionId, ICICMessengerEndPoint sender, ICICMessengerEndPoint recipient)
        {
            host.Send(new BuzzMessage() { SessionId = sessionId, Sender = new CICMessengerEndPoint(sender), Recipient = new CICMessengerEndPoint(recipient) });
        }

        public static void UserIsTyping(this ChatHost host, Guid sessionId, ICICMessengerEndPoint sender, ICICMessengerEndPoint recipient)
        {
            host.Send(new UserTypingMessage() { SessionId = sessionId, Sender = new CICMessengerEndPoint(sender), Recipient = new CICMessengerEndPoint(recipient) });
        }

        public static void ReceiveMessage(this ChatHost host, Guid messageId, Guid sessionId, ICICMessengerEndPoint sender, ICICMessengerEndPoint recipient, string fontName, int fontSize, Color color, FontStyle fontStyle, string message)
        {
            host.Send(new TextMessage() 
            { 
                Id = messageId,
                SessionId = sessionId, 
                Sender = new CICMessengerEndPoint(sender), 
                Recipient = new CICMessengerEndPoint(recipient), 
                FontName = fontName, 
                FontSize = fontSize, 
                Color = color, 
                FontStyle = fontStyle, 
                Message = message 
            });
        }

        public static void UpdateMessage(this ChatHost host, Guid messageId, Guid sessionId, ICICMessengerEndPoint sender, ICICMessengerEndPoint recipient, string message)
        {
            host.Send(new UpdateTextMessage()
            {
                Id = messageId,
                SessionId = sessionId,
                Sender = new CICMessengerEndPoint(sender),
                Recipient = new CICMessengerEndPoint(recipient),
                Message = message
            });
        }

        public static void ReceiveChatInvite(this ChatHost host, Guid sessionId, ICICMessengerEndPoint sender, ICICMessengerEndPoint recipient, IEnumerable<ICICMessengerEndPoint> participants)
        {
            host.Send(new ChatInviteMessage() { SessionId = sessionId, Sender = new CICMessengerEndPoint(sender), Recipient = new CICMessengerEndPoint(recipient), Participants = participants.Select(p=>new CICMessengerEndPoint(p)).ToList() });
        }

        public static void JoinChat(this ChatHost host, Guid sessionId, ICICMessengerEndPoint sender, ICICMessengerEndPoint recipient)
        {
            host.Send(new ChatJoinMessage() { SessionId = sessionId, Sender = new CICMessengerEndPoint(sender), Recipient = new CICMessengerEndPoint(recipient) });
        }

        public static void LeaveChat(this ChatHost host, Guid sessionId, ICICMessengerEndPoint sender, ICICMessengerEndPoint recipient)
        {
            host.Send(new ChatLeaveMessage() { SessionId = sessionId, Sender = new CICMessengerEndPoint(sender), Recipient = new CICMessengerEndPoint(recipient) });
        }

        public static void ReceiveActivityInvite(this ChatHost host, Guid sessionId, ICICMessengerEndPoint sender, ICICMessengerEndPoint recipient, Guid activityId, Guid activitySessionId, IEnumerable<KeyValuePair<string, string>> metadata)
        {
            host.Send(new ActivityInviteMessage() { SessionId = sessionId, Sender = new CICMessengerEndPoint(sender), Recipient = new CICMessengerEndPoint(recipient), ActivityId = activityId, ActivitySessionId = activitySessionId, Metadata = metadata.ToDictionary(kv => kv.Key, kv => kv.Value) });
        }

        public static void ReceiveActivityData(this ChatHost host, Guid activitySessionId, ICICMessengerEndPoint sender, ICICMessengerEndPoint recipient, byte[] chunk)
        {
            host.Send(new ActivityDataMessage() { SessionId = activitySessionId, Sender = new CICMessengerEndPoint(sender), Recipient = new CICMessengerEndPoint(recipient), Data = chunk });
        }

        public static void AcceptActivityInvite(this ChatHost host, Guid activitySessionId, ICICMessengerEndPoint sender, ICICMessengerEndPoint recipient)
        {
            host.Send(new ActivityInviteAcceptMessage() { SessionId = activitySessionId, Sender = new CICMessengerEndPoint(sender), Recipient = new CICMessengerEndPoint(recipient) });
        }

        public static void CancelActivitySession(this ChatHost host, Guid activitySessionId, ICICMessengerEndPoint sender, ICICMessengerEndPoint recipient)
        {
            host.Send(new ActivityCancelMessage() { SessionId = activitySessionId, Sender = new CICMessengerEndPoint(sender), Recipient = new CICMessengerEndPoint(recipient) });
        }
    }
}
