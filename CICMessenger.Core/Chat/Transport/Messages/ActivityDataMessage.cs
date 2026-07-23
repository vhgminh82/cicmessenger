using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CICMessenger.Core.Chat.Transport.Messages
{
    class ActivityDataMessage : Message
    {
        public byte[] Data { get; set; } = null!;
    }
}
