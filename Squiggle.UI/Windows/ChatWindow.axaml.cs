using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using Squiggle.Client;
using Squiggle.UI.Services;

namespace Squiggle.UI.Windows;

public enum ChatMessageKind
{
    Text,
    Image,
    Video
}

public class ChatMessage
{
    public string SenderName { get; init; } = "";
    public string Text { get; init; } = "";
    public string Timestamp { get; init; } = "";
    public IBrush Background { get; init; } = Brushes.Transparent;
    public ChatMessageKind Kind { get; init; } = ChatMessageKind.Text;
    public string? FilePath { get; init; }
    public Avalonia.Media.Imaging.Bitmap? ImageSource { get; init; }

    /// <summary>True for messages sent by the local user (rendered right-aligned).</summary>
    public bool IsOwn { get; init; }

    /// <summary>System notices span the full width; user messages hug their side.</summary>
    public bool IsSystem { get; init; }

    public bool IsText => Kind == ChatMessageKind.Text;
    public bool IsImage => Kind == ChatMessageKind.Image;
    public bool IsVideo => Kind == ChatMessageKind.Video;

    public Avalonia.Layout.HorizontalAlignment BubbleAlignment => IsSystem
        ? Avalonia.Layout.HorizontalAlignment.Center
        : IsOwn
            ? Avalonia.Layout.HorizontalAlignment.Right
            : Avalonia.Layout.HorizontalAlignment.Left;
}

public partial class ChatWindow : Window
{
    private readonly IBuddy? _buddy;
    private IChat? _chatSession;
    private readonly ObservableCollection<ChatMessage> _messages = new();

    private string ConversationName => _buddy?.DisplayName ?? Title ?? "chat";

    public ChatWindow()
    {
        InitializeComponent();
        _buddy = null;

        // A TextBox with AcceptsReturn marks Enter as handled in its own class handler to
        // insert a newline, so a normal (bubbling) KeyDown handler never sees it. Hook the
        // tunnel route instead so Enter reaches us first and can send the message.
        txtMessage.AddHandler(KeyDownEvent, TxtMessage_KeyDown, RoutingStrategies.Tunnel);
    }

    public ChatWindow(IBuddy buddy, IChat? chatSession = null) : this()
    {
        _buddy = buddy;
        _chatSession = chatSession;
        Title = $"Trò chuyện - {buddy.DisplayName}";

        messagesControl.ItemsSource = _messages;

        if (_chatSession != null)
            SetupChatSession();
    }

    /// <summary>
    /// Creates a window for a broadcast conversation, which targets everyone online
    /// rather than one specific contact.
    /// </summary>
    public ChatWindow(IChat broadcastChat, string title) : this()
    {
        _chatSession = broadcastChat;
        Title = title;

        messagesControl.ItemsSource = _messages;
        SetupChatSession();
    }

    private void SetupChatSession()
    {
        if (_chatSession == null)
            return;

        _chatSession.MessageReceived += ChatSession_MessageReceived;
        _chatSession.BuddyTyping += ChatSession_BuddyTyping;
        _chatSession.BuddyJoined += ChatSession_BuddyJoined;
        _chatSession.BuddyLeft += ChatSession_BuddyLeft;
        _chatSession.MessageFailed += ChatSession_MessageFailed;
    }

    private void DetachChatSession()
    {
        if (_chatSession == null)
            return;

        _chatSession.MessageReceived -= ChatSession_MessageReceived;
        _chatSession.BuddyTyping -= ChatSession_BuddyTyping;
        _chatSession.BuddyJoined -= ChatSession_BuddyJoined;
        _chatSession.BuddyLeft -= ChatSession_BuddyLeft;
        _chatSession.MessageFailed -= ChatSession_MessageFailed;
    }

    public void SetChatSession(IChat chat)
    {
        if (ReferenceEquals(_chatSession, chat))
            return;

        var previous = _chatSession;
        DetachChatSession();
        _chatSession = chat;
        SetupChatSession();

        // Leave the session we're replacing, otherwise it lingers on both peers
        if (previous != null)
            Squiggle.Utilities.ExceptionMonster.EatTheException(previous.Leave, "leaving replaced chat session.");
    }

