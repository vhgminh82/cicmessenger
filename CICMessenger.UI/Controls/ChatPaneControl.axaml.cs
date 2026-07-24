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
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using CICMessenger.Client;
using CICMessenger.Client.Activities;
using CICMessenger.Core.Chat.Activity;
using CICMessenger.FileTransfer;
using CICMessenger.UI.Models;
using CICMessenger.UI.Services;
using CICMessenger.UI.Windows;

namespace CICMessenger.UI.Controls;

public enum ChatMessageKind
{
    Text,
    Image,
    Video,
    File
}

public class ChatMessage
{
    public string SenderName { get; init; } = "";
    public string Text { get; init; } = "";
    public string Timestamp { get; init; } = "";

    /// <summary>When this message was sent/received, used by the auto-delete sweep to age messages out of the visible pane.</summary>
    public DateTime SentAt { get; init; } = DateTime.Now;
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
    public bool IsFile => Kind == ChatMessageKind.File;

    public Avalonia.Layout.HorizontalAlignment BubbleAlignment => IsSystem
        ? Avalonia.Layout.HorizontalAlignment.Center
        : IsOwn
            ? Avalonia.Layout.HorizontalAlignment.Right
            : Avalonia.Layout.HorizontalAlignment.Left;
}

/// <summary>
/// The single embedded chat surface (middle column). Rebinds to whichever conversation
/// (buddy or room) is selected on the left, instead of one popup window per conversation.
/// Live incoming events are routed in by MainWindow via <see cref="ConversationCoordinator"/>
/// only when this pane is currently showing that conversation.
/// </summary>
public partial class ChatPaneControl : UserControl
{
    private IBuddy? _buddy;
    private Room? _room;
    private IChat? _chatSession;
    private readonly ObservableCollection<ChatMessage> _messages = new();

    /// <summary>Conversation key ("buddy:id" / "room:id") of what's currently bound, or null.</summary>
    public string? ConversationKey { get; private set; }

    public event EventHandler<ChatMessage>? AttachmentAdded;

    private string ConversationName => _room?.Name ?? _buddy?.DisplayName ?? "";

    public ChatPaneControl()
    {
        InitializeComponent();
        messagesControl.ItemsSource = _messages;

        // A TextBox with AcceptsReturn marks Enter as handled in its own class handler to
        // insert a newline, so a normal (bubbling) KeyDown handler never sees it. Hook the
        // tunnel route instead so Enter reaches us first and can send the message.
        txtMessage.AddHandler(KeyDownEvent, TxtMessage_KeyDown, RoutingStrategies.Tunnel);

        // Off by default (ChatSettingsViewModel.AutoDeleteMessages) — when on, ages expired
        // messages out of the visible pane. The actual history-DB purge runs separately in
        // AutoDeleteService so conversations that aren't currently open still expire.
        _autoDeleteTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _autoDeleteTimer.Tick += (_, _) => PruneExpiredMessages();
        _autoDeleteTimer.Start();
    }

    private readonly DispatcherTimer _autoDeleteTimer;

    private void PruneExpiredMessages()
    {
        var settings = App.Services.GetService<SettingsService>()?.Load().ChatSettings;
        if (settings == null || !settings.AutoDeleteMessages)
            return;

        var cutoff = DateTime.Now - settings.GetAutoDeleteTimeSpan();
        for (int i = _messages.Count - 1; i >= 0; i--)
        {
            if (_messages[i].SentAt < cutoff)
                _messages.RemoveAt(i);
        }
    }

    /// <summary>Switches the pane to a one-to-one conversation with <paramref name="buddy"/>.</summary>
    public void BindBuddy(IBuddy buddy, IChat? chat)
    {
        _buddy = buddy;
        _room = null;
        _chatSession = chat;
        ConversationKey = ConversationCoordinator.BuddyKey(buddy.Id);

        headerName.Text = buddy.DisplayName;
        headerSubtitle.Text = buddy.IsOnline() ? "Trực tuyến" : "Ngoại tuyến";

        _messages.Clear();
        LoadPreviousMessages(() =>
        {
            var history = App.Services.GetService<CICMessenger.History.HistoryManager>();
            return history?.GetRecentMessagesWithContact(buddy.Id);
        });
    }

    /// <summary>Switches the pane to a room conversation.</summary>
    public void BindRoom(Room room, IChat? chat, int memberCount)
    {
        _buddy = null;
        _room = room;
        _chatSession = chat;
        ConversationKey = ConversationCoordinator.RoomKey(room.Id);

        headerName.Text = room.Name;
        headerSubtitle.Text = $"Phòng · {memberCount} thành viên";

        _messages.Clear();
        LoadPreviousMessages(() =>
        {
            var history = App.Services.GetService<CICMessenger.History.HistoryManager>();
            return history?.GetRecentMessages(room.Id);
        });
    }

