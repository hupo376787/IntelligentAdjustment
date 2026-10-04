using System.Globalization;
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

    public bool PasteRows(string clipboardText)
    {
        if (SelectedLine is null || string.IsNullOrWhiteSpace(clipboardText))
        {
            return false;
        }

        string[] lines = clipboardText
            .Replace("\r\n", "\n", StringComparison.Ordinal)
            .Replace('\r', '\n')
            .Split('\n', StringSplitOptions.RemoveEmptyEntries);

        var parsed = new List<RawObservationRowViewModel>();
        long nextId = NextTemporaryRawId();
        int nextSequence = document.RawObservations
            .Where(x => x.LineId == SelectedLine.Id)
            .Select(x => x.Sequence)
            .DefaultIfEmpty(-1)
            .Max() + 1;

        foreach (string line in lines)
        {
            string[] cells = line.Split('\t');
            if (cells.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            if (!TryParseClipboardRow(
                    cells,
                    SelectedLine.Id,
                    nextId--,
                    nextSequence++,
                    out RawObservationRowViewModel? row))
            {
                dialogs.Error(
                    "剪贴板原始观测无法识别。支持 10 列：起点、终点、B1、B2、F1、F2、后距1、后距2、前距1、前距2；也支持从本表整行复制后粘贴。");
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
            foreach (RawObservationRowViewModel row in parsed)
            {
                document.RawObservations.Add(row);
            }
        });

        Items.Refresh();

        if (document.Settings.AutoUpdateLevelDifferences &&
            dialogs.Confirm(
                $"已粘贴 {parsed.Count} 个测站。是否立即重新计算当前线路高差？",
                "更新高差"))
        {
            GenerateDifferences();
        }

        return true;
    }

    private static bool TryParseClipboardRow(
        IReadOnlyList<string> cells,
        long lineId,
        long id,
        int sequence,
        out RawObservationRowViewModel? row)
    {
        row = null;

        // External/Excel format:
        // From, To, B1, B2, F1, F2, DB1, DB2, DF1, DF2 [, Mode] [, Order]
        if (cells.Count >= 10 &&
            !TryInt(cells[0], out _))
        {
            row = BuildClipboardRow(
                cells,
                offset: 0,
                lineId,
                id,
                sequence,
                modeCell: cells.Count > 10 ? cells[10] : "BFFB",
                orderCell: cells.Count > 11 ? cells[11] : "BFFB",
                validCell: cells.Count > 12 ? cells[12] : "true",
                reasonCell: cells.Count > 13 ? cells[13] : null,
                temperatureCell: cells.Count > 14 ? cells[14] : null,
                commentCell: cells.Count > 15 ? cells[15] : null);
            return row is not null;
        }

        // Rows copied from this grid:
        // Seq, From, To, B1, B2, F1, F2, DB1, DB2, DF1, DF2, Mode, Order, Valid,
        // InvalidReason, MeasuredAt, Temperature, SourceFile, Comment
        if (cells.Count >= 14)
        {
            row = BuildClipboardRow(
                cells,
                offset: 1,
                lineId,
                id,
                sequence,
                modeCell: cells.Count > 11 ? cells[11] : "BFFB",
                orderCell: cells.Count > 12 ? cells[12] : "BFFB",
                validCell: cells.Count > 13 ? cells[13] : "true",
                reasonCell: cells.Count > 14 ? cells[14] : null,
                temperatureCell: cells.Count > 16 ? cells[16] : null,
                commentCell: cells.Count > 18 ? cells[18] : null);
            return row is not null;
        }

        return false;
    }

    private static RawObservationRowViewModel? BuildClipboardRow(
        IReadOnlyList<string> cells,
        int offset,
        long lineId,
        long id,
        int sequence,
        string? modeCell,
        string? orderCell,
        string? validCell,
        string? reasonCell,
        string? temperatureCell,
        string? commentCell)
    {
        int fromIndex = offset;
        int toIndex = offset + 1;
        int b1Index = offset + 2;
        int b2Index = offset + 3;
        int f1Index = offset + 4;
        int f2Index = offset + 5;
        int db1Index = offset + 6;
        int db2Index = offset + 7;
        int df1Index = offset + 8;
        int df2Index = offset + 9;

        if (cells.Count <= df2Index)
        {
            return null;
        }

        string from = cells[fromIndex].Trim();
        string to = cells[toIndex].Trim();

        if (from.Length == 0 || to.Length == 0 ||
            !TryNullableDouble(cells[b1Index], out double? b1) ||
            !TryNullableDouble(cells[b2Index], out double? b2) ||
            !TryNullableDouble(cells[f1Index], out double? f1) ||
            !TryNullableDouble(cells[f2Index], out double? f2) ||
            !TryNullableDouble(cells[db1Index], out double? db1) ||
            !TryNullableDouble(cells[db2Index], out double? db2) ||
            !TryNullableDouble(cells[df1Index], out double? df1) ||
            !TryNullableDouble(cells[df2Index], out double? df2))
        {
            return null;
        }

        MeasurementMode mode = ParseMeasurementMode(modeCell);
        ObservationOrder order = ParseObservationOrder(orderCell);

        bool isValid = !string.Equals(validCell?.Trim(), "false", StringComparison.OrdinalIgnoreCase)
                       && !string.Equals(validCell?.Trim(), "否", StringComparison.Ordinal);

        _ = TryNullableDouble(temperatureCell ?? string.Empty, out double? temperature);

        return new RawObservationRowViewModel
        {
            Id = id,
            LineId = lineId,
            Sequence = sequence,
            FromPoint = from,
            ToPoint = to,
            B1 = b1,
            B2 = b2,
            F1 = f1,
            F2 = f2,
            DistanceB1 = db1,
            DistanceB2 = db2,
            DistanceF1 = df1,
            DistanceF2 = df2,
            MeasurementMode = mode,
            ObservationOrder = order,
            IsValid = isValid,
            InvalidReason = string.IsNullOrWhiteSpace(reasonCell) ? null : reasonCell.Trim(),
            TemperatureCelsius = temperature,
            Comment = string.IsNullOrWhiteSpace(commentCell) ? null : commentCell.Trim()
        };
    }

    private static MeasurementMode ParseMeasurementMode(string? value)
    {
        string normalized = value?.Trim() ?? string.Empty;
        if (Enum.TryParse(normalized, ignoreCase: true, out MeasurementMode parsed))
        {
            return parsed;
        }

        return normalized.ToUpperInvariant() switch
        {
            "ABF" => MeasurementMode.AlternatingBF,
            "ABFFB" => MeasurementMode.AlternatingBFFB,
            "BF" => MeasurementMode.BF,
            "BBFF" => MeasurementMode.BBFF,
            _ => MeasurementMode.BFFB
        };
    }

    private static ObservationOrder ParseObservationOrder(string? value) =>
        Enum.TryParse(value?.Trim(), ignoreCase: true, out ObservationOrder parsed)
            ? parsed
            : ObservationOrder.BFFB;

    private static bool TryNullableDouble(string text, out double? value)
    {
        string normalized = text.Trim();
        if (normalized.Length == 0)
        {
            value = null;
            return true;
        }

        if (double.TryParse(normalized, NumberStyles.Float, CultureInfo.InvariantCulture, out double result) ||
            double.TryParse(normalized, NumberStyles.Float, CultureInfo.CurrentCulture, out result))
        {
            value = result;
            return double.IsFinite(result);
        }

        value = null;
        return false;
    }

    private static bool TryInt(string text, out int value) =>
        int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out value)
        || int.TryParse(text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out value);

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
