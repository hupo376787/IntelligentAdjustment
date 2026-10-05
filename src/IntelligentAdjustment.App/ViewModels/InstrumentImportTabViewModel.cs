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
    private string fileSummary = LocalizationService.Text("Loc.Import.NoFiles");

    public InstrumentImportTabViewModel(
        ProjectSessionService session,
        IUserDialogService dialogs,
        Func<Task<bool>> ensureSaved,
        Action<ProjectWorkspace> applyWorkspace)
        : base("instrument-import", LocalizationService.Text("Loc.Nav.InstrumentImport"))
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
                ? LocalizationService.Text("Loc.Import.ParserReady")
                : LocalizationService.Text("Loc.Import.ParserPending");

    partial void OnSelectedImporterChanged(InstrumentImporterDescriptor? value)
    {
        SelectedFiles.Clear();
        FileSummary = LocalizationService.Text("Loc.Import.NoFiles");
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
            ? LocalizationService.Text("Loc.Import.NoFiles")
            : LocalizationService.Format("Loc.Import.SelectedFiles", files.Count);

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
            LocalizationService.Format(
                "Loc.Import.ImportedFiles",
                SelectedFiles.Count,
                SelectedImporter.DisplayName));

        SelectedFiles.Clear();
        FileSummary = LocalizationService.Text("Loc.Import.NoFiles");
        ImportCommand.NotifyCanExecuteChanged();
    }

    private bool CanImport() =>
        SelectedImporter is not null &&
        SelectedImporter.IsImplemented &&
        SelectedFiles.Count > 0;
}
