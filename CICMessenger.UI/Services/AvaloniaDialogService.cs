using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;
using CICMessenger.UI.Windows;

namespace CICMessenger.UI.Services;

public class AvaloniaDialogService : IDialogService
{
    private Window? GetMainWindow()
    {
        if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            return desktop.MainWindow;
        return null;
    }

    /// <summary>
    /// Finds a window that can own a modal dialog. Avalonia throws if the owner is hidden,
    /// and the main window is hidden whenever the app is minimised to the tray — which used
    /// to crash the app on any dialog (update check, incoming file prompt, ...).
    /// </summary>
    private Window? GetDialogOwner()
    {
        if (Application.Current?.ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop)
            return null;

        foreach (var window in desktop.Windows)
            if (window.IsVisible && window.IsActive)
                return window;

        foreach (var window in desktop.Windows)
            if (window.IsVisible)
                return window;

        // Everything is hidden (tray mode): bring the main window back so the dialog has a
        // valid owner and the user can actually see what is being asked.
        var main = desktop.MainWindow;
        if (main != null)
        {
            main.Show();
            main.WindowState = WindowState.Normal;
            main.Activate();
            return main;
        }

        return null;
    }

    private IStorageProvider? GetStorageProvider()
    {
        var window = GetDialogOwner() ?? GetMainWindow();
        return window != null ? TopLevel.GetTopLevel(window)?.StorageProvider : null;
    }

    public async Task<IStorageFile?> OpenFileAsync(string title, params FilePickerFileType[] filters)
    {
        var provider = GetStorageProvider();
        if (provider == null)
            return null;

        var results = await provider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = filters.Length > 0 ? filters : null,
        });

        return results.FirstOrDefault();
    }

    public async Task<IStorageFile?> SaveFileAsync(string title, string? defaultFileName, params FilePickerFileType[] filters)
    {
        var provider = GetStorageProvider();
        if (provider == null)
            return null;

        return await provider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = title,
            SuggestedFileName = defaultFileName,
            FileTypeChoices = filters.Length > 0 ? filters : null,
        });
    }

    public async Task<IStorageFolder?> OpenFolderAsync(string title)
    {
        var provider = GetStorageProvider();
        if (provider == null)
            return null;

        var results = await provider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        });

        return results.FirstOrDefault();
    }

    public async Task<MessageBoxResult> ShowMessageBoxAsync(string title, string message, MessageBoxButton buttons = MessageBoxButton.Ok)
    {
        var dialog = new MessageBoxWindow(title, message, buttons);
        var owner = GetDialogOwner();

        if (owner != null && owner.IsVisible)
            return await dialog.ShowDialog<MessageBoxResult>(owner);

        // No window we can hang a modal off — show it standalone rather than throwing,
        // which would take the whole app down from an async void event handler.
        var closed = new TaskCompletionSource<MessageBoxResult>();
        dialog.Closed += (_, _) => closed.TrySetResult(dialog.Result);
        dialog.Show();
        dialog.Activate();
        return await closed.Task;
    }
}
