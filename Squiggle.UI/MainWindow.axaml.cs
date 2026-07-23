using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Interactivity;
using Squiggle.Client;
using Squiggle.Core.Presence;
using Squiggle.UI.Controls;
using Squiggle.UI.Services;
using Squiggle.UI.ViewModel;
using Microsoft.Extensions.DependencyInjection;

namespace Squiggle.UI;

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

        var version = Services.UpdateService.CurrentVersion;
        versionLabel.Text = $"V{version.Major}.{version.Minor}";
    }

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
        var dialogService = App.Services.GetRequiredService<IDialogService>();
        var updateService = new UpdateService();

        try
        {
            var update = await updateService.CheckForUpdateAsync();
            if (update == null)
            {
                await dialogService.ShowMessageBoxAsync("CICMessenger",
                    FindTranslation("Update_UpToDate", "You are on the latest version."));
                return;
            }

            var message = string.Format(
                FindTranslation("Update_NewVersionFound", "New version {0} available. Download and update now?"),
                update.TagName);
            var result = await dialogService.ShowMessageBoxAsync("CICMessenger", message, MessageBoxButton.YesNo);
            if (result != MessageBoxResult.Yes)
                return;

            await updateService.DownloadAndApplyAsync(update);

            // The helper script replaces the exe after we exit
            _forceClose = true;
            Close();
        }
        catch (Exception)
        {
            await dialogService.ShowMessageBoxAsync("CICMessenger",
                FindTranslation("Update_Failed", "Could not check for updates. Please check your connection."));
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
