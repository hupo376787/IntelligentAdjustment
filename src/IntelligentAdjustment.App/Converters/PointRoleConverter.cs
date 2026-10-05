using System.Globalization;
using System.Windows.Data;
using IntelligentAdjustment.Domain;
using IntelligentAdjustment.App.Services;

namespace IntelligentAdjustment.App.Converters;

public sealed class PointRoleConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is PointRole role
            ? role == PointRole.AdjustmentPoint
                ? LocalizationService.Text("Loc.Role.Adjustment")
                : LocalizationService.Text("Loc.Role.Transition")
            : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string text = value?.ToString() ?? string.Empty;
        return string.Equals(
            text,
            LocalizationService.Text("Loc.Role.Transition"),
            StringComparison.OrdinalIgnoreCase)
            ? PointRole.TransitionPoint
            : PointRole.AdjustmentPoint;
    }
}

public sealed class RouteTypeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is RouteType routeType
            ? routeType == RouteType.ClosedLoop
                ? LocalizationService.Text("Loc.RouteType.Closed")
                : LocalizationService.Text("Loc.RouteType.Attached")
            : string.Empty;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
