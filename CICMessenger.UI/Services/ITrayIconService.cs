using System;
using CICMessenger.Core.Presence;

namespace CICMessenger.UI.Services;

public interface ITrayIconService
{
    event EventHandler? TrayIconClicked;

    void ShowTrayIcon();
    void HideTrayIcon();
    void SetTooltip(string tooltip);
    void SetStatusIcon(UserStatus status);
}
