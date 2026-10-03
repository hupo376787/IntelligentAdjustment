using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using IntelligentAdjustment.Application.Models;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.App.ViewModels;

public partial class ProjectDocumentViewModel : ObservableObject
{
    private bool suppressDirty;
    private bool suppressUndo;
    private readonly Stack<ProjectInputSnapshot> undoStack = new();
    private readonly Stack<ProjectInputSnapshot> redoStack = new();
    private ProjectInputSnapshot? savedSnapshot;

    [ObservableProperty]
    private string projectName = string.Empty;

    [ObservableProperty]
    private string projectNumber = string.Empty;

    [ObservableProperty]
    private string unitName = string.Empty;

    [ObservableProperty]
    private string projectLeader = string.Empty;

    [ObservableProperty]
    private string reviewer = string.Empty;

    [ObservableProperty]
    private ProjectSettings settings = new();

    [ObservableProperty]
    private ProjectRevisionState revision = new(0, -1, null);

    [ObservableProperty]
    private bool isDirty;

    [ObservableProperty]
    private bool calculationInputsChanged;

    public ObservableCollection<ObservationLineInfo> Lines { get; } = new();
    public ObservableCollection<LevelDifferenceRowViewModel> LevelDifferences { get; } = new();
    public ObservableCollection<KnownHeightRowViewModel> KnownHeights { get; } = new();
    public ObservableCollection<RawObservationRowViewModel> RawObservations { get; } = new();

    public bool CanUndo => undoStack.Count > 0;
    public bool CanRedo => redoStack.Count > 0;

    public event EventHandler? UndoStateChanged;

    public ProjectDocumentViewModel()
    {
        LevelDifferences.CollectionChanged += LevelDifferences_CollectionChanged;
        KnownHeights.CollectionChanged += KnownHeights_CollectionChanged;
        RawObservations.CollectionChanged += RawObservations_CollectionChanged;
        PropertyChanging += Document_PropertyChanging;
        PropertyChanged += Document_PropertyChanged;
    }

    public void Load(ProjectWorkspace workspace)
    {
        suppressDirty = true;
        suppressUndo = true;
        try
        {
            ProjectName = workspace.Metadata.ProjectName;
            ProjectNumber = workspace.Metadata.ProjectNumber;
            UnitName = workspace.Metadata.UnitName;
            ProjectLeader = workspace.Metadata.ProjectLeader;
            Reviewer = workspace.Metadata.Reviewer;
            Settings = workspace.Settings;
            Revision = workspace.Revision;

            ReplaceRowsCore(workspace.LevelDifferences, workspace.KnownHeights, workspace.RawObservations);

            undoStack.Clear();
            redoStack.Clear();
            savedSnapshot = CaptureSnapshot();
            CalculationInputsChanged = false;
            IsDirty = false;
            RaiseUndoStateChanged();
        }
        finally
        {
            suppressUndo = false;
            suppressDirty = false;
        }
    }

    public ProjectWorkspace ToWorkspace(ProjectWorkspace basis)
    {
        var metadata = basis.Metadata with
        {
            ProjectName = ProjectName.Trim(),
            ProjectNumber = ProjectNumber.Trim(),
            UnitName = UnitName.Trim(),
            ProjectLeader = ProjectLeader.Trim(),
            Reviewer = Reviewer.Trim(),
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };

        return new ProjectWorkspace(
            metadata,
            Settings,
            Revision,
            Lines.ToArray(),
            LevelDifferences.Select(x => x.ToDomain()).ToArray(),
            KnownHeights.Select(x => x.ToDomain()).ToArray(),
            RawObservations.Select(x => x.ToDomain()).ToArray());
    }

    public void AcceptChanges(ProjectRevisionState revision)
    {
        suppressDirty = true;
        suppressUndo = true;
        try
        {
            Revision = revision;
            undoStack.Clear();
            redoStack.Clear();
            savedSnapshot = CaptureSnapshot();
            CalculationInputsChanged = false;
            IsDirty = false;
            RaiseUndoStateChanged();
        }
        finally
        {
            suppressUndo = false;
            suppressDirty = false;
        }
    }

    public void ExecuteUndoable(Action action)
    {
        ArgumentNullException.ThrowIfNull(action);

        if (suppressUndo)
        {
            action();
            return;
        }

        PushUndoSnapshot();
        suppressUndo = true;
        try
        {
            action();
        }
        finally
        {
            suppressUndo = false;
        }

        MarkDirty();
    }

    public void ReplaceLevelDifferences(IReadOnlyList<LevelDifference> differences)
    {
        ExecuteUndoable(() =>
        {
            UnsubscribeRows(LevelDifferences);
            LevelDifferences.Clear();
            foreach (var item in differences.OrderBy(x => x.LineId).ThenBy(x => x.Sequence))
            {
                LevelDifferences.Add(LevelDifferenceRowViewModel.FromDomain(item));
            }
        });
    }

