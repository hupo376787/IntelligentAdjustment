using System.Text;
using IntelligentAdjustment.Application.Models;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.Application.Reporting;

public sealed class ResultTextExportService
{
    public void Export(
        ProjectWorkspace workspace,
        CalculationBundle? calculation,
        string filePath,
        string delimiter = "\t")
    {
        ArgumentNullException.ThrowIfNull(workspace);
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        using var writer = new StreamWriter(
            filePath,
            append: false,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        writer.WriteLine("Intelligent Adjustment 高程控制网成果");
        writer.WriteLine($"项目名称{delimiter}{workspace.Metadata.ProjectName}");
        writer.WriteLine($"项目编号{delimiter}{workspace.Metadata.ProjectNumber}");
        writer.WriteLine($"工程单位{delimiter}{workspace.Metadata.UnitName}");
        writer.WriteLine($"平差方式{delimiter}{(workspace.Settings.AdjustmentMethod == AdjustmentMethod.Classical ? "经典平差" : "拟稳平差")}");
        writer.WriteLine($"结果状态{delimiter}{GetResultState(calculation)}");
        writer.WriteLine();

        WriteSection(
            writer,
            "已知点成果",
            ["点名", "高程(m)", "备注"],
            workspace.KnownHeights.Select(x => new[]
            {
                x.PointName,
                x.Height.ToString($"F{workspace.Settings.HeightDecimals}"),
                x.Comment ?? string.Empty
            }),
            delimiter);

        WriteSection(
            writer,
            "高差观测值成果",
            ["从点", "到点", "高差(m)", "距离(m)", "测站数", "点类型", "备注"],
            workspace.LevelDifferences.Select(x => new[]
            {
                x.FromPoint,
                x.ToPoint,
                x.HeightDifference.ToString($"F{workspace.Settings.HeightDecimals}"),
                x.DistanceMeters.ToString($"F{workspace.Settings.DistanceDecimals}"),
                x.StationCount.ToString(),
                x.ToPointRole == PointRole.TransitionPoint ? "过渡点" : "平差点",
                x.Comment ?? string.Empty
            }),
            delimiter);

        if (calculation is not null)
        {
            WriteSection(
                writer,
                "闭合/附合路线",
                ["序号", "类型", "路线", "长度(m)", "测站数", "闭合差(mm)", "限差(mm)", "超限"],
                calculation.Routes.Select(route =>
                {
                    double tolerance = workspace.Settings.ToleranceMode == ClosureToleranceMode.Distance
                        ? route.LengthToleranceMeters
                        : route.StationToleranceMeters;

                    return new[]
                    {
                        route.Index.ToString(),
                        route.RouteType == RouteType.ClosedLoop ? "闭合" : "附合",
                        string.Join(" -> ", route.Points),
                        route.LengthMeters.ToString($"F{workspace.Settings.DistanceDecimals}"),
                        route.StationCount.ToString(),
                        (route.ClosureMeters * 1000).ToString("F3"),
                        (tolerance * 1000).ToString("F3"),
                        Math.Abs(route.ClosureMeters) > tolerance ? "是" : "否"
                    };
                }),
                delimiter);

            WriteSection(
                writer,
                "平差高差及残差",
                ["从点", "到点", "观测高差(m)", "平差高差(m)", "残差(m)", "中误差(m)", "距离(m)", "测站数"],
                calculation.AdjustmentResult.Differences.Select(x => new[]
                {
                    x.FromPoint,
                    x.ToPoint,
                    x.ObservedDifference.ToString($"F{workspace.Settings.HeightDecimals}"),
                    x.AdjustedDifferenceValue.ToString($"F{workspace.Settings.HeightDecimals}"),
                    x.Residual.ToString($"F{workspace.Settings.HeightDecimals}"),
                    x.StandardError.ToString($"F{workspace.Settings.HeightDecimals}"),
                    x.DistanceMeters.ToString($"F{workspace.Settings.DistanceDecimals}"),
                    x.StationCount.ToString()
                }),
                delimiter);

            WriteSection(
                writer,
                "高程成果",
                ["点名", "高程(m)", "中误差(m)", "点类型"],
                calculation.AdjustmentResult.Heights.Select(x => new[]
                {
                    x.PointName,
                    x.Height.ToString($"F{workspace.Settings.HeightDecimals}"),
                    x.StandardError.ToString($"F{workspace.Settings.HeightDecimals}"),
                    x.IsReferencePoint ? "已知/基准点" : "平差点"
                }),
                delimiter);
        }
    }

    private static string GetResultState(CalculationBundle? calculation) =>
        calculation is null
            ? "尚未执行高程平差"
            : calculation.Revision.ResultsAreStale
                ? "当前结果基于旧数据"
                : "结果与当前输入一致";

    private static void WriteSection(
        TextWriter writer,
        string title,
        IReadOnlyList<string> headers,
        IEnumerable<string[]> rows,
        string delimiter)
    {
        writer.WriteLine($"[{title}]");
        writer.WriteLine(string.Join(delimiter, headers.Select(Escape)));

        foreach (string[] row in rows)
        {
            writer.WriteLine(string.Join(delimiter, row.Select(Escape)));
        }

        writer.WriteLine();
    }

    private static string Escape(string value) =>
        value.Replace("\r", " ", StringComparison.Ordinal)
             .Replace("\n", " ", StringComparison.Ordinal)
             .Replace("\t", " ", StringComparison.Ordinal);
}
