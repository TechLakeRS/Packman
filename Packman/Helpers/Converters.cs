using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace Packman.Helpers;

/// <summary>Converts a "#rrggbb" string into a SolidColorBrush (per-app tile colour).</summary>
public sealed class HexToBrushConverter : IValueConverter
{
    // Cached and frozen: rows re-run this on every recycle.
    private static readonly Dictionary<string, SolidColorBrush> Cache = new();

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var hex = value as string;
        if (string.IsNullOrEmpty(hex)) return Brushes.Gray;
        if (Cache.TryGetValue(hex, out var cached)) return cached;
        try
        {
            var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex));
            brush.Freeze();
            Cache[hex] = brush;
            return brush;
        }
        catch { return Brushes.Gray; }
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>Two-way equality against ConverterParameter, for segmented toggles.</summary>
public sealed class StringMatchConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => string.Equals(value as string, parameter as string, StringComparison.Ordinal);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => value is true ? parameter : Binding.DoNothing;
}

/// <summary>Maps a count to a star GridLength for proportional bars.</summary>
public sealed class CountToStarConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => new GridLength(System.Convert.ToDouble(value), GridUnitType.Star);

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => Binding.DoNothing;
}
