using System.ComponentModel;
using System.Collections.ObjectModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IntelligentAdjustment.App.Services;
using IntelligentAdjustment.Application.Observations;
using IntelligentAdjustment.Core.Transitions;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.App.ViewModels;

public partial class RawObservationsTabViewModel : WorkspaceTabViewModel
{
    private readonly ProjectDocumentViewModel document;
    private readonly IUserDialogService dialogs;
    private readonly TransitionPointProcessor transitionProcessor = new();

    [ObservableProperty]
    private ObservationLineInfo? selectedLine;

    public RawObservationsTabViewModel(
        ProjectDocumentViewModel document,
        IUserDialogService dialogs)
        : base("raw-observations", "原始观测")
    {
        this.document = document;
        this.dialogs = dialogs;

        Items = CollectionViewSource.GetDefaultView(document.RawObservations);
        Items.Filter = FilterByLine;

        SelectedRows.CollectionChanged += (_, _) =>
        {
            DeleteSelectedCommand.NotifyCanExecuteChanged();
        };

        Refresh();
    }

    public ProjectDocumentViewModel Document => document;
    public ObservableCollection<ObservationLineInfo> Lines => document.Lines;
    public ICollectionView Items { get; }
    public ObservableCollection<RawObservationRowViewModel> SelectedRows { get; } = new();

