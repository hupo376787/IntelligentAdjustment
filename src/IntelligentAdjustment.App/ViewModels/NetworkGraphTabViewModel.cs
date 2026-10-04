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
    private string statusText = "水准网图由草图坐标与当前高差网络拓扑生成。";

    [ObservableProperty]
    private int missingCoordinateCount;

    public NetworkGraphTabViewModel(
        ProjectDocumentViewModel document,
        IReadOnlyList<NetworkRoute> routes)
        : base("network-graph", "水准网图")
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
            ? "水准网图使用全部已保存草图坐标。点击图中测段可联动右侧表格。"
            : $"{MissingCoordinateCount} 个点尚未设置草图坐标，当前以临时布局显示；到“网形草图”定位后会自动替换。";

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
