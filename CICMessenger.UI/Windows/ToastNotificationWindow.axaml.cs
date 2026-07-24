using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;

namespace CICMessenger.UI.Windows;

/// <summary>
/// A real desktop-level toast (not attached to any app window), so it stays visible even
/// when the main window is hidden to the tray. Positioned by the caller; auto-dismisses.
/// </summary>
public partial class ToastNotificationWindow : Window
{
    private readonly DispatcherTimer _dismissTimer;
    private Action? _onClick;

    public ToastNotificationWindow()
    {
        InitializeComponent();
        _dismissTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _dismissTimer.Tick += (_, _) => { _dismissTimer.Stop(); Close(); };
    }

    public ToastNotificationWindow(string title, string message, Action? onClick) : this()
    {
        titleText.Text = title;
        messageText.Text = message;
        _onClick = onClick;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _dismissTimer.Start();
    }

    private void Root_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _dismissTimer.Stop();
        _onClick?.Invoke();
        Close();
    }

    private void Close_Click(object? sender, RoutedEventArgs e)
    {
        _dismissTimer.Stop();
        Close();
    }
}
