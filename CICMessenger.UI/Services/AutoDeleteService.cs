using System;
using System.Timers;
using CICMessenger.History;

namespace CICMessenger.UI.Services;

/// <summary>
/// Periodically purges chat/file history older than the configured auto-delete threshold.
/// Off by default (<see cref="Models.ChatSettingsViewModel"/> equivalent — see
/// <c>ChatSettingsViewModel.AutoDeleteMessages</c>); re-reads settings on every sweep so a
/// change made in the Settings window takes effect without restarting the app.
/// </summary>
public class AutoDeleteService : IDisposable
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromSeconds(30);

    private readonly HistoryManager _history;
    private readonly SettingsService _settingsService;
    private readonly Timer _timer;

    public AutoDeleteService(HistoryManager history, SettingsService settingsService)
    {
        _history = history;
        _settingsService = settingsService;
        _timer = new Timer(SweepInterval.TotalMilliseconds) { AutoReset = true };
        _timer.Elapsed += (_, _) => Sweep();
    }

    public void Start() => _timer.Start();

    private void Sweep()
    {
        try
        {
            var chatSettings = _settingsService.Load().ChatSettings;
            if (!chatSettings.AutoDeleteMessages)
                return;

            var cutoffUtc = DateTime.UtcNow - chatSettings.GetAutoDeleteTimeSpan();
            _history.DeleteEventsOlderThan(cutoffUtc);
        }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex, "Auto-delete sweep failed");
        }
    }

    public void Dispose() => _timer.Dispose();
}