    /// <summary>Call after inviting more members online, or after renaming, to refresh the live session reference and header.</summary>
    public void UpdateRoomSession(IChat? chat, int memberCount)
    {
        if (_room == null)
            return;
        _chatSession = chat;
        headerName.Text = _room.Name;
        headerSubtitle.Text = $"Phòng · {memberCount} thành viên";
    }

    /// <summary>
    /// Switches the pane to an ad hoc broadcast chat (fan-out to everyone currently online).
    /// Not tied to a buddy or room, so it has no persisted history — matches the previous
    /// broadcast-chat popup's behavior.
    /// </summary>
    public void BindBroadcast(string title, IChat chat, string conversationKey)
    {
        _buddy = null;
        _room = null;
        _chatSession = chat;
        ConversationKey = conversationKey;

        headerName.Text = title;
        headerSubtitle.Text = "";

        _messages.Clear();
    }

    /// <summary>
    /// Repopulates the pane with the recent conversation from the history database, so
    /// switching back to a conversation (or restarting the app) doesn't present it empty.
    /// </summary>
    private void LoadPreviousMessages(Func<IEnumerable<CICMessenger.History.DAL.Entities.Event>?> fetch)
    {
        var chatClient = App.Services.GetService<IChatClient>();

        try
        {
            var events = fetch();
            if (events == null)
                return;

            var selfId = chatClient?.CurrentUser.Id;
            var youLabel = FindTranslation("Global_You", "You");

            foreach (var evnt in events)
            {
                bool isOwn = selfId != null && evnt.SenderId == selfId;
                var background = new SolidColorBrush(Color.Parse(isOwn ? "#DCF8C6" : "#E3F2FD"));
                var senderName = isOwn ? youLabel : evnt.SenderName;
                var sentAt = evnt.Stamp.ToLocalTime();
                var timestamp = sentAt.ToString("dd/MM HH:mm");

                if (evnt.Type == CICMessenger.History.DAL.Entities.EventType.File)
                {
                    var filePath = evnt.Data;
                    var fileName = System.IO.Path.GetFileName(filePath);
                    var exists = System.IO.File.Exists(filePath);

                    var kind = !exists ? ChatMessageKind.File
                             : IsImage(filePath) ? ChatMessageKind.Image
                             : IsVideo(filePath) ? ChatMessageKind.Video
                             : ChatMessageKind.File;

                    Avalonia.Media.Imaging.Bitmap? bitmap = null;
                    if (kind == ChatMessageKind.Image)
                        try { bitmap = new Avalonia.Media.Imaging.Bitmap(filePath); } catch { kind = ChatMessageKind.File; }

                    _messages.Add(new ChatMessage
                    {
                        SenderName = senderName,
                        Text = exists ? fileName : $"{fileName} ({FindTranslation("ChatWindow_FileNoLongerAvailable", "File không còn tồn tại")})",
                        Timestamp = timestamp,
                        SentAt = sentAt,
                        Background = background,
                        Kind = kind,
                        FilePath = exists ? filePath : null,
                        ImageSource = bitmap,
                        IsOwn = isOwn
                    });
                }
                else
                {
                    _messages.Add(new ChatMessage
                    {
                        SenderName = senderName,
                        Text = evnt.Data,
                        Timestamp = timestamp,
                        SentAt = sentAt,
                        Background = background,
                        IsOwn = isOwn
                    });
                }
            }

            if (_messages.Count > 0)
                Avalonia.Threading.Dispatcher.UIThread.Post(ScrollToBottom,
                    Avalonia.Threading.DispatcherPriority.Loaded);
        }
        catch
        {
            // History is a convenience — never block opening the conversation over it
        }
    }

    /// <summary>Called by MainWindow when a message arrives on the currently bound conversation.</summary>
    public void AppendIncomingMessage(IBuddy sender, string text)
    {
        _messages.Add(new ChatMessage
        {
            SenderName = sender.DisplayName,
            Text = text,
            Timestamp = DateTime.Now.ToString("HH:mm"),
            Background = new SolidColorBrush(Color.Parse("#E3F2FD")),
            IsOwn = false
        });
        ScrollToBottom();
        typingIndicator.IsVisible = false;
    }

    /// <summary>Called by MainWindow when the buddy typing indicator changes for the bound conversation.</summary>
    public void SetTyping(string? buddyName)
    {
        if (buddyName == null)
        {
            typingIndicator.IsVisible = false;
            return;
        }

        typingIndicator.Text = $"{buddyName} {FindTranslation("ChatWindow_IsTyping", "is typing...")}";
        typingIndicator.IsVisible = true;
    }

