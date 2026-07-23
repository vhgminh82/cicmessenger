using Avalonia.Controls;
using Avalonia.Interactivity;
using Squiggle.UI.Services;

namespace Squiggle.UI.Windows;

public partial class MessageBoxWindow : Window
{
    private readonly MessageBoxButton _buttons;

    /// <summary>Outcome, for when the box is shown without an owner to be modal against.</summary>
    public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

    public MessageBoxWindow()
    {
        InitializeComponent();
        _buttons = MessageBoxButton.Ok;
    }

    public MessageBoxWindow(string title, string message, MessageBoxButton buttons)
    {
        InitializeComponent();
        _buttons = buttons;

        Title = title;
        txtMessage.Text = message;

        switch (buttons)
        {
            case MessageBoxButton.Ok:
                btnPrimary.Content = "Đồng ý";
                break;
            case MessageBoxButton.OkCancel:
                btnPrimary.Content = "Đồng ý";
                btnSecondary.Content = "Hủy";
                btnSecondary.IsVisible = true;
                break;
            case MessageBoxButton.YesNo:
                btnPrimary.Content = "Có";
                btnSecondary.Content = "Không";
                btnSecondary.IsVisible = true;
                break;
        }
    }

    private void PrimaryButton_Click(object? sender, RoutedEventArgs e)
    {
        Result = _buttons switch
        {
            MessageBoxButton.YesNo => MessageBoxResult.Yes,
            _ => MessageBoxResult.Ok,
        };
        Close(Result);
    }

    private void SecondaryButton_Click(object? sender, RoutedEventArgs e)
    {
        Result = _buttons switch
        {
            MessageBoxButton.YesNo => MessageBoxResult.No,
            _ => MessageBoxResult.Cancel,
        };
        Close(Result);
    }
}
