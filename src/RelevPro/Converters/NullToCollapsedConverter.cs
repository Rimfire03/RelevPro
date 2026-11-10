using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace RelevPro.Converters;

/// <summary>Masque un élément quand la valeur liée est null.</summary>
public class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
