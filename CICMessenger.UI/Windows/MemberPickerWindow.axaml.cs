using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CICMessenger.Client;

namespace CICMessenger.UI.Windows;

/// <summary>
/// Generic multi-select buddy picker, reused for both "add member" (candidates = contacts not
/// already in the room) and "remove member" (candidates = current room members). Returns the
/// selected buddies via ShowDialog (null/empty = cancelled).
/// </summary>
public partial class MemberPickerWindow : Window
{
    public MemberPickerWindow()
    {
        InitializeComponent();
    }

    public MemberPickerWindow(IEnumerable<IBuddy> candidates, string prompt) : this()
    {
        candidatesList.ItemsSource = candidates.ToList();
        promptText.Text = prompt;
    }

    private void Confirm_Click(object? sender, RoutedEventArgs e)
    {
        var selected = candidatesList.SelectedItems?.OfType<IBuddy>().ToList();
        if (selected == null || selected.Count == 0)
        {
            Close(null);
            return;
        }

        Close(selected);
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e)
    {
        Close(null);
    }
}
