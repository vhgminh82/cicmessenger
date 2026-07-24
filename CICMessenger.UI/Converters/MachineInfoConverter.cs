using System;
using System.Globalization;
using Avalonia.Data.Converters;
using CICMessenger.Client;

namespace CICMessenger.UI.Converters;

/// <summary>
/// Formats a buddy's machine name and IP address as "MachineName - IP", tolerating
/// either piece being unavailable (buddy just discovered, or never came online).
/// </summary>
public class MachineInfoConverter : IValueConverter
{
    public static readonly MachineInfoConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not IBuddy buddy)
            return null;

        var machineName = buddy.Properties?.MachineName;
        var ip = buddy.ChatEndPoint?.Address?.ToString();

        if (string.IsNullOrEmpty(machineName) && string.IsNullOrEmpty(ip))
            return null;
        if (string.IsNullOrEmpty(ip))
            return machineName;
        if (string.IsNullOrEmpty(machineName))
            return ip;
        return $"{machineName} - {ip}";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
