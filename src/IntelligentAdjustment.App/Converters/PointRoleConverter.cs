using System.Globalization;
using System.Windows.Data;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.App.Converters;

public sealed class PointRoleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is PointRole role
            ? role == PointRole.AdjustmentPoint ? "平差点" : "过渡点"
            : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string text = value?.ToString() ?? string.Empty;
        return text == "过渡点" ? PointRole.TransitionPoint : PointRole.AdjustmentPoint;
    }
}

public sealed class RouteTypeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is RouteType routeType
            ? routeType == RouteType.ClosedLoop ? "闭合环" : "附合路线"
            : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
