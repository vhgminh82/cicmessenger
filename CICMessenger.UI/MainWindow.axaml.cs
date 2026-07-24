using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using CICMessenger.Client;
using CICMessenger.Core.Presence;
using CICMessenger.UI.Controls;
using CICMessenger.UI.Models;
using CICMessenger.UI.Services;
using CICMessenger.UI.ViewModel;
using Microsoft.Extensions.DependencyInjection;

namespace CICMessenger.UI;

public partial class MainWindow : Window
{
    private ClientViewModel? _viewModel;
    private ITrayIconService? _trayIconService;
    private INotificationService? _notificationService;
    private ConversationCoordinator? _conversations;
    private bool _forceClose;

    /// <summary>Conversation key ("buddy:id" / "room:id") currently shown in the middle pane, or null.</summary>
    private string? _currentConversationKey;
    private Room? _currentRoom;

    public MainWindow()
    {
        InitializeComponent();

        Loaded += MainWindow_Loaded;
        Closing += MainWindow_Closing;
    }

    private void MainWindow_Loaded(object? sender, RoutedEventArgs e)
    {
        var chatClient = App.Services.GetRequiredService<IChatClient>();
        _viewModel = new ClientViewModel(chatClient);
        DataContext = _viewModel;

        signInControl.LoginRequested += SignInControl_LoginRequested;

        chatClient.BuddyOnline += ChatClient_BuddyOnline;

        if (chatClient is ChatClient concreteClient)
            concreteClient.OfflineMessagesDelivered += ChatClient_OfflineMessagesDelivered;

        // Watch for incoming file offers at app level, so an offer is never missed because
        // the conversation hadn't been opened yet.
        var fileTransfers = App.Services.GetRequiredService<FileTransferCoordinator>();
        fileTransfers.Observe(chatClient);
        fileTransfers.Progress += FileTransfers_Progress;

        _conversations = App.Services.GetRequiredService<ConversationCoordinator>();
        _conversations.MessageReceived += Conversations_MessageReceived;
        _conversations.SystemNotice += Conversations_SystemNotice;
        _conversations.TypingChanged += Conversations_TypingChanged;
        _conversations.FileReceived += Conversations_FileReceived;
        _conversations.ChatReplaced += Conversations_ChatReplaced;

        contactListControl.BuddySelected += ContactListControl_BuddySelected;
        contactListControl.RoomSelected += ContactListControl_RoomSelected;
        contactListControl.SendFileRequestedForBuddy += ContactListControl_SendFileRequestedForBuddy;
        contactListControl.RoomRenamed += ContactListControl_RoomRenamed;

        chatPane.AttachmentAdded += ChatPane_AttachmentAdded;

        // Pre-populate sign-in form with saved settings
        var settingsService = App.Services.GetRequiredService<SettingsService>();
        var settings = settingsService.Load();
        signInControl.SetDefaults(
            settings.PersonalSettings.DisplayName,
            settings.PersonalSettings.GroupName,
            settings.PersonalSettings.Password,
            settings.PersonalSettings.RememberMe);

        InitializeTrayIcon();
        InitializeNotifications();

        App.Services.GetRequiredService<AutoDeleteService>().Start();

        versionLabel.Text = "V" + UpdateService.DisplayVersion;

        // Check quietly in the background; the badge is the only sign until the user acts.
        _ = CheckForUpdateBadgeAsync();
    }

    // ----- Conversation selection -----

    private void ContactListControl_BuddySelected(object? sender, IBuddy buddy)
    {
        var chat = _conversations!.GetOrCreateBuddyChat(buddy);
        _currentRoom = null;
        _currentConversationKey = ConversationCoordinator.BuddyKey(buddy.Id);
        contactListControl.ClearUnread(buddy.Id, isRoom: false);

        chatPane.BindBuddy(buddy, chat);
        ShowPane();

        sharedMediaPanel.LoadForBuddy(buddy.Id);
    }

    private void ContactListControl_RoomSelected(object? sender, Room room)
    {
        var members = contactListControl.ResolveMembers(room);
        var chat = _conversations!.GetOrCreateRoomChat(room, members);
        _currentRoom = room;
        _currentConversationKey = ConversationCoordinator.RoomKey(room.Id);
        contactListControl.ClearUnread(room.Id, isRoom: true);

        chatPane.BindRoom(room, chat, members.Count);
        ShowPane();

        sharedMediaPanel.LoadForRoom(room.Id);
    }

    private void ContactListControl_RoomRenamed(object? sender, Room room)
    {
        if (_currentRoom?.Id != room.Id)
            return;

        var members = contactListControl.ResolveMembers(room);
        _conversations!.RenameRoom(room, members);
        chatPane.UpdateRoomSession(_conversations.TryGet(ConversationCoordinator.RoomKey(room.Id)), members.Count);
    }

