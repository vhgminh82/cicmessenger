using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CICMessenger.Client;

namespace CICMessenger.UI.Windows;

/// <summary>
/// Lets the user pick several online contacts to start a group chat room with.
/// Returns the selected buddies via ShowDialog result (null = cancelled).
/// </summary>
public partial class CreateRoomWindow : Window
{
    public CreateRoomWindow()
    {
        InitializeComponent();
    }

    public CreateRoomWindow(IEnumerable<IBuddy> onlineBuddies) : this()
    {
        contactsList.ItemsSource = onlineBuddies.ToList();
    }

    private void Create_Click(object? sender, RoutedEventArgs e)
    {
        var selected = contactsList.SelectedItems?.OfType<IBuddy>().ToList();
        if (selected == null || selected.Count == 0)
            return;

        Close(selected);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
