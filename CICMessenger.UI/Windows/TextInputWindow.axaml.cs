using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace CICMessenger.UI.Windows;

/// <summary>Minimal single-line text prompt (rename, etc). Returns the trimmed text, or null if cancelled.</summary>
public partial class TextInputWindow : Window
{
    public TextInputWindow()
    {
        InitializeComponent();
    }

    public TextInputWindow(string prompt, string initialValue = "") : this()
    {
        promptText.Text = prompt;
        inputBox.Text = initialValue;
    }

    private void InputBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
            Submit();
        else if (e.Key == Key.Escape)
            Close(null);
    }

    private void Ok_Click(object? sender, RoutedEventArgs e) => Submit();

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close(null);

    private void Submit()
    {
        var text = inputBox.Text?.Trim();
        Close(string.IsNullOrEmpty(text) ? null : text);
    }
}
