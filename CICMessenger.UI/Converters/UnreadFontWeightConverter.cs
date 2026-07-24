using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using CICMessenger.UI.Services;

namespace CICMessenger.UI.Converters;

/// <summary>Bolds a contact/room's name while it has an unread message.</summary>
public class UnreadFontWeightConverter : IValueConverter
{
    public static readonly UnreadFontWeightConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var id = UnreadBadgeConverter.EntityId(value);
        bool hasUnread = id != null && UnreadTracker.GetCount(id) > 0;
        return hasUnread ? FontWeight.Bold : FontWeight.Normal;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
