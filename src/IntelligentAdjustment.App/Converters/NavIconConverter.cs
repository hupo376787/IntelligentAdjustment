using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace IntelligentAdjustment.App.Converters;

public sealed class NavIconConverter : IValueConverter
{
    private static readonly IReadOnlyDictionary<string, Geometry> Icons =
        new Dictionary<string, Geometry>(StringComparer.Ordinal)
        {
            ["dashboard"] = Geometry.Parse("M3,11 L12,3 L21,11 M5,10 L5,21 L19,21 L19,10 M9,21 L9,14 L15,14 L15,21"),
            ["lines"] = Geometry.Parse("M4,6 L20,6 M4,12 L20,12 M4,18 L20,18"),
            ["instrument-import"] = Geometry.Parse("M12,3 L12,16 M7,11 L12,16 L17,11 M5,21 L19,21"),
            ["raw-observations"] = Geometry.Parse("M4,4 L20,4 L20,20 L4,20 Z M4,9 L20,9 M9,4 L9,20"),
            ["differences"] = Geometry.Parse("M4,17 L8,10 L12,14 L17,5 L21,5 M17,5 L17,10 M17,5 L22,5"),
            ["known-heights"] = Geometry.Parse("M12,21 C12,21 5,14 5,9 A7,7 0 0 1 19,9 C19,14 12,21 12,21 Z M12,7 A2.5,2.5 0 1 0 12,12 A2.5,2.5 0 1 0 12,7"),
            ["map-sketch"] = Geometry.Parse("M5,17 L9,6 L18,10 M5,17 L18,10 M5,17 A2,2 0 1 0 5,21 A2,2 0 1 0 5,17 M9,4 A2,2 0 1 0 9,8 A2,2 0 1 0 9,4 M18,8 A2,2 0 1 0 18,12 A2,2 0 1 0 18,8"),
            ["action-search-routes"] = Geometry.Parse("M10,4 A6,6 0 1 0 10,16 A6,6 0 1 0 10,4 M15,15 L21,21"),
            ["routes"] = Geometry.Parse("M4,6 A2,2 0 1 0 4,10 A2,2 0 1 0 4,6 M20,14 A2,2 0 1 0 20,18 A2,2 0 1 0 20,14 M6,8 C12,8 12,16 18,16"),
            ["action-run-adjustment"] = Geometry.Parse("M5,12 L10,17 L20,7 M4,4 L20,4 M4,21 L20,21"),
            ["adjustment-results"] = Geometry.Parse("M5,20 L5,13 L9,13 L9,20 M11,20 L11,9 L15,9 L15,20 M17,20 L17,5 L21,5 L21,20"),
            ["network-graph"] = Geometry.Parse("M5,18 L9,7 L14,14 L19,5 M5,18 L14,14 M9,7 L19,5"),
            ["report"] = Geometry.Parse("M5,3 L15,3 L20,8 L20,21 L5,21 Z M15,3 L15,8 L20,8 M8,12 L17,12 M8,16 L17,16"),
            ["settings"] = Geometry.Parse("M12,4 A2,2 0 0 1 14,6 L14.5,7.5 L16,8 L17.5,7.5 L19.5,9.5 L19,11 L20,12.5 L21.5,13 L21.5,16 L20,16.5 L19,18 L19.5,19.5 L17.5,21.5 L16,21 L14.5,21.5 L14,23 L10,23 L9.5,21.5 L8,21 L6.5,21.5 L4.5,19.5 L5,18 L4,16.5 L2.5,16 L2.5,13 L4,12.5 L5,11 L4.5,9.5 L6.5,7.5 L8,8 L9.5,7.5 L10,6 A2,2 0 0 1 12,4 M12,10 A3,3 0 1 0 12,16 A3,3 0 1 0 12,10")
        };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        string key = value?.ToString() ?? string.Empty;
        return Icons.TryGetValue(key, out Geometry? geometry)
            ? geometry
            : Geometry.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
