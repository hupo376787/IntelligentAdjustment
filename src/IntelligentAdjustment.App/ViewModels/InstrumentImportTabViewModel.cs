using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IntelligentAdjustment.App.Services;
using IntelligentAdjustment.Application.Import;
using IntelligentAdjustment.Application.Models;
using IntelligentAdjustment.Application.Services;

namespace IntelligentAdjustment.App.ViewModels;

public partial class InstrumentImportTabViewModel : WorkspaceTabViewModel
{
    private readonly ProjectSessionService session;
    private readonly IUserDialogService dialogs;
    private readonly Func<Task<bool>> ensureSaved;
    private readonly Action<ProjectWorkspace> applyWorkspace;

    [ObservableProperty]
    private InstrumentImporterDescriptor? selectedImporter;

    [ObservableProperty]
    private string fileSummary = "尚未选择文件";

    public InstrumentImportTabViewModel(
        ProjectSessionService session,
        IUserDialogService dialogs,
        Func<Task<bool>> ensureSaved,
        Action<ProjectWorkspace> applyWorkspace)
        : base("instrument-import", "仪器导入")
    {
        this.session = session;
        this.dialogs = dialogs;
        this.ensureSaved = ensureSaved;
        this.applyWorkspace = applyWorkspace;

        Catalog = session.InstrumentImportCatalog;
        SelectedImporter = Catalog.FirstOrDefault(x => x.IsImplemented)
            ?? Catalog.FirstOrDefault();
    }

    public IReadOnlyList<InstrumentImporterDescriptor> Catalog { get; }
    public ObservableCollection<string> SelectedFiles { get; } = new();

    public string SupportText =>
        SelectedImporter is null
            ? string.Empty
            : SelectedImporter.IsImplemented
                ? "当前解析器已可用。多选文件时默认“一文件一线路”导入。"
                : "该厂商解析器框架已预留；需要结合对应样本格式完成解析器后才能导入。";

    partial void OnSelectedImporterChanged(InstrumentImporterDescriptor? value)
    {
        SelectedFiles.Clear();
        FileSummary = "尚未选择文件";
        OnPropertyChanged(nameof(SupportText));
        ChooseFilesCommand.NotifyCanExecuteChanged();
        ImportCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(CanChooseFiles))]
    private void ChooseFiles()
    {
        if (SelectedImporter is null)
        {
            return;
        }

        IReadOnlyList<string> files = dialogs.PickInstrumentFiles(SelectedImporter);
        SelectedFiles.Clear();
        foreach (string file in files)
        {
            SelectedFiles.Add(file);
        }

        FileSummary = files.Count == 0
            ? "尚未选择文件"
            : $"已选择 {files.Count} 个文件";

        ImportCommand.NotifyCanExecuteChanged();
    }

    private bool CanChooseFiles() =>
        SelectedImporter is not null &&
        SelectedImporter.IsImplemented;

    [RelayCommand(CanExecute = nameof(CanImport))]
    private async Task ImportAsync()
    {
        if (SelectedImporter is null || SelectedFiles.Count == 0)
        {
            return;
        }

        if (!await ensureSaved())
        {
            return;
        }

        ProjectWorkspace workspace = await session.ImportInstrumentFilesAsync(
            SelectedImporter.Vendor,
            SelectedFiles.ToArray());

        applyWorkspace(workspace);
        dialogs.Info(
            $"已导入 {SelectedFiles.Count} 个 {SelectedImporter.DisplayName} 文件。\n每个文件建立一条线路。");

        SelectedFiles.Clear();
        FileSummary = "尚未选择文件";
        ImportCommand.NotifyCanExecuteChanged();
    }

    private bool CanImport() =>
        SelectedImporter is not null &&
        SelectedImporter.IsImplemented &&
        SelectedFiles.Count > 0;
}
