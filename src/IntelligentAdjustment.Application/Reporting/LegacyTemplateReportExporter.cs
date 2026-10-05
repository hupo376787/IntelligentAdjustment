using System.Globalization;
using IntelligentAdjustment.Application.Models;
using IntelligentAdjustment.Domain;
using NPOI.XWPF.UserModel;

namespace IntelligentAdjustment.Application.Reporting;

/// <summary>
/// Exports the legacy AdjustLevel report by filling the supplied Report.docx template.
/// The template bytes are embedded verbatim in split base64 resources so the original
/// page setup, fonts, paragraph styles, numbering and table geometry are retained.
/// </summary>
internal sealed class LegacyTemplateReportExporter
{
    private const string EmptyValue = "—";

    public void Export(
        ProjectWorkspace workspace,
        CalculationBundle? calculation,
        string filePath)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        using Stream template = LegacyReportTemplate.Open();
        using var document = new XWPFDocument(template);

        DateTime now = DateTime.Now;
        ProjectSettings settings = workspace.Settings;

        ReplaceToken(document, "<Unit/>", workspace.Metadata.UnitName);
        ReplaceToken(document, "<Y/>", now.Year.ToString(CultureInfo.InvariantCulture));
        ReplaceToken(document, "<M/>", now.Month.ToString("00", CultureInfo.InvariantCulture));
        ReplaceToken(document, "<D/>", now.Day.ToString("00", CultureInfo.InvariantCulture));

        FillLabel(document, "项目名称：", workspace.Metadata.ProjectName);
        FillLabel(document, "项目编号：", workspace.Metadata.ProjectNumber);
        FillLabel(document, "项目负责人：", workspace.Metadata.ProjectLeader);
        FillLabel(document, "审 核 人：", workspace.Metadata.Reviewer);

        FillNarrativeSections(document, workspace.ReportText);
        FillAccuracyParagraphs(document, workspace, calculation);

        FillClosureTable(document, workspace, calculation);
        FillKnownHeightTable(document, workspace);
        FillObservedDifferenceTable(document, workspace);
        FillAdjustedDifferenceTable(document, workspace, calculation);
        FillAdjustedHeightTable(document, workspace, calculation);

        string conclusion = string.IsNullOrWhiteSpace(workspace.ReportText.ConclusionAndRecommendations)
            ? "请根据项目实际检查、验收和使用要求填写结论与建议。本软件不自动生成无法由计算结果验证的检查、验收结论。"
            : workspace.ReportText.ConclusionAndRecommendations.Trim();
        ReplaceParagraphBeginningWith(document, "本平差成果按照有关测绘产品检查验收", conclusion);

        // Remove any unfilled legacy placeholders instead of leaking template tokens into the report.
        foreach (string token in new[]
        {
            "<Length/>", "<RM0/>", "<MaxM0/>", "<MaxM0Name/>",
            "<MaxSideM0/>", "<MaxSideM0FromName/>", "<MaxSideM0ToName/>",
            "<LimitType/>", "<Limit/>", "<R_Closure/>", "<D_Known/>",
            "<D_Diff/>", "<R_Diff/>", "<R_Height/>"
        })
        {
            ReplaceToken(document, token, EmptyValue);
        }

