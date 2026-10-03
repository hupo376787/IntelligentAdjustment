using System.Collections.ObjectModel;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.App.ViewModels;

public sealed class RoutesTabViewModel : WorkspaceTabViewModel
{
    public RoutesTabViewModel()
        : base("routes", "闭合/附合路线")
    {
    }

    public ObservableCollection<RouteDisplayRow> Items { get; } = new();

    public void Load(IEnumerable<NetworkRoute> routes, ProjectSettings settings)
    {
        Items.Clear();
        foreach (var route in routes)
        {
            double activeTolerance = settings.ToleranceMode == ClosureToleranceMode.Distance
                ? route.LengthToleranceMeters
                : route.StationToleranceMeters;

            Items.Add(new RouteDisplayRow(
                route.Index,
                route.RouteType,
                string.Join(" → ", route.Points),
                route.EdgeCount,
                route.LengthMeters,
                route.StationCount,
                route.ClosureMeters * 1000.0,
                activeTolerance * 1000.0,
                Math.Abs(route.ClosureMeters) > activeTolerance));
        }
    }
}

public sealed record RouteDisplayRow(
    int Index,
    RouteType RouteType,
    string PointPath,
    int EdgeCount,
    double LengthMeters,
    int StationCount,
    double ClosureMillimeters,
    double ActiveToleranceMillimeters,
    bool IsOverLimit);
