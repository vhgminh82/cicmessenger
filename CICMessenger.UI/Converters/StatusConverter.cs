using System;
using System.Globalization;
using Avalonia.Data.Converters;
using CICMessenger.Core.Presence;

namespace CICMessenger.UI.Converters;

public class StatusConverter : IValueConverter
{
    public static readonly StatusConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is UserStatus status)
        {
            return status switch
            {
                UserStatus.Online => "Trực tuyến",
                UserStatus.Busy => "Bận",
                UserStatus.BeRightBack => "Sẽ quay lại ngay",
                UserStatus.Away => "Vắng mặt",
                UserStatus.Idle => "Không hoạt động",
                UserStatus.Offline => "Ngoại tuyến",
                _ => status.ToString()
            };
        }
        return value;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
