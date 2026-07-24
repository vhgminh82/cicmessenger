using System;
using System.Collections.Generic;
using System.Linq;
using CICMessenger.Client;
using CICMessenger.UI.Models;

namespace CICMessenger.UI.Services;

public class ConversationMessageEventArgs : EventArgs
{
    public string ConversationKey { get; init; } = "";
    public IBuddy Sender { get; init; } = null!;
    public string Message { get; init; } = "";
}

public class ConversationSystemEventArgs : EventArgs
{
    public string ConversationKey { get; init; } = "";
    public string Text { get; init; } = "";
}

public class ConversationTypingEventArgs : EventArgs
{
    public string ConversationKey { get; init; } = "";
    /// <summary>Null hides the typing indicator.</summary>
    public string? BuddyName { get; init; }
}

public class ConversationFileEventArgs : EventArgs
{
    public string ConversationKey { get; init; } = "";
    public IBuddy Sender { get; init; } = null!;
    public string SavedPath { get; init; } = "";
    public string FileName { get; init; } = "";
}

/// <summary>
/// Owns every live chat session (one-to-one or room) independently of which conversation
/// happens to be showing in the single chat pane, so background conversations keep logging
/// history and can still raise notifications while another conversation is open.
/// </summary>
public class ConversationCoordinator
{
    private readonly Dictionary<string, IChat> _chats = new();
    private readonly IChatClient _chatClient;
    private readonly FileTransferCoordinator _fileTransfers;

    public event EventHandler<ConversationMessageEventArgs>? MessageReceived;
    public event EventHandler<ConversationSystemEventArgs>? SystemNotice;
    public event EventHandler<ConversationTypingEventArgs>? TypingChanged;
    public event EventHandler<ConversationFileEventArgs>? FileReceived;

    public ConversationCoordinator(IChatClient chatClient, FileTransferCoordinator fileTransfers)
    {
        _chatClient = chatClient;
        _fileTransfers = fileTransfers;

        // Peer-initiated chats never go through GetOrCreateBuddyChat, so register them here.
        _chatClient.ChatStarted += (_, e) =>
        {
            var buddy = e.Buddies.FirstOrDefault();
            if (buddy != null)
                Register(BuddyKey(buddy.Id), e.Chat);
        };

        _fileTransfers.FileReceived += (_, e) =>
        {
            var key = BuddyKey(e.Buddy.Id);
            if (_chats.TryGetValue(key, out var chat))
                chat.LogFileTransfer(outgoing: false, e.Buddy, e.FileName, e.FilePath);

            FileReceived?.Invoke(this, new ConversationFileEventArgs
            {
                ConversationKey = key,
                Sender = e.Buddy,
                SavedPath = e.FilePath,
                FileName = e.FileName
            });
        };
    }

    public static string BuddyKey(string buddyId) => $"buddy:{buddyId}";
    public static string RoomKey(string roomId) => $"room:{roomId}";
    public static string NewAdHocKey() => $"adhoc:{Guid.NewGuid()}";

    /// <summary>Registers a chat that isn't tied to a buddy or room (e.g. a one-off broadcast), under a caller-supplied key.</summary>
    public void RegisterAdHoc(string key, IChat chat) => Register(key, chat);

    public IChat? TryGet(string key) => _chats.TryGetValue(key, out var chat) ? chat : null;

    public IChat GetOrCreateBuddyChat(IBuddy buddy)
    {
        var key = BuddyKey(buddy.Id);
        if (_chats.TryGetValue(key, out var existing))
            return existing;

        var chat = _chatClient.StartChat(buddy);
        Register(key, chat);
        return chat;
    }

    /// <summary>
    /// Starts (or resumes) the live session for a room by chatting with the first online
    /// member and inviting the rest — the underlying protocol promotes it to a real
    /// multi-party session as invitees join. Returns null if nobody is online.
    /// </summary>
    public IChat? GetOrCreateRoomChat(Room room, List<IBuddy> allMembers)
    {
        var key = RoomKey(room.Id);
        if (_chats.TryGetValue(key, out var existing))
            return existing;

        var online = allMembers.Where(b => b.IsOnline()).ToList();
        if (online.Count == 0)
            return null;

        var chat = _chatClient.StartChat(online[0]);
        foreach (var buddy in online.Skip(1))
            chat.Invite(buddy);

        chat.SetRoomContext(room.Id, room.Name, allMembers);
        Register(key, chat);
        return chat;
    }

    /// <summary>Updates the room name on its live session (if one exists) after a rename.</summary>
    public void RenameRoom(Room room, List<IBuddy> allMembers)
    {
        if (_chats.TryGetValue(RoomKey(room.Id), out var chat))
            chat.SetRoomContext(room.Id, room.Name, allMembers);
    }

    private void Register(string key, IChat chat)
    {
        if (!_chats.TryAdd(key, chat))
            return;

        // Locally started chats never raise ChatClient.ChatStarted, so attach here too or a
        // file the peer sends back on this session would go unanswered.
        _fileTransfers.Attach(chat);

        chat.MessageReceived += (_, e) => MessageReceived?.Invoke(this,
            new ConversationMessageEventArgs { ConversationKey = key, Sender = e.Sender, Message = e.Message });

        chat.BuddyTyping += (_, e) => TypingChanged?.Invoke(this,
            new ConversationTypingEventArgs { ConversationKey = key, BuddyName = e.Buddy.DisplayName });

        chat.BuddyJoined += (_, e) => SystemNotice?.Invoke(this,
            new ConversationSystemEventArgs { ConversationKey = key, Text = $"{e.Buddy.DisplayName} đã tham gia." });

        chat.BuddyLeft += (_, e) => SystemNotice?.Invoke(this,
            new ConversationSystemEventArgs { ConversationKey = key, Text = $"{e.Buddy.DisplayName} đã rời đi." });

        chat.MessageFailed += (_, e) => SystemNotice?.Invoke(this,
            new ConversationSystemEventArgs { ConversationKey = key, Text = $"Không gửi được tin nhắn: {e.Message}" });
    }

    /// <summary>
    /// Drops every cached live session — called on logout. Deliberately does NOT call
    /// chat.Leave() on each one: IChatClient.Logout() tears down every session at once via
    /// chatService.Stop() right after this runs, and racing that wholesale teardown against
    /// N individual async Leave() calls on the same underlying channels was hanging logout.
    /// </summary>
    public void EndAll()
    {
        _chats.Clear();
    }
}
