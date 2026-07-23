using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CICMessenger.Core.Chat.Activity;

namespace CICMessenger.Client.Activities
{
    public interface IVoiceChatHandler: IActivityHandler
    {
        bool IsMuted { get; set; }
        float Volume { get; set; }
    }
}
