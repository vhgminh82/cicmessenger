using System;
using System.IO;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using CICMessenger.Client;
using CICMessenger.History;
using CICMessenger.UI.Helpers;
using CICMessenger.UI.Services;

namespace CICMessenger.UI;

public partial class App : Application
{
    public static IServiceProvider Services { get; private set; } = null!;
    public static bool RunInBackground { get; set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        Services = ConfigureServices();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            if (desktop.Args is { Length: > 0 } && desktop.Args[0].Trim() == "/background")
                RunInBackground = true;

            // Apply saved theme on startup
            var themeService = Services.GetRequiredService<ThemeService>();
            themeService.ApplyTheme("System");

            desktop.MainWindow = new MainWindow();
            desktop.ShutdownMode = Avalonia.Controls.ShutdownMode.OnMainWindowClose;
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static IServiceProvider ConfigureServices()
    {
        var services = new ServiceCollection();

        var appLocation = AppDomain.CurrentDomain.BaseDirectory;

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.File(
                Path.Combine(appLocation, "logs", "cicmessenger-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();

        services.AddLogging(builder => builder.AddSerilog(dispose: true));

        // Store history DB in AppData, not next to the exe — the portable exe may sit in a
        // read-only or cloud-synced folder where SQLite writes fail or get corrupted.
        var dataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CICMessenger");
        Directory.CreateDirectory(dataFolder);
        var dbPath = Path.Combine(dataFolder, "history.db");
        var historyManager = new HistoryManager($"Data Source={dbPath}");
        services.AddSingleton(historyManager);

        var offlineStore = new OfflineMessageStore(Path.Combine(dataFolder, "pending-messages.json"));
        services.AddSingleton(offlineStore);

        services.AddSingleton<IChatClient>(provider =>
        {
            var loggerFactory = provider.GetRequiredService<ILoggerFactory>();
            return new ChatClient(SettingsService.GetOrCreateClientId(), historyManager, loggerFactory, offlineStore);
        });

        // Plugins
        var pluginsPath = Path.Combine(appLocation, "Plugins");
#pragma warning disable IL2026 // RequiresUnreferencedCode - plugin loading is inherently reflection-based
        var pluginLoader = new PluginLoader(pluginsPath);
#pragma warning restore IL2026
        services.AddSingleton(pluginLoader);

        // Platform services
        services.AddSingleton<SettingsService>();
        services.AddSingleton<ThemeService>();
        services.AddSingleton<ITrayIconService, AvaloniaTrayIconService>();
        services.AddSingleton<INotificationService, AvaloniaNotificationService>();
        services.AddSingleton<IDialogService, AvaloniaDialogService>();
        services.AddSingleton<IClipboardService, AvaloniaClipboardService>();
        services.AddSingleton<IAutoStartService, WindowsAutoStartService>();
        services.AddSingleton<RoomsService>();
        services.AddSingleton<FileTransferCoordinator>();
        services.AddSingleton<ConversationCoordinator>();
        services.AddSingleton<AutoDeleteService>();

        return services.BuildServiceProvider();
    }
}
