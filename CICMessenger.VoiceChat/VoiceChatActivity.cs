using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CICMessenger.Client.Activities;
using CICMessenger.Core.Chat.Activity;
using CICMessenger.Plugins;

namespace CICMessenger.VoiceChat
{
    public class VoiceChatActivity : IActivity
    {
        public Guid Id => CICMessengerActivities.VoiceChat;

        public string Title => "Voice Chat";

        public IActivityHandler FromInvite(IActivityExecutor executor, IDictionary<string, string> metadata)
        {
            return new VoiceChatHandler(executor);
        }

        public IActivityHandler CreateInvite(IActivityExecutor executor, IDictionary<string, object> args)
        {
            return new VoiceChatHandler(executor);
        }

        public Task<IDictionary<string, object>> LaunchInviteUI(ICICMessengerContext context, IChatWindow window)
        {
            throw new NotImplementedException();
        }
    }
}
