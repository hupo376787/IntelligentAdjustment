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

    [ObservableProperty]
    private string taskOverview = string.Empty;

    [ObservableProperty]
    private string naturalGeography = string.Empty;

    [ObservableProperty]
    private string existingData = string.Empty;

    [ObservableProperty]
    private string referencedStandards = string.Empty;

    [ObservableProperty]
    private string technicalIndicators = string.Empty;

    [ObservableProperty]
    private string fieldWorkSummary = string.Empty;

    [ObservableProperty]
    private string conclusionAndRecommendations = string.Empty;

    public ObservableCollection<ObservationLineInfo> Lines { get; } = new();
    public ObservableCollection<LevelDifferenceRowViewModel> LevelDifferences { get; } = new();
    public ObservableCollection<KnownHeightRowViewModel> KnownHeights { get; } = new();
    public ObservableCollection<RawObservationRowViewModel> RawObservations { get; } = new();
    public ObservableCollection<NetworkMapPointRowViewModel> MapPoints { get; } = new();

    public bool CanUndo => undoStack.Count > 0;
    public bool CanRedo => redoStack.Count > 0;

    public event EventHandler? UndoStateChanged;

    public ProjectDocumentViewModel()
    {
        LevelDifferences.CollectionChanged += LevelDifferences_CollectionChanged;
        KnownHeights.CollectionChanged += KnownHeights_CollectionChanged;
        RawObservations.CollectionChanged += RawObservations_CollectionChanged;
        MapPoints.CollectionChanged += MapPoints_CollectionChanged;
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

            Lines.Clear();
            foreach (ObservationLineInfo line in workspace.Lines.OrderBy(x => x.DisplayOrder).ThenBy(x => x.Id))
            {
                Lines.Add(line);
            }

            TaskOverview = workspace.ReportText.TaskOverview;
            NaturalGeography = workspace.ReportText.NaturalGeography;
            ExistingData = workspace.ReportText.ExistingData;
            ReferencedStandards = workspace.ReportText.ReferencedStandards;
            TechnicalIndicators = workspace.ReportText.TechnicalIndicators;
            FieldWorkSummary = workspace.ReportText.FieldWorkSummary;
            ConclusionAndRecommendations = workspace.ReportText.ConclusionAndRecommendations;

            ReplaceRowsCore(
                workspace.LevelDifferences,
                workspace.KnownHeights,
                workspace.RawObservations,
                workspace.MapPoints);

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

    public void Clear()
    {
        suppressDirty = true;
        suppressUndo = true;
        try
        {
            ProjectName = string.Empty;
            ProjectNumber = string.Empty;
            UnitName = string.Empty;
            ProjectLeader = string.Empty;
            Reviewer = string.Empty;
            Settings = new ProjectSettings();
            Revision = new ProjectRevisionState(0, -1, null);
            TaskOverview = string.Empty;
            NaturalGeography = string.Empty;
            ExistingData = string.Empty;
            ReferencedStandards = string.Empty;
            TechnicalIndicators = string.Empty;
            FieldWorkSummary = string.Empty;
            ConclusionAndRecommendations = string.Empty;

            Lines.Clear();
            ReplaceRowsCore(
                Array.Empty<LevelDifference>(),
                Array.Empty<KnownHeight>(),
                Array.Empty<RawObservation>(),
                Array.Empty<NetworkMapPoint>());

            undoStack.Clear();
            redoStack.Clear();
            savedSnapshot = null;
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
            RawObservations.Select(x => x.ToDomain()).ToArray(),
            MapPoints.Select(x => x.ToDomain()).ToArray(),
            new ReportTextContent(
                TaskOverview,
                NaturalGeography,
                ExistingData,
                ReferencedStandards,
                TechnicalIndicators,
                FieldWorkSummary,
                ConclusionAndRecommendations));
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
            or nameof(Settings)
            or nameof(TaskOverview)
            or nameof(NaturalGeography)
            or nameof(ExistingData)
            or nameof(ReferencedStandards)
            or nameof(TechnicalIndicators)
            or nameof(FieldWorkSummary)
            or nameof(ConclusionAndRecommendations))
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
            or nameof(Settings)
            or nameof(TaskOverview)
            or nameof(NaturalGeography)
            or nameof(ExistingData)
            or nameof(ReferencedStandards)
            or nameof(TechnicalIndicators)
            or nameof(FieldWorkSummary)
            or nameof(ConclusionAndRecommendations))
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

    private void MapPoints_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        UpdateRowSubscriptions(e);
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
        if (!suppressDirty && sender is not NetworkMapPointRowViewModel)
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
        TaskOverview,
        NaturalGeography,
        ExistingData,
        ReferencedStandards,
        TechnicalIndicators,
        FieldWorkSummary,
        ConclusionAndRecommendations,
        LevelDifferences.Select(x => x.ToDomain()).ToArray(),
        KnownHeights.Select(x => x.ToDomain()).ToArray(),
        RawObservations.Select(x => x.ToDomain()).ToArray(),
        MapPoints.Select(x => x.ToDomain()).ToArray());

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
            TaskOverview = snapshot.TaskOverview;
            NaturalGeography = snapshot.NaturalGeography;
            ExistingData = snapshot.ExistingData;
            ReferencedStandards = snapshot.ReferencedStandards;
            TechnicalIndicators = snapshot.TechnicalIndicators;
            FieldWorkSummary = snapshot.FieldWorkSummary;
            ConclusionAndRecommendations = snapshot.ConclusionAndRecommendations;
            ReplaceRowsCore(
                snapshot.LevelDifferences,
                snapshot.KnownHeights,
                snapshot.RawObservations,
                snapshot.MapPoints);
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
        IReadOnlyList<RawObservation> rawObservations,
        IReadOnlyList<NetworkMapPoint> mapPoints)
    {
        UnsubscribeRows(LevelDifferences);
        UnsubscribeRows(KnownHeights);
        UnsubscribeRows(RawObservations);
        UnsubscribeRows(MapPoints);

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

        MapPoints.Clear();
        foreach (var item in mapPoints)
        {
            MapPoints.Add(NetworkMapPointRowViewModel.FromDomain(item));
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
            || !string.Equals(left.TaskOverview, right.TaskOverview, StringComparison.Ordinal)
            || !string.Equals(left.NaturalGeography, right.NaturalGeography, StringComparison.Ordinal)
            || !string.Equals(left.ExistingData, right.ExistingData, StringComparison.Ordinal)
            || !string.Equals(left.ReferencedStandards, right.ReferencedStandards, StringComparison.Ordinal)
            || !string.Equals(left.TechnicalIndicators, right.TechnicalIndicators, StringComparison.Ordinal)
            || !string.Equals(left.FieldWorkSummary, right.FieldWorkSummary, StringComparison.Ordinal)
            || !string.Equals(left.ConclusionAndRecommendations, right.ConclusionAndRecommendations, StringComparison.Ordinal)
            || left.LevelDifferences.Count != right.LevelDifferences.Count
            || left.KnownHeights.Count != right.KnownHeights.Count
            || left.RawObservations.Count != right.RawObservations.Count
            || left.MapPoints.Count != right.MapPoints.Count)
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

        for (int i = 0; i < left.MapPoints.Count; i++)
        {
            if (left.MapPoints[i] != right.MapPoints[i])
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
        string TaskOverview,
        string NaturalGeography,
        string ExistingData,
        string ReferencedStandards,
        string TechnicalIndicators,
        string FieldWorkSummary,
        string ConclusionAndRecommendations,
        IReadOnlyList<LevelDifference> LevelDifferences,
        IReadOnlyList<KnownHeight> KnownHeights,
        IReadOnlyList<RawObservation> RawObservations,
        IReadOnlyList<NetworkMapPoint> MapPoints);
}
