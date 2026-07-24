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
using CICMessenger.UI.Services;
using CICMessenger.UI.ViewModel;
using Microsoft.Extensions.DependencyInjection;

namespace CICMessenger.UI;

public partial class MainWindow : Window
{
    private ClientViewModel? _viewModel;
    private ITrayIconService? _trayIconService;
    private INotificationService? _notificationService;
    private bool _forceClose;

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

        // Wire up incoming chat sessions and buddy notifications
        chatClient.ChatStarted += ChatClient_ChatStarted;
        chatClient.BuddyOnline += ChatClient_BuddyOnline;

        if (chatClient is ChatClient concreteClient)
            concreteClient.OfflineMessagesDelivered += ChatClient_OfflineMessagesDelivered;

        // Watch for incoming file offers at app level, so an offer is never missed because
        // the chat window hadn't been created yet.
        var fileTransfers = App.Services.GetRequiredService<FileTransferCoordinator>();
        fileTransfers.Observe(chatClient);
        fileTransfers.FileReceived += FileTransfers_FileReceived;
        fileTransfers.Progress += FileTransfers_Progress;

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

        versionLabel.Text = "V" + UpdateService.DisplayVersion;

        // Check quietly in the background; the badge is the only sign until the user acts.
        _ = CheckForUpdateBadgeAsync();
    }

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

        // Minimize to tray instead of closing
        e.Cancel = true;
        Hide();
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
            chatClient.Logout();
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

        var window = new Windows.ChatWindow(broadcast,
            FindTranslation("ChatWindow_BroadCastChatTitle", "Broadcast chat"));
        window.Show();
        window.Activate();
    }

    private async void CreateRoomMenu_Click(object? sender, RoutedEventArgs e)
    {
        var chatClient = App.Services.GetRequiredService<IChatClient>();
        var dialogService = App.Services.GetRequiredService<IDialogService>();

        var onlineBuddies = chatClient.Buddies.Where(b => b.IsOnline()).ToList();
        if (onlineBuddies.Count == 0)
        {
            await dialogService.ShowMessageBoxAsync("CICMessenger",
                FindTranslation("CreateRoom_NoContacts", "No contacts are online."));
            return;
        }

        var picker = new Windows.CreateRoomWindow(onlineBuddies);
        var selected = await picker.ShowDialog<System.Collections.Generic.List<IBuddy>?>(this);
        if (selected == null || selected.Count == 0)
            return;

        // Start a session with the first member, then invite the rest into the same
        // session — the core promotes it to a group chat as invitees join.
        var chat = chatClient.StartChat(selected[0]);
        foreach (var buddy in selected.Skip(1))
            chat.Invite(buddy);

        var windowManager = App.Services.GetRequiredService<ChatWindowManager>();
        windowManager.OpenGroupRoom(selected[0], chat);
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

    private void FileTransfers_FileReceived(object? sender, FileReceivedEventArgs e)
    {
        var chatClient = App.Services.GetRequiredService<IChatClient>();
        var windowManager = App.Services.GetRequiredService<ChatWindowManager>();
        var window = windowManager.OpenOrFocus(e.Buddy, () => chatClient.StartChat(e.Buddy));
        window.ShowReceivedFile(e.FilePath, e.FileName);

        _notificationService?.ShowMessageNotification(e.Buddy.DisplayName, $"Đã nhận file: {e.FileName}");
    }

    private void FileTransfers_Progress(object? sender, FileTransferProgressEventArgs e)
    {
        var chatClient = App.Services.GetRequiredService<IChatClient>();
        var windowManager = App.Services.GetRequiredService<ChatWindowManager>();
        var window = windowManager.OpenOrFocus(e.Buddy, () => chatClient.StartChat(e.Buddy));
        window.ShowSystemNotice(e.Message);
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

    private void ChatClient_ChatStarted(object? sender, ChatStartedEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var buddy = e.Buddies.FirstOrDefault();
            if (buddy != null)
            {
                var windowManager = App.Services.GetRequiredService<ChatWindowManager>();
                windowManager.OpenOrFocus(buddy, e.Chat);

                // Show notification if main window is not active
                if (!IsActive)
                {
                    _notificationService?.ShowMessageNotification(
                        buddy.DisplayName, "Đã bắt đầu một cuộc trò chuyện");
                }
            }
        });
    }
}
