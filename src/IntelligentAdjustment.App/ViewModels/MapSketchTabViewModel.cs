using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IntelligentAdjustment.App.Services;

namespace IntelligentAdjustment.App.ViewModels;

public enum DiagramNodeKind
{
    Normal,
    Known,
    Transition
}

public partial class MapSketchTabViewModel : WorkspaceTabViewModel
{
    private readonly ProjectDocumentViewModel document;

    [ObservableProperty]
    private string? selectedPointName;

    [ObservableProperty]
    private bool snapEnabled = true;

    [ObservableProperty]
    private double snapSize = 20.0;

    [ObservableProperty]
    private bool isPlacementMode;

    [ObservableProperty]
    private string statusText = LocalizationService.Text("Loc.Map.StatusHelp");

    public MapSketchTabViewModel(ProjectDocumentViewModel document)
        : base("map-sketch", LocalizationService.Text("Loc.Nav.MapSketch"))
    {
        this.document = document;
        Refresh();
    }

    public ProjectDocumentViewModel Document => document;
    public ObservableCollection<string> NetworkPointNames { get; } = new();
    public ObservableCollection<string> UnpositionedPointNames { get; } = new();

    public event EventHandler? FitRequested;
    public event EventHandler? RedrawRequested;

    public void Refresh()
    {
        string? selected = SelectedPointName;

        var lineOrder = document.Lines.ToDictionary(
            x => x.Id,
            x => x.DisplayOrder);

        var names = document.LevelDifferences
            .OrderBy(x => lineOrder.TryGetValue(x.LineId, out int order) ? order : int.MaxValue)
            .ThenBy(x => x.Sequence)
            .ThenBy(x => x.Id)
            .SelectMany(x => new[] { x.FromPoint.Trim(), x.ToPoint.Trim() })
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        NetworkPointNames.Clear();
        foreach (string name in names)
        {
            NetworkPointNames.Add(name);
        }

        var positioned = document.MapPoints
            .Select(x => x.PointName.Trim())
            .ToHashSet(StringComparer.Ordinal);

        UnpositionedPointNames.Clear();
        foreach (string name in names.Where(x => !positioned.Contains(x)))
        {
            UnpositionedPointNames.Add(name);
        }

        SelectedPointName = selected is not null && names.Contains(selected, StringComparer.Ordinal)
            ? selected
            : UnpositionedPointNames.FirstOrDefault() ?? names.FirstOrDefault();

        RedrawRequested?.Invoke(this, EventArgs.Empty);
    }

    public IReadOnlyDictionary<string, (double X, double Y)> GetPositions() =>
        document.MapPoints.ToDictionary(
            x => x.PointName,
            x => (x.X, x.Y),
            StringComparer.Ordinal);

    public DiagramNodeKind GetNodeKind(string pointName)
    {
        if (document.KnownHeights.Any(x =>
                string.Equals(x.PointName.Trim(), pointName, StringComparison.Ordinal)))
        {
            return DiagramNodeKind.Known;
        }

        if (document.LevelDifferences.Any(x =>
                string.Equals(x.ToPoint.Trim(), pointName, StringComparison.Ordinal) &&
                x.ToPointRole == IntelligentAdjustment.Domain.PointRole.TransitionPoint))
        {
            return DiagramNodeKind.Transition;
        }

        return DiagramNodeKind.Normal;
    }

    public void PlacePoint(string pointName, double x, double y)
    {
        if (string.IsNullOrWhiteSpace(pointName))
        {
            return;
        }

        (x, y) = Snap(x, y);
        string normalized = pointName.Trim();

        document.ExecuteUndoable(() =>
        {
            NetworkMapPointRowViewModel? existing = document.MapPoints.FirstOrDefault(p =>
                string.Equals(p.PointName, normalized, StringComparison.Ordinal));

            if (existing is null)
            {
                document.MapPoints.Add(new NetworkMapPointRowViewModel
                {
                    PointName = normalized,
                    X = x,
                    Y = y,
                    UpdatedAtUtc = DateTimeOffset.UtcNow
                });
            }
            else
            {
                existing.X = x;
                existing.Y = y;
                existing.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }
        });

        IsPlacementMode = false;
        StatusText = LocalizationService.Format("Loc.Map.Positioned", normalized, x, y);
        Refresh();
    }

    public void MovePoints(
        IReadOnlyCollection<string> pointNames,
        double deltaX,
        double deltaY)
    {
        if (pointNames.Count == 0 ||
            (Math.Abs(deltaX) < double.Epsilon && Math.Abs(deltaY) < double.Epsilon))
        {
            return;
        }

        HashSet<string> selected = pointNames.ToHashSet(StringComparer.Ordinal);

        document.ExecuteUndoable(() =>
        {
            foreach (NetworkMapPointRowViewModel point in document.MapPoints.Where(x => selected.Contains(x.PointName)))
            {
                (double x, double y) = Snap(point.X + deltaX, point.Y + deltaY);
                point.X = x;
                point.Y = y;
                point.UpdatedAtUtc = DateTimeOffset.UtcNow;
            }
        });

        Refresh();
    }

