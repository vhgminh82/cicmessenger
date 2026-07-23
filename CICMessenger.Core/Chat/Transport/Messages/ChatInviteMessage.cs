using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CICMessenger.Core.Chat.Transport.Messages
{
    class ChatInviteMessage : Message, IMessageHasParticipants
    {
        public List<CICMessengerEndPoint> Participants { get; set; }

        public ChatInviteMessage()
        {
            Participants = new List<CICMessengerEndPoint>();
        }
    }
}
