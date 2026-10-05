using System.Collections.ObjectModel;
using System.Globalization;
using CommunityToolkit.Mvvm.Input;
using IntelligentAdjustment.App.Services;
using IntelligentAdjustment.Core.Transitions;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.App.ViewModels;

public partial class LevelDifferencesTabViewModel : WorkspaceTabViewModel
{
    private readonly ProjectDocumentViewModel document;
    private readonly IUserDialogService dialogs;
    private readonly TransitionPointProcessor transitionProcessor = new();

    public LevelDifferencesTabViewModel(
        ProjectDocumentViewModel document,
        IUserDialogService dialogs)
        : base("differences", LocalizationService.Text("Loc.Nav.Differences"))
    {
        this.document = document;
        this.dialogs = dialogs;

        SelectedRows.CollectionChanged += (_, _) =>
        {
            DeleteSelectedCommand.NotifyCanExecuteChanged();
            SetAdjustmentPointCommand.NotifyCanExecuteChanged();
            SetTransitionPointCommand.NotifyCanExecuteChanged();
            TogglePointRoleCommand.NotifyCanExecuteChanged();
            MergeSelectedAndDeleteCommand.NotifyCanExecuteChanged();
        };
    }

    public ObservableCollection<LevelDifferenceRowViewModel> Items => document.LevelDifferences;
    public ObservableCollection<LevelDifferenceRowViewModel> SelectedRows { get; } = new();

    [RelayCommand(CanExecute = nameof(CanAdd))]
    private void Add()
    {
        long lineId = SelectedRows.FirstOrDefault()?.LineId ?? document.Lines.FirstOrDefault()?.Id ?? 0;
        if (lineId <= 0)
        {
            dialogs.Info(LocalizationService.Text("Loc.Diff.NoEditableLineImport"));
            return;
        }

        document.ExecuteUndoable(() =>
        {
            int sequence = NextSequence(lineId);
            Items.Add(new LevelDifferenceRowViewModel
            {
                Id = NextTemporaryId(),
                LineId = lineId,
                Sequence = sequence,
                LineName = document.Lines.FirstOrDefault(x => x.Id == lineId)?.Name ?? lineId.ToString(),
                StationCount = 1,
                ToPointRole = PointRole.AdjustmentPoint
            });
        });
    }