    public void Refresh()
    {
        long? selectedId = SelectedLine?.Id;
        SelectedLine = selectedId is null
            ? Lines.FirstOrDefault()
            : Lines.FirstOrDefault(x => x.Id == selectedId) ?? Lines.FirstOrDefault();

        Items.Refresh();
        AddCommand.NotifyCanExecuteChanged();
        GenerateDifferencesCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedLineChanged(ObservationLineInfo? value)
    {
        SelectedRows.Clear();
        Items.Refresh();
        AddCommand.NotifyCanExecuteChanged();
        GenerateDifferencesCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(HasLine))]
    private void Add()
    {
        if (SelectedLine is null)
        {
            return;
        }

        long lineId = SelectedLine.Id;
        document.ExecuteUndoable(() =>
        {
            int sequence = document.RawObservations
                .Where(x => x.LineId == lineId)
                .Select(x => x.Sequence)
                .DefaultIfEmpty(-1)
                .Max() + 1;

            string from = document.RawObservations
                .Where(x => x.LineId == lineId)
                .OrderBy(x => x.Sequence)
                .LastOrDefault()?.ToPoint ?? string.Empty;

            document.RawObservations.Add(new RawObservationRowViewModel
            {
                Id = NextTemporaryRawId(),
                LineId = lineId,
                Sequence = sequence,
                FromPoint = from,
                ToPoint = string.Empty,
                MeasurementMode = MeasurementMode.BFFB,
                ObservationOrder = ObservationOrder.BFFB,
                IsValid = true
            });
        });

        Items.Refresh();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DeleteSelected()
    {
        RawObservationRowViewModel[] selected = SelectedRows.ToArray();
        document.ExecuteUndoable(() =>
        {
            foreach (var row in selected)
            {
                document.RawObservations.Remove(row);
            }

            ResequenceSelectedLine();
        });

        SelectedRows.Clear();
        Items.Refresh();
    }

    [RelayCommand(CanExecute = nameof(HasLine))]
    private void GenerateDifferences()
    {
        if (SelectedLine is null)
        {
            return;
        }

        long lineId = SelectedLine.Id;
        RawObservationRowViewModel[] rawRows = document.RawObservations
            .Where(x => x.LineId == lineId)
            .OrderBy(x => x.Sequence)
            .ToArray();

        if (rawRows.Length == 0)
        {
            dialogs.Info("当前线路没有原始观测。");
            return;
        }

        var existingBySequence = document.LevelDifferences
            .Where(x => x.LineId == lineId)
            .GroupBy(x => x.Sequence)
            .ToDictionary(x => x.Key, x => x.First());

        var generated = new List<LevelDifference>();
        var failures = new List<string>();
        long temporaryId = NextTemporaryDifferenceId();

        foreach (RawObservationRowViewModel row in rawRows)
        {
            long id = existingBySequence.TryGetValue(row.Sequence, out var existing)
                ? existing.Id
                : temporaryId--;

            if (!RawObservationDifferenceBuilder.TryBuild(
                    row.ToDomain(),
                    id,
                    out LevelDifference? difference,
                    out string? error))
            {
                failures.Add($"第 {row.Sequence + 1} 站：{error}");
                continue;
            }

            if (existing is not null)
            {
                difference = difference! with
                {
                    ToPointRole = existing.ToPointRole,
                    IsRoleManuallySpecified = existing.IsRoleManuallySpecified
                };
            }

            generated.Add(difference!);
        }

        IReadOnlyList<LevelDifference> combined = document.LevelDifferences
            .Where(x => x.LineId != lineId)
            .Select(x => x.ToDomain())
            .Concat(generated)
            .OrderBy(x => x.LineId)
            .ThenBy(x => x.Sequence)
            .ToArray();

        if (document.Settings.AutoMergeTransitionPoints)
        {
            IReadOnlySet<string> knownPoints = document.KnownHeights
                .Select(x => x.PointName.Trim())
                .Where(x => x.Length > 0)
                .ToHashSet(StringComparer.Ordinal);

            combined = transitionProcessor.AutoDetectTransitionPoints(combined, knownPoints);
        }

        document.ReplaceLevelDifferences(combined);

        if (failures.Count == 0)
        {
            dialogs.Info($"已从线路“{SelectedLine.Name}”生成 {generated.Count} 条高差观测。");
        }
        else
        {
            string preview = string.Join(Environment.NewLine, failures.Take(8));
            if (failures.Count > 8)
            {
                preview += $"{Environment.NewLine}……另有 {failures.Count - 8} 条";
            }

            dialogs.Info(
                $"成功生成 {generated.Count} 条高差观测，{failures.Count} 条原始观测未生成：{Environment.NewLine}{Environment.NewLine}{preview}");
        }
    }

    public Task HandleCellCommittedAsync(
        RawObservationRowViewModel row,
        string? columnHeader)
    {
        if (!document.Settings.AutoUpdateLevelDifferences ||
            SelectedLine is null ||
            row.LineId != SelectedLine.Id ||
            !AffectsDifference(columnHeader))
        {
            return Task.CompletedTask;
        }

        bool update = dialogs.Confirm(
            "原始观测值已经修改。是否立即重新计算当前线路的高差观测？",
            "更新高差");

        if (update)
        {
            GenerateDifferences();
        }

        return Task.CompletedTask;
    }

    private bool FilterByLine(object item) =>
        item is RawObservationRowViewModel row &&
        (SelectedLine is null || row.LineId == SelectedLine.Id);

    private bool HasLine() => SelectedLine is not null;

    private bool HasSelection() => SelectedRows.Count > 0;

    private long NextTemporaryRawId()
    {
        long minimum = document.RawObservations.Select(x => x.Id).DefaultIfEmpty(0).Min();
        return minimum <= 0 ? minimum - 1 : -1;
    }

    private long NextTemporaryDifferenceId()
    {
        long minimum = document.LevelDifferences.Select(x => x.Id).DefaultIfEmpty(0).Min();
        return minimum <= 0 ? minimum - 1 : -1;
    }

    private void ResequenceSelectedLine()
    {
        if (SelectedLine is null)
        {
            return;
        }

        int sequence = 0;
        foreach (var row in document.RawObservations
                     .Where(x => x.LineId == SelectedLine.Id)
                     .OrderBy(x => x.Sequence))
        {
            row.Sequence = sequence++;
        }
    }

    private static bool AffectsDifference(string? header) =>
        header is "起点" or "终点"
            or "B1" or "B2" or "F1" or "F2"
            or "后距1" or "后距2" or "前距1" or "前距2"
            or "测量模式" or "观测顺序" or "有效";
}
