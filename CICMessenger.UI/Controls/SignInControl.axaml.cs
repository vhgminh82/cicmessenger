using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CICMessenger.UI.Controls;

public class LoginEventArgs : EventArgs
{
    public string DisplayName { get; init; } = "";
    public string GroupName { get; init; } = "";
    public string Password { get; init; } = "";
    public bool SaveNameAndPassword { get; init; }
}

public partial class SignInControl : UserControl
{
    public event EventHandler<LoginEventArgs>? LoginRequested;

    public SignInControl()
    {
        InitializeComponent();
    }

    public void SetDefaults(string displayName, string groupName, string password = "", bool saveNameAndPassword = false)
    {
        if (!string.IsNullOrEmpty(displayName))
            txtDisplayName.Text = displayName;

        // Group name is no longer asked for at sign-in; keep whatever was configured
        // in Settings so existing grouping still works.
        _groupName = groupName;
    }

    private string _groupName = "";

    private void SignIn_Click(object? sender, RoutedEventArgs e)
    {
        var displayName = txtDisplayName.Text?.Trim() ?? "";

        if (string.IsNullOrEmpty(displayName))
            return;

        LoginRequested?.Invoke(this, new LoginEventArgs
        {
            DisplayName = displayName,
            GroupName = _groupName,
            Password = "",
            SaveNameAndPassword = false
        });
    }
}
