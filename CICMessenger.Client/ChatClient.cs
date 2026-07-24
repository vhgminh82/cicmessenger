using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using CICMessenger.Core;
using CICMessenger.Core.Chat;
using CICMessenger.Core.Presence;
using CICMessenger.History;
using CICMessenger.Utilities;

namespace CICMessenger.Client
{
    public class ChatClient: IChatClient
    {
        IChatService chatService = null!;
        IPresenceService presenceService = null!;
        CICMessengerEndPoint chatEndPoint = null!;
        BuddyList buddies;
        HistoryManager history;
        readonly ILoggerFactory loggerFactory;

        public event EventHandler<ChatStartedEventArgs> ChatStarted = delegate { };
        public event EventHandler<BuddyOnlineEventArgs> BuddyOnline = delegate { };
        public event EventHandler<BuddyEventArgs> BuddyOffline = delegate { };
        public event EventHandler<BuddyEventArgs> BuddyUpdated = delegate { };
        public event EventHandler LoggedIn = delegate { };
        public event EventHandler LoggedOut = delegate { };

        public ISelfBuddy CurrentUser { get; private set; }

        public IEnumerable<IBuddy> Buddies 
        {
            get { return buddies; }
        }

        bool _isLoggedIn;
        public bool IsLoggedIn
        {
            get { return _isLoggedIn; }
            set
            {
                _isLoggedIn = value;
                if (_isLoggedIn)
                    LoggedIn(this, EventArgs.Empty);
                else
                    LoggedOut(this, EventArgs.Empty);
            }
        }

        public bool EnableLogging { get; set; }

        readonly OfflineMessageStore? offlineStore;

        /// <summary>
        /// Raised after messages queued while a buddy was offline have been delivered.
        /// </summary>
        public event EventHandler<OfflineMessagesDeliveredEventArgs> OfflineMessagesDelivered = delegate { };

        public ChatClient(string clientId, HistoryManager history, ILoggerFactory? loggerFactory = null, OfflineMessageStore? offlineStore = null)
        {
            this.history = history;
            this.loggerFactory = loggerFactory ?? NullLoggerFactory.Instance;
            this.offlineStore = offlineStore;
            buddies = new BuddyList();
            CurrentUser = new SelfBuddy(this, clientId, String.Empty, UserStatus.Offline, new BuddyProperties());
        }

        public bool HasPendingMessages(string buddyId) => offlineStore?.HasPending(buddyId) ?? false;

        public int PendingMessageCount(string buddyId) => offlineStore?.PendingCount(buddyId) ?? 0;

        Chat CreateChat(IChatSession session, IEnumerable<IBuddy> buddyList)
        {
            var chat = new Chat(session, CurrentUser, buddyList, id => buddies[id], history)
            {
                EnableLogging = EnableLogging
            };

            if (offlineStore != null)
                chat.MessageUndelivered = offlineStore.Enqueue;

            return chat;
        }

        /// <summary>
        /// Sends anything that was queued while this buddy was offline, oldest first.
        /// </summary>
        void FlushOfflineMessages(IBuddy buddy)
        {
            if (offlineStore == null || !offlineStore.HasPending(buddy.Id))
                return;

            var queued = offlineStore.Dequeue(buddy.Id);
            if (queued.Count == 0)
                return;

            var delivered = new List<PendingMessage>();
            var failed = new List<PendingMessage>();

            ExceptionMonster.EatTheException(() =>
            {
                IChatSession session = chatService.CreateSession(new CICMessengerEndPoint(buddy.Id, ((Buddy)buddy).ChatEndPoint));
                foreach (var pending in queued)
                {
                    if (ExceptionMonster.EatTheException(
                            () => session.SendMessage(Guid.NewGuid(), "Segoe UI", 12,
                                                      System.Drawing.Color.Black,
                                                      System.Drawing.FontStyle.Regular,
                                                      pending.Text),
                            "delivering queued offline message"))
                        delivered.Add(pending);
                    else
                        failed.Add(pending);
                }
            }, "flushing offline messages for " + buddy.Id);

            // Anything still undelivered goes back on the queue for the next time
            var undelivered = failed.Concat(queued.Except(delivered).Except(failed)).ToList();
            if (undelivered.Count > 0)
                offlineStore.Requeue(undelivered);

            if (delivered.Count > 0)
                OfflineMessagesDelivered(this, new OfflineMessagesDeliveredEventArgs
                {
                    Buddy = buddy,
                    Count = delivered.Count
                });
        }

