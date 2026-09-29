using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace OpenDynamic.App.ViewModels;

public static class SettingsConverters
{
    public static readonly IValueConverter BooleanToVisibility = new BooleanToVisibilityConverterInternal();

    private sealed class BooleanToVisibilityConverterInternal : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            if (value is bool b && b)
            {
                return Visibility.Visible;
            }
            return Visibility.Collapsed;
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            return value is Visibility v && v == Visibility.Visible;
        }
    }
}
