using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using CICMessenger.History;
using CICMessenger.History.DAL;

namespace CICMessenger.UI.Windows;

public partial class HistoryViewer : Window
{
    public HistoryViewer()
    {
        InitializeComponent();
        // Load both tabs up-front so the window isn't empty until the user clicks Search
        Loaded += async (_, _) =>
        {
            await SearchAsync(null, null, null);
            await LoadStatusHistoryAsync();
        };
    }

    private async void Search_Click(object? sender, RoutedEventArgs e)
    {
        var from = txtFrom.SelectedDate;
        var to = txtTo.SelectedDate;
        var message = txtMessage.Text;

        await SearchAsync(from?.DateTime, to?.DateTime, message);
    }

    private async Task SearchAsync(DateTime? from, DateTime? to, string? message)
    {
        var historyManager = App.Services.GetService<HistoryManager>();
        if (historyManager == null)
            return;

        var sessions = await Task.Run(() =>
        {
            return historyManager.GetSessions(new SessionCriteria
            {
                From = from?.ToUniversalTime(),
                To = to?.ToUniversalTime(),
                Text = string.IsNullOrEmpty(message) ? null : message,
            })
            .Select(s => new HistoryResult
            {
                Id = s.Id,
                Start = s.Start.ToLocalTime(),
                End = s.End?.ToLocalTime(),
                Participants = string.Join(", ", s.Participants.Select(p => p.ContactName))
            })
            .ToList();
        });

        resultsGrid.ItemsSource = sessions;
    }

    private void Results_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (resultsGrid.SelectedItem is HistoryResult result)
        {
            var viewer = new ConversationViewer(result.Id);
            viewer.ShowDialog(this);
        }
    }

    private void Clear_Click(object? sender, RoutedEventArgs e)
    {
        txtFrom.SelectedDate = null;
        txtTo.SelectedDate = null;
        txtMessage.Text = "";
        resultsGrid.ItemsSource = null;
    }

    private async void RefreshStatus_Click(object? sender, RoutedEventArgs e)
    {
        await LoadStatusHistoryAsync();
    }

    private async Task LoadStatusHistoryAsync()
    {
        var historyManager = App.Services.GetService<HistoryManager>();
        if (historyManager == null)
            return;

        var updates = await Task.Run(() =>
        {
            return historyManager.GetStatusUpdates(new StatusCriteria())
                .OrderByDescending(u => u.Stamp)
                .Select(u => new StatusHistoryResult
                {
                    Time = u.Stamp.ToLocalTime().ToString("dd/MM/yyyy HH:mm"),
                    Name = u.ContactName,
                    Status = TranslateStatus(u.StatusCode)
                })
                .ToList();
        });

        statusGrid.ItemsSource = updates;
    }

    private static string TranslateStatus(int statusCode) => statusCode switch
    {
        0 => "Trực tuyến",
        1 => "Bận",
        2 => "Sẽ quay lại ngay",
        3 => "Vắng mặt",
        4 => "Không hoạt động",
        5 => "Ngoại tuyến",
        _ => statusCode.ToString()
    };
}

public class HistoryResult
{
    public string Id { get; set; } = "";
    public DateTime Start { get; set; }
    public DateTime? End { get; set; }
    public string Participants { get; set; } = "";
}

public class StatusHistoryResult
{
    public string Time { get; set; } = "";
    public string Name { get; set; } = "";
    public string Status { get; set; } = "";
}
