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
    string? PickSaveAsProjectPath(string? currentProjectPath);
    UnsavedChangesChoice AskUnsavedChanges();
    bool Confirm(string message, string title);
    void Info(string message, string title = "IntelligentAdjustment");
    void Error(string message, string title = "IntelligentAdjustment");
}
