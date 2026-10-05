using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IntelligentAdjustment.App.Services;
using IntelligentAdjustment.Application.Models;
using IntelligentAdjustment.Application.Reporting;
using IntelligentAdjustment.Application.Services;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.App.ViewModels;

public partial class ReportTabViewModel : WorkspaceTabViewModel
{
    private readonly ProjectDocumentViewModel document;
    private readonly ProjectSessionService session;
    private readonly IUserDialogService dialogs;
    private readonly Func<Task<bool>> ensureSaved;
    private readonly ReportExportService exporter = new();
    private readonly ResultTextExportService textExporter = new();

    [ObservableProperty]
    private string statusText = LocalizationService.Text("Loc.Report.StatusInitial");

    public ReportTabViewModel(
        ProjectDocumentViewModel document,
        ProjectSessionService session,
        IUserDialogService dialogs,
        Func<Task<bool>> ensureSaved)
        : base("report", LocalizationService.Text("Loc.Nav.Report"))
    {
        this.document = document;
        this.session = session;
        this.dialogs = dialogs;
        this.ensureSaved = ensureSaved;
    }

    public ProjectDocumentViewModel Document => document;

    [RelayCommand]
    private async Task ExportDocxAsync()
    {
        if (!await ensureSaved())
        {
            return;
        }

        ProjectWorkspace workspace = await session.LoadAsync();
        CalculationBundle? calculation = await session.LoadLatestCalculationAsync();

        if (!ConfirmStaleResult(calculation))
        {
            return;
        }

        string? path = dialogs.PickReportDocxPath(workspace.Metadata.ProjectName);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        byte[]? networkSketchPng = BuildNetworkSketchPng(workspace, calculation);
        exporter.ExportDocx(workspace, calculation, path, networkSketchPng);

        // NPOI preserves the Word TOC field but does not have Word's pagination
        // engine. When Microsoft Word is installed, silently paginate, update the
        // cached TOC and save now so the user opens an already-updated report.
        _ = WordReportPostProcessor.TryRefreshTableOfContents(path);

        StatusText = LocalizationService.Format("Loc.Report.ExportedDocx", path);
        dialogs.Info(StatusText);
    }

    [RelayCommand]
    private async Task ExportXlsxAsync()
    {
        if (!await ensureSaved())
        {
            return;
        }

        ProjectWorkspace workspace = await session.LoadAsync();
        CalculationBundle? calculation = await session.LoadLatestCalculationAsync();

        if (!ConfirmStaleResult(calculation))
        {
            return;
        }

        string? path = dialogs.PickReportXlsxPath(workspace.Metadata.ProjectName);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        exporter.ExportXlsx(workspace, calculation, path);
        StatusText = LocalizationService.Format("Loc.Report.ExportedXlsx", path);
        dialogs.Info(StatusText);
    }

    [RelayCommand]
    private async Task ExportTextAsync()
    {
        if (!await ensureSaved())
        {
            return;
        }

        ProjectWorkspace workspace = await session.LoadAsync();
        CalculationBundle? calculation = await session.LoadLatestCalculationAsync();

        if (!ConfirmStaleResult(calculation))
        {
            return;
        }

        string? path = dialogs.PickResultTextPath(workspace.Metadata.ProjectName);
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        textExporter.Export(workspace, calculation, path);
        StatusText = LocalizationService.Format("Loc.Report.ExportedTxt", path);
        dialogs.Info(StatusText);
    }

    private byte[]? BuildNetworkSketchPng(
        ProjectWorkspace workspace,
        CalculationBundle? calculation)
    {
        if (document.LevelDifferences.Count == 0)
        {
            return null;
        }

        IReadOnlyList<NetworkRoute> routes =
            calculation?.Routes ?? Array.Empty<NetworkRoute>();

        NetworkDiagramScene scene = NetworkDiagramSceneBuilder.Build(
            document,
            routes,
            workspace.Settings);

        if (scene.Nodes.Count == 0)
        {
            return null;
        }

        string tempFile = Path.Combine(
            Path.GetTempPath(),
            $"IntelligentAdjustment-report-network-{Guid.NewGuid():N}.png");

        try
        {
            NetworkDiagramExporter.ExportPng(
                scene,
                tempFile,
                width: 1800,
                height: 1100,
                settings: workspace.Settings);

            return File.ReadAllBytes(tempFile);
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                File.Delete(tempFile);
            }
        }
    }

    private bool ConfirmStaleResult(CalculationBundle? calculation)
    {
        if (calculation is null)
        {
            return dialogs.Confirm(
                LocalizationService.Text("Loc.Report.NoAdjustmentConfirm"),
                LocalizationService.Text("Loc.Report.ExportTitle"));
        }

        if (calculation.Revision.ResultsAreStale)
        {
            return dialogs.Confirm(
                LocalizationService.Text("Loc.Report.StaleConfirm"),
                LocalizationService.Text("Loc.Report.ExportTitle"));
        }

        return true;
    }
}
