using System;

namespace CICMessenger.Client
{
    /// <summary>
    /// Raised once messages that were queued while a buddy was offline have been sent.
    /// </summary>
    public class OfflineMessagesDeliveredEventArgs : EventArgs
    {
        public IBuddy Buddy { get; set; } = null!;
        public int Count { get; set; }
    }
}
