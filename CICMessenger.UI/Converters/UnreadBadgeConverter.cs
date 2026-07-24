using System;
using System.Globalization;
using Avalonia.Data.Converters;
using CICMessenger.Client;
using CICMessenger.UI.Models;
using CICMessenger.UI.Services;

namespace CICMessenger.UI.Converters;

/// <summary>Formats the unread suffix shown next to a contact/room name, e.g. " (3 tin mới)", or "" when read.</summary>
public class UnreadBadgeConverter : IValueConverter
{
    public static readonly UnreadBadgeConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var id = EntityId(value);
        if (id == null)
            return "";

        int count = UnreadTracker.GetCount(id);
        return count > 0 ? $" ({count} tin mới)" : "";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();

    internal static string? EntityId(object? value) => value switch
    {
        IBuddy buddy => buddy.Id,
        Room room => room.Id,
        _ => null
    };
}
