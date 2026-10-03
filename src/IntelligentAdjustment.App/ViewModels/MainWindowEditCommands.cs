using CommunityToolkit.Mvvm.Input;

namespace IntelligentAdjustment.App.ViewModels;

public partial class MainWindowViewModel
{
    [RelayCommand(CanExecute = nameof(CanUndo))]
    private void Undo()
    {
        Document.Undo();
        StatusMessage = Document.CanUndo ? "已撤销一步。" : "已撤销到最近保存状态。";
    }

    private bool CanUndo() => Document.CanUndo;

    [RelayCommand(CanExecute = nameof(CanRedo))]
    private void Redo()
    {
        Document.Redo();
        StatusMessage = "已重做一步。";
    }

    private bool CanRedo() => Document.CanRedo;
}
