using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;

namespace CICMessenger.UI;

class Program
{
    private static Mutex? _singleInstanceMutex;

    [STAThread]
    public static void Main(string[] args)
    {
        // CICMESSENGER_INSTANCE lets multiple instances run side by side on one machine
        // (e.g. for local P2P testing without a second physical machine).
        var instanceId = Environment.GetEnvironmentVariable("CICMESSENGER_INSTANCE");
        var mutexName = string.IsNullOrEmpty(instanceId)
            ? "CICMessenger_SingleInstance"
            : $"CICMessenger_SingleInstance_{instanceId}";
        _singleInstanceMutex = new Mutex(true, mutexName, out bool createdNew);

        if (!createdNew)
        {
            // Another instance is already running
            return;
        }

        InstallCrashLogging();

        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            WriteCrashLog("Startup", ex);
            throw;
        }
        finally
        {
            _singleInstanceMutex.ReleaseMutex();
            _singleInstanceMutex.Dispose();
        }
    }

    /// <summary>
    /// Records exceptions that would otherwise kill the process without a trace — the app
    /// previously just vanished, leaving nothing to diagnose.
    /// </summary>
    static void InstallCrashLogging()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            WriteCrashLog("Unhandled", e.ExceptionObject as Exception);

        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            WriteCrashLog("UnobservedTask", e.Exception);
            e.SetObserved();
        };
    }

    static void WriteCrashLog(string source, Exception? ex)
    {
        try
        {
            var folder = Path.Combine(AppContext.BaseDirectory, "logs");
            Directory.CreateDirectory(folder);
            var file = Path.Combine(folder, "crash.log");
            File.AppendAllText(file,
                $"=== {DateTime.Now:yyyy-MM-dd HH:mm:ss} [{source}] ==={Environment.NewLine}{ex}{Environment.NewLine}{Environment.NewLine}");
        }
        catch
        {
            // Nothing useful to do if even the crash log can't be written
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
