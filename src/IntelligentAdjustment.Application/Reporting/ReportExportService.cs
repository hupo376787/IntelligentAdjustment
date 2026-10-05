using IntelligentAdjustment.Application.Models;
using IntelligentAdjustment.Domain;
using NPOI.SS.UserModel;
using NPOI.XSSF.UserModel;
using NPOI.XWPF.UserModel;
using SpreadsheetCell = NPOI.SS.UserModel.ICell;
using SpreadsheetHorizontalAlignment = NPOI.SS.UserModel.HorizontalAlignment;
using SpreadsheetVerticalAlignment = NPOI.SS.UserModel.VerticalAlignment;

namespace IntelligentAdjustment.Application.Reporting;

public sealed class ReportExportService
{
    public void ExportDocx(
        ProjectWorkspace workspace,
        CalculationBundle? calculation,
        string filePath)
    {
        var exporter = new LegacyTemplateReportExporter();
        exporter.Export(workspace, calculation, filePath);
    }

    public void ExportXlsx(
        ProjectWorkspace workspace,
        CalculationBundle? calculation,
        string filePath)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        using IWorkbook workbook = new XSSFWorkbook();
        ICellStyle headerStyle = CreateHeaderStyle(workbook);
        ICellStyle numberStyle = CreateNumberStyle(
            workbook,
            BuildNumberFormat(workspace.Settings.HeightDecimals));
        ICellStyle distanceStyle = CreateNumberStyle(
            workbook,
            BuildNumberFormat(workspace.Settings.DistanceDecimals));

        WriteProjectInfoSheet(workbook, workspace, calculation, headerStyle);
        WriteClosureSheet(workbook, workspace, calculation, headerStyle, distanceStyle);
        WriteKnownHeightSheet(workbook, workspace.KnownHeights, headerStyle, numberStyle);
        WriteObservedDifferenceSheet(workbook, workspace.LevelDifferences, headerStyle, numberStyle, distanceStyle);
        WriteAdjustedDifferenceSheet(
            workbook,
            calculation?.AdjustmentResult.Differences ?? [],
            headerStyle,
            numberStyle,
            distanceStyle);
        WriteAdjustedHeightSheet(
            workbook,
            calculation?.AdjustmentResult.Heights ?? [],
            headerStyle,
            numberStyle);

