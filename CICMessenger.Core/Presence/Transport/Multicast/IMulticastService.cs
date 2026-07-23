using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CICMessenger.Core.Presence.Transport.Multicast
{
    interface IMulticastService
    {
        event EventHandler<MessageReceivedEventArgs> MessageReceived;
        void SendMessage(Message message);
        void Start();
        void Stop();
    }
}
