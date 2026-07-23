using System;
using System.Collections.Generic;
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
using Squiggle.Client.Activities;
using Squiggle.Core.Chat.Activity;
using Squiggle.FileTransfer;
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
        LoadPreviousMessages(buddy);

        if (_chatSession != null)
            SetupChatSession();
    }

    /// <summary>
    /// Repopulates the window with the recent conversation from the history database, so
    /// reopening a chat (or restarting the app) doesn't present an empty window.
    /// </summary>
    private void LoadPreviousMessages(IBuddy buddy)
    {
        var history = App.Services.GetService<Squiggle.History.HistoryManager>();
        var chatClient = App.Services.GetService<IChatClient>();
        if (history == null)
            return;

        try
        {
            var selfId = chatClient?.CurrentUser.Id;
            var youLabel = FindTranslation("Global_You", "You");

            foreach (var evnt in history.GetRecentMessagesWithContact(buddy.Id))
            {
                bool isOwn = selfId != null && evnt.SenderId == selfId;
                _messages.Add(new ChatMessage
                {
                    SenderName = isOwn ? youLabel : evnt.SenderName,
                    Text = evnt.Data,
                    Timestamp = evnt.Stamp.ToLocalTime().ToString("dd/MM HH:mm"),
                    Background = new SolidColorBrush(Color.Parse(isOwn ? "#DCF8C6" : "#E3F2FD")),
                    IsOwn = isOwn
                });
            }

            if (_messages.Count > 0)
                Avalonia.Threading.Dispatcher.UIThread.Post(ScrollToBottom,
                    Avalonia.Threading.DispatcherPriority.Loaded);
        }
        catch
        {
            // History is a convenience — never block opening the chat window over it
        }
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

        // Only detach — do not Leave() the old session. The peer may still be using it,
        // and tearing it down mid-conversation breaks in-flight transfers.
        DetachChatSession();
        _chatSession = chat;
        SetupChatSession();
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

        // Tell the user the message is parked rather than lost when the peer is away
        if (_buddy != null && !_buddy.IsOnline())
        {
            AddSystemMessage(string.Format(
                FindTranslation("ChatWindow_QueuedForOffline",
                    "{0} is offline. The message will be sent when they come back online."),
                _buddy.DisplayName));
        }

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
            SendFile(files[0].Path.LocalPath);
    }

    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };
    private static readonly string[] VideoExtensions = { ".mp4", ".avi", ".mov", ".mkv", ".webm", ".wmv" };

    static bool IsImage(string path) => ImageExtensions.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant());
    static bool IsVideo(string path) => VideoExtensions.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant());

    /// <summary>
    /// Sends a file to the peer over the activity channel and shows it in our own
    /// transcript (with an inline preview for images and videos).
    /// </summary>
    public void SendFile(string filePath)
    {
        var fileName = System.IO.Path.GetFileName(filePath);

        if (_chatSession == null)
        {
            AddSystemMessage(FindTranslation("ChatWindow_FileTransferNoSession", "Chưa có phiên trò chuyện để gửi file."));
            return;
        }

        if (_chatSession.IsGroupChat)
        {
            AddSystemMessage(FindTranslation("ChatWindow_FileTransferNotAllowedInGroup",
                "Không thể truyền file trong cuộc trò chuyện nhóm."));
            return;
        }

        if (_buddy != null && !_buddy.IsOnline())
        {
            AddSystemMessage(string.Format(
                FindTranslation("ChatWindow_FileTransferPeerOffline",
                    "{0} đang ngoại tuyến nên chưa gửi được file."),
                _buddy.DisplayName));
            return;
        }

        try
        {
            var info = new System.IO.FileInfo(filePath);
            var content = System.IO.File.OpenRead(filePath);

            var executor = _chatSession.CreateActivity(SquiggleActivities.FileTransfer);
            var handler = new FileTransferActivity().CreateInvite(executor, new Dictionary<string, object>
            {
                ["content"] = content,
                ["name"] = info.Name,
                ["size"] = info.Length
            });

            AddOutgoingFileMessage(filePath, fileName);
            AttachTransferFeedback(handler, fileName, sending: true, savedPath: null, disposeOnFinish: content);

            Serilog.Log.Information("Sending file '{Name}' ({Size} bytes) to {Buddy}",
                info.Name, info.Length, _buddy?.DisplayName);
            handler.Start();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Sending file {Name} failed", fileName);
            AddSystemMessage($"{FindTranslation("ChatWindow_CouldNotReadFile", "Không đọc được file")} {fileName}: {ex.Message}");
        }
    }

    private void AddOutgoingFileMessage(string filePath, string fileName)
    {
        var kind = IsImage(filePath) ? ChatMessageKind.Image
                 : IsVideo(filePath) ? ChatMessageKind.Video
                 : ChatMessageKind.Text;

        Avalonia.Media.Imaging.Bitmap? bitmap = null;
        if (kind == ChatMessageKind.Image)
            try { bitmap = new Avalonia.Media.Imaging.Bitmap(filePath); } catch { kind = ChatMessageKind.Text; }

        _messages.Add(new ChatMessage
        {
            SenderName = FindTranslation("Global_You", "You"),
            Text = fileName,
            Timestamp = DateTime.Now.ToString("HH:mm"),
            Background = new SolidColorBrush(Color.Parse("#DCF8C6")),
            Kind = kind,
            FilePath = filePath,
            ImageSource = bitmap,
            IsOwn = true
        });
        ScrollToBottom();
    }

    /// <summary>
    /// Reports transfer progress and outcome as system messages, and shows the received
    /// file inline once it has fully arrived.
    /// </summary>
    private void AttachTransferFeedback(IActivityHandler handler, string fileName, bool sending,
                                        string? savedPath, System.IO.Stream? disposeOnFinish)
    {
        handler.TransferCompleted += (_, _) =>
        {
            if (sending)
            {
                AddSystemMessage(string.Format(
                    FindTranslation("ChatWindow_FileSent", "Đã gửi file: {0}"), fileName));
            }
            else if (savedPath != null)
            {
                AddSystemMessage(string.Format(
                    FindTranslation("ChatWindow_FileReceived", "Đã nhận file: {0}"), savedPath));
                Avalonia.Threading.Dispatcher.UIThread.Post(() => AddIncomingFileMessage(savedPath, fileName));
            }
        };

        handler.TransferCancelled += (_, _) => AddSystemMessage(string.Format(
            FindTranslation("ChatWindow_FileCancelled", "Đã hủy truyền file: {0}"), fileName));

        handler.Error += (_, args) => AddSystemMessage(string.Format(
            FindTranslation("ChatWindow_FileFailed", "Lỗi truyền file {0}: {1}"), fileName, args.GetException().Message));

        handler.TransferFinished += (_, _) => disposeOnFinish?.Dispose();
    }

    public void ShowReceivedFile(string savedPath, string fileName) => AddIncomingFileMessage(savedPath, fileName);

    public void ShowSystemNotice(string text) => AddSystemMessage(text);

    private void AddIncomingFileMessage(string savedPath, string fileName)
    {
        var kind = IsImage(savedPath) ? ChatMessageKind.Image
                 : IsVideo(savedPath) ? ChatMessageKind.Video
                 : ChatMessageKind.Text;

        Avalonia.Media.Imaging.Bitmap? bitmap = null;
        if (kind == ChatMessageKind.Image)
            try { bitmap = new Avalonia.Media.Imaging.Bitmap(savedPath); } catch { kind = ChatMessageKind.Text; }

        if (kind == ChatMessageKind.Text)
            return; // plain files already announced via the system message

        _messages.Add(new ChatMessage
        {
            SenderName = _buddy?.DisplayName ?? "",
            Text = fileName,
            Timestamp = DateTime.Now.ToString("HH:mm"),
            Background = new SolidColorBrush(Color.Parse("#E3F2FD")),
            Kind = kind,
            FilePath = savedPath,
            ImageSource = bitmap,
            IsOwn = false
        });
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

    /// <summary>
    /// Hides this window, lets the user rubber-band a region of the desktop, then sends
    /// the cropped image as a PNG.
    /// </summary>
    private async void Screenshot_Click(object? sender, RoutedEventArgs e)
    {
        try
        {
            var shot = await ScreenRegionPicker.CaptureRegionAsync(this);
            if (shot == null)
                return;

            var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
                $"CICMessenger-screenshot-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            shot.Save(path);
            SendFile(path);
        }
        catch (Exception ex)
        {
            // async void: an escaping exception would tear down the whole app
            AddSystemMessage($"{FindTranslation("ChatWindow_ScreenshotFailed", "Không chụp được màn hình.")} {ex.Message}");
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
