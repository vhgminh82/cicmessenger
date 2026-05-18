using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Microsoft.Extensions.DependencyInjection;
using Squiggle.Client;
using Squiggle.UI.Windows;

namespace Squiggle.UI.Controls;

public partial class ContactListControl : UserControl
{
    public ContactListControl()
    {
        InitializeComponent();
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
        var chat = chatClient.StartChat(buddy);
        var chatWindow = new ChatWindow(buddy, chat);
        chatWindow.Show();
    }

    private void SendFile_Click(object? sender, RoutedEventArgs e)
    {
        // File send will be implemented later
    }

    private void SendEmail_Click(object? sender, RoutedEventArgs e)
    {
        // Email will be implemented later
    }
}
