using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IntelligentAdjustment.App.Services;
using IntelligentAdjustment.Application.Models;
using IntelligentAdjustment.Application.Reporting;
using IntelligentAdjustment.Application.Services;

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
    private string statusText = "报告章节内容随工程保存；DOCX/XLSX 导出不依赖本机 Office。";

    public ReportTabViewModel(
        ProjectDocumentViewModel document,
        ProjectSessionService session,
        IUserDialogService dialogs,
        Func<Task<bool>> ensureSaved)
        : base("report", "报告与导出")
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

        exporter.ExportDocx(workspace, calculation, path);
        StatusText = $"DOCX 已导出：{path}";
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
        StatusText = $"XLSX 已导出：{path}";
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
        StatusText = $"TXT 已导出：{path}";
        dialogs.Info(StatusText);
    }

    private bool ConfirmStaleResult(CalculationBundle? calculation)
    {
        if (calculation is null)
        {
            return dialogs.Confirm(
                "当前工程尚未执行高程平差。报告仍可导出，但平差结果、闭合路线和精度统计将为空。是否继续？",
                "导出报告");
        }

        if (calculation.Revision.ResultsAreStale)
        {
            return dialogs.Confirm(
                "当前平差结果基于旧数据。导出的报告会明确标注“当前结果基于旧数据”。是否继续？",
                "导出报告");
        }

        return true;
    }
}
