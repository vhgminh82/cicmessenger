using System;
using System.Collections.Generic;
using System.Linq;
using CICMessenger.Core.Presence;

namespace CICMessenger.Client
{    
    public interface IChatClient: IDisposable
    {
        event EventHandler<ChatStartedEventArgs> ChatStarted;
        event EventHandler<BuddyOnlineEventArgs> BuddyOnline;
        event EventHandler<BuddyEventArgs> BuddyOffline;
        event EventHandler<BuddyEventArgs> BuddyUpdated;
        event EventHandler LoggedIn;
        event EventHandler LoggedOut;

        ISelfBuddy CurrentUser {get; }
        IEnumerable<IBuddy> Buddies { get; }
        bool IsLoggedIn { get; }
        bool EnableLogging { get; set; }

        IChat StartChat(IBuddy buddy);
        IChat? StartBroadcastChat();

        /// <summary>
        /// Starts a chat fanned out to exactly the given (online) buddies — used for group
        /// rooms, which target a fixed member list rather than everyone currently online.
        /// Returns null if none of them are online.
        /// </summary>
        IChat? StartChat(IEnumerable<IBuddy> buddies);
        void Login(LoginOptions options);
        void Logout();
        bool RemoveBuddy(IBuddy buddy);
    }
}
