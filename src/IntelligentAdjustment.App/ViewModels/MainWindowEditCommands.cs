using CommunityToolkit.Mvvm.Input;
using IntelligentAdjustment.Application.Models;
using IntelligentAdjustment.App.Services;

namespace IntelligentAdjustment.App.ViewModels;

public partial class MainWindowViewModel
{
    [RelayCommand(CanExecute = nameof(CanUndo))]
    private async Task UndoAsync()
    {
        if (Document.CanUndo)
        {
            Document.Undo();
            StatusMessage = Document.CanUndo || databaseUndoStack.Count > 0
                ? LocalizationService.Text("Loc.Status.UndoStep")
                : LocalizationService.Text("Loc.Status.UndoSaved");
            return;
        }

        if (databaseUndoStack.Count == 0 || string.IsNullOrWhiteSpace(CurrentProjectPath))
        {
            return;
        }

        DatabaseUndoEntry entry = databaseUndoStack.Peek();
        string projectPath = CurrentProjectPath;

        await RunBusyAsync(async () =>
        {
            ProjectWorkspace workspace = await session.RestoreProjectSnapshotAsync(entry.Before);
            _ = databaseUndoStack.Pop();
            databaseRedoStack.Push(entry);
            LoadWorkspace(workspace, projectPath, resetTabs: false);
            StatusMessage = LocalizationService.Text("Loc.Status.UndoLine");
            RefreshUndoCommands();
        });
    }

    private bool CanUndo() => Document.CanUndo || databaseUndoStack.Count > 0;

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private async Task RedoAsync()
    {
        if (Document.CanRedo)
        {
            Document.Redo();
            StatusMessage = LocalizationService.Text("Loc.Status.RedoStep");
            return;
        }

        if (databaseRedoStack.Count == 0 || string.IsNullOrWhiteSpace(CurrentProjectPath))
        {
            return;
        }

        DatabaseUndoEntry entry = databaseRedoStack.Peek();
        string projectPath = CurrentProjectPath;

        await RunBusyAsync(async () =>
        {
            ProjectWorkspace workspace = await session.RestoreProjectSnapshotAsync(entry.After);
            _ = databaseRedoStack.Pop();
            databaseUndoStack.Push(entry);
            LoadWorkspace(workspace, projectPath, resetTabs: false);
            StatusMessage = LocalizationService.Text("Loc.Status.RedoLine");
            RefreshUndoCommands();
        });
    }

    private bool CanRedo() => Document.CanRedo || databaseRedoStack.Count > 0;
}