        string? directory = Path.GetDirectoryName(Path.GetFullPath(filePath));
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        using FileStream stream = File.Create(filePath);
        document.Write(stream);
    }

    private static void FillNarrativeSections(XWPFDocument document, ReportTextContent text)
    {
        IReadOnlyDictionary<string, string> task = ParseTaggedText(
            text.TaskOverview,
            ["任务来源", "目的", "测区范围"]);
        SetBodyAfter(document, "(1) 任务来源", GetOrFallback(task, "任务来源", text.TaskOverview));
        SetBodyAfter(document, "(2) 目的", GetOrEmpty(task, "目的"));
        SetBodyAfter(document, "(3) 测区范围", GetOrEmpty(task, "测区范围"));

        IReadOnlyDictionary<string, string> geography = ParseTaggedText(
            text.NaturalGeography,
            ["测区地理特征", "测区居民情况", "测区交通情况", "测区气候情况", "测区困难类别"]);
        SetBodyAfter(document, "(1) 测区地理特征", GetOrFallback(geography, "测区地理特征", text.NaturalGeography));
        SetBodyAfter(document, "(2) 测区居民情况", GetOrEmpty(geography, "测区居民情况"));
        SetBodyAfter(document, "(3) 测区交通情况", GetOrEmpty(geography, "测区交通情况"));
        SetBodyAfter(document, "(4) 测区气候情况", GetOrEmpty(geography, "测区气候情况"));
        SetBodyAfter(document, "(5) 测区困难类别", GetOrEmpty(geography, "测区困难类别"));

        IReadOnlyDictionary<string, string> existing = ParseTaggedText(
            text.ExistingData,
            ["已有资料的数量、形式、施测年代", "采用的高程基准", "已有资料的质量情况及评价", "对已有资料的利用方案"]);
        SetBodyAfter(document, "(1) 已有资料的数量、形式、施测年代", GetOrFallback(existing, "已有资料的数量、形式、施测年代", text.ExistingData));
        SetBodyAfter(document, "(2) 采用的高程基准", GetOrEmpty(existing, "采用的高程基准"));
        SetBodyAfter(document, "(3) 已有资料的质量情况及评价", GetOrEmpty(existing, "已有资料的质量情况及评价"));
        SetBodyAfter(document, "(4) 对已有资料的利用方案", GetOrEmpty(existing, "对已有资料的利用方案"));

        // The supplied template already contains the legacy standards list.
        // User-entered references are therefore placed in the "其它文件" slot.
        SetBodyAfter(document, "(2) 引用的其它文件", text.ReferencedStandards);

        IReadOnlyDictionary<string, string> indicators = ParseTaggedText(
            text.TechnicalIndicators,
            ["测量仪器的类型及精度指标", "施测精度"]);
        SetBodyAfter(document, "(1) 测量仪器的类型及精度指标", GetOrFallback(indicators, "测量仪器的类型及精度指标", text.TechnicalIndicators));
        SetBodyAfter(document, "(2) 施测精度", GetOrEmpty(indicators, "施测精度"));

        SetBodyAfter(document, "2.4 外业完成的工作量", text.FieldWorkSummary);
    }

    private static IReadOnlyDictionary<string, string> ParseTaggedText(
        string source,
        IReadOnlyList<string> labels)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(source))
        {
            return result;
        }

        string? current = null;
        var buffer = new List<string>();

        void Flush()
        {
            if (current is not null)
            {
                result[current] = string.Join(Environment.NewLine, buffer).Trim();
            }

            buffer.Clear();
        }

        foreach (string rawLine in source.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            string line = rawLine.Trim();
            string? matched = labels.FirstOrDefault(label =>
                line.StartsWith(label + "：", StringComparison.Ordinal)
                || line.StartsWith(label + ":", StringComparison.Ordinal)
                || string.Equals(line, label, StringComparison.Ordinal));

            if (matched is not null)
            {
                Flush();
                current = matched;
                int separator = line.IndexOfAny(['：', ':']);
                if (separator >= 0 && separator + 1 < line.Length)
                {
                    buffer.Add(line[(separator + 1)..].Trim());
                }
            }
            else if (current is not null)
            {
                buffer.Add(rawLine);
            }
        }

        Flush();
        return result;
    }

    private static string GetOrEmpty(IReadOnlyDictionary<string, string> values, string key) =>
        values.TryGetValue(key, out string? value) ? value : string.Empty;

    private static string GetOrFallback(
        IReadOnlyDictionary<string, string> values,
        string key,
        string fallback) =>
        values.Count == 0 ? fallback : GetOrEmpty(values, key);

    private static void FillAccuracyParagraphs(
        XWPFDocument document,
        ProjectWorkspace workspace,
        CalculationBundle? calculation)
    {
        int distanceDecimals = Math.Clamp(workspace.Settings.DistanceDecimals, 0, 10);
        int heightDecimals = Math.Clamp(workspace.Settings.HeightDecimals, 0, 10);
        string distanceFormat = "F" + distanceDecimals.ToString(CultureInfo.InvariantCulture);
        string heightFormat = "F" + heightDecimals.ToString(CultureInfo.InvariantCulture);

        double totalLength = workspace.LevelDifferences.Sum(x => x.DistanceMeters);
        ReplaceToken(document, "<Length/>", totalLength.ToString(distanceFormat, CultureInfo.InvariantCulture));

        if (calculation is null)
        {
            ReplaceToken(document, "<RM0/>", EmptyValue);
            ReplaceToken(document, "<MaxM0/>", EmptyValue);
            ReplaceToken(document, "<MaxM0Name/>", EmptyValue);
            ReplaceToken(document, "<MaxSideM0/>", EmptyValue);
            ReplaceToken(document, "<MaxSideM0FromName/>", EmptyValue);
            ReplaceToken(document, "<MaxSideM0ToName/>", EmptyValue);
        }
        else
        {
            AdjustmentResult result = calculation.AdjustmentResult;
            AdjustedHeight? maxHeight = result.Heights
                .OrderByDescending(x => x.StandardError)
                .FirstOrDefault();
            AdjustedDifference? maxSide = result.Differences
                .OrderByDescending(x => x.StandardError)
                .FirstOrDefault();

            ReplaceToken(document, "<RM0/>",
                result.UnitWeightStandardDeviation.ToString(heightFormat, CultureInfo.InvariantCulture));
            ReplaceToken(document, "<MaxM0/>",
                maxHeight?.StandardError.ToString(heightFormat, CultureInfo.InvariantCulture) ?? EmptyValue);
            ReplaceToken(document, "<MaxM0Name/>", maxHeight?.PointName ?? EmptyValue);
            ReplaceToken(document, "<MaxSideM0/>",
                maxSide?.StandardError.ToString(heightFormat, CultureInfo.InvariantCulture) ?? EmptyValue);
            ReplaceToken(document, "<MaxSideM0FromName/>", maxSide?.FromPoint ?? EmptyValue);
            ReplaceToken(document, "<MaxSideM0ToName/>", maxSide?.ToPoint ?? EmptyValue);
        }

        if (workspace.Settings.ToleranceMode == ClosureToleranceMode.Distance)
        {
            ReplaceToken(document, "<LimitType/>", "线路长度");
            ReplaceToken(document, "<Limit/>",
                workspace.Settings.DistanceToleranceCoefficientMm.ToString("0.###", CultureInfo.InvariantCulture) + "√L mm（L为km）");
        }
        else
        {
            ReplaceToken(document, "<LimitType/>", "测站数");
            ReplaceToken(document, "<Limit/>",
                workspace.Settings.StationToleranceCoefficientMm.ToString("0.###", CultureInfo.InvariantCulture) + "√n mm");
        }
    }

    private static void FillClosureTable(
        XWPFDocument document,
        ProjectWorkspace workspace,
        CalculationBundle? calculation)
    {
        IReadOnlyList<NetworkRoute> routes = calculation?.Routes ?? [];
        var rows = new List<string[]>(routes.Count);

        foreach (NetworkRoute route in routes)
        {
            int pointCount = route.Points.Count;
            if (pointCount > 1 &&
                string.Equals(route.Points[0], route.Points[^1], StringComparison.Ordinal))
            {
                pointCount--;
            }

            double selectedLimit = workspace.Settings.ToleranceMode == ClosureToleranceMode.Distance
                ? route.LengthToleranceMeters
                : route.StationToleranceMeters;

            rows.Add(
            [
                route.Index.ToString(CultureInfo.InvariantCulture),
                pointCount.ToString(CultureInfo.InvariantCulture),
                (route.LengthMeters / 1000.0).ToString("F3", CultureInfo.InvariantCulture),
                (route.ClosureMeters * 1000.0).ToString("F3", CultureInfo.InvariantCulture),
                (selectedLimit * 1000.0).ToString("F3", CultureInfo.InvariantCulture),
                route.RouteType == RouteType.ClosedLoop ? "闭合环" : "附合路线"
            ]);
        }

        FillMarkerTable(document, "<R_Closure/>", rows);
    }

    private static void FillKnownHeightTable(XWPFDocument document, ProjectWorkspace workspace)
    {
        int decimals = Math.Clamp(workspace.Settings.HeightDecimals, 0, 10);
        string format = "F" + decimals.ToString(CultureInfo.InvariantCulture);

        Dictionary<string, int> pointOrder = BuildObservationPointOrder(workspace.LevelDifferences);

        List<string[]> rows = workspace.KnownHeights
            .Select((item, originalIndex) => (item, originalIndex))
            .OrderBy(x => pointOrder.TryGetValue(x.item.PointName, out int order) ? order : int.MaxValue)
            .ThenBy(x => x.originalIndex)
            .Select(x => new[]
            {
                x.item.PointName,
                x.item.Height.ToString(format, CultureInfo.InvariantCulture),
                x.item.Comment ?? string.Empty
            })
            .ToList();

        FillMarkerTable(document, "<D_Known/>", rows);
    }

    private static void FillObservedDifferenceTable(XWPFDocument document, ProjectWorkspace workspace)
    {
        int heightDecimals = Math.Clamp(workspace.Settings.HeightDecimals, 0, 10);
        int distanceDecimals = Math.Clamp(workspace.Settings.DistanceDecimals, 0, 10);
        string heightFormat = "F" + heightDecimals.ToString(CultureInfo.InvariantCulture);
        string distanceFormat = "F" + distanceDecimals.ToString(CultureInfo.InvariantCulture);
        HashSet<string> known = workspace.KnownHeights
            .Select(x => x.PointName)
            .ToHashSet(StringComparer.Ordinal);

        Dictionary<long, int> lineOrder = workspace.Lines
            .ToDictionary(x => x.Id, x => x.DisplayOrder);

        List<string[]> rows = workspace.LevelDifferences
            .OrderBy(x => lineOrder.TryGetValue(x.LineId, out int order) ? order : int.MaxValue)
            .ThenBy(x => x.Sequence)
            .ThenBy(x => x.Id)
            .Select(x => new[]
            {
                x.FromPoint,
                x.ToPoint,
                x.HeightDifference.ToString(heightFormat, CultureInfo.InvariantCulture),
                x.DistanceMeters.ToString(distanceFormat, CultureInfo.InvariantCulture),
                x.StationCount.ToString(CultureInfo.InvariantCulture),
                known.Contains(x.ToPoint)
                    ? "已知点"
                    : x.ToPointRole == PointRole.TransitionPoint ? "过渡点" : "平差点",
                x.Comment ?? string.Empty
            })
            .ToList();

        FillMarkerTable(document, "<D_Diff/>", rows);
    }

    private static void FillAdjustedDifferenceTable(
        XWPFDocument document,
        ProjectWorkspace workspace,
        CalculationBundle? calculation)
    {
        int heightDecimals = Math.Clamp(workspace.Settings.HeightDecimals, 0, 10);
        int distanceDecimals = Math.Clamp(workspace.Settings.DistanceDecimals, 0, 10);
        string heightFormat = "F" + heightDecimals.ToString(CultureInfo.InvariantCulture);
        string distanceFormat = "F" + distanceDecimals.ToString(CultureInfo.InvariantCulture);

        Dictionary<long, string?> comments = workspace.LevelDifferences
            .ToDictionary(x => x.Id, x => x.Comment);

        List<string[]> rows = (calculation?.AdjustmentResult.Differences ?? [])
            .Select(x => new[]
            {
                x.FromPoint,
                x.ToPoint,
                x.AdjustedDifferenceValue.ToString(heightFormat, CultureInfo.InvariantCulture),
                x.DistanceMeters.ToString(distanceFormat, CultureInfo.InvariantCulture),
                x.StandardError.ToString(heightFormat, CultureInfo.InvariantCulture),
                x.Residual.ToString(heightFormat, CultureInfo.InvariantCulture),
                comments.TryGetValue(x.ObservationId, out string? comment) ? comment ?? string.Empty : string.Empty
            })
            .ToList();

        FillMarkerTable(document, "<R_Diff/>", rows);
    }

    private static void FillAdjustedHeightTable(
        XWPFDocument document,
        ProjectWorkspace workspace,
        CalculationBundle? calculation)
    {
        int decimals = Math.Clamp(workspace.Settings.HeightDecimals, 0, 10);
        string format = "F" + decimals.ToString(CultureInfo.InvariantCulture);
        Dictionary<string, string?> comments = workspace.KnownHeights
            .ToDictionary(x => x.PointName, x => x.Comment, StringComparer.Ordinal);

        List<string[]> rows = (calculation?.AdjustmentResult.Heights ?? [])
            .Select(x => new[]
            {
                x.PointName,
                x.Height.ToString(format, CultureInfo.InvariantCulture),
                x.StandardError.ToString(format, CultureInfo.InvariantCulture),
                x.IsReferencePoint ? "已知点" : "平差点",
                comments.TryGetValue(x.PointName, out string? comment) ? comment ?? string.Empty : string.Empty
            })
            .ToList();

        FillMarkerTable(document, "<R_Height/>", rows);
    }

    private static Dictionary<string, int> BuildObservationPointOrder(
        IReadOnlyList<LevelDifference> differences)
    {
        var result = new Dictionary<string, int>(StringComparer.Ordinal);
        int index = 0;
        foreach (LevelDifference difference in differences)
        {
            if (result.TryAdd(difference.FromPoint, index))
            {
                index++;
            }

            if (result.TryAdd(difference.ToPoint, index))
            {
                index++;
            }
        }

        return result;
    }

    private static void FillMarkerTable(
        XWPFDocument document,
        string marker,
        IReadOnlyList<string[]> values)
    {
        XWPFTable? table = document.Tables.FirstOrDefault(t =>
            t.Rows.Any(row => RowText(row).Contains(marker, StringComparison.Ordinal)));

        if (table is null)
        {
            throw new InvalidDataException($"报告模板缺少表格占位符 {marker}。");
        }

        int markerIndex = -1;
        for (int i = 0; i < table.Rows.Count; i++)
        {
            if (RowText(table.Rows[i]).Contains(marker, StringComparison.Ordinal))
            {
                markerIndex = i;
                break;
            }
        }

        if (markerIndex < 0)
        {
            throw new InvalidDataException($"报告模板缺少表格占位符 {marker}。");
        }

        for (int i = table.Rows.Count - 1; i > markerIndex; i--)
        {
            table.RemoveRow(i);
        }

        XWPFTableRow templateRow = table.Rows[markerIndex];

        if (values.Count == 0)
        {
            SetRowValues(templateRow, Enumerable.Repeat(string.Empty, templateRow.GetTableCells().Count).ToArray());
            return;
        }

        SetRowValues(templateRow, values[0]);
        for (int rowIndex = 1; rowIndex < values.Count; rowIndex++)
        {
            XWPFTableRow row = table.CreateRow();
            SetRowValues(row, values[rowIndex]);
        }
    }

    private static string RowText(XWPFTableRow row) =>
        string.Concat(row.GetTableCells().Select(cell => cell.GetText()));

    private static void SetRowValues(XWPFTableRow row, IReadOnlyList<string> values)
    {
        IList<XWPFTableCell> cells = row.GetTableCells();
        while (cells.Count < values.Count)
        {
            row.AddNewTableCell();
            cells = row.GetTableCells();
        }

        for (int i = 0; i < cells.Count; i++)
        {
            SetCellText(cells[i], i < values.Count ? values[i] : string.Empty);
        }
    }

    private static void SetCellText(XWPFTableCell cell, string value)
    {
        XWPFParagraph paragraph = cell.Paragraphs.FirstOrDefault() ?? cell.AddParagraph();
        ClearRuns(paragraph);
        paragraph.Alignment = ParagraphAlignment.CENTER;
        paragraph.CreateRun().SetText(value ?? string.Empty);

        foreach (XWPFParagraph extra in cell.Paragraphs.Skip(1))
        {
            ClearRuns(extra);
        }
    }

    private static void FillLabel(XWPFDocument document, string label, string value)
    {
        XWPFParagraph? paragraph = document.Paragraphs.FirstOrDefault(p =>
            string.Equals(Normalize(p.Text), Normalize(label), StringComparison.Ordinal));

        if (paragraph is null)
        {
            return;
        }

        SetParagraphText(paragraph, label + (value ?? string.Empty));
    }

    private static void SetBodyAfter(XWPFDocument document, string heading, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return;
        }

        for (int i = 0; i < document.Paragraphs.Count - 1; i++)
        {
            XWPFParagraph paragraph = document.Paragraphs[i];
            if (!Normalize(paragraph.Text).StartsWith(Normalize(heading), StringComparison.Ordinal))
            {
                continue;
            }

            XWPFParagraph target = document.Paragraphs[i + 1];
            SetParagraphText(target, value.Trim());
            return;
        }
    }

    private static void ReplaceParagraphBeginningWith(
        XWPFDocument document,
        string prefix,
        string replacement)
    {
        XWPFParagraph? paragraph = document.Paragraphs.FirstOrDefault(p =>
            Normalize(p.Text).StartsWith(Normalize(prefix), StringComparison.Ordinal));

        if (paragraph is not null)
        {
            SetParagraphText(paragraph, replacement);
        }
    }

    private static void ReplaceToken(XWPFDocument document, string token, string replacement)
    {
        foreach (XWPFParagraph paragraph in EnumerateParagraphs(document))
        {
            string text = paragraph.Text;
            if (text.Contains(token, StringComparison.Ordinal))
            {
                SetParagraphText(paragraph, text.Replace(token, replacement ?? string.Empty, StringComparison.Ordinal));
            }
        }
    }

    private static IEnumerable<XWPFParagraph> EnumerateParagraphs(XWPFDocument document)
    {
        foreach (XWPFParagraph paragraph in document.Paragraphs)
        {
            yield return paragraph;
        }

        foreach (XWPFTable table in document.Tables)
        {
            foreach (XWPFTableRow row in table.Rows)
            {
                foreach (XWPFTableCell cell in row.GetTableCells())
                {
                    foreach (XWPFParagraph paragraph in cell.Paragraphs)
                    {
                        yield return paragraph;
                    }
                }
            }
        }
    }

    private static void SetParagraphText(XWPFParagraph paragraph, string value)
    {
        // NPOI 2.7.2 cannot RemoveRun() when the run is nested in a Word field
        // or hyperlink. The legacy template contains such runs, so keep the run
        // containers and clear only their literal text nodes.
        XWPFRun? target = paragraph.Runs.FirstOrDefault(run =>
            run is not XWPFFieldRun && run is not XWPFHyperlinkRun);

        foreach (XWPFRun run in paragraph.Runs)
        {
            ClearRunText(run);
        }

        target ??= paragraph.CreateRun();
        target.SetText(value ?? string.Empty, 0);
    }

    private static void ClearRuns(XWPFParagraph paragraph)
    {
        // Do not call paragraph.RemoveRun() here. Older NPOI versions throw
        // "Removing Field or Hyperlink runs not yet supported" for template
        // paragraphs containing fields/hyperlinks.
        foreach (XWPFRun run in paragraph.Runs)
        {
            ClearRunText(run);
        }
    }

    private static void ClearRunText(XWPFRun run)
    {
        var ctRun = run.GetCTR();
        for (int i = ctRun.SizeOfTArray() - 1; i >= 0; i--)
        {
            ctRun.RemoveT(i);
        }
    }

    private static string Normalize(string? value) =>
        (value ?? string.Empty)
            .Replace(" ", string.Empty, StringComparison.Ordinal)
            .Replace("\u3000", string.Empty, StringComparison.Ordinal)
            .Replace("\t", string.Empty, StringComparison.Ordinal)
            .Trim();
}