    public void DeleteCoordinates(IReadOnlyCollection<string> pointNames)
    {
        if (pointNames.Count == 0)
        {
            return;
        }

        HashSet<string> selected = pointNames.ToHashSet(StringComparer.Ordinal);

        document.ExecuteUndoable(() =>
        {
            foreach (NetworkMapPointRowViewModel point in document.MapPoints
                         .Where(x => selected.Contains(x.PointName))
                         .ToArray())
            {
                document.MapPoints.Remove(point);
            }
        });

        StatusText = LocalizationService.Format("Loc.Map.DeletedCoordinates", selected.Count);
        Refresh();
    }

    [RelayCommand]
    private void StartPlacement()
    {
        if (string.IsNullOrWhiteSpace(SelectedPointName))
        {
            StatusText = LocalizationService.Text("Loc.Map.SelectPointFirst");
            return;
        }

        IsPlacementMode = true;
        StatusText = LocalizationService.Format("Loc.Map.ClickToPlace", SelectedPointName);
    }

    [RelayCommand]
    private void CancelPlacement()
    {
        IsPlacementMode = false;
        StatusText = LocalizationService.Text("Loc.Map.PlacementCancelled");
    }

    [RelayCommand]
    private void FitToView() => FitRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void AutoLayoutUnpositioned()
    {
        string[] missing = UnpositionedPointNames.ToArray();
        if (missing.Length == 0)
        {
            StatusText = LocalizationService.Text("Loc.Map.AllPositioned");
            return;
        }

        double centerX = document.MapPoints.Count == 0 ? 300 : document.MapPoints.Average(x => x.X);
        double centerY = document.MapPoints.Count == 0 ? 220 : document.MapPoints.Average(x => x.Y);
        double radius = Math.Max(120, 36 * missing.Length);

        document.ExecuteUndoable(() =>
        {
            AddCircularLayout(missing, centerX, centerY, radius);
        });

        StatusText = LocalizationService.Format("Loc.Map.AutoLaidOut", missing.Length);
        Refresh();
        FitRequested?.Invoke(this, EventArgs.Empty);
    }

    public void AutoLayoutAllPoints()
    {
        string[] names = NetworkPointNames.ToArray();
        if (names.Length == 0)
        {
            StatusText = LocalizationService.Text("Loc.Map.NoPointsToLayout");
            return;
        }

        double centerX = document.MapPoints.Count == 0 ? 300 : document.MapPoints.Average(x => x.X);
        double centerY = document.MapPoints.Count == 0 ? 220 : document.MapPoints.Average(x => x.Y);
        double radius = Math.Max(160, 32 * Math.Max(6, names.Length));

        document.ExecuteUndoable(() =>
        {
            document.MapPoints.Clear();
            AddCircularLayout(names, centerX, centerY, radius);
        });

        StatusText = LocalizationService.Format("Loc.Map.RelayoutAll", names.Length);
        Refresh();
        FitRequested?.Invoke(this, EventArgs.Empty);
    }

    public void ClearAllCoordinates()
    {
        if (document.MapPoints.Count == 0)
        {
            StatusText = LocalizationService.Text("Loc.Map.NoSavedCoordinates");
            return;
        }

        int count = document.MapPoints.Count;
        document.ExecuteUndoable(document.MapPoints.Clear);
        StatusText = LocalizationService.Format("Loc.Map.ClearedCoordinates", count);
        Refresh();
        FitRequested?.Invoke(this, EventArgs.Empty);
    }

    private void AddCircularLayout(
        IReadOnlyList<string> pointNames,
        double centerX,
        double centerY,
        double radius)
    {
        for (int i = 0; i < pointNames.Count; i++)
        {
            double angle = Math.PI * 2 * i / pointNames.Count - Math.PI / 2;
            (double x, double y) = Snap(
                centerX + Math.Cos(angle) * radius,
                centerY + Math.Sin(angle) * radius);

            document.MapPoints.Add(new NetworkMapPointRowViewModel
            {
                PointName = pointNames[i],
                X = x,
                Y = y,
                UpdatedAtUtc = DateTimeOffset.UtcNow
            });
        }
    }

    public void BeginRelocate(string pointName)
    {
        SelectedPointName = pointName;
        IsPlacementMode = true;
        StatusText = LocalizationService.Format("Loc.Map.RelocatePrompt", pointName);
    }

    public (double X, double Y) Snap(double x, double y)
    {
        if (!SnapEnabled || !double.IsFinite(SnapSize) || SnapSize <= 0)
        {
            return (x, y);
        }

        return (
            Math.Round(x / SnapSize) * SnapSize,
            Math.Round(y / SnapSize) * SnapSize);
    }
}
