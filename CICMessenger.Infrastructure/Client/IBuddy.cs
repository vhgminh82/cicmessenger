using System;
namespace CICMessenger.Client
{
    public interface IBuddy
    {
        string Id { get; }
        string DisplayName { get; }
        DateTime LastUpdated { get; }
        CICMessenger.Core.Presence.UserStatus Status { get; }
        event EventHandler Offline;
        event EventHandler Online;
        CICMessenger.Core.Presence.IBuddyProperties Properties { get; }
        event System.ComponentModel.PropertyChangedEventHandler PropertyChanged;
    }
}
