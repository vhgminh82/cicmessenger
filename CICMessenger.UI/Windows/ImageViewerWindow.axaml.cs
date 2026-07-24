using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;

namespace CICMessenger.UI.Windows;

/// <summary>Full-size popup for an image chat attachment. Click anywhere (or the ✕) to close.</summary>
public partial class ImageViewerWindow : Window
{
    public ImageViewerWindow()
    {
        InitializeComponent();
    }

    public ImageViewerWindow(Bitmap image, string title) : this()
    {
        fullImage.Source = image;
        Title = title;
    }

    private void Image_PointerPressed(object? sender, PointerPressedEventArgs e) => Close();

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
