using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CICMessenger.UI.Windows;

/// <summary>
/// Displays the app's Serilog log files and the separate unhandled-exception crash.log
/// (see Program.WriteCrashLog) so the user can inspect recent errors without leaving the app.
/// </summary>
public partial class LogViewerWindow : Window
{
    private static readonly string LogsFolder = Path.Combine(AppContext.BaseDirectory, "logs");

    public LogViewerWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await LoadFileListAsync();
    }

    private async Task LoadFileListAsync()
    {
        var files = await Task.Run(() =>
        {
            if (!Directory.Exists(LogsFolder))
                return Array.Empty<string>();

            return Directory.GetFiles(LogsFolder, "*.log")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Select(Path.GetFileName)
                .ToArray();
        });

        fileCombo.ItemsSource = files;
        if (files.Length > 0)
            fileCombo.SelectedIndex = 0;
        else
            logText.Text = FindTranslation("LogViewer_NoLogs", "Không tìm thấy tệp log nào.");
    }

    private async void FileCombo_SelectionChanged(object? sender, SelectionChangedEventArgs e)
        => await LoadSelectedFileAsync();

    private async void Refresh_Click(object? sender, RoutedEventArgs e)
        => await LoadSelectedFileAsync();

    private async void ErrorsOnlyCheck_Click(object? sender, RoutedEventArgs e)
        => await LoadSelectedFileAsync();

    private async Task LoadSelectedFileAsync()
    {
        if (fileCombo.SelectedItem is not string fileName)
            return;

        var path = Path.Combine(LogsFolder, fileName);
        var errorsOnly = errorsOnlyCheck.IsChecked == true;

        var text = await Task.Run(() =>
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                using var reader = new StreamReader(stream);
                var lines = reader.ReadToEnd().Split('\n');

                if (errorsOnly)
                {
                    lines = lines
                        .Where(l => l.Contains("[ERR]", StringComparison.Ordinal)
                                 || l.Contains("[FTL]", StringComparison.Ordinal)
                                 || l.Contains("Exception", StringComparison.Ordinal))
                        .ToArray();
                }

                return string.Join('\n', lines);
            }
            catch (Exception ex)
            {
                return $"Không thể đọc tệp log: {ex.Message}";
            }
        });

        logText.Text = text;
    }

    private string FindTranslation(string key, string fallback)
        => this.TryFindResource(key, out var value) && value is string s ? s : fallback;
}
