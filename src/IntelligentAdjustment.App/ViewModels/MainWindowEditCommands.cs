using CommunityToolkit.Mvvm.Input;

namespace IntelligentAdjustment.App.ViewModels;

public partial class MainWindowViewModel
{
    [RelayCommand]
    private void Undo()
    {
        Document.Undo();
        StatusMessage = Document.CanUndo ? "已撤销一步。" : "已撤销到最近保存状态。";
    }

    [RelayCommand]
    private void Redo()
    {
        Document.Redo();
        StatusMessage = "已重做一步。";
    }
}