    private bool CanAdd() => document.Lines.Count > 0;

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void DeleteSelected()
    {
        LevelDifferenceRowViewModel[] selected = SelectedRows.ToArray();
        document.ExecuteUndoable(() =>
        {
            foreach (var row in selected)
            {
                Items.Remove(row);
            }

            Resequence();
        });

        SelectedRows.Clear();
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void SetAdjustmentPoint() => SetRole(PointRole.AdjustmentPoint);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void SetTransitionPoint() => SetRole(PointRole.TransitionPoint);

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void TogglePointRole()
    {
        LevelDifferenceRowViewModel[] selected = SelectedRows.ToArray();
        document.ExecuteUndoable(() =>
        {
            foreach (var row in selected)
            {
                row.ToPointRole = row.ToPointRole == PointRole.AdjustmentPoint
                    ? PointRole.TransitionPoint
                    : PointRole.AdjustmentPoint;
                row.IsRoleManuallySpecified = true;
            }
        });
    }

    [RelayCommand]
    private void AutoDetectTransitionPoints()
    {
        if (Items.Count == 0)
        {
            return;
        }

        IReadOnlySet<string> knownPoints = document.KnownHeights
            .Select(x => x.PointName.Trim())
            .Where(x => x.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        IReadOnlyList<LevelDifference> updated = transitionProcessor.AutoDetectTransitionPoints(
            Items.Select(x => x.ToDomain()).ToArray(),
            knownPoints);

        document.ReplaceLevelDifferences(updated);
    }

    [RelayCommand(CanExecute = nameof(HasSelection))]
    private void MergeSelectedAndDelete()
    {
        long[] selectedIds = SelectedRows.Select(x => x.Id).ToArray();
        if (selectedIds.Distinct().Count() != selectedIds.Length)
        {
            dialogs.Error(LocalizationService.Text("Loc.Diff.DuplicateInternalId"));
            return;
        }

        IReadOnlyList<LevelDifference> merged = transitionProcessor.MergeSelectedAndDelete(
            Items.Select(x => x.ToDomain()).ToArray(),
            selectedIds.ToHashSet());

        document.ReplaceLevelDifferences(merged);
        SelectedRows.Clear();
    }

    [RelayCommand]
    private void DeleteTransitionPoints()
    {
        if (!Items.Any(x => x.ToPointRole == PointRole.TransitionPoint))
        {
            dialogs.Info(LocalizationService.Text("Loc.Diff.NoTransitionPoints"));
            return;
        }

        if (!dialogs.Confirm(
                LocalizationService.Text("Loc.Diff.DeleteTransitionConfirm"),
                LocalizationService.Text("Loc.Diff.DeleteTransition")))
        {
            return;
        }

        IReadOnlyList<LevelDifference> merged = transitionProcessor.DeleteTransitionPoints(
            Items.Select(x => x.ToDomain()).ToArray());

        document.ReplaceLevelDifferences(merged);
        SelectedRows.Clear();
    }

    public bool PasteRows(string clipboardText)
    {
        if (string.IsNullOrWhiteSpace(clipboardText))
        {
            return false;
        }

        long defaultLineId = SelectedRows.FirstOrDefault()?.LineId ?? document.Lines.FirstOrDefault()?.Id ?? 0;
        if (defaultLineId <= 0)
        {
            dialogs.Info(LocalizationService.Text("Loc.Diff.NoEditableLine"));
            return false;
        }

        var parsed = new List<PasteRow>();
        string[] lines = clipboardText
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (string rawLine in lines)
        {
            string[] cells = rawLine.Split('\t');
            if (cells.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            if (!TryParseClipboardRow(cells, defaultLineId, out PasteRow? row))
            {
                dialogs.Error(LocalizationService.Text("Loc.Diff.PasteFormat"));
                return false;
            }

            parsed.Add(row!);
        }

        if (parsed.Count == 0)
        {
            return false;
        }

        document.ExecuteUndoable(() =>
        {
            foreach (PasteRow row in parsed)
            {
                long lineId = document.Lines.Any(x => x.Id == row.LineId)
                    ? row.LineId
                    : defaultLineId;

                Items.Add(new LevelDifferenceRowViewModel
                {
                    Id = NextTemporaryId(),
                    LineId = lineId,
                    Sequence = NextSequence(lineId),
                    LineName = document.Lines.FirstOrDefault(x => x.Id == lineId)?.Name ?? lineId.ToString(),
                    FromPoint = row.FromPoint,
                    ToPoint = row.ToPoint,
                    HeightDifference = row.HeightDifference,
                    DistanceMeters = row.DistanceMeters,
                    StationCount = row.StationCount,
                    ToPointRole = row.Role,
                    IsRoleManuallySpecified = row.RoleWasSpecified,
                    Comment = row.Comment
                });
            }
        });

        return true;
    }

    private bool HasSelection() => SelectedRows.Count > 0;

    private void SetRole(PointRole role)
    {
        LevelDifferenceRowViewModel[] selected = SelectedRows.ToArray();
        document.ExecuteUndoable(() =>
        {
            foreach (var row in selected)
            {
                row.ToPointRole = role;
                row.IsRoleManuallySpecified = true;
            }
        });
    }

    private int NextSequence(long lineId) =>
        Items.Where(x => x.LineId == lineId)
            .Select(x => x.Sequence)
            .DefaultIfEmpty(-1)
            .Max() + 1;

    private long NextTemporaryId()
    {
        long minimum = Items.Select(x => x.Id).DefaultIfEmpty(0).Min();
        return minimum <= 0 ? minimum - 1 : -1;
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

    private static bool TryParseClipboardRow(
        IReadOnlyList<string> cells,
        long defaultLineId,
        out PasteRow? row)
    {
        row = null;

        // Common external/Excel format:
        // From, To, HeightDifference, Distance, StationCount [, Role] [, Comment]
        if (cells.Count >= 5
            && TryDouble(cells[2], out double diff5)
            && TryDouble(cells[3], out double distance5)
            && TryInt(cells[4], out int stations5))
        {
            PointRole role = ParseRole(cells.Count > 5 ? cells[5] : null, out bool roleSpecified);
            row = new PasteRow(
                cells[0].Trim(),
                cells[1].Trim(),
                diff5,
                distance5,
                stations5,
                role,
                roleSpecified,
                defaultLineId,
                cells.Count > 6 ? cells[^1].Trim() : null);
            return IsValid(row);
        }

        // Rows copied from this DataGrid:
        // Sequence, From, To, HeightDifference, Distance, StationCount, Role, LineId, Comment
        if (cells.Count >= 8
            && TryDouble(cells[3], out double copiedDiff)
            && TryDouble(cells[4], out double copiedDistance)
            && TryInt(cells[5], out int copiedStations))
        {
            PointRole role = ParseRole(cells[6], out bool roleSpecified);
            long lineId = TryLong(cells[7], out long parsedLineId) ? parsedLineId : defaultLineId;
            row = new PasteRow(
                cells[1].Trim(),
                cells[2].Trim(),
                copiedDiff,
                copiedDistance,
                copiedStations,
                role,
                roleSpecified,
                lineId,
                cells.Count > 8 ? cells[8].Trim() : null);
            return IsValid(row);
        }

        return false;
    }

    private static bool IsValid(PasteRow row) =>
        row.FromPoint.Length > 0
        && row.ToPoint.Length > 0
        && !string.Equals(row.FromPoint, row.ToPoint, StringComparison.Ordinal)
        && double.IsFinite(row.HeightDifference)
        && double.IsFinite(row.DistanceMeters)
        && row.DistanceMeters > 0
        && row.StationCount > 0;

    private static PointRole ParseRole(string? text, out bool specified)
    {
        string normalized = text?.Trim() ?? string.Empty;
        if (normalized.Equals(LocalizationService.Text("Loc.Role.Transition"), StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("过渡点", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Transition point", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals(nameof(PointRole.TransitionPoint), StringComparison.OrdinalIgnoreCase))
        {
            specified = true;
            return PointRole.TransitionPoint;
        }

        if (normalized.Equals(LocalizationService.Text("Loc.Role.Adjustment"), StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("平差点", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("Adjustment point", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals(nameof(PointRole.AdjustmentPoint), StringComparison.OrdinalIgnoreCase))
        {
            specified = true;
            return PointRole.AdjustmentPoint;
        }

        specified = false;
        return PointRole.AdjustmentPoint;
    }

    private static bool TryDouble(string text, out double value) =>
        double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
        || double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.CurrentCulture, out value);

    private static bool TryInt(string text, out int value) =>
        int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
        || int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out value);

    private static bool TryLong(string text, out long value) =>
        long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
        || long.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out value);

    private sealed record PasteRow(
        string FromPoint,
        string ToPoint,
        double HeightDifference,
        double DistanceMeters,
        int StationCount,
        PointRole Role,
        bool RoleWasSpecified,
        long LineId,
        string? Comment);
}
