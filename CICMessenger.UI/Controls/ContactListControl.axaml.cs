using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using CICMessenger.Client;
using CICMessenger.Core.Presence;
using CICMessenger.UI.Models;
using CICMessenger.UI.Services;
using CICMessenger.UI.ViewModel;
using CICMessenger.UI.Windows;

namespace CICMessenger.UI.Controls;

public partial class ContactListControl : UserControl
{
    private readonly ObservableCollection<Room> _rooms = new();

    /// <summary>Raised when a one-to-one contact is selected in the list.</summary>
    public event System.EventHandler<IBuddy>? BuddySelected;

    /// <summary>Raised when a room is selected in the list.</summary>
    public event System.EventHandler<Room>? RoomSelected;

    /// <summary>Raised when "Send file" is chosen for a contact from the context menu.</summary>
    public event System.EventHandler<IBuddy>? SendFileRequestedForBuddy;

    public ContactListControl()
    {
        InitializeComponent();
        roomsList.ItemsSource = _rooms;

        Loaded += (_, _) =>
        {
            filterBox.FilterChanged += FilterBox_FilterChanged;
            ReloadRooms();
        };
    }

    private RoomsService Rooms => App.Services.GetRequiredService<RoomsService>();

    private void ReloadRooms()
    {
        _rooms.Clear();
        foreach (var room in Rooms.Load())
            _rooms.Add(room);
    }

    /// <summary>Resolves a room's member ids to live IBuddy objects (missing/unknown contacts are skipped).</summary>
    public System.Collections.Generic.List<IBuddy> ResolveMembers(Room room)
    {
        var chatClient = App.Services.GetRequiredService<IChatClient>();
        return room.MemberIds
            .Select(id => chatClient.Buddies.FirstOrDefault(b => b.Id == id))
            .Where(b => b != null)
            .Select(b => b!)
            .ToList();
    }

    private void FilterBox_FilterChanged(object? sender, string filter)
    {
        if (DataContext is not ClientViewModel vm)
            return;

        if (string.IsNullOrWhiteSpace(filter))
        {
            contactsList.ItemsSource = vm.Buddies;
        }
        else
        {
            var filtered = vm.Buddies
                .Where(b => b.DisplayName.Contains(filter, System.StringComparison.OrdinalIgnoreCase))
                .ToList();
            contactsList.ItemsSource = filtered;
        }
    }

    private void ContactsList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (contactsList.SelectedItem is not IBuddy buddy)
            return;

        roomsList.SelectedItem = null;
        BuddySelected?.Invoke(this, buddy);
    }

    private void RoomsList_SelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (roomsList.SelectedItem is not Room room)
            return;

        contactsList.SelectedItem = null;
        RoomSelected?.Invoke(this, room);
    }

    private void StartChat_Click(object? sender, RoutedEventArgs e)
    {
        if (contactsList.SelectedItem is IBuddy buddy)
            BuddySelected?.Invoke(this, buddy);
    }

    private void SendFile_Click(object? sender, RoutedEventArgs e)
    {
        if (contactsList.SelectedItem is IBuddy buddy)
            SendFileRequestedForBuddy?.Invoke(this, buddy);
    }

    private void SendEmail_Click(object? sender, RoutedEventArgs e)
    {
        if (contactsList.SelectedItem is not IBuddy buddy)
            return;

        var email = buddy.Properties?["EmailAddress"];
        if (!string.IsNullOrEmpty(email))
        {
            Process.Start(new ProcessStartInfo($"mailto:{email}") { UseShellExecute = true });
        }
    }

    private async void RemoveContact_Click(object? sender, RoutedEventArgs e)
    {
        if (contactsList.SelectedItem is not IBuddy buddy)
            return;

        var chatClient = App.Services.GetRequiredService<IChatClient>();
        if (!chatClient.RemoveBuddy(buddy))
        {
            var dialogService = App.Services.GetRequiredService<IDialogService>();
            await dialogService.ShowMessageBoxAsync(
                FindTranslation("Error", "Error"),
                FindTranslation("Buddy_RemoveContact_StillOnline", "This contact is still online and can't be removed."));
            return;
        }

        if (DataContext is ClientViewModel vm)
            vm.Buddies.Remove(buddy);
    }

    private async void NewRoom_Click(object? sender, RoutedEventArgs e)
    {
        var chatClient = App.Services.GetRequiredService<IChatClient>();
        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is not Window owner)
            return;

        if (!chatClient.Buddies.Any())
        {
            var dialogService = App.Services.GetRequiredService<IDialogService>();
            await dialogService.ShowMessageBoxAsync("CICMessenger",
                FindTranslation("CreateRoom_NoContacts", "No contacts are online."));
            return;
        }

        var picker = new CreateRoomWindow(chatClient.Buddies);
        var result = await picker.ShowDialog<RoomCreationResult?>(owner);
        if (result == null)
            return;

        var room = new Room { Name = result.Name, MemberIds = result.Members.Select(b => b.Id).ToList() };
        var all = Rooms.Load();
        all.Add(room);
        Rooms.Save(all);

        _rooms.Add(room);
        roomsList.SelectedItem = room;
    }

    private async void RenameRoom_Click(object? sender, RoutedEventArgs e)
    {
        if (roomsList.SelectedItem is not Room room)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel is not Window owner)
            return;

        var input = new TextInputWindow(FindTranslation("RoomList_RenamePrompt", "Nhập tên mới cho phòng:"), room.Name);
        var newName = await input.ShowDialog<string?>(owner);
        if (string.IsNullOrWhiteSpace(newName))
            return;

        room.Name = newName;
        var all = Rooms.Load();
        var stored = all.FirstOrDefault(r => r.Id == room.Id);
        if (stored != null)
            stored.Name = newName;
        Rooms.Save(all);

        RoomRenamed?.Invoke(this, room);

        // Refresh the row's binding
        var index = _rooms.IndexOf(room);
        _rooms.RemoveAt(index);
        _rooms.Insert(index, room);
        roomsList.SelectedItem = room;
    }

    /// <summary>Raised after a room is renamed, so the currently open conversation's header/history can update.</summary>
    public event System.EventHandler<Room>? RoomRenamed;

    private void DeleteRoom_Click(object? sender, RoutedEventArgs e)
    {
        if (roomsList.SelectedItem is not Room room)
            return;

        var all = Rooms.Load();
        all.RemoveAll(r => r.Id == room.Id);
        Rooms.Save(all);

        _rooms.Remove(room);
    }

    private void SetStatusOnline_Click(object? sender, RoutedEventArgs e) => SetStatus(UserStatus.Online);

    private void SetStatusBusy_Click(object? sender, RoutedEventArgs e) => SetStatus(UserStatus.Busy);

    private void SetStatus(UserStatus status)
    {
        var chatClient = App.Services.GetRequiredService<IChatClient>();
        if (!chatClient.IsLoggedIn)
            return;

        chatClient.CurrentUser.Status = status;

        var trayIcon = App.Services.GetService<ITrayIconService>();
        trayIcon?.SetStatusIcon(status);
    }

    private string FindTranslation(string key, string fallback)
        => this.TryFindResource(key, out var value) && value is string s ? s : fallback;
}