    public void ReplaceRawObservations(IReadOnlyList<RawObservation> observations)
    {
        ExecuteUndoable(() =>
        {
            UnsubscribeRows(RawObservations);
            RawObservations.Clear();
            foreach (var item in observations.OrderBy(x => x.LineId).ThenBy(x => x.Sequence))
            {
                RawObservations.Add(RawObservationRowViewModel.FromDomain(item));
            }
        });
    }

    public void Undo()
    {
        if (!CanUndo)
        {
            return;
        }

        ProjectInputSnapshot current = CaptureSnapshot();
        ProjectInputSnapshot target = undoStack.Pop();
        redoStack.Push(current);
        ApplySnapshot(target);
        UpdateDirtyFromSavedSnapshot();
        RaiseUndoStateChanged();
    }

    public void Redo()
    {
        if (!CanRedo)
        {
            return;
        }

        ProjectInputSnapshot current = CaptureSnapshot();
        ProjectInputSnapshot target = redoStack.Pop();
        undoStack.Push(current);
        ApplySnapshot(target);
        UpdateDirtyFromSavedSnapshot();
        RaiseUndoStateChanged();
    }

    public void MarkDirty()
    {
        if (!suppressDirty)
        {
            IsDirty = true;
        }
    }

    private void Document_PropertyChanging(object? sender, PropertyChangingEventArgs e)
    {
        if (suppressUndo)
        {
            return;
        }

        if (e.PropertyName is nameof(ProjectName)
            or nameof(ProjectNumber)
            or nameof(UnitName)
            or nameof(ProjectLeader)
            or nameof(Reviewer)
            or nameof(Settings))
        {
            PushUndoSnapshot();
        }
    }

