using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform;
using Squiggle.Core.Presence;

namespace Squiggle.UI.Services;

public class AvaloniaTrayIconService : ITrayIconService, IDisposable
{
    private TrayIcon? _trayIcon;
    private readonly Dictionary<UserStatus, string> _statusLabels = new()
    {
        [UserStatus.Online] = "Trực tuyến",
        [UserStatus.Away] = "Vắng mặt",
        [UserStatus.Busy] = "Bận",
        [UserStatus.BeRightBack] = "Sẽ quay lại ngay",
        [UserStatus.Idle] = "Không hoạt động",
        [UserStatus.Offline] = "Ngoại tuyến",
    };

    public event EventHandler? TrayIconClicked;

    public void ShowTrayIcon()
    {
        if (_trayIcon != null)
            return;

        _trayIcon = new TrayIcon
        {
            ToolTipText = "CICMessenger",
            Icon = new WindowIcon(AssetLoader.Open(new Uri("avares://CICMessenger/Assets/cic-logo-256.png"))),
            Menu = CreateContextMenu(),
        };

        _trayIcon.Clicked += (_, _) => TrayIconClicked?.Invoke(this, EventArgs.Empty);

        var icons = TrayIcon.GetIcons(Application.Current!);
        if (icons == null)
        {
            icons = new TrayIcons();
            TrayIcon.SetIcons(Application.Current!, icons);
        }
        icons.Add(_trayIcon);
    }

    public void HideTrayIcon()
    {
        if (_trayIcon == null)
            return;

        var icons = TrayIcon.GetIcons(Application.Current!);
        icons?.Remove(_trayIcon);
        _trayIcon.Dispose();
        _trayIcon = null;
    }

    public void SetTooltip(string tooltip)
    {
        if (_trayIcon != null)
            _trayIcon.ToolTipText = tooltip;
    }

    public void SetStatusIcon(UserStatus status)
    {
        var label = _statusLabels.GetValueOrDefault(status, "CICMessenger");
        SetTooltip($"CICMessenger - {label}");
    }

    private NativeMenu CreateContextMenu()
    {
        var menu = new NativeMenu();

        var openItem = new NativeMenuItem("Mở CICMessenger");
        openItem.Click += (_, _) => TrayIconClicked?.Invoke(this, EventArgs.Empty);
        menu.Add(openItem);

        menu.Add(new NativeMenuItemSeparator());

        var statusMenu = new NativeMenu();
        foreach (var (status, label) in _statusLabels)
        {
            if (status == UserStatus.Idle || status == UserStatus.Offline)
                continue;

            var item = new NativeMenuItem(label);
            item.Click += (_, _) => StatusSelected?.Invoke(this, status);
            statusMenu.Add(item);
        }

        var statusItem = new NativeMenuItem("Trạng thái") { Menu = statusMenu };
        menu.Add(statusItem);

        menu.Add(new NativeMenuItemSeparator());

        var signOutItem = new NativeMenuItem("Đăng xuất");
        signOutItem.Click += (_, _) => SignOutRequested?.Invoke(this, EventArgs.Empty);
        menu.Add(signOutItem);

        var exitItem = new NativeMenuItem("Thoát");
        exitItem.Click += (_, _) => ExitRequested?.Invoke(this, EventArgs.Empty);
        menu.Add(exitItem);

        return menu;
    }

    public event EventHandler<UserStatus>? StatusSelected;
    public event EventHandler? SignOutRequested;
    public event EventHandler? ExitRequested;

    public void Dispose()
    {
        HideTrayIcon();
        GC.SuppressFinalize(this);
    }
}
