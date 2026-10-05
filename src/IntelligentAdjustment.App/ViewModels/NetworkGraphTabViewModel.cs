using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IntelligentAdjustment.App.Services;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.App.ViewModels;

public enum DiagramExportFormat
{
    Png,
    Svg
}

public partial class NetworkGraphTabViewModel : WorkspaceTabViewModel
{
    private readonly ProjectDocumentViewModel document;

    [ObservableProperty]
    private LevelDifferenceRowViewModel? selectedDifference;

    [ObservableProperty]
    private string statusText = LocalizationService.Text("Loc.Graph.StatusInitial");

    [ObservableProperty]
    private int missingCoordinateCount;

    public NetworkGraphTabViewModel(
        ProjectDocumentViewModel document,
        IReadOnlyList<NetworkRoute> routes)
        : base("network-graph", LocalizationService.Text("Loc.Nav.NetworkGraph"))
    {
        this.document = document;
        Routes = routes;
        Edges = document.LevelDifferences;
        Refresh();
    }

    public ProjectDocumentViewModel Document => document;
    public ObservableCollection<LevelDifferenceRowViewModel> Edges { get; }
    public IReadOnlyList<NetworkRoute> Routes { get; private set; }

    public event EventHandler? FitRequested;
    public event EventHandler? RedrawRequested;
    public event EventHandler<DiagramExportFormat>? ExportRequested;

    public void LoadRoutes(IReadOnlyList<NetworkRoute> routes)
    {
        Routes = routes;
        Refresh();
    }

    public NetworkDiagramScene BuildScene() =>
        NetworkDiagramSceneBuilder.Build(document, Routes, document.Settings);

    public void Refresh()
    {
        NetworkDiagramScene scene = BuildScene();
        MissingCoordinateCount = scene.MissingCoordinateCount;
        StatusText = MissingCoordinateCount == 0
            ? LocalizationService.Text("Loc.Graph.StatusSavedCoordinates")
            : LocalizationService.Format("Loc.Graph.StatusMissingCoordinates", MissingCoordinateCount);

        RedrawRequested?.Invoke(this, EventArgs.Empty);
    }

    partial void OnSelectedDifferenceChanged(LevelDifferenceRowViewModel? value) =>
        RedrawRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void FitToView() => FitRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void ExportPng() => ExportRequested?.Invoke(this, DiagramExportFormat.Png);

    [RelayCommand]
    private void ExportSvg() => ExportRequested?.Invoke(this, DiagramExportFormat.Svg);
}
