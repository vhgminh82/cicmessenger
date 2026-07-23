using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using CICMessenger.Client;
using CICMessenger.UI.Windows;

namespace CICMessenger.UI.Services;

/// <summary>
/// Tracks open chat windows so re-opening a conversation focuses the existing window
/// instead of spawning a duplicate popup. One-to-one chats are keyed by buddy id;
/// group rooms are keyed by their own session id so they never collide with the
/// one-to-one window of any member.
/// </summary>
public class ChatWindowManager
{
    private readonly Dictionary<string, ChatWindow> _openWindows = new();

    /// <summary>
    /// Focuses the buddy's existing one-to-one window, or creates the chat session via
    /// <paramref name="chatFactory"/> only when a new window is actually needed —
    /// avoids spawning a redundant session each time the user re-opens a chat.
    /// </summary>
    public ChatWindow OpenOrFocus(IBuddy buddy, Func<IChat> chatFactory)
    {
        if (TryFocus(buddy.Id, out var existing))
            return existing;

        return Open(buddy.Id, buddy, chatFactory());
    }

    /// <summary>
    /// Opens (or focuses) the window for an incoming one-to-one chat session.
    /// </summary>
    public ChatWindow OpenOrFocus(IBuddy buddy, IChat chat)
    {
        if (TryFocus(buddy.Id, out var existing))
        {
            existing.SetChatSession(chat);
            return existing;
        }

        return Open(buddy.Id, buddy, chat);
    }

    /// <summary>
    /// Opens a window for a newly created group room. Each room gets its own key so it
    /// never collides with (or gets swallowed by) a member's one-to-one conversation.
    /// </summary>
    public ChatWindow OpenGroupRoom(IBuddy primaryBuddy, IChat chat)
    {
        return Open($"room:{Guid.NewGuid()}", primaryBuddy, chat);
    }

    private bool TryFocus(string key, out ChatWindow window)
    {
        if (_openWindows.TryGetValue(key, out var existing))
        {
            existing.Show();
            existing.WindowState = Avalonia.Controls.WindowState.Normal;
            existing.Activate();
            window = existing;
            return true;
        }

        window = null!;
        return false;
    }

    private ChatWindow Open(string key, IBuddy buddy, IChat chat)
    {
        // Locally started chats never raise ChatStarted, so hook them here too or a file
        // the peer sends back on this session would go unanswered.
        App.Services.GetRequiredService<FileTransferCoordinator>().Attach(chat);

        var window = new ChatWindow(buddy, chat);
        _openWindows[key] = window;
        window.Closed += (_, _) => _openWindows.Remove(key);
        window.Show();
        window.Activate();
        return window;
    }
}