    private async void ContactListControl_SendFileRequestedForBuddy(object? sender, IBuddy buddy)
    {
        ContactListControl_BuddySelected(sender, buddy);

        var files = await StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "Gửi file",
            AllowMultiple = false
        });

        if (files.Count > 0)
            chatPane.SendFile(files[0].Path.LocalPath);
    }

    private void ShowPane()
    {
        chatPane.IsVisible = true;
        noConversationText.IsVisible = false;
    }

    // ----- Central routing of live conversation events -----

    private void Conversations_MessageReceived(object? sender, ConversationMessageEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            bool isCurrent = e.ConversationKey == _currentConversationKey;
            if (isCurrent)
                chatPane.AppendIncomingMessage(e.Sender, e.Message);
            else
                MarkConversationUnread(e.ConversationKey);

            // Notify unless the user is actively looking at exactly this conversation
            if (!isCurrent || !IsActive)
            {
                _notificationService?.ShowMessageNotification(
                    e.Sender.DisplayName,
                    e.Message.Length > 50 ? e.Message[..50] + "..." : e.Message);
            }
        });
    }

    /// <summary>Marks the buddy or room behind a conversation key as having an unread message, for the contact list badge.</summary>
    private void MarkConversationUnread(string conversationKey)
    {
        if (conversationKey.StartsWith("buddy:"))
            contactListControl.MarkUnread(conversationKey["buddy:".Length..], isRoom: false);
        else if (conversationKey.StartsWith("room:"))
            contactListControl.MarkUnread(conversationKey["room:".Length..], isRoom: true);
    }

    /// <summary>
    /// The buddy's live chat session got replaced (see ConversationCoordinator.Register) —
    /// if that conversation is the one currently showing, rebind the pane to it so sends go
    /// out over the session that's actually connected to the peer, instead of the stale one
    /// that was open when the collision happened.
    /// </summary>
    private void Conversations_ChatReplaced(object? sender, ChatReplacedEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (e.ConversationKey != _currentConversationKey || _currentRoom != null)
                return;

            var buddyId = e.ConversationKey.StartsWith("buddy:") ? e.ConversationKey["buddy:".Length..] : null;
            var buddy = buddyId != null ? _viewModel?.Buddies.FirstOrDefault(b => b.Id == buddyId) : null;
            if (buddy != null)
                chatPane.BindBuddy(buddy, e.Chat);
        });
    }

    private void Conversations_SystemNotice(object? sender, ConversationSystemEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (e.ConversationKey == _currentConversationKey)
                chatPane.AppendSystemMessage(e.Text);
        });
    }

    private void Conversations_TypingChanged(object? sender, ConversationTypingEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (e.ConversationKey == _currentConversationKey)
                chatPane.SetTyping(e.BuddyName);
        });
    }

    private void Conversations_FileReceived(object? sender, ConversationFileEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            bool isCurrent = e.ConversationKey == _currentConversationKey;
            if (isCurrent)
                chatPane.AppendIncomingFileMessage(e.SavedPath, e.FileName);
            else
                MarkConversationUnread(e.ConversationKey);

            // Notify unless the user is actively looking at exactly this conversation
            if (!isCurrent || !IsActive)
            {
                _notificationService?.ShowMessageNotification(e.Sender.DisplayName, $"Đã nhận file: {e.FileName}");
            }
        });
    }

    private void FileTransfers_Progress(object? sender, FileTransferProgressEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            if (_currentConversationKey == ConversationCoordinator.BuddyKey(e.Buddy.Id))
                chatPane.AppendSystemMessage(e.Message);
        });
    }

    private void ChatPane_AttachmentAdded(object? sender, ChatMessage message)
    {
        var senderName = message.IsOwn ? _viewModel?.LoggedInUser.DisplayName ?? "" : message.SenderName;
        sharedMediaPanel.Add(senderName, message.Text, message.FilePath, message.IsImage, message.ImageSource);
    }

    // ----- Update badge -----

    private UpdateService.UpdateInfo? _pendingUpdate;

    /// <summary>
    /// Looks for a newer release without interrupting the user, and flags it next to the
    /// version number so a new build is noticeable without opening any menu.
    /// </summary>
    private async System.Threading.Tasks.Task CheckForUpdateBadgeAsync()
    {
        try
        {
            var update = await new UpdateService().CheckForUpdateAsync();
            if (update == null)
                return;

            _pendingUpdate = update;
            updateBadgeText.Text = FindTranslation("Update_BadgeNew", "Bản mới") + " " + update.TagName;
            updateBadge.IsVisible = true;
            ToolTip.SetTip(versionButton, string.Format(
                FindTranslation("Update_NewVersionFound", "Có phiên bản mới {0}. Bấm để cập nhật."),
                update.TagName));
        }
        catch (Exception ex)
        {
            // A failed check must stay invisible — it is not something the user asked for
            Serilog.Log.Debug(ex, "Background update check failed");
        }
    }

    private void VersionLabel_Click(object? sender, RoutedEventArgs e) => UpdateMenu_Click(sender, e);

    private void InitializeTrayIcon()
    {
        _trayIconService = App.Services.GetRequiredService<ITrayIconService>();
        _trayIconService.ShowTrayIcon();
        _trayIconService.TrayIconClicked += (_, _) => ShowAndActivate();

        if (_trayIconService is AvaloniaTrayIconService avaloniaTray)
        {
            avaloniaTray.SignOutRequested += (_, _) => SignOutMenu_Click(this, new RoutedEventArgs());
            avaloniaTray.ExitRequested += (_, _) =>
            {
                _forceClose = true;
                Close();
            };
            avaloniaTray.StatusSelected += (_, status) =>
            {
                var chatClient = App.Services.GetRequiredService<IChatClient>();
                if (chatClient.IsLoggedIn)
                {
                    chatClient.CurrentUser.Status = status;
                    avaloniaTray.SetStatusIcon(status);
                }
            };
        }
    }

    private void InitializeNotifications()
    {
        _notificationService = App.Services.GetRequiredService<INotificationService>();
        _notificationService.Initialize(this);
    }

    private void ShowAndActivate()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void MainWindow_Closing(object? sender, WindowClosingEventArgs e)
    {
        if (_forceClose)
            return;

        // Minimize instead of closing — Hide() would drop the window from the taskbar
        // entirely, leaving only the tray icon. Minimizing keeps a running-app button in
        // the taskbar (and pinned-icon group) alongside the tray icon, restorable from either.
        e.Cancel = true;
        WindowState = WindowState.Minimized;
    }

    private async void SignInControl_LoginRequested(object? sender, LoginEventArgs e)
    {
        var chatClient = App.Services.GetRequiredService<IChatClient>();
        var dialogService = App.Services.GetRequiredService<IDialogService>();
        var settingsService = App.Services.GetRequiredService<SettingsService>();

        try
        {
            var settings = settingsService.Load();
            var connSettings = settings.ConnectionSettings;

            var localIP = GetLocalIPAddress();
            if (localIP == null)
            {
                await dialogService.ShowMessageBoxAsync("Lỗi",
                    "Không phát hiện được địa chỉ mạng nội bộ. Vui lòng kiểm tra kết nối mạng của bạn.");
                return;
            }

            var chatEndPoint = new IPEndPoint(localIP, connSettings.ChatPort);
            var multicastAddress = IPAddress.Parse(connSettings.PresenceAddress);
            var multicastEndPoint = new IPEndPoint(multicastAddress, connSettings.PresencePort);
            var multicastReceiveEndPoint = new IPEndPoint(IPAddress.Any, connSettings.PresencePort);
            var presenceEndPoint = new IPEndPoint(localIP, connSettings.PresencePort + 1);

            var properties = new BuddyProperties();
            properties.GroupName = e.GroupName;
            properties.MachineName = Environment.MachineName;

            var loginOptions = new LoginOptions
            {
                DisplayName = e.DisplayName,
                ChatEndPoint = chatEndPoint,
                MulticastEndPoint = multicastEndPoint,
                MulticastReceiveEndPoint = multicastReceiveEndPoint,
                PresenceServiceEndPoint = presenceEndPoint,
                KeepAliveTime = TimeSpan.FromMilliseconds(connSettings.KeepAliveTime),
                UserProperties = properties
            };

            chatClient.EnableLogging = settings.ChatSettings.EnableLogging;
            chatClient.Login(loginOptions);

            // Save display name for next time
            settings.PersonalSettings.DisplayName = e.DisplayName;
            settings.PersonalSettings.GroupName = e.GroupName;
            settings.PersonalSettings.RememberMe = e.SaveNameAndPassword;
            settings.PersonalSettings.Password = e.SaveNameAndPassword ? e.Password : "";
            settingsService.Save(settings);
        }
        catch (Exception ex)
        {
            await dialogService.ShowMessageBoxAsync("Lỗi", $"Đăng nhập thất bại: {ex.Message}");
        }
    }

    private static IPAddress? GetLocalIPAddress()
    {
        try
        {
            return NetworkInterface.GetAllNetworkInterfaces()
                .Where(ni => ni.OperationalStatus == OperationalStatus.Up
                          && ni.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .SelectMany(ni => ni.GetIPProperties().UnicastAddresses)
                .Where(addr => addr.Address.AddressFamily == AddressFamily.InterNetwork)
                .Select(addr => addr.Address)
                .FirstOrDefault();
        }
        catch
        {
            return null;
        }
    }

    private void SignOutMenu_Click(object? sender, RoutedEventArgs e)
    {
        var chatClient = App.Services.GetRequiredService<IChatClient>();
        if (chatClient.IsLoggedIn)
        {
            _conversations?.EndAll();
            _currentConversationKey = null;
            _currentRoom = null;
            chatPane.IsVisible = false;
            noConversationText.IsVisible = true;
            sharedMediaPanel.Clear();
            chatClient.Logout();
        }
    }

    private void CloseMenu_Click(object? sender, RoutedEventArgs e)
    {
        _forceClose = true;
        Close();
    }

    private async void SettingsMenu_Click(object? sender, RoutedEventArgs e)
    {
        var settingsWindow = new Windows.SettingsWindow();
        await settingsWindow.ShowDialog(this);
    }

    private async void HistoryMenu_Click(object? sender, RoutedEventArgs e)
    {
        var viewer = new Windows.HistoryViewer();
        await viewer.ShowDialog(this);
    }

    private async void BroadcastMenu_Click(object? sender, RoutedEventArgs e)
    {
        var chatClient = App.Services.GetRequiredService<IChatClient>();
        var dialogService = App.Services.GetRequiredService<IDialogService>();

        var broadcast = chatClient.StartBroadcastChat();
        if (broadcast == null)
        {
            await dialogService.ShowMessageBoxAsync("CICMessenger",
                FindTranslation("CreateRoom_NoContacts", "No contacts are online."));
            return;
        }

        var key = ConversationCoordinator.NewAdHocKey();
        _conversations!.RegisterAdHoc(key, broadcast);

        _currentRoom = null;
        _currentConversationKey = key;
        chatPane.BindBroadcast(FindTranslation("ChatWindow_BroadCastChatTitle", "Broadcast chat"), broadcast, key);
        ShowPane();
        sharedMediaPanel.Clear();
    }

    private async void UpdateMenu_Click(object? sender, RoutedEventArgs e)
    {
        // async void: anything escaping this method kills the process, so every path —
        // including the error path — has to be guarded.
        try
        {
            var dialogService = App.Services.GetRequiredService<IDialogService>();
            var updateService = new UpdateService();

            UpdateService.UpdateInfo? update;
            try
            {
                update = await updateService.CheckForUpdateAsync();
            }
            catch (Exception ex)
            {
                await ShowUpdateMessageAsync(
                    FindTranslation("Update_Failed", "Could not check for updates.") + $"\n\n{ex.Message}");
                return;
            }

            if (update == null)
            {
                // Nothing newer after all — clear any badge left from the startup check
                updateBadge.IsVisible = false;
                _pendingUpdate = null;

                await ShowUpdateMessageAsync(
                    FindTranslation("Update_UpToDate", "You are on the latest version.")
                    + $" (V{UpdateService.DisplayVersion})");
                return;
            }

            var message = string.Format(
                FindTranslation("Update_NewVersionFound", "New version {0} available. Download and update now?"),
                update.TagName);
            var result = await dialogService.ShowMessageBoxAsync("CICMessenger", message, MessageBoxButton.YesNo);
            if (result != MessageBoxResult.Yes)
                return;

            try
            {
                updateService.Progress += (msg, pct) => Avalonia.Threading.Dispatcher.UIThread.Post(() =>
                    versionLabel.Text = pct >= 0 ? $"{msg} {pct}%" : msg);
                await updateService.DownloadAndApplyAsync(update);
            }
            catch (Exception ex)
            {
                versionLabel.Text = "V" + UpdateService.DisplayVersion;
                await ShowUpdateMessageAsync(
                    FindTranslation("Update_Failed", "Could not download the update.") + $"\n\n{ex.Message}");
                return;
            }

            // The helper script replaces the exe after we exit
            _forceClose = true;
            Close();
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Update check failed");
        }
    }

    private async System.Threading.Tasks.Task ShowUpdateMessageAsync(string message)
    {
        try
        {
            var dialogService = App.Services.GetRequiredService<IDialogService>();
            await dialogService.ShowMessageBoxAsync("CICMessenger", message);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Could not show update dialog");
        }
    }

    private string FindTranslation(string key, string fallback)
    {
        if (this.TryFindResource(key, out var value) && value is string s)
            return s;
        return fallback;
    }

    private void ChatClient_BuddyOnline(object? sender, BuddyOnlineEventArgs e)
    {
        if (e.Discovered)
            return;

        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _notificationService?.ShowNotification("Liên hệ trực tuyến",
                $"{e.Buddy.DisplayName} đang trực tuyến");
        });
    }

    private void ChatClient_OfflineMessagesDelivered(object? sender, OfflineMessagesDeliveredEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            _notificationService?.ShowNotification("CICMessenger",
                string.Format(
                    FindTranslation("ChatWindow_QueuedMessagesDelivered", "Delivered {0} queued message(s)."),
                    e.Count) + $" ({e.Buddy.DisplayName})");
        });
    }
}
