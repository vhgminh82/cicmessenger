using System.Reflection;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Squiggle.UI.Windows;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        var version = Assembly.GetExecutingAssembly().GetName().Version;
        txtVersion.Text = $"Version {version?.ToString(3) ?? "4.0.1"}";
    }

    private void OkButton_Click(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}
