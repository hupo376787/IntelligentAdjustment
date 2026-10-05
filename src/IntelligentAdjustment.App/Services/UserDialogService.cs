using System.IO;
using IntelligentAdjustment.Application.Import;
using IntelligentAdjustment.App.Views;
using Microsoft.Win32;
using System.Windows;
using HandyMessageBox = HandyControl.Controls.MessageBox;

namespace IntelligentAdjustment.App.Services;

public sealed class UserDialogService : IUserDialogService
{
    public string? PickNewProjectPath()
    {
        var dialog = new SaveFileDialog
        {
            Title = LocalizationService.Text("Loc.Dialog.NewProjectTitle"),
            Filter = LocalizationService.Text("Loc.Dialog.ProjectFilter"),
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
            Title = LocalizationService.Text("Loc.Dialog.OpenProjectTitle"),
            Filter = LocalizationService.Text("Loc.Dialog.ProjectFilter"),
            Multiselect = false,
            CheckFileExists = true
        };
        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public IReadOnlyList<string> PickOutFiles()
    {
        var dialog = new OpenFileDialog
        {
            Title = LocalizationService.Text("Loc.Dialog.ImportOutTitle"),
            Filter = LocalizationService.Text("Loc.Dialog.OutFilter"),
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
            ? LocalizationService.Text("Loc.Dialog.AllFiles")
            : $"{descriptor.DisplayName} ({patterns})|{patterns}|{LocalizationService.Text("Loc.Dialog.AllFiles")}";

        var dialog = new OpenFileDialog
        {
            Title = LocalizationService.Format("Loc.Dialog.ImportInstrumentTitle", descriptor.DisplayName),
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
            Title = LocalizationService.Text("Loc.Dialog.SaveAsTitle"),
            Filter = LocalizationService.Text("Loc.Dialog.ProjectFilter"),
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
            Title = LocalizationService.Text("Loc.Dialog.ExportReportTitle"),
            Filter = LocalizationService.Text("Loc.Dialog.WordFilter"),
            DefaultExt = ".docx",
            AddExtension = true,
            FileName = $"{SanitizeFileName(projectName)}-{LocalizationService.Text("Loc.Dialog.ReportFileSuffix")}.docx"
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickReportXlsxPath(string projectName)
    {
        var dialog = new SaveFileDialog
        {
            Title = LocalizationService.Text("Loc.Dialog.ExportExcelTitle"),
            Filter = LocalizationService.Text("Loc.Dialog.ExcelFilter"),
            DefaultExt = ".xlsx",
            AddExtension = true,
            FileName = $"{SanitizeFileName(projectName)}-{LocalizationService.Text("Loc.Dialog.ResultFileSuffix")}.xlsx"
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    public string? PickResultTextPath(string projectName)
    {
        var dialog = new SaveFileDialog
        {
            Title = LocalizationService.Text("Loc.Dialog.ExportTextTitle"),
            Filter = LocalizationService.Text("Loc.Dialog.TextFilter"),
            DefaultExt = ".txt",
            AddExtension = true,
            FileName = $"{SanitizeFileName(projectName)}-{LocalizationService.Text("Loc.Dialog.ResultFileSuffix")}.txt"
        };

        return dialog.ShowDialog() == true ? dialog.FileName : null;
    }

    private static string SanitizeFileName(string? value)
    {
        string source = string.IsNullOrWhiteSpace(value) ? LocalizationService.Text("Loc.Project.Unnamed") : value.Trim();
        foreach (char invalid in Path.GetInvalidFileNameChars())
        {
            source = source.Replace(invalid, '_');
        }

        return source;
    }

    public UnsavedChangesChoice AskUnsavedChanges()
    {
        MessageBoxResult result = HandyMessageBox.Show(
            GetOwner(),
            LocalizationService.Text("Loc.Dialog.Unsaved"),
            "Intelligent Adjustment",
            MessageBoxButton.YesNoCancel,
            MessageBoxImage.Question,
            MessageBoxResult.Cancel);

        return result switch
        {
            MessageBoxResult.Yes => UnsavedChangesChoice.Save,
            MessageBoxResult.No => UnsavedChangesChoice.Discard,
            _ => UnsavedChangesChoice.Cancel
        };
    }

    public bool Confirm(string message, string title) =>
        HandyMessageBox.Show(
            GetOwner(),
            message,
            title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question,
            MessageBoxResult.No) == MessageBoxResult.Yes;

    public void Info(string message, string title = "Intelligent Adjustment") =>
        HandyMessageBox.Show(
            GetOwner(),
            message,
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Information,
            MessageBoxResult.OK);

    public void Error(string message, string title = "Intelligent Adjustment") =>
        HandyMessageBox.Show(
            GetOwner(),
            message,
            title,
            MessageBoxButton.OK,
            MessageBoxImage.Error,
            MessageBoxResult.OK);

    public void ShowAbout()
    {
        var window = new AboutWindow
        {
            Owner = GetOwner()
        };
        _ = window.ShowDialog();
    }

    private static System.Windows.Window? GetOwner() =>
        System.Windows.Application.Current?.Windows
            .OfType<System.Windows.Window>()
            .FirstOrDefault(x => x.IsActive)
        ?? System.Windows.Application.Current?.MainWindow;
}
