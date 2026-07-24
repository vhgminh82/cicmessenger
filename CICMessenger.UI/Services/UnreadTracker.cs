using System.Collections.Generic;

namespace CICMessenger.UI.Services;

/// <summary>
/// Tracks unread-message counts per conversation (keyed by buddy or room id) for the contact
/// list badge. Purely a UI concern — deliberately not stored on <see cref="Client.IBuddy"/> or
/// <see cref="Models.Room"/>, which are shared with the network/persistence layers.
/// </summary>
public static class UnreadTracker
{
    private static readonly Dictionary<string, int> _counts = new();

    public static int GetCount(string entityId) => _counts.TryGetValue(entityId, out var count) ? count : 0;

    public static void Increment(string entityId) => _counts[entityId] = GetCount(entityId) + 1;

    /// <summary>Returns true if the entity actually had an unread count to clear.</summary>
    public static bool Clear(string entityId) => _counts.Remove(entityId);
}
