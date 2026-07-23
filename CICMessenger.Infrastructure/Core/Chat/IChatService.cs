using System;
using System.Collections;
using System.Collections.Generic;
using System.Net;

namespace CICMessenger.Core.Chat
{
    public class ChatStartedEventArgs: EventArgs
    {
        public IChatSession Session {get; set; } = null!;
    }

    public interface IChatService
    {
        void Start();
        void Stop();
        IChatSession CreateSession(ICICMessengerEndPoint endpoint);
        event EventHandler<ChatStartedEventArgs> ChatStarted;
    }
}
