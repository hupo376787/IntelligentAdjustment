using CommunityToolkit.Mvvm.Input;
using IntelligentAdjustment.Application.Models;

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
                ? "已撤销一步。"
                : "已撤销到最近保存状态。";
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
            StatusMessage = "已撤销线路操作。";
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
            StatusMessage = "已重做一步。";
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
            StatusMessage = "已重做线路操作。";
            RefreshUndoCommands();
        });
    }

    private bool CanRedo() => Document.CanRedo || databaseRedoStack.Count > 0;
}
