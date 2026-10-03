using System.IO;
using Microsoft.Win32;
using System.Windows;

namespace IntelligentAdjustment.App.Services;

public sealed class UserDialogService : IUserDialogService
{
    public string? PickNewProjectPath()
    {
        var dialog = new SaveFileDialog
        {
            Title = "新建 IntelligentAdjustment 工程",
            Filter = "IntelligentAdjustment 工程 (*.iap)|*.iap",
            DefaultExt = ".iap",
            AddExtension = true,
            OverwritePrompt = false
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickProjectToOpen()
    {
        var dialog = new OpenFileDialog
        {
            Title = "打开 IntelligentAdjustment 工程",
            Filter = "IntelligentAdjustment 工程 (*.iap)|*.iap",
            Multiselect = false,
            CheckFileExists = true
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public IReadOnlyList<string> PickOutFiles()
    {
        var dialog = new OpenFileDialog
        {
            Title = "导入高差观测数据",
            Filter = "高差观测数据 (*.out)|*.out|所有文件 (*.*)|*.*",
            Multiselect = true,
            CheckFileExists = true
        };
        return dialog.ShowDialog() == true ? dialog.FileNames : Array.Empty<string>();
    }

    public string? PickSaveAsProjectPath(string? currentProjectPath)
    {
        var dialog = new SaveFileDialog
        {
            Title = "工程另存为",
            Filter = "IntelligentAdjustment 工程 (*.iap)|*.iap",
            DefaultExt = ".iap",
            AddExtension = true,
            OverwritePrompt = false,
            FileName = string.IsNullOrWhiteSpace(currentProjectPath)
                ? string.Empty
                : Path.GetFileName(currentProjectPath)
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public UnsavedChangesChoice AskUnsavedChanges()
    {
        MessageBoxResult result = MessageBox.Show(
            "当前工程有尚未保存的修改。是否保存？",
            "IntelligentAdjustment",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question);

        return result switch
        {
            MessageBoxResult.Yes => UnsavedChangesChoice.Save,
            MessageBoxResult.No => UnsavedChangesChoice.Discard,
            _ => UnsavedChangesChoice.Cancel
        };
    }

    public bool Confirm(string message, string title) =>
        MessageBox.Show(message, title, MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes;

    public void Info(string message, string title = "IntelligentAdjustment") =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);

    public void Error(string message, string title = "IntelligentAdjustment") =>
        MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
}
