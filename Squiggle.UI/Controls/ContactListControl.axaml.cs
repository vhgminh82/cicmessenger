using System.Diagnostics;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using Squiggle.Client;
using Squiggle.UI.Services;
using Squiggle.UI.ViewModel;
using Squiggle.UI.Windows;

namespace Squiggle.UI.Controls;

public partial class ContactListControl : UserControl
{
    public ContactListControl()
    {
        InitializeComponent();
        Loaded += (_, _) =>
        {
            filterBox.FilterChanged += FilterBox_FilterChanged;
        };
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

    private void ContactsList_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (contactsList.SelectedItem is IBuddy buddy)
        {
            OpenChatWindow(buddy);
        }
    }

    private void StartChat_Click(object? sender, RoutedEventArgs e)
    {
        if (contactsList.SelectedItem is IBuddy buddy)
        {
            OpenChatWindow(buddy);
        }
    }

    private void OpenChatWindow(IBuddy buddy)
    {
        var chatClient = App.Services.GetRequiredService<IChatClient>();
        var windowManager = App.Services.GetRequiredService<ChatWindowManager>();
        windowManager.OpenOrFocus(buddy, () => chatClient.StartChat(buddy));
    }

    private async void SendFile_Click(object? sender, RoutedEventArgs e)
    {
        if (contactsList.SelectedItem is not IBuddy buddy)
            return;

        var topLevel = TopLevel.GetTopLevel(this);
        if (topLevel == null) return;

        var files = await topLevel.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Gửi file",
            AllowMultiple = false
        });

        if (files.Count > 0)
        {
            var chatClient = App.Services.GetRequiredService<IChatClient>();
            var windowManager = App.Services.GetRequiredService<ChatWindowManager>();
            var chatWindow = windowManager.OpenOrFocus(buddy, () => chatClient.StartChat(buddy));
            chatWindow.SendFile(files[0].Path.LocalPath);
        }
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

    private string FindTranslation(string key, string fallback)
        => this.TryFindResource(key, out var value) && value is string s ? s : fallback;
}