        using FileStream stream = File.Create(filePath);
        workbook.Write(stream, leaveOpen: false);
    }

    private static string BuildAccuracySummary(
        ProjectWorkspace workspace,
        CalculationBundle? calculation)
    {
        double length = workspace.LevelDifferences.Sum(x => x.DistanceMeters);
        if (calculation is null)
        {
            return $"本高程控制网线路总长为：{length:F4} 米。尚未执行高程平差。";
        }

        AdjustmentResult result = calculation.AdjustmentResult;
        AdjustedHeight? maxHeight = result.Heights
            .Where(x => !x.IsReferencePoint)
            .OrderByDescending(x => x.StandardError)
            .FirstOrDefault();

        AdjustedDifference? maxSide = result.Differences
            .OrderByDescending(x => x.StandardError)
            .FirstOrDefault();

        string maxPointText = maxHeight is null
            ? "无未知高程点。"
            : $"平差后最大点位中误差为：{maxHeight.StandardError:F6} 米；点位为：{maxHeight.PointName}。";

        string maxSideText = maxSide is null
            ? "无可统计测段。"
            : $"平差后最大相邻点中误差为：{maxSide.StandardError:F6} 米；相邻点为：{maxSide.FromPoint} → {maxSide.ToPoint}。";

        string stale = calculation.Revision.ResultsAreStale
            ? "注意：以下平差结果基于旧输入数据。"
            : "平差结果与当前输入数据一致。";

        return
            $"本高程控制网线路总长为：{length:F4} 米。{Environment.NewLine}" +
            $"平差后单位权中误差为：{result.UnitWeightStandardDeviation:F6} 米。{Environment.NewLine}" +
            $"{maxPointText}{Environment.NewLine}" +
            $"{maxSideText}{Environment.NewLine}" +
            $"{stale}";
    }

    private static void AddTitle(XWPFDocument document, string text, int size)
    {
        XWPFParagraph paragraph = document.CreateParagraph();
        paragraph.Alignment = ParagraphAlignment.CENTER;
        XWPFRun run = paragraph.CreateRun();
        run.SetText(text);
        run.IsBold = true;
        run.FontSize = size;
        run.FontFamily = "Microsoft YaHei";
    }

    private static void AddCenteredParagraph(XWPFDocument document, string? text, int size)
    {
        XWPFParagraph paragraph = document.CreateParagraph();
        paragraph.Alignment = ParagraphAlignment.CENTER;
        XWPFRun run = paragraph.CreateRun();
        run.SetText(text ?? string.Empty);
        run.FontSize = size;
        run.FontFamily = "Microsoft YaHei";
    }

    private static void AddHeading(XWPFDocument document, string text, int level)
    {
        XWPFParagraph paragraph = document.CreateParagraph();
        XWPFRun run = paragraph.CreateRun();
        run.SetText(text);
        run.IsBold = true;
        run.FontSize = level == 1 ? 16 : 13;
        run.FontFamily = "Microsoft YaHei";
    }

    private static void AddBody(XWPFDocument document, string? text)
    {
        string normalized = string.IsNullOrWhiteSpace(text) ? "（未填写）" : text.Trim();
        foreach (string line in normalized.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            XWPFParagraph paragraph = document.CreateParagraph();
            XWPFRun run = paragraph.CreateRun();
            run.SetText(line);
            run.FontSize = 11;
            run.FontFamily = "Microsoft YaHei";
        }
    }

    private static void AddKeyValueTable(
        XWPFDocument document,
        IReadOnlyList<(string Key, string? Value)> rows)
    {
        XWPFTable table = document.CreateTable(rows.Count, 2);
        for (int i = 0; i < rows.Count; i++)
        {
            SetCell(table.GetRow(i).GetCell(0), rows[i].Key, true);
            SetCell(table.GetRow(i).GetCell(1), rows[i].Value ?? string.Empty, false);
        }
    }

    private static void AddClosureTable(
        XWPFDocument document,
        ProjectWorkspace workspace,
        CalculationBundle? calculation)
    {
        IReadOnlyList<NetworkRoute> routes = calculation?.Routes ?? [];
        XWPFTable table = document.CreateTable(routes.Count + 1, 6);
        string[] headers = ["环序号", "环点数", "环长度(km)", "闭合差(mm)", "限差(mm)", "环类型"];
        SetHeaderRow(table.GetRow(0), headers);

        for (int i = 0; i < routes.Count; i++)
        {
            NetworkRoute route = routes[i];
            double tolerance = workspace.Settings.ToleranceMode == ClosureToleranceMode.Distance
                ? route.LengthToleranceMeters
                : route.StationToleranceMeters;

            XWPFTableRow row = table.GetRow(i + 1);
            SetCell(row.GetCell(0), route.Index.ToString(), false);
            SetCell(row.GetCell(1), route.Points.Count.ToString(), false);
            SetCell(row.GetCell(2), (route.LengthMeters / 1000.0).ToString("F4"), false);
            SetCell(row.GetCell(3), (route.ClosureMeters * 1000.0).ToString("F3"), false);
            SetCell(row.GetCell(4), (tolerance * 1000.0).ToString("F3"), false);
            SetCell(row.GetCell(5), route.RouteType == RouteType.ClosedLoop ? "闭合" : "附合", false);
        }
    }

    private static void AddKnownHeightTable(
        XWPFDocument document,
        IReadOnlyList<KnownHeight> items)
    {
        XWPFTable table = document.CreateTable(items.Count + 1, 3);
        SetHeaderRow(table.GetRow(0), ["点名", "高程(m)", "备注"]);

        for (int i = 0; i < items.Count; i++)
        {
            XWPFTableRow row = table.GetRow(i + 1);
            SetCell(row.GetCell(0), items[i].PointName, false);
            SetCell(row.GetCell(1), items[i].Height.ToString("F5"), false);
            SetCell(row.GetCell(2), items[i].Comment ?? string.Empty, false);
        }
    }

    private static void AddObservedDifferenceTable(
        XWPFDocument document,
        IReadOnlyList<LevelDifference> items)
    {
        XWPFTable table = document.CreateTable(items.Count + 1, 7);
        SetHeaderRow(table.GetRow(0), ["从点", "到点", "高差(m)", "距离(m)", "测站数", "点类型", "备注"]);

        for (int i = 0; i < items.Count; i++)
        {
            LevelDifference item = items[i];
            XWPFTableRow row = table.GetRow(i + 1);
            SetCell(row.GetCell(0), item.FromPoint, false);
            SetCell(row.GetCell(1), item.ToPoint, false);
            SetCell(row.GetCell(2), item.HeightDifference.ToString("F5"), false);
            SetCell(row.GetCell(3), item.DistanceMeters.ToString("F4"), false);
            SetCell(row.GetCell(4), item.StationCount.ToString(), false);
            SetCell(row.GetCell(5), item.ToPointRole == PointRole.TransitionPoint ? "过渡点" : "平差点", false);
            SetCell(row.GetCell(6), item.Comment ?? string.Empty, false);
        }
    }

    private static void AddAdjustedDifferenceTable(
        XWPFDocument document,
        IReadOnlyList<AdjustedDifference> items)
    {
        XWPFTable table = document.CreateTable(items.Count + 1, 7);
        SetHeaderRow(table.GetRow(0), ["从点", "到点", "平差高差(m)", "距离(m)", "中误差(m)", "残差(m)", "测站数"]);

        for (int i = 0; i < items.Count; i++)
        {
            AdjustedDifference item = items[i];
            XWPFTableRow row = table.GetRow(i + 1);
            SetCell(row.GetCell(0), item.FromPoint, false);
            SetCell(row.GetCell(1), item.ToPoint, false);
            SetCell(row.GetCell(2), item.AdjustedDifferenceValue.ToString("F5"), false);
            SetCell(row.GetCell(3), item.DistanceMeters.ToString("F4"), false);
            SetCell(row.GetCell(4), item.StandardError.ToString("F5"), false);
            SetCell(row.GetCell(5), item.Residual.ToString("F5"), false);
            SetCell(row.GetCell(6), item.StationCount.ToString(), false);
        }
    }

    private static void AddAdjustedHeightTable(
        XWPFDocument document,
        IReadOnlyList<AdjustedHeight> items)
    {
        XWPFTable table = document.CreateTable(items.Count + 1, 4);
        SetHeaderRow(table.GetRow(0), ["点名", "高程(m)", "中误差(m)", "点类型"]);

        for (int i = 0; i < items.Count; i++)
        {
            AdjustedHeight item = items[i];
            XWPFTableRow row = table.GetRow(i + 1);
            SetCell(row.GetCell(0), item.PointName, false);
            SetCell(row.GetCell(1), item.Height.ToString("F5"), false);
            SetCell(row.GetCell(2), item.StandardError.ToString("F5"), false);
            SetCell(row.GetCell(3), item.IsReferencePoint ? "已知/基准点" : "平差点", false);
        }
    }

    private static void SetHeaderRow(XWPFTableRow row, IReadOnlyList<string> headers)
    {
        for (int i = 0; i < headers.Count; i++)
        {
            SetCell(row.GetCell(i), headers[i], true);
        }
    }

    private static void SetCell(XWPFTableCell cell, string text, bool bold)
    {
        XWPFParagraph paragraph = cell.Paragraphs.Count > 0
            ? cell.Paragraphs[0]
            : cell.AddParagraph();

        XWPFRun run = paragraph.CreateRun();
        run.SetText(text ?? string.Empty);
        run.IsBold = bold;
        run.FontSize = 10;
        run.FontFamily = "Microsoft YaHei";
    }

    private static ICellStyle CreateHeaderStyle(IWorkbook workbook)
    {
        IFont font = workbook.CreateFont();
        font.IsBold = true;

        ICellStyle style = workbook.CreateCellStyle();
        style.SetFont(font);
        style.Alignment = SpreadsheetHorizontalAlignment.Center;
        style.VerticalAlignment = SpreadsheetVerticalAlignment.Center;
        return style;
    }

    private static string BuildNumberFormat(int decimals)
    {
        int normalized = Math.Clamp(decimals, 0, 10);
        return normalized == 0
            ? "0"
            : "0." + new string('0', normalized);
    }

    private static ICellStyle CreateNumberStyle(IWorkbook workbook, string format)
    {
        ICellStyle style = workbook.CreateCellStyle();
        style.DataFormat = workbook.CreateDataFormat().GetFormat(format);
        return style;
    }

    private static void WriteProjectInfoSheet(
        IWorkbook workbook,
        ProjectWorkspace workspace,
        CalculationBundle? calculation,
        ICellStyle headerStyle)
    {
        ISheet sheet = workbook.CreateSheet("工程信息");
        WriteKeyValue(sheet, 0, "项目名称", workspace.Metadata.ProjectName, headerStyle);
        WriteKeyValue(sheet, 1, "项目编号", workspace.Metadata.ProjectNumber, headerStyle);
        WriteKeyValue(sheet, 2, "工程单位", workspace.Metadata.UnitName, headerStyle);
        WriteKeyValue(sheet, 3, "项目负责人", workspace.Metadata.ProjectLeader, headerStyle);
        WriteKeyValue(sheet, 4, "审核人", workspace.Metadata.Reviewer, headerStyle);
        WriteKeyValue(
            sheet,
            5,
            "平差方式",
            workspace.Settings.AdjustmentMethod == AdjustmentMethod.Classical ? "经典平差" : "拟稳平差",
            headerStyle);
        WriteKeyValue(
            sheet,
            6,
            "结果状态",
            calculation is null
                ? "尚未计算"
                : calculation.Revision.ResultsAreStale
                    ? "基于旧数据"
                    : "与当前输入一致",
            headerStyle);

        string[] sectionNames =
        [
            "任务概述",
            "测区自然地理情况",
            "已有资料情况",
            "引用文件",
            "主要技术指标",
            "外业完成的工作量",
            "结论和建议"
        ];
        string[] sectionValues =
        [
            workspace.ReportText.TaskOverview,
            workspace.ReportText.NaturalGeography,
            workspace.ReportText.ExistingData,
            workspace.ReportText.ReferencedStandards,
            workspace.ReportText.TechnicalIndicators,
            workspace.ReportText.FieldWorkSummary,
            workspace.ReportText.ConclusionAndRecommendations
        ];

        for (int i = 0; i < sectionNames.Length; i++)
        {
            WriteKeyValue(sheet, 8 + i, sectionNames[i], sectionValues[i], headerStyle);
        }

        sheet.SetColumnWidth(0, 22 * 256);
        sheet.SetColumnWidth(1, 72 * 256);
    }

    private static void WriteClosureSheet(
        IWorkbook workbook,
        ProjectWorkspace workspace,
        CalculationBundle? calculation,
        ICellStyle headerStyle,
        ICellStyle distanceStyle)
    {
        ISheet sheet = workbook.CreateSheet("闭合路线");
        string[] headers = ["序号", "类型", "点数", "长度(m)", "测站数", "闭合差(mm)", "当前限差(mm)", "超限", "路线"];
        WriteHeader(sheet, headers, headerStyle);

        IReadOnlyList<NetworkRoute> routes = calculation?.Routes ?? [];
        for (int i = 0; i < routes.Count; i++)
        {
            NetworkRoute route = routes[i];
            double tolerance = workspace.Settings.ToleranceMode == ClosureToleranceMode.Distance
                ? route.LengthToleranceMeters
                : route.StationToleranceMeters;

            IRow row = sheet.CreateRow(i + 1);
            row.CreateCell(0).SetCellValue(route.Index);
            row.CreateCell(1).SetCellValue(route.RouteType == RouteType.ClosedLoop ? "闭合" : "附合");
            row.CreateCell(2).SetCellValue(route.Points.Count);
            SetNumeric(row, 3, route.LengthMeters, distanceStyle);
            row.CreateCell(4).SetCellValue(route.StationCount);
            row.CreateCell(5).SetCellValue(route.ClosureMeters * 1000.0);
            row.CreateCell(6).SetCellValue(tolerance * 1000.0);
            row.CreateCell(7).SetCellValue(Math.Abs(route.ClosureMeters) > tolerance ? "是" : "否");
            row.CreateCell(8).SetCellValue(string.Join(" → ", route.Points));
        }

        AutoSize(sheet, headers.Length);
    }

    private static void WriteKnownHeightSheet(
        IWorkbook workbook,
        IReadOnlyList<KnownHeight> items,
        ICellStyle headerStyle,
        ICellStyle numberStyle)
    {
        ISheet sheet = workbook.CreateSheet("已知点");
        string[] headers = ["点名", "高程(m)", "备注"];
        WriteHeader(sheet, headers, headerStyle);

        for (int i = 0; i < items.Count; i++)
        {
            IRow row = sheet.CreateRow(i + 1);
            row.CreateCell(0).SetCellValue(items[i].PointName);
            SpreadsheetCell height = row.CreateCell(1);
            height.SetCellValue(items[i].Height);
            height.CellStyle = numberStyle;
            row.CreateCell(2).SetCellValue(items[i].Comment ?? string.Empty);
        }

        AutoSize(sheet, headers.Length);
    }

    private static void WriteObservedDifferenceSheet(
        IWorkbook workbook,
        IReadOnlyList<LevelDifference> items,
        ICellStyle headerStyle,
        ICellStyle numberStyle,
        ICellStyle distanceStyle)
    {
        ISheet sheet = workbook.CreateSheet("高差观测");
        string[] headers = ["从点", "到点", "高差(m)", "距离(m)", "测站数", "点类型", "备注"];
        WriteHeader(sheet, headers, headerStyle);

        for (int i = 0; i < items.Count; i++)
        {
            LevelDifference item = items[i];
            IRow row = sheet.CreateRow(i + 1);
            row.CreateCell(0).SetCellValue(item.FromPoint);
            row.CreateCell(1).SetCellValue(item.ToPoint);
            SpreadsheetCell diff = row.CreateCell(2);
            diff.SetCellValue(item.HeightDifference);
            diff.CellStyle = numberStyle;
            SpreadsheetCell distance = row.CreateCell(3);
            distance.SetCellValue(item.DistanceMeters);
            distance.CellStyle = distanceStyle;
            row.CreateCell(4).SetCellValue(item.StationCount);
            row.CreateCell(5).SetCellValue(item.ToPointRole == PointRole.TransitionPoint ? "过渡点" : "平差点");
            row.CreateCell(6).SetCellValue(item.Comment ?? string.Empty);
        }

        AutoSize(sheet, headers.Length);
    }

    private static void WriteAdjustedDifferenceSheet(
        IWorkbook workbook,
        IReadOnlyList<AdjustedDifference> items,
        ICellStyle headerStyle,
        ICellStyle numberStyle,
        ICellStyle distanceStyle)
    {
        ISheet sheet = workbook.CreateSheet("平差高差");
        string[] headers = ["从点", "到点", "观测高差(m)", "平差高差(m)", "残差(m)", "距离(m)", "测站数", "中误差(m)"];
        WriteHeader(sheet, headers, headerStyle);

        for (int i = 0; i < items.Count; i++)
        {
            AdjustedDifference item = items[i];
            IRow row = sheet.CreateRow(i + 1);
            row.CreateCell(0).SetCellValue(item.FromPoint);
            row.CreateCell(1).SetCellValue(item.ToPoint);
            SetNumeric(row, 2, item.ObservedDifference, numberStyle);
            SetNumeric(row, 3, item.AdjustedDifferenceValue, numberStyle);
            SetNumeric(row, 4, item.Residual, numberStyle);
            SetNumeric(row, 5, item.DistanceMeters, distanceStyle);
            row.CreateCell(6).SetCellValue(item.StationCount);
            SetNumeric(row, 7, item.StandardError, numberStyle);
        }

        AutoSize(sheet, headers.Length);
    }

    private static void WriteAdjustedHeightSheet(
        IWorkbook workbook,
        IReadOnlyList<AdjustedHeight> items,
        ICellStyle headerStyle,
        ICellStyle numberStyle)
    {
        ISheet sheet = workbook.CreateSheet("高程成果");
        string[] headers = ["点名", "高程(m)", "中误差(m)", "点类型"];
        WriteHeader(sheet, headers, headerStyle);

        for (int i = 0; i < items.Count; i++)
        {
            AdjustedHeight item = items[i];
            IRow row = sheet.CreateRow(i + 1);
            row.CreateCell(0).SetCellValue(item.PointName);
            SetNumeric(row, 1, item.Height, numberStyle);
            SetNumeric(row, 2, item.StandardError, numberStyle);
            row.CreateCell(3).SetCellValue(item.IsReferencePoint ? "已知/基准点" : "平差点");
        }

        AutoSize(sheet, headers.Length);
    }

    private static void WriteKeyValue(
        ISheet sheet,
        int rowIndex,
        string key,
        string? value,
        ICellStyle headerStyle)
    {
        IRow row = sheet.CreateRow(rowIndex);
        SpreadsheetCell keyCell = row.CreateCell(0);
        keyCell.SetCellValue(key);
        keyCell.CellStyle = headerStyle;
        row.CreateCell(1).SetCellValue(value ?? string.Empty);
    }

    private static void WriteHeader(ISheet sheet, IReadOnlyList<string> headers, ICellStyle headerStyle)
    {
        IRow row = sheet.CreateRow(0);
        for (int i = 0; i < headers.Count; i++)
        {
            SpreadsheetCell cell = row.CreateCell(i);
            cell.SetCellValue(headers[i]);
            cell.CellStyle = headerStyle;
        }
    }

    private static void SetNumeric(IRow row, int index, double value, ICellStyle style)
    {
        SpreadsheetCell cell = row.CreateCell(index);
        cell.SetCellValue(value);
        cell.CellStyle = style;
    }

    private static void AutoSize(ISheet sheet, int columnCount)
    {
        for (int i = 0; i < columnCount; i++)
        {
            sheet.AutoSizeColumn(i);
            double current = sheet.GetColumnWidth(i);
            int targetWidth = (int)Math.Min(current + 512.0, 80 * 256.0);
            sheet.SetColumnWidth(i, targetWidth);
        }
    }
}
