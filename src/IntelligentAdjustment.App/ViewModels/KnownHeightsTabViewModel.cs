using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IntelligentAdjustment.App.Services;

namespace IntelligentAdjustment.App.ViewModels;

public partial class KnownHeightsTabViewModel : WorkspaceTabViewModel
{
    private readonly ProjectDocumentViewModel document;
    private readonly IUserDialogService dialogs;

    [ObservableProperty]
    private string newPointName = string.Empty;

    [ObservableProperty]
    private double newHeight;

    public KnownHeightsTabViewModel(ProjectDocumentViewModel document, IUserDialogService dialogs)
        : base("known-heights", "已知高程")
    {
        this.document = document;
        this.dialogs = dialogs;
        SelectedRows.CollectionChanged += (_, _) => DeleteSelectedCommand.NotifyCanExecuteChanged();
    }

    public ObservableCollection<KnownHeightRowViewModel> Items => document.KnownHeights;
    public ObservableCollection<KnownHeightRowViewModel> SelectedRows { get; } = new();

    [RelayCommand]
    private void AddKnownHeight()
    {
        string name = NewPointName.Trim();
        if (name.Length == 0)
        {
            dialogs.Info("请输入点名。");
            return;
        }

        if (!double.IsFinite(NewHeight))
        {
            dialogs.Info("请输入有效的高程数值。");
            return;
        }

        if (Items.Any(x => string.Equals(x.PointName.Trim(), name, StringComparison.Ordinal)))
        {
            dialogs.Info($"点名“{name}”已经存在，重复输入无效。");
            return;
        }

        document.ExecuteUndoable(() =>
        {
            Items.Add(new KnownHeightRowViewModel
            {
                PointName = name,
                Height = NewHeight
            });
        });

        NewPointName = string.Empty;
        NewHeight = 0;
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DeleteSelected()
    {
        KnownHeightRowViewModel[] selected = SelectedRows.ToArray();
        document.ExecuteUndoable(() =>
        {
            foreach (var row in selected)
            {
                Items.Remove(row);
            }
        });

        SelectedRows.Clear();
    }

    private bool HasSelection() => SelectedRows.Count > 0;
}
