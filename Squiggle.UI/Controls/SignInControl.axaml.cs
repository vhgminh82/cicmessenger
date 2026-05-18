using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Squiggle.UI.Controls;

public class LoginEventArgs : EventArgs
{
    public string DisplayName { get; init; } = "";
    public string GroupName { get; init; } = "";
}

public partial class SignInControl : UserControl
{
    public event EventHandler<LoginEventArgs>? LoginRequested;

    public SignInControl()
    {
        InitializeComponent();
    }

    public void SetDefaults(string displayName, string groupName)
    {
        if (!string.IsNullOrEmpty(displayName))
            txtDisplayName.Text = displayName;
        if (!string.IsNullOrEmpty(groupName))
            txtGroupName.Text = groupName;
    }

    private void SignIn_Click(object? sender, RoutedEventArgs e)
    {
        var displayName = txtDisplayName.Text?.Trim() ?? "";
        var groupName = txtGroupName.Text?.Trim() ?? "";

        if (string.IsNullOrEmpty(displayName))
            return;

        LoginRequested?.Invoke(this, new LoginEventArgs
        {
            DisplayName = displayName,
            GroupName = groupName
        });
    }
}
