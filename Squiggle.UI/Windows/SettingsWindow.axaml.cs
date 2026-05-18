using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Microsoft.Extensions.DependencyInjection;
using Squiggle.UI.Services;
using Squiggle.UI.ViewModel;

namespace Squiggle.UI.Windows;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _viewModel;

    public SettingsWindow()
    {
        InitializeComponent();
        var settingsService = App.Services.GetRequiredService<SettingsService>();
        _viewModel = settingsService.Load();
        DataContext = _viewModel;
    }

    public SettingsWindow(SettingsViewModel viewModel) : this()
    {
        _viewModel = viewModel;
        DataContext = _viewModel;
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e)
    {
        var settingsService = App.Services.GetRequiredService<SettingsService>();
        settingsService.Save(_viewModel);

        var themeService = App.Services.GetRequiredService<ThemeService>();
        themeService.ApplyTheme(_viewModel.GeneralSettings.ThemeMode);
        Close(true);
    }

    private void CancelButton_Click(object? sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private async void BrowseFolder_Click(object? sender, RoutedEventArgs e)
    {
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            Title = "Select Downloads Folder",
            AllowMultiple = false
        });

        if (folders.Count > 0)
        {
            _viewModel.GeneralSettings.DownloadsFolder = folders[0].Path.LocalPath;
        }
    }
}
