using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CICMessenger.Client;

namespace CICMessenger.UI.Windows;

/// <summary>Result of the room-creation dialog: the chosen name and member contacts.</summary>
public class RoomCreationResult
{
    public string Name { get; init; } = "";
    public List<IBuddy> Members { get; init; } = new();
}

/// <summary>
/// Lets the user name a room and pick contacts to add as members.
/// Returns the result via ShowDialog (null = cancelled).
/// </summary>
public partial class CreateRoomWindow : Window
{
    public CreateRoomWindow()
    {
        InitializeComponent();
    }

    public CreateRoomWindow(IEnumerable<IBuddy> buddies) : this()
    {
        contactsList.ItemsSource = buddies.ToList();
    }

    private void Create_Click(object? sender, RoutedEventArgs e)
    {
        var selected = contactsList.SelectedItems?.OfType<IBuddy>().ToList();
        if (selected == null || selected.Count == 0)
            return;

        var name = roomNameBox.Text?.Trim();
        if (string.IsNullOrEmpty(name))
            name = string.Join(", ", selected.Select(b => b.DisplayName));

        Close(new RoomCreationResult { Name = name, Members = selected });
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