        public IChat StartChat(IBuddy buddy)
        {
            if (!IsLoggedIn)
                throw new InvalidOperationException("Not logged in.");

            IChatSession session = chatService.CreateSession(new CICMessengerEndPoint(buddy.Id, ((Buddy)buddy).ChatEndPoint));
            return CreateChat(session, new[] { buddy });
        }

        public IChat? StartChat(IEnumerable<IBuddy> buddies)
        {
            if (!IsLoggedIn)
                throw new InvalidOperationException("Not logged in.");

            var sessions = buddies.Where(b => b.IsOnline())
                                  .Select(b => StartChat(b))
                                  .ToList();

            if (sessions.Count == 0)
                return null;

            var broadcast = new BroadcastChat(sessions);
            broadcast.EnableLogging = EnableLogging;
            return broadcast;
        }

        public void Login(LoginOptions options)
        {
            string username = options.DisplayName.Trim();

            this.chatEndPoint = new CICMessengerEndPoint(CurrentUser.Id, options.ChatEndPoint);
            StartChatService();

            // Some of the users may have gone offline. Lets try to re-discover all the buddies.
            foreach (Buddy buddy in buddies)
                buddy.Status = UserStatus.Offline;

            var presenceOptions = new PresenceServiceOptions()
            {
                ChatEndPoint = chatEndPoint,
                MulticastEndPoint = options.MulticastEndPoint,
                MulticastReceiveEndPoint = options.MulticastReceiveEndPoint,
                PresenceServiceEndPoint = options.PresenceServiceEndPoint,
                KeepAliveTime = options.KeepAliveTime
            };
            StartPresenceService(username, options.UserProperties, presenceOptions);

            var self = (SelfBuddy)CurrentUser;
            self.Update(UserStatus.Online, options.DisplayName, chatEndPoint.Address, options.UserProperties.ToDictionary());
            self.EnableUpdates = true;
            LogStatus(CurrentUser);

            IsLoggedIn = true;
        }        

        /// <summary>
        /// Opens a one-to-one session with every online buddy and wraps them in a single
        /// broadcast chat, so one typed message fans out to everyone on the LAN.
        /// Returns null when nobody is online.
        /// </summary>
        public IChat? StartBroadcastChat()
        {
            if (!IsLoggedIn)
                throw new InvalidOperationException("Not logged in.");

            var sessions = buddies.Where(b => b.IsOnline())
                                  .Select(b => StartChat(b))
                                  .ToList();

            if (sessions.Count == 0)
                return null;

            var broadcast = new BroadcastChat(sessions);
            broadcast.EnableLogging = EnableLogging;
            return broadcast;
        }

        /// <summary>
        /// Removes a buddy from the contact list. Only allowed while the buddy is offline,
        /// so a stale/duplicate entry (e.g. left over from a reinstalled or renamed client)
        /// can be cleaned up without accidentally dropping someone who's still connected.
        /// </summary>
        public bool RemoveBuddy(IBuddy buddy)
        {
            if (buddy.IsOnline())
                return false;

            var existing = buddies[buddy.Id];
            if (existing == null)
                return false;

            return buddies.Remove(existing);
        }

        public void Logout()
        {
            IsLoggedIn = false;
            chatService.Stop();
            presenceService.Logout();

            var self = (SelfBuddy)CurrentUser;
            self.EnableUpdates = false;
            self.Status = UserStatus.Offline;

            LogStatus(CurrentUser);
        }
        
        void Update()
        {
            LogStatus(CurrentUser);
            var properties = CurrentUser.Properties.Clone();
            presenceService.SendUpdate(CurrentUser.DisplayName, properties, CurrentUser.Status);
        }

        void chatService_ChatStarted(object? sender, CICMessenger.Core.Chat.ChatStartedEventArgs e)
        {
            IEnumerable<IBuddy> buddyList = e.Session.RemoteUsers
                                                     .Select(u => buddies[u.ClientID])
                                                     .Where(b => b != null)
                                                     .ToList();
            
            if (buddyList.Any())
            {
                var chat = CreateChat(e.Session, buddyList);
                ChatStarted(this, new ChatStartedEventArgs() { Chat = chat, Buddies = buddyList });
            }
        }

