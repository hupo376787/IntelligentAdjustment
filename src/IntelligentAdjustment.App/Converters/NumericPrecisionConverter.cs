using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace IntelligentAdjustment.App.Converters;

public sealed class NumericPrecisionConverter : IMultiValueConverter
{
    public object Convert(
        object[] values,
        Type targetType,
        object parameter,
        CultureInfo culture)
    {
        if (values.Length == 0 ||
            values[0] is null ||
            values[0] == DependencyProperty.UnsetValue)
        {
            return string.Empty;
        }

        int decimals = values.Length > 1 && values[1] is int configured
            ? Math.Clamp(configured, 0, 10)
            : 4;

        return values[0] is IFormattable formattable
            ? formattable.ToString($"F{decimals}", culture) ?? string.Empty
            : values[0].ToString() ?? string.Empty;
    }

    public object[] ConvertBack(
        object value,
        Type[] targetTypes,
        object parameter,
        CultureInfo culture)
    {
        object[] result = Enumerable
            .Repeat<object>(Binding.DoNothing, targetTypes.Length)
            .ToArray();

        if (targetTypes.Length == 0)
        {
            return result;
        }

        string text = value?.ToString()?.Trim() ?? string.Empty;
        Type valueType = Nullable.GetUnderlyingType(targetTypes[0]) ?? targetTypes[0];

        if (text.Length == 0 && Nullable.GetUnderlyingType(targetTypes[0]) is not null)
        {
            result[0] = null!;
            return result;
        }

        if (valueType == typeof(double) &&
            double.TryParse(
                text,
                NumberStyles.Float | NumberStyles.AllowThousands,
                culture,
                out double parsed))
        {
            result[0] = parsed;
        }

        return result;
    }
}
