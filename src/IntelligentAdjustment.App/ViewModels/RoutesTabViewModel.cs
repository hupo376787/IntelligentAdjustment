using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using IntelligentAdjustment.Domain;
using IntelligentAdjustment.App.Services;

namespace IntelligentAdjustment.App.ViewModels;

public partial class RoutesTabViewModel : WorkspaceTabViewModel
{
    [ObservableProperty]
    private bool isStale;

    public RoutesTabViewModel()
        : base("routes", LocalizationService.Text("Loc.Nav.Routes"))
    {
    }

    public ObservableCollection<RouteDisplayRow> Items { get; } = new();

    public void Load(
        IEnumerable<NetworkRoute> routes,
        ProjectSettings settings,
        bool isStale = false)
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

        IsStale = isStale;
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
