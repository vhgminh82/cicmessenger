using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using CICMessenger.UI.Windows;

namespace CICMessenger.UI.Services;

/// <summary>
/// Shows real desktop-level toasts (their own topmost, no-taskbar windows) anchored to the
/// bottom-right corner of the screen's working area, just above the taskbar. Unlike an
/// overlay attached to the main window, these stay visible even while the main window is
/// minimized or hidden to the tray.
/// </summary>
public class AvaloniaNotificationService : INotificationService
{
    private const int MaxToasts = 3;
    private const int Margin = 16;
    private const int Spacing = 10;

    private Window? _mainWindow;
    private readonly List<ToastNotificationWindow> _activeToasts = new();

    public void Initialize(Window mainWindow)
    {
        _mainWindow = mainWindow;
    }

    public void ShowNotification(string title, string message) => ShowToast(title, message, null);

    public void ShowMessageNotification(string senderName, string messagePreview) =>
        ShowToast(senderName, messagePreview, BringMainWindowToFront);

    private void BringMainWindowToFront()
    {
        if (_mainWindow == null)
            return;

        _mainWindow.WindowState = WindowState.Normal;
        _mainWindow.Show();
        _mainWindow.Activate();
    }

    private void ShowToast(string title, string message, Action? onClick)
    {
        Dispatcher.UIThread.Post(() =>
        {
            if (_activeToasts.Count >= MaxToasts)
            {
                var oldest = _activeToasts[^1];
                _activeToasts.RemoveAt(_activeToasts.Count - 1);
                oldest.Close();
            }

            var toast = new ToastNotificationWindow(title, message, onClick);
            toast.Closed += (_, _) =>
            {
                _activeToasts.Remove(toast);
                RepositionAll();
            };

            _activeToasts.Insert(0, toast);
            RepositionAll();
            toast.Show();
        });
    }

    private void RepositionAll()
    {
        for (int i = 0; i < _activeToasts.Count; i++)
            PositionToast(_activeToasts[i], i);
    }

    /// <summary>stackIndex 0 = closest to the taskbar (newest); higher indexes stack upward.</summary>
    private void PositionToast(ToastNotificationWindow toast, int stackIndex)
    {
        var screens = _mainWindow?.Screens;
        var screen = screens?.Primary ?? (screens != null && screens.All.Count > 0 ? screens.All[0] : null);
        if (screen == null)
            return;

        var wa = screen.WorkingArea;
        int width = (int)toast.Width;
        int height = (int)toast.Height;

        int x = wa.X + wa.Width - width - Margin;
        int y = wa.Y + wa.Height - Margin - (stackIndex + 1) * height - stackIndex * Spacing;

        toast.Position = new PixelPoint(x, y);
    }
}
