using IntelligentAdjustment.Application.Import;

namespace IntelligentAdjustment.App.Services;

public enum UnsavedChangesChoice
{
    Save,
    Discard,
    Cancel
}

public interface IUserDialogService
{
    string? PickNewProjectPath();
    string? PickProjectToOpen();
    IReadOnlyList<string> PickOutFiles();
    string? PickOutExportPath(string projectName);
    IReadOnlyList<string> PickInstrumentFiles(InstrumentImporterDescriptor descriptor);
    string? PickSaveAsProjectPath(string? currentProjectPath);
    string? PickReportDocxPath(string projectName);
    string? PickReportXlsxPath(string projectName);
    string? PickResultTextPath(string projectName);
    UnsavedChangesChoice AskUnsavedChanges();
    bool Confirm(string message, string title);
    void Info(string message, string title = "Intelligent Adjustment");
    void Error(string message, string title = "Intelligent Adjustment");
    void ShowAbout();
}