    private void ChatSession_MessageReceived(object? sender, ChatMessageReceivedEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _messages.Add(new ChatMessage
            {
                SenderName = e.Sender.DisplayName,
                Text = e.Message,
                Timestamp = DateTime.Now.ToString("HH:mm"),
                Background = new SolidColorBrush(Color.Parse("#E3F2FD")),
                IsOwn = false
            });
            ScrollToBottom();
            typingIndicator.IsVisible = false;

            // Show notification if window is not active
            if (!IsActive)
            {
                var notificationService = App.Services.GetService<INotificationService>();
                notificationService?.ShowMessageNotification(
                    e.Sender.DisplayName,
                    e.Message.Length > 50 ? e.Message[..50] + "..." : e.Message);
            }
        });
    }

    private void ChatSession_BuddyTyping(object? sender, BuddyEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            typingIndicator.Text = $"{e.Buddy.DisplayName} {FindTranslation("ChatWindow_IsTyping", "is typing...")}";
            typingIndicator.IsVisible = true;
        });
    }

    private void ChatSession_BuddyJoined(object? sender, BuddyEventArgs e)
    {
        AddSystemMessage($"{e.Buddy.DisplayName} {FindTranslation("ChatWindow_HasJoinedConversation", "has joined the conversation.")}");
    }

    private void ChatSession_BuddyLeft(object? sender, BuddyEventArgs e)
    {
        AddSystemMessage($"{e.Buddy.DisplayName} {FindTranslation("ChatWindow_HasLeftConversation", "has left the conversation.")}");
    }

    private void ChatSession_MessageFailed(object? sender, MessageFailedEventArgs e)
    {
        AddSystemMessage($"{FindTranslation("ChatWindow_MessageCouldNotBeDelivered", "Message could not be delivered:")} {e.Message}");
    }

    private void SendButton_Click(object? sender, RoutedEventArgs e)
    {
        SendMessage();
    }

    private void TxtMessage_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;

            if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
                InsertNewLine();
            else
                SendMessage();

            return;
        }

        _chatSession?.NotifyTyping();
    }

    private void InsertNewLine()
    {
        var text = txtMessage.Text ?? "";
        var caret = Math.Clamp(txtMessage.CaretIndex, 0, text.Length);

        // Replace the selection (if any) with the newline, mirroring normal typing
        var selectionStart = Math.Clamp(Math.Min(txtMessage.SelectionStart, txtMessage.SelectionEnd), 0, text.Length);
        var selectionEnd = Math.Clamp(Math.Max(txtMessage.SelectionStart, txtMessage.SelectionEnd), 0, text.Length);

        if (selectionEnd > selectionStart)
        {
            text = text.Remove(selectionStart, selectionEnd - selectionStart);
            caret = selectionStart;
        }

        txtMessage.Text = text.Insert(caret, Environment.NewLine);
        txtMessage.CaretIndex = caret + Environment.NewLine.Length;
    }

    private void SendMessage()
    {
        string message = txtMessage.Text?.Trim() ?? "";
        if (string.IsNullOrEmpty(message))
            return;

        _messages.Add(new ChatMessage
        {
            SenderName = FindTranslation("Global_You", "You"),
            Text = message,
            Timestamp = DateTime.Now.ToString("HH:mm"),
            Background = new SolidColorBrush(Color.Parse("#DCF8C6")),
            IsOwn = true
        });

        _chatSession?.SendMessage(
            Guid.NewGuid(),
            "Segoe UI",
            12,
            System.Drawing.Color.Black,
            System.Drawing.FontStyle.Regular,
            message);

        txtMessage.Text = "";
        ScrollToBottom();
    }

    private void AddSystemMessage(string text)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _messages.Add(new ChatMessage
            {
                SenderName = "",
                Text = text,
                Timestamp = DateTime.Now.ToString("HH:mm"),
                Background = new SolidColorBrush(Color.Parse("#F5F5F5")),
                IsSystem = true
            });
            ScrollToBottom();
        });
    }

    private void ScrollToBottom()
    {
        messageScroller.Offset = new Vector(0, messageScroller.Extent.Height);
    }

    private async void SendFile_Click(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Gửi file",
            AllowMultiple = false
        });

        if (files.Count > 0)
            AddLocalFileMessage(files[0].Path.LocalPath);
    }

    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };
    private static readonly string[] VideoExtensions = { ".mp4", ".avi", ".mov", ".mkv", ".webm", ".wmv" };

    /// <summary>
    /// Adds a locally-picked file to the conversation, rendering an inline preview for
    /// images/videos. Note: P2P file transfer isn't wired up yet, so this only previews
    /// the file in the sender's own window — it is not actually sent to the peer.
    /// </summary>
    public void AddLocalFileMessage(string filePath)
    {
        var fileName = System.IO.Path.GetFileName(filePath);
        var ext = System.IO.Path.GetExtension(filePath).ToLowerInvariant();

        if (ImageExtensions.Contains(ext))
        {
            Avalonia.Media.Imaging.Bitmap? bitmap = null;
            try
            {
                bitmap = new Avalonia.Media.Imaging.Bitmap(filePath);
            }
            catch
            {
                AddSystemMessage($"{FindTranslation("ChatWindow_CouldNotReadFile", "Could not read file")} {fileName}");
                return;
            }

            _messages.Add(new ChatMessage
            {
                SenderName = FindTranslation("Global_You", "You"),
                Text = fileName,
                Timestamp = DateTime.Now.ToString("HH:mm"),
                Background = new SolidColorBrush(Color.Parse("#DCF8C6")),
                Kind = ChatMessageKind.Image,
                FilePath = filePath,
                ImageSource = bitmap,
                IsOwn = true
            });
        }
        else if (VideoExtensions.Contains(ext))
        {
            _messages.Add(new ChatMessage
            {
                SenderName = FindTranslation("Global_You", "You"),
                Text = fileName,
                Timestamp = DateTime.Now.ToString("HH:mm"),
                Background = new SolidColorBrush(Color.Parse("#DCF8C6")),
                Kind = ChatMessageKind.Video,
                FilePath = filePath,
                IsOwn = true
            });
        }
        else
        {
            AddSystemMessage($"Chưa hỗ trợ gửi loại file này: {fileName}");
            return;
        }

        ScrollToBottom();
    }

    private void PlayVideo_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: string filePath })
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(filePath) { UseShellExecute = true });
            }
            catch
            {
                AddSystemMessage($"{FindTranslation("ChatWindow_CouldNotReadFile", "Could not read file")} {System.IO.Path.GetFileName(filePath)}");
            }
        }
    }

    private async void Emoticon_Click(object? sender, RoutedEventArgs e)
    {
        var picker = new EmoticonPicker();
        var result = await picker.ShowDialog<string?>(this);
        if (!string.IsNullOrEmpty(result))
        {
            var currentText = txtMessage.Text ?? "";
            var caretIndex = txtMessage.CaretIndex;
            txtMessage.Text = currentText.Insert(caretIndex, result);
            txtMessage.CaretIndex = caretIndex + result.Length;
            txtMessage.Focus();
        }
    }

    private async void Save_Click(object? sender, RoutedEventArgs e)
    {
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Lưu cuộc trò chuyện",
            DefaultExtension = "txt",
            SuggestedFileName = $"Chat-{ConversationName}-{DateTime.Now:yyyy-MM-dd}",
            FileTypeChoices = new[]
            {
                new FilePickerFileType("Tệp văn bản") { Patterns = new[] { "*.txt" } }
            }
        });

        if (file != null)
        {
            var lines = _messages.Select(m =>
                string.IsNullOrEmpty(m.SenderName)
                    ? $"[{m.Timestamp}] {m.Text}"
                    : $"[{m.Timestamp}] {m.SenderName}: {m.Text}");
            await System.IO.File.WriteAllLinesAsync(file.Path.LocalPath, lines);
        }
    }

    private async void Copy_Click(object? sender, RoutedEventArgs e)
    {
        var clipboard = App.Services.GetRequiredService<IClipboardService>();
        var text = string.Join(Environment.NewLine,
            _messages.Select(m =>
                string.IsNullOrEmpty(m.SenderName)
                    ? $"[{m.Timestamp}] {m.Text}"
                    : $"[{m.Timestamp}] {m.SenderName}: {m.Text}"));
        await clipboard.CopyTextAsync(text);
    }

    private async void SelectAll_Click(object? sender, RoutedEventArgs e)
    {
        // Copy all messages to clipboard (equivalent of select all + copy in a chat context)
        var clipboard = App.Services.GetRequiredService<IClipboardService>();
        var text = string.Join(Environment.NewLine,
            _messages.Select(m =>
                string.IsNullOrEmpty(m.SenderName)
                    ? $"[{m.Timestamp}] {m.Text}"
                    : $"[{m.Timestamp}] {m.SenderName}: {m.Text}"));
        await clipboard.CopyTextAsync(text);
    }

    private void CloseMenu_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }

    protected override void OnClosed(EventArgs e)
    {
        DetachChatSession();
        _chatSession?.Leave();
        base.OnClosed(e);
    }

    private string FindTranslation(string key, string fallback)
    {
        if (this.TryFindResource(key, out var value) && value is string s)
            return s;
        return fallback;
    }
}
