using System;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Squiggle.UI.Windows;

public partial class EmoticonPicker : Window
{
    private static readonly string[] Emoticons =
    {
        "😊", "😂", "😍", "🤔", "😢", "😡", "👍", "👎",
        "❤️", "🎉", "🔥", "✨", "😎", "🙏", "💪", "👋",
        "😅", "🤣", "😘", "😜", "😱", "🤗", "🙄", "😴"
    };

    public string? SelectedEmoticon { get; private set; }

    public EmoticonPicker()
    {
        InitializeComponent();
        BuildEmoticonGrid();
    }

    private void BuildEmoticonGrid()
    {
        foreach (var emoji in Emoticons)
        {
            var button = new Button
            {
                Content = emoji,
                FontSize = 20,
                Width = 40,
                Height = 40,
                Margin = new Avalonia.Thickness(2),
                Background = Avalonia.Media.Brushes.Transparent,
                BorderThickness = new Avalonia.Thickness(0)
            };
            button.Click += EmoticonButton_Click;
            emoticonPanel.Children.Add(button);
        }
    }

    private void EmoticonButton_Click(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Content is string emoji)
        {
            SelectedEmoticon = emoji;
            Close(emoji);
        }
    }

    protected override void OnLostFocus(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnLostFocus(e);
        Close(null);
    }
}
