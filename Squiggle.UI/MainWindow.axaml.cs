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

        // Wire up incoming chat sessions
        chatClient.ChatStarted += ChatClient_ChatStarted;

        // Pre-populate sign-in form with saved settings
        var settingsService = App.Services.GetRequiredService<SettingsService>();
        var settings = settingsService.Load();
        signInControl.SetDefaults(settings.PersonalSettings.DisplayName, settings.PersonalSettings.GroupName);

        InitializeTrayIcon();
        InitializeNotifications();
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
                await dialogService.ShowMessageBoxAsync("Error",
                    "Could not detect a local network address. Please check your network connection.");
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

            chatClient.Login(loginOptions);

            // Save display name for next time
            settings.PersonalSettings.DisplayName = e.DisplayName;
            settings.PersonalSettings.GroupName = e.GroupName;
            settingsService.Save(settings);
        }
        catch (Exception ex)
        {
            await dialogService.ShowMessageBoxAsync("Error", $"Failed to sign in: {ex.Message}");
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

    private void AboutMenu_Click(object? sender, RoutedEventArgs e)
    {
        // About dialog - will be implemented later
    }

    private void ChatClient_ChatStarted(object? sender, ChatStartedEventArgs e)
    {
        Avalonia.Threading.Dispatcher.UIThread.Post(() =>
        {
            var buddy = e.Buddies.FirstOrDefault();
            if (buddy != null)
            {
                var chatWindow = new Windows.ChatWindow(buddy, e.Chat);
                chatWindow.Show();
                chatWindow.Activate();
            }
        });
    }
}
