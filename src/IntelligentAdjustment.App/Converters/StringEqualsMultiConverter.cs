using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace IntelligentAdjustment.App.Converters;

public sealed class StringEqualsMultiConverter : IMultiValueConverter
{
    public object Convert(
        object[] values,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        if (values.Length < 2
            || values[0] is null
            || values[1] is null
            || values[0] == DependencyProperty.UnsetValue
            || values[1] == DependencyProperty.UnsetValue)
        {
            return false;
        }

        return string.Equals(
            values[0].ToString(),
            values[1].ToString(),
            StringComparison.Ordinal);
    }

    public object[] ConvertBack(
        object value,
        Type[] targetTypes,
        object parameter,
        CultureInfo culture) =>
        throw new NotSupportedException();
}
