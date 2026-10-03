using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.Input;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.App.ViewModels;

public partial class LevelDifferencesTabViewModel : WorkspaceTabViewModel
{
    private readonly ProjectDocumentViewModel document;

    public LevelDifferencesTabViewModel(ProjectDocumentViewModel document)
        : base("differences", "高差观测")
    {
        this.document = document;
        SelectedRows.CollectionChanged += (_, _) =>
        {
            DeleteSelectedCommand.NotifyCanExecuteChanged();
            SetAdjustmentPointCommand.NotifyCanExecuteChanged();
            SetTransitionPointCommand.NotifyCanExecuteChanged();
            TogglePointRoleCommand.NotifyCanExecuteChanged();
        };
    }

    public ObservableCollection<LevelDifferenceRowViewModel> Items => document.LevelDifferences;
    public ObservableCollection<LevelDifferenceRowViewModel> SelectedRows { get; } = new();

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add()
    {
        long lineId = document.Lines[0].Id;
        int sequence = Items.Where(x => x.LineId == lineId).Select(x => x.Sequence).DefaultIfEmpty(-1).Max() + 1;
        Items.Add(new LevelDifferenceRowViewModel
        {
            Id = 0,
            LineId = lineId,
            Sequence = sequence,
            StationCount = 1,
            ToPointRole = PointRole.AdjustmentPoint
        });
    }

    private bool CanAdd() => document.Lines.Count > 0;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DeleteSelected()
    {
        foreach (var row in SelectedRows.ToArray())
        {
            Items.Remove(row);
        }

        SelectedRows.Clear();
        Resequence();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void SetAdjustmentPoint() => SetRole(PointRole.AdjustmentPoint);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void SetTransitionPoint() => SetRole(PointRole.TransitionPoint);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void TogglePointRole()
    {
        foreach (var row in SelectedRows)
        {
            row.ToPointRole = row.ToPointRole == PointRole.AdjustmentPoint
                ? PointRole.TransitionPoint
                : PointRole.AdjustmentPoint;
            row.IsRoleManuallySpecified = true;
        }
    }

    private bool HasSelection() => SelectedRows.Count > 0;

    private void SetRole(PointRole role)
    {
        foreach (var row in SelectedRows)
        {
            row.ToPointRole = role;
            row.IsRoleManuallySpecified = true;
        }
    }

    private void Resequence()
    {
        foreach (var group in Items.GroupBy(x => x.LineId))
        {
            int index = 0;
            foreach (var row in group.OrderBy(x => x.Sequence))
            {
                row.Sequence = index++;
            }
        }
    }
}
