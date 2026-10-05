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
            ["settings"] = Geometry.Parse("M12,4 A2,2 0 0 1 14,6 L14.5,7.5 L16,8 L17.5,7.5 L19.5,9.5 L19,11 L20,12.5 L21.5,13 L21.5,16 L20,16.5 L19,18 L19.5,19.5 L17.5,21.5 L16,21 L14.5,21.5 L14,23 L10,23 L9.5,21.5 L8,21 L6.5,21.5 L4.5,19.5 L5,18 L4,16.5 L2.5,16 L2.5,13 L4,12.5 L5,11 L4.5,9.5 L6.5,7.5 L8,8 L9.5,7.5 L10,6 A2,2 0 0 1 12,4 M12,10 A3,3 0 1 0 12,16 A3,3 0 1 0 12,10"),
            ["menu-file"] = Geometry.Parse("M4,4 L10,4 L12,7 L21,7 L21,20 L4,20 Z"),
            ["menu-edit"] = Geometry.Parse("M5,19 L8,19 L19,8 L16,5 L5,16 Z M14.5,6.5 L17.5,9.5"),
            ["menu-calculate"] = Geometry.Parse("M5,4 L19,4 L19,20 L5,20 Z M8,8 L16,8 M8,12 L10,12 M14,12 L16,12 M8,16 L10,16 M14,16 L16,16"),
            ["menu-view"] = Geometry.Parse("M3,12 C6,7 9,5 12,5 C15,5 18,7 21,12 C18,17 15,19 12,19 C9,19 6,17 3,12 Z M12,9 A3,3 0 1 0 12,15 A3,3 0 1 0 12,9"),
            ["menu-tools"] = Geometry.Parse("M5,19 L10,14 M14,10 L19,5 M8,4 L11,7 L7,11 L4,8 M16,13 L20,17 L17,20 L13,16"),
            ["menu-help"] = Geometry.Parse("M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 M9.5,9 A2.5,2.5 0 1 1 13,11.3 C12,12 12,13 12,14 M12,17 L12,17.1"),
            ["menu-new"] = Geometry.Parse("M12,4 L12,20 M4,12 L20,12"),
            ["menu-open"] = Geometry.Parse("M3,7 L9,7 L11,10 L21,10 L19,20 L3,20 Z"),
            ["menu-save"] = Geometry.Parse("M4,3 L18,3 L21,6 L21,21 L3,21 L3,3 Z M7,3 L7,9 L17,9 L17,3 M7,14 L17,14 L17,20 L7,20 Z"),
            ["menu-save-as"] = Geometry.Parse("M4,3 L17,3 L20,6 L20,14 M7,3 L7,9 L16,9 L16,3 M7,14 L13,14 M14,20 L20,14 M18,14 L20,16"),
            ["menu-close"] = Geometry.Parse("M5,5 L19,19 M19,5 L5,19"),
            ["menu-import"] = Geometry.Parse("M12,3 L12,16 M7,11 L12,16 L17,11 M5,21 L19,21"),
            ["menu-export"] = Geometry.Parse("M12,21 L12,8 M7,13 L12,8 L17,13 M5,3 L19,3"),
            ["menu-exit"] = Geometry.Parse("M10,4 L5,4 L5,20 L10,20 M14,8 L19,12 L14,16 M19,12 L9,12"),
            ["menu-undo"] = Geometry.Parse("M10,6 L5,10 L10,14 M6,10 L14,10 C18,10 20,13 20,17"),
            ["menu-redo"] = Geometry.Parse("M14,6 L19,10 L14,14 M18,10 L10,10 C6,10 4,13 4,17"),
            ["menu-about"] = Geometry.Parse("M12,3 A9,9 0 1 0 12,21 A9,9 0 1 0 12,3 M12,10 L12,17 M12,7 L12,7.1")
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
