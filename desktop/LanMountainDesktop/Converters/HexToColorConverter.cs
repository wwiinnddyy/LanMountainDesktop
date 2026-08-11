using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace LanMountainDesktop.Converters;

public class HexToColorConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string hex && !string.IsNullOrWhiteSpace(hex))
        {
            try
            {
                return Color.Parse(hex);
            }
            catch
            {
                // Ignore parse errors
            }
        }

        return Colors.Gray;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
