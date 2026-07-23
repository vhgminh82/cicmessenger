using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CICMessenger.Client
{
    public class PendingMessage
    {
        public string BuddyId { get; set; } = "";
        public string Text { get; set; } = "";
        public DateTime QueuedAt { get; set; }
    }

    /// <summary>
    /// Holds messages typed while the recipient was offline and hands them back once the
    /// recipient reappears. There is no server in the network, so the sender keeps the
    /// message on disk and delivers it itself — meaning delivery also requires the sender
    /// to be running when the recipient comes online.
    /// </summary>
    public class OfflineMessageStore
    {
        readonly string filePath;
        readonly List<PendingMessage> pending = new();
        readonly object gate = new();

        static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

        public OfflineMessageStore(string filePath)
        {
            this.filePath = filePath;
            Load();
        }

        public void Enqueue(string buddyId, string text)
        {
            lock (gate)
            {
                pending.Add(new PendingMessage
                {
                    BuddyId = buddyId,
                    Text = text,
                    QueuedAt = DateTime.Now
                });
                Save();
            }
        }

        /// <summary>
        /// Removes and returns everything queued for a buddy, oldest first.
        /// </summary>
        public IReadOnlyList<PendingMessage> Dequeue(string buddyId)
        {
            lock (gate)
            {
                var taken = pending.Where(m => m.BuddyId == buddyId)
                                   .OrderBy(m => m.QueuedAt)
                                   .ToList();
                if (taken.Count == 0)
                    return Array.Empty<PendingMessage>();

                pending.RemoveAll(m => m.BuddyId == buddyId);
                Save();
                return taken;
            }
        }

        public bool HasPending(string buddyId)
        {
            lock (gate)
                return pending.Any(m => m.BuddyId == buddyId);
        }

        public int PendingCount(string buddyId)
        {
            lock (gate)
                return pending.Count(m => m.BuddyId == buddyId);
        }

        /// <summary>Puts messages back after a failed delivery attempt.</summary>
        public void Requeue(IEnumerable<PendingMessage> messages)
        {
            lock (gate)
            {
                pending.AddRange(messages);
                Save();
            }
        }

        [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
            Justification = "PendingMessage is a simple POCO that is statically referenced")]
        void Load()
        {
            try
            {
                if (!File.Exists(filePath))
                    return;

                var json = File.ReadAllText(filePath);
                var loaded = JsonSerializer.Deserialize<List<PendingMessage>>(json, JsonOptions);
                if (loaded != null)
                    pending.AddRange(loaded);
            }
            catch
            {
                // A corrupt queue file must not stop the app from starting
            }
        }

        [System.Diagnostics.CodeAnalysis.UnconditionalSuppressMessage("Trimming", "IL2026:RequiresUnreferencedCode",
            Justification = "PendingMessage is a simple POCO that is statically referenced")]
        void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(filePath);
                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                File.WriteAllText(filePath, JsonSerializer.Serialize(pending, JsonOptions));
            }
            catch
            {
                // Best effort — keep the in-memory queue even if disk write fails
            }
        }
    }
}
