using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace ClaudeMon.Converters;

/// <summary>
/// Converts a percentage double to display text.
/// Returns "Limit" when >= 99.5 (rounds to 100%), otherwise "{value:F0}%".
/// Pass ConverterParameter="~" to prefix estimated values with "~".
/// </summary>
public class PercentToTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not double percentage || percentage < 0)
            return "?";

        if (percentage >= 99.5)
            return "Limit";

        var prefix = parameter as string ?? string.Empty;
        return $"{prefix}{percentage:F0}%";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return DependencyProperty.UnsetValue;
    }
}
