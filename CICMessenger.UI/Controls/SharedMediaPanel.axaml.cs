using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Microsoft.Extensions.DependencyInjection;
using CICMessenger.History;
using CICMessenger.History.DAL.Entities;
using CICMessenger.UI.Models;
using CICMessenger.UI.Windows;

namespace CICMessenger.UI.Controls;

public class SharedMediaItem
{
    public string SenderName { get; init; } = "";
    public string FileName { get; init; } = "";
    public string? FilePath { get; init; }
    public bool IsImage { get; init; }
    public Bitmap? Thumbnail { get; init; }
    public string TimestampText { get; init; } = "";
}

public class SharedMediaGroup
{
    public string SenderName { get; init; } = "";
    public List<SharedMediaItem> Items { get; init; } = new();
}

/// <summary>
/// Right column: every file/image sent or received in the currently open conversation,
/// grouped by who sent it — reads from history so it survives switching conversations.
/// </summary>
public partial class SharedMediaPanel : UserControl
{
    private static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };
    private static bool IsImagePath(string path) =>
        ImageExtensions.Contains(System.IO.Path.GetExtension(path).ToLowerInvariant());

    private readonly ObservableCollection<SharedMediaGroup> _groups = new();

    public SharedMediaPanel()
    {
        InitializeComponent();
        groupsControl.ItemsSource = _groups;
    }

    public void Clear()
    {
        _groups.Clear();
        emptyText.Text = FindTranslation("SharedMedia_SelectConversation", "Chọn một cuộc trò chuyện để bắt đầu.");
        emptyText.IsVisible = true;
    }

    public void LoadForBuddy(string buddyId)
    {
        var history = App.Services.GetService<HistoryManager>();
        Load(history?.GetRecentMessagesWithContact(buddyId, limit: 200));
    }

    public void LoadForRoom(string roomId)
    {
        var history = App.Services.GetService<HistoryManager>();
        Load(history?.GetRecentMessages(roomId, limit: 200));
    }

    private void Load(IEnumerable<Event>? events)
    {
        _groups.Clear();

        var fileEvents = events?.Where(e => e.Type == EventType.File).ToList() ?? new List<Event>();
        if (fileEvents.Count == 0)
        {
            emptyText.Text = FindTranslation("SharedMedia_Empty", "Chưa có file hoặc ảnh nào.");
            emptyText.IsVisible = true;
            return;
        }

        emptyText.IsVisible = false;

        foreach (var group in fileEvents.GroupBy(e => e.SenderName))
        {
            var items = group.Select(ToItem).ToList();
            _groups.Add(new SharedMediaGroup { SenderName = group.Key, Items = items });
        }
    }

    private static SharedMediaItem ToItem(Event evnt)
    {
        var filePath = evnt.Data;
        var exists = System.IO.File.Exists(filePath);
        var isImage = exists && IsImagePath(filePath);

        Bitmap? thumbnail = null;
        if (isImage)
            try { thumbnail = new Bitmap(filePath); } catch { isImage = false; }

        return new SharedMediaItem
        {
            SenderName = evnt.SenderName,
            FileName = System.IO.Path.GetFileName(filePath),
            FilePath = exists ? filePath : null,
            IsImage = isImage,
            Thumbnail = thumbnail,
            TimestampText = evnt.Stamp.ToLocalTime().ToString("dd/MM HH:mm")
        };
    }

    /// <summary>Adds a just-sent/received attachment to the top group for its sender, for live updates without a full reload.</summary>
    public void Add(string senderName, string fileName, string? filePath, bool isImage, Bitmap? thumbnail)
    {
        emptyText.IsVisible = false;

        var item = new SharedMediaItem
        {
            SenderName = senderName,
            FileName = fileName,
            FilePath = filePath,
            IsImage = isImage,
            Thumbnail = thumbnail,
            TimestampText = DateTime.Now.ToString("dd/MM HH:mm")
        };

        var group = _groups.FirstOrDefault(g => g.SenderName == senderName);
        if (group == null)
        {
            group = new SharedMediaGroup { SenderName = senderName };
            _groups.Insert(0, group);
        }
        group.Items.Insert(0, item);

        // Nested list has no change notification of its own — force the ItemsControl to refresh this group's row.
        var index = _groups.IndexOf(group);
        _groups.RemoveAt(index);
        _groups.Insert(index, group);
    }

    private void Item_Tapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Control { Tag: SharedMediaItem item })
            return;

        if (item.FilePath == null)
            return;

        if (item.IsImage && item.Thumbnail != null)
        {
            if (TopLevel.GetTopLevel(this) is Window owner)
                new ImageViewerWindow(item.Thumbnail, item.FileName).Show(owner);
            return;
        }

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(item.FilePath) { UseShellExecute = true });
        }
        catch
        {
            // best effort — right-click "Open in Explorer" is the fallback
        }
    }

    private void OpenInExplorer_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is not MenuItem { Tag: string filePath } || !System.IO.File.Exists(filePath))
            return;

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe", $"/select,\"{filePath}\""));
        }
        catch
        {
            // best effort
        }
    }

    private string FindTranslation(string key, string fallback)
        => this.TryFindResource(key, out var value) && value is string s ? s : fallback;
}
