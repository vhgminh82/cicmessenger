using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CICMessenger.Core.Chat.Transport.Messages
{
    class SessionInfoMessage : Message, IMessageHasParticipants
    {
        public List<CICMessengerEndPoint> Participants { get; set; }

        public SessionInfoMessage()
        {
            Participants = new List<CICMessengerEndPoint>();
        }
    }
}