    private void Document_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ProjectName)
            or nameof(ProjectNumber)
            or nameof(UnitName)
            or nameof(ProjectLeader)
            or nameof(Reviewer)
            or nameof(Settings))
        {
            MarkDirty();
        }
    }

    private void LevelDifferences_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateRowSubscriptions(e);
        if (!suppressDirty)
        {
            CalculationInputsChanged = true;
        }

        MarkDirty();
    }

    private void KnownHeights_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateRowSubscriptions(e);
        if (!suppressDirty)
        {
            CalculationInputsChanged = true;
        }

        MarkDirty();
    }

    private void RawObservations_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateRowSubscriptions(e);
        if (!suppressDirty)
        {
            CalculationInputsChanged = true;
        }

        MarkDirty();
    }

    private void UpdateRowSubscriptions(NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
        {
            foreach (object item in e.OldItems)
            {
                UnsubscribeRow(item);
            }
        }

        if (e.NewItems is not null)
        {
            foreach (object item in e.NewItems)
            {
                SubscribeRow(item);
            }
        }
    }

    private void SubscribeRow(object item)
    {
        if (item is INotifyPropertyChanged changed)
        {
            changed.PropertyChanged += Row_PropertyChanged;
        }

        if (item is INotifyPropertyChanging changing)
        {
            changing.PropertyChanging += Row_PropertyChanging;
        }
    }

    private void UnsubscribeRow(object item)
    {
        if (item is INotifyPropertyChanged changed)
        {
            changed.PropertyChanged -= Row_PropertyChanged;
        }

        if (item is INotifyPropertyChanging changing)
        {
            changing.PropertyChanging -= Row_PropertyChanging;
        }
    }

    private void Row_PropertyChanging(object? sender, PropertyChangingEventArgs e)
    {
        if (!suppressUndo)
        {
            PushUndoSnapshot();
        }
    }

    private void Row_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!suppressDirty)
        {
            CalculationInputsChanged = true;
        }

        MarkDirty();
    }

    partial void OnSettingsChanging(ProjectSettings value)
    {
        if (!suppressDirty && CalculationSettingsDiffer(Settings, value))
        {
            CalculationInputsChanged = true;
        }
    }

    private void PushUndoSnapshot()
    {
        if (suppressUndo)
        {
            return;
        }

        ProjectInputSnapshot snapshot = CaptureSnapshot();
        if (undoStack.Count == 0 || !SnapshotsEqual(undoStack.Peek(), snapshot))
        {
            undoStack.Push(snapshot);
        }

        redoStack.Clear();
        RaiseUndoStateChanged();
    }

    private ProjectInputSnapshot CaptureSnapshot() => new(
        ProjectName,
        ProjectNumber,
        UnitName,
        ProjectLeader,
        Reviewer,
        Settings,
        LevelDifferences.Select(x => x.ToDomain()).ToArray(),
        KnownHeights.Select(x => x.ToDomain()).ToArray(),
        RawObservations.Select(x => x.ToDomain()).ToArray());

    private void ApplySnapshot(ProjectInputSnapshot snapshot)
    {
        suppressDirty = true;
        suppressUndo = true;
        try
        {
            ProjectName = snapshot.ProjectName;
            ProjectNumber = snapshot.ProjectNumber;
            UnitName = snapshot.UnitName;
            ProjectLeader = snapshot.ProjectLeader;
            Reviewer = snapshot.Reviewer;
            Settings = snapshot.Settings;
            ReplaceRowsCore(snapshot.LevelDifferences, snapshot.KnownHeights, snapshot.RawObservations);
        }
        finally
        {
            suppressUndo = false;
            suppressDirty = false;
        }
    }

    private void ReplaceRowsCore(
        IReadOnlyList<LevelDifference> levelDifferences,
        IReadOnlyList<KnownHeight> knownHeights,
        IReadOnlyList<RawObservation> rawObservations)
    {
        UnsubscribeRows(LevelDifferences);
        UnsubscribeRows(KnownHeights);
        UnsubscribeRows(RawObservations);

        LevelDifferences.Clear();
        foreach (var item in levelDifferences)
        {
            LevelDifferences.Add(LevelDifferenceRowViewModel.FromDomain(item));
        }

        KnownHeights.Clear();
        foreach (var item in knownHeights)
        {
            KnownHeights.Add(KnownHeightRowViewModel.FromDomain(item));
        }

        RawObservations.Clear();
        foreach (var item in rawObservations)
        {
            RawObservations.Add(RawObservationRowViewModel.FromDomain(item));
        }
    }

    private void UnsubscribeRows<T>(IEnumerable<T> rows)
    {
        foreach (object? row in rows.Cast<object>())
        {
            if (row is not null)
            {
                UnsubscribeRow(row);
            }
        }
    }

    private void UpdateDirtyFromSavedSnapshot()
    {
        ProjectInputSnapshot current = CaptureSnapshot();
        IsDirty = savedSnapshot is null || !SnapshotsEqual(savedSnapshot, current);
        CalculationInputsChanged = savedSnapshot is null || !CalculationInputsEqual(savedSnapshot, current);
    }

    private static bool CalculationInputsEqual(ProjectInputSnapshot left, ProjectInputSnapshot right)
    {
        if (CalculationSettingsDiffer(left.Settings, right.Settings)
            || left.LevelDifferences.Count != right.LevelDifferences.Count
            || left.KnownHeights.Count != right.KnownHeights.Count
            || left.RawObservations.Count != right.RawObservations.Count)
        {
            return false;
        }

        for (int i = 0; i < left.LevelDifferences.Count; i++)
        {
            if (left.LevelDifferences[i] != right.LevelDifferences[i])
            {
                return false;
            }
        }

        for (int i = 0; i < left.KnownHeights.Count; i++)
        {
            if (left.KnownHeights[i] != right.KnownHeights[i])
            {
                return false;
            }
        }

        for (int i = 0; i < left.RawObservations.Count; i++)
        {
            if (left.RawObservations[i] != right.RawObservations[i])
            {
                return false;
            }
        }

        return true;
    }

    private static bool CalculationSettingsDiffer(ProjectSettings left, ProjectSettings right) =>
        left.ToleranceMode != right.ToleranceMode
        || left.DistanceToleranceCoefficientMm != right.DistanceToleranceCoefficientMm
        || left.StationToleranceCoefficientMm != right.StationToleranceCoefficientMm
        || left.AdjustmentMethod != right.AdjustmentMethod;

    private static bool SnapshotsEqual(ProjectInputSnapshot left, ProjectInputSnapshot right)
    {
        if (!string.Equals(left.ProjectName, right.ProjectName, StringComparison.Ordinal)
            || !string.Equals(left.ProjectNumber, right.ProjectNumber, StringComparison.Ordinal)
            || !string.Equals(left.UnitName, right.UnitName, StringComparison.Ordinal)
            || !string.Equals(left.ProjectLeader, right.ProjectLeader, StringComparison.Ordinal)
            || !string.Equals(left.Reviewer, right.Reviewer, StringComparison.Ordinal)
            || left.Settings != right.Settings
            || left.LevelDifferences.Count != right.LevelDifferences.Count
            || left.KnownHeights.Count != right.KnownHeights.Count)
        {
            return false;
        }

        for (int i = 0; i < left.LevelDifferences.Count; i++)
        {
            if (left.LevelDifferences[i] != right.LevelDifferences[i])
            {
                return false;
            }
        }

        for (int i = 0; i < left.KnownHeights.Count; i++)
        {
            if (left.KnownHeights[i] != right.KnownHeights[i])
            {
                return false;
            }
        }

        for (int i = 0; i < left.RawObservations.Count; i++)
        {
            if (left.RawObservations[i] != right.RawObservations[i])
            {
                return false;
            }
        }

        return true;
    }

    private void RaiseUndoStateChanged() => UndoStateChanged?.Invoke(this, EventArgs.Empty);

    private sealed record ProjectInputSnapshot(
        string ProjectName,
        string ProjectNumber,
        string UnitName,
        string ProjectLeader,
        string Reviewer,
        ProjectSettings Settings,
        IReadOnlyList<LevelDifference> LevelDifferences,
        IReadOnlyList<KnownHeight> KnownHeights,
        IReadOnlyList<RawObservation> RawObservations);
}
