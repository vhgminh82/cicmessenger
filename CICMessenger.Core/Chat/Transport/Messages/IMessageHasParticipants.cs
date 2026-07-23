using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CICMessenger.Core.Chat.Transport.Messages
{
    public interface IMessageHasParticipants
    {
        List<CICMessengerEndPoint> Participants { get; set; }
    }
}
