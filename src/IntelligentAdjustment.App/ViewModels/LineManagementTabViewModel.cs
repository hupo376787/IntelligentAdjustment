using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IntelligentAdjustment.App.Services;
using IntelligentAdjustment.Application.Models;
using IntelligentAdjustment.Application.Services;

namespace IntelligentAdjustment.App.ViewModels;

public partial class LineManagementTabViewModel : WorkspaceTabViewModel
{
    private readonly ProjectDocumentViewModel document;
    private readonly ProjectSessionService session;
    private readonly IUserDialogService dialogs;
    private readonly Func<Task<bool>> ensureSaved;
    private readonly Action<ProjectWorkspace> applyWorkspace;
    private readonly Func<Func<Task<ProjectWorkspace>>, Task<ProjectWorkspace>> executeUndoableDatabaseAction;

    [ObservableProperty]
    private LineDisplayRow? selectedLine;

    [ObservableProperty]
    private string editName = string.Empty;

    [ObservableProperty]
    private string newLineName = "新线路";

    public LineManagementTabViewModel(
        ProjectDocumentViewModel document,
        ProjectSessionService session,
        IUserDialogService dialogs,
        Func<Task<bool>> ensureSaved,
        Action<ProjectWorkspace> applyWorkspace,
        Func<Func<Task<ProjectWorkspace>>, Task<ProjectWorkspace>> executeUndoableDatabaseAction)
        : base("lines", "线路管理")
    {
        this.document = document;
        this.session = session;
        this.dialogs = dialogs;
        this.ensureSaved = ensureSaved;
        this.applyWorkspace = applyWorkspace;
        this.executeUndoableDatabaseAction = executeUndoableDatabaseAction;
        Refresh();
    }

    public ObservableCollection<LineDisplayRow> Items { get; } = new();

    public void Refresh()
    {
        long? selectedId = SelectedLine?.Id;
        Items.Clear();

        foreach (var line in document.Lines.OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id))
        {
            int rawCount = document.RawObservations.Count(x => x.LineId == line.Id);
            int differenceCount = document.LevelDifferences.Count(x => x.LineId == line.Id);

            Items.Add(new LineDisplayRow(
                line.Id,
                line.DisplayOrder,
                line.Name,
                line.InstrumentType ?? string.Empty,
                line.SourceFileName ?? string.Empty,
                rawCount,
                differenceCount));
        }

        SelectedLine = selectedId is null
            ? Items.FirstOrDefault()
            : Items.FirstOrDefault(x => x.Id == selectedId) ?? Items.FirstOrDefault();
    }

    partial void OnSelectedLineChanged(LineDisplayRow? value)
    {
        EditName = value?.Name ?? string.Empty;
        RenameCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
        MoveUpCommand.NotifyCanExecuteChanged();
        MoveDownCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (!await ensureSaved())
        {
            return;
        }

        string name = string.IsNullOrWhiteSpace(NewLineName) ? "新线路" : NewLineName.Trim();
        ProjectWorkspace workspace = await executeUndoableDatabaseAction(
            () => session.CreateLineAsync(name));
        applyWorkspace(workspace);
        Refresh();

        SelectedLine = Items.LastOrDefault();
        NewLineName = "新线路";
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task RenameAsync()
    {
        if (SelectedLine is null)
        {
            return;
        }

        string name = EditName.Trim();
        if (name.Length == 0)
        {
            dialogs.Info("线路名称不能为空。");
            return;
        }

        if (!await ensureSaved())
        {
            return;
        }

        ProjectWorkspace workspace = await executeUndoableDatabaseAction(
            () => session.RenameLineAsync(SelectedLine.Id, name));
        long id = SelectedLine.Id;
        applyWorkspace(workspace);
        Refresh();
        SelectedLine = Items.FirstOrDefault(x => x.Id == id);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task DeleteAsync()
    {
        if (SelectedLine is null)
        {
            return;
        }

        if (!dialogs.Confirm(
                $"确定删除线路“{SelectedLine.Name}”吗？\n\n该线路下的原始观测和高差观测也会一起删除。",
                "删除线路"))
        {
            return;
        }

        if (!await ensureSaved())
        {
            return;
        }

        ProjectWorkspace workspace = await executeUndoableDatabaseAction(
            () => session.DeleteLineAsync(SelectedLine.Id));
        applyWorkspace(workspace);
        Refresh();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task MoveUpAsync() => await MoveAsync(-1);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private async Task MoveDownAsync() => await MoveAsync(1);

    private async Task MoveAsync(int direction)
    {
        if (SelectedLine is null)
        {
            return;
        }

        if (!await ensureSaved())
        {
            return;
        }

        long id = SelectedLine.Id;
        ProjectWorkspace workspace = await executeUndoableDatabaseAction(
            () => session.MoveLineAsync(id, direction));
        applyWorkspace(workspace);
        Refresh();
        SelectedLine = Items.FirstOrDefault(x => x.Id == id);
    }

    private bool HasSelection() => SelectedLine is not null;
}

public sealed record LineDisplayRow(
    long Id,
    int DisplayOrder,
    string Name,
    string InstrumentType,
    string SourceFileName,
    int RawObservationCount,
    int LevelDifferenceCount);
