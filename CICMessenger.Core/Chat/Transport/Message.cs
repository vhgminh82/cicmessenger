using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CICMessenger.Core.Chat.Transport.Messages;

namespace CICMessenger.Core.Chat.Transport
{
    public abstract class Message
    {
        public Guid SessionId { get; set; }
        /// <summary>
        /// Chat endpoint for the sender
        /// </summary>
        public CICMessengerEndPoint Sender { get; set; } = null!;

        /// <summary>
        /// Chat endpoint for the recipient
        /// </summary>
        public CICMessengerEndPoint Recipient { get; set; } = null!;
    }
}