    public void AppendSystemMessage(string text)
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
    }

    /// <summary>Called by MainWindow when a file arrives on the currently bound conversation (already logged to history by the coordinator).</summary>
    public void AppendIncomingFileMessage(string savedPath, string fileName)
    {
        var kind = IsImage(savedPath) ? ChatMessageKind.Image
                 : IsVideo(savedPath) ? ChatMessageKind.Video
                 : ChatMessageKind.File;

        Avalonia.Media.Imaging.Bitmap? bitmap = null;
        if (kind == ChatMessageKind.Image)
            try { bitmap = new Avalonia.Media.Imaging.Bitmap(savedPath); } catch { kind = ChatMessageKind.File; }

        var message = new ChatMessage
        {
            SenderName = _buddy?.DisplayName ?? "",
            Text = fileName,
            Timestamp = DateTime.Now.ToString("HH:mm"),
            Background = new SolidColorBrush(Color.Parse("#E3F2FD")),
            Kind = kind,
            FilePath = savedPath,
            ImageSource = bitmap,
            IsOwn = false
        };
        _messages.Add(message);
        ScrollToBottom();
        AttachmentAdded?.Invoke(this, message);
    }

    private void SendButton_Click(object? sender, RoutedEventArgs e) => SendMessage();

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

        if (_buddy != null && !_buddy.IsOnline())
        {
            AddSystemMessageLocal(string.Format(
                FindTranslation("ChatWindow_QueuedForOffline",
                    "{0} is offline. The message will be sent when they come back online."),
                _buddy.DisplayName));
        }
        else if (_room != null && _chatSession == null)
        {
            AddSystemMessageLocal(FindTranslation("ChatWindow_RoomNoOneOnline",
                "Không có thành viên nào trong phòng đang trực tuyến. Tin nhắn sẽ không được gửi."));
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

    private void AddSystemMessageLocal(string text)
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
    }

    private void ScrollToBottom()
    {
        messageScroller.Offset = new Vector(0, messageScroller.Extent.Height);
    }

    private TopLevel? Host => TopLevel.GetTopLevel(this);

    private async void SendFile_Click(object? sender, RoutedEventArgs e)
    {
        var storageProvider = Host?.StorageProvider;
        if (storageProvider == null)
            return;

        var files = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
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
            AddSystemMessageLocal(FindTranslation("ChatWindow_FileTransferNoSession", "Chưa có phiên trò chuyện để gửi file."));
            return;
        }

        if (_chatSession.IsGroupChat)
        {
            AddSystemMessageLocal(FindTranslation("ChatWindow_FileTransferNotAllowedInGroup",
                "Không thể truyền file trong cuộc trò chuyện nhóm."));
            return;
        }

        if (_buddy != null && !_buddy.IsOnline())
        {
            AddSystemMessageLocal(string.Format(
                FindTranslation("ChatWindow_FileTransferPeerOffline",
                    "{0} đang ngoại tuyến nên chưa gửi được file."),
                _buddy.DisplayName));
            return;
        }

        try
        {
            var info = new System.IO.FileInfo(filePath);
            var content = System.IO.File.OpenRead(filePath);

            var executor = _chatSession.CreateActivity(CICMessengerActivities.FileTransfer);
            var handler = new FileTransferActivity().CreateInvite(executor, new Dictionary<string, object>
            {
                ["content"] = content,
                ["name"] = info.Name,
                ["size"] = info.Length
            });

            AddOutgoingFileMessage(filePath, fileName);
            AttachTransferFeedback(handler, fileName, content);

            Serilog.Log.Information("Sending file '{Name}' ({Size} bytes) to {Buddy}",
                info.Name, info.Length, _buddy?.DisplayName);
            handler.Start();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Sending file {Name} failed", fileName);
            AddSystemMessageLocal($"{FindTranslation("ChatWindow_CouldNotReadFile", "Không đọc được file")} {fileName}: {ex.Message}");
        }
    }

    private void AddOutgoingFileMessage(string filePath, string fileName)
    {
        var kind = IsImage(filePath) ? ChatMessageKind.Image
                 : IsVideo(filePath) ? ChatMessageKind.Video
                 : ChatMessageKind.File;

        Avalonia.Media.Imaging.Bitmap? bitmap = null;
        if (kind == ChatMessageKind.Image)
            try { bitmap = new Avalonia.Media.Imaging.Bitmap(filePath); } catch { kind = ChatMessageKind.File; }

        var message = new ChatMessage
        {
            SenderName = FindTranslation("Global_You", "You"),
            Text = fileName,
            Timestamp = DateTime.Now.ToString("HH:mm"),
            Background = new SolidColorBrush(Color.Parse("#DCF8C6")),
            Kind = kind,
            FilePath = filePath,
            ImageSource = bitmap,
            IsOwn = true
        };
        _messages.Add(message);
        ScrollToBottom();

        _chatSession?.LogFileTransfer(outgoing: true, _buddy, fileName, filePath);
        AttachmentAdded?.Invoke(this, message);
    }

    /// <summary>Reports transfer progress/outcome as system messages (send side only — receive side is handled centrally by the coordinator).</summary>
    private void AttachTransferFeedback(IActivityHandler handler, string fileName, System.IO.Stream? disposeOnFinish)
    {
        handler.TransferCompleted += (_, _) => AddSystemMessageLocal(string.Format(
            FindTranslation("ChatWindow_FileSent", "Đã gửi file: {0}"), fileName));

        handler.TransferCancelled += (_, _) => AddSystemMessageLocal(string.Format(
            FindTranslation("ChatWindow_FileCancelled", "Đã hủy truyền file: {0}"), fileName));

        handler.Error += (_, args) => AddSystemMessageLocal(string.Format(
            FindTranslation("ChatWindow_FileFailed", "Lỗi truyền file {0}: {1}"), fileName, args.GetException().Message));

        handler.TransferFinished += (_, _) => disposeOnFinish?.Dispose();
    }

    /// <summary>Opens a video or generic-file attachment with the OS default app.</summary>
    private void OpenFile_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Control { Tag: string filePath })
            OpenWithDefaultApp(filePath);
    }

    private void OpenWithDefaultApp(string filePath)
    {
        if (!System.IO.File.Exists(filePath))
        {
            AddSystemMessageLocal($"{FindTranslation("ChatWindow_FileNoLongerAvailable", "File không còn tồn tại")}: {System.IO.Path.GetFileName(filePath)}");
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(filePath) { UseShellExecute = true });
        }
        catch
        {
            AddSystemMessageLocal($"{FindTranslation("ChatWindow_CouldNotReadFile", "Could not read file")} {System.IO.Path.GetFileName(filePath)}");
        }
    }

    /// <summary>Opens the full-size image viewer for an image attachment.</summary>
    private void Image_Tapped(object? sender, TappedEventArgs e)
    {
        if (sender is Control { Tag: ChatMessage { ImageSource: { } bitmap, Text: var name } } && Host is Window owner)
            new ImageViewerWindow(bitmap, name).Show(owner);
    }

    /// <summary>Right-click action shared by image/video/file bubbles: reveal the local file in Explorer.</summary>
    private void OpenInExplorer_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string filePath })
            return;

        if (!System.IO.File.Exists(filePath))
        {
            AddSystemMessageLocal($"{FindTranslation("ChatWindow_FileNoLongerAvailable", "File không còn tồn tại")}: {System.IO.Path.GetFileName(filePath)}");
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\""));
        }
        catch
        {
            AddSystemMessageLocal($"{FindTranslation("ChatWindow_CouldNotReadFile", "Could not read file")} {System.IO.Path.GetFileName(filePath)}");
        }
    }

    /// <summary>
    /// Hides the host window, lets the user rubber-band a region of the desktop, then sends
    /// the cropped image as a PNG.
    /// </summary>
    private async void Screenshot_Click(object? sender, RoutedEventArgs e)
    {
        if (Host is not Window owner)
            return;

        try
        {
            var shot = await ScreenRegionPicker.CaptureRegionAsync(owner);
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
            AddSystemMessageLocal($"{FindTranslation("ChatWindow_ScreenshotFailed", "Không chụp được màn hình.")} {ex.Message}");
        }
    }

    private async void Emoticon_Click(object? sender, RoutedEventArgs e)
    {
        if (Host is not Window owner)
            return;

        var picker = new EmoticonPicker();
        var result = await picker.ShowDialog<string?>(owner);
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
        var storageProvider = Host?.StorageProvider;
        if (storageProvider == null)
            return;

        var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
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

    private async void DeleteConversation_Click(object? sender, RoutedEventArgs e)
    {
        if (_buddy == null && _room == null)
            return;

        var dialogService = App.Services.GetRequiredService<IDialogService>();
        var confirmed = await dialogService.ShowMessageBoxAsync(
            "Xóa hội thoại",
            $"Xóa toàn bộ lịch sử trò chuyện với \"{ConversationName}\"? Hành động này không thể hoàn tác.",
            MessageBoxButton.YesNo);

        if (confirmed != MessageBoxResult.Yes)
            return;

        var history = App.Services.GetService<CICMessenger.History.HistoryManager>();
        if (history != null)
        {
            if (_buddy != null)
            {
                var sessionIds = history.GetSessions(new CICMessenger.History.DAL.SessionCriteria { Participant = _buddy.Id })
                    .Select(s => s.Id);
                history.DeleteSessions(sessionIds);
            }
            else if (_room != null)
            {
                history.DeleteSessions(new[] { _room.Id });
            }
        }

        _messages.Clear();
    }

    private string FindTranslation(string key, string fallback)
    {
        if (this.TryFindResource(key, out var value) && value is string s)
            return s;
        return fallback;
    }
}