        void presenceService_UserUpdated(object? sender, UserEventArgs e)
        {
            var buddy = buddies[e.User.ID];
            if (buddy != null)
            {
                UserStatus lastStatus = buddy.Status;
                UpdateBuddy(buddy, e.User);

                if (lastStatus != UserStatus.Offline && !buddy.IsOnline)
                    OnBuddyOffline(buddy);
                else if (lastStatus == UserStatus.Offline && buddy.IsOnline)
                    OnBuddyOnline(buddy, false);
                else
                    OnBuddyUpdated(buddy);
            }
        }        

        void presenceService_UserOnline(object? sender, UserOnlineEventArgs e)
        {
            var buddy = buddies[e.User.ID];
            if (buddy == null)
            {
                buddy = new Buddy(e.User.ID, e.User.DisplayName, e.User.Status, e.User.ChatEndPoint, new BuddyProperties(e.User.Properties));
                buddies.Add(buddy);
            }
            else
                UpdateBuddy(buddy, e.User);
            
            OnBuddyOnline(buddy, e.Discovered);
        }        

        void presenceService_UserOffline(object? sender, UserEventArgs e)
        {
            var buddy = buddies[e.User.ID];
            if (buddy != null)
            {
                buddy.Update(e.User.Status, e.User.DisplayName, e.User.ChatEndPoint, e.User.Properties);
                OnBuddyOffline(buddy);
            }
        }

        void OnBuddyUpdated(Buddy buddy)
        {
            LogStatus(buddy);
            BuddyUpdated(this, new BuddyEventArgs( buddy ));
        } 

        void OnBuddyOnline(IBuddy buddy, bool discovered)
        {
            if (!discovered)
                LogStatus(buddy);
            BuddyOnline(this, new BuddyOnlineEventArgs() { Buddy = buddy, Discovered = discovered });

            // Deliver anything typed while they were away. Off the event thread so a slow
            // or failing peer can't stall presence handling.
            if (offlineStore != null && offlineStore.HasPending(buddy.Id))
                System.Threading.Tasks.Task.Run(() => FlushOfflineMessages(buddy));
        }

        void OnBuddyOffline(IBuddy buddy)
        {
            LogStatus(buddy);
            BuddyOffline(this, new BuddyEventArgs( buddy ));
        }

        void UpdateBuddy(IBuddy buddy, IUserInfo user)
        {
            ((Buddy)buddy).Update(user.Status, user.DisplayName, user.ChatEndPoint, user.Properties);
        }

        void LogStatus(IBuddy buddy)
        {
            if (EnableLogging)
                ExceptionMonster.EatTheException(() =>
                {
                    history.AddStatusUpdate(buddy.Id, buddy.DisplayName, (int)buddy.Status);
                }, "logging history.");
        }

        void StartPresenceService(string username, IBuddyProperties properties, PresenceServiceOptions presenceOptions)
        {
            presenceService = new PresenceService(presenceOptions);
            presenceService.UserOffline += presenceService_UserOffline;
            presenceService.UserOnline += presenceService_UserOnline;
            presenceService.UserUpdated += presenceService_UserUpdated;
            presenceService.Login(username, properties);
        }

        void StartChatService()
        {
            chatService = new ChatService(chatEndPoint, loggerFactory);
            chatService.ChatStarted += chatService_ChatStarted;
            chatService.Start();
        }

        #region IDisposable Members

        public void Dispose()
        {
            Logout();
        }

        #endregion

        class SelfBuddy : Buddy, ISelfBuddy
        {
            IChatClient client;

            public bool EnableUpdates { get; set; }

            public SelfBuddy(IChatClient client, string id, string displayName, UserStatus status, IBuddyProperties properties) : base(id, displayName, status, null!, properties)
            {
                this.client = client;
            }

            public new string DisplayName
            {
                get { return base.DisplayName; }
                set
                {
                    base.DisplayName = value;
                    Update();
                }
            }
            
            public new UserStatus Status
            {
                get { return base.Status; }
                set
                {
                    base.Status = value;
                    Update();
                }
            }

            protected override void OnBuddyPropertiesChanged()
            {
                base.OnBuddyPropertiesChanged();
                Update();
            }

            void Update()
            {
                if (EnableUpdates)
                    ((ChatClient)client).Update();
            }
        }
    }
}
