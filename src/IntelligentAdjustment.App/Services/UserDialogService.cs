using System.IO;
using IntelligentAdjustment.Application.Import;
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

    public IReadOnlyList<string> PickInstrumentFiles(InstrumentImporterDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(descriptor);

        string patterns = string.Join(
            ";",
            descriptor.Extensions.Select(x => $"*{x}"));

        string filter = descriptor.Extensions.Count == 0
            ? "所有文件 (*.*)|*.*"
            : $"{descriptor.DisplayName} ({patterns})|{patterns}|所有文件 (*.*)|*.*";

        var dialog = new OpenFileDialog
        {
            Title = $"导入 {descriptor.DisplayName} 数据",
            Filter = filter,
            Multiselect = true,
            CheckFileExists = true
        };

        return dialog.ShowDialog() == true
            ? dialog.FileNames
            : Array.Empty<string>();
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

    public string? PickReportDocxPath(string projectName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出高程控制网平差报告",
            Filter = "Word 文档 (*.docx)|*.docx",
            DefaultExt = ".docx",
            AddExtension = true,
            FileName = $"{SanitizeFileName(projectName)}-高程控制网平差报告.docx"
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickReportXlsxPath(string projectName)
    {
        var dialog = new SaveFileDialog
        {
            Title = "导出平差成果 Excel",
            Filter = "Excel 工作簿 (*.xlsx)|*.xlsx",
            DefaultExt = ".xlsx",
            AddExtension = true,
            FileName = $"{SanitizeFileName(projectName)}-平差成果.xlsx"
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private static string SanitizeFileName(string? value)
    {
        string source = string.IsNullOrWhiteSpace(value) ? "未命名工程" : value.Trim();
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            source = source.Replace(invalid, '_');
        }

        return source;
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
