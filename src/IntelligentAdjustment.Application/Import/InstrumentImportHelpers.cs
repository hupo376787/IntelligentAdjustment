using System.Globalization;
using IntelligentAdjustment.Application.Observations;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.Application.Import;

internal static class InstrumentImportHelpers
{
    public static string NormalizePointName(string raw)
    {
        string value = raw.Replace("#", string.Empty, StringComparison.Ordinal).Trim();
        if (value.Length == 0)
        {
            return value;
        }

        string withoutLeadingZero = value.TrimStart('0');
        return withoutLeadingZero.Length == 0 ? "0" : withoutLeadingZero;
    }

    public static double ParseDouble(string value) =>
        double.Parse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture);

    public static bool TryParseDouble(string value, out double result) =>
        double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out result);

    public static LevelDifference BuildDifference(RawObservation raw, long id)
    {
        if (!RawObservationDifferenceBuilder.TryBuild(raw, id, out LevelDifference? difference, out string? error) ||
            difference is null)
        {
            throw new FormatException(
                $"无法从原始观测 {raw.FromPoint} → {raw.ToPoint} 生成高差：{error}");
        }

        return difference;
    }

    public static ObservationOrder GetOrder(IReadOnlyList<string> labels)
    {
        string sequence = string.Concat(labels.Select(x => x.ToUpperInvariant()));
        return sequence switch
        {
            "B1F1F2B2" => ObservationOrder.BFFB,
            "F1B1B2F2" => ObservationOrder.FBBF,
            "B1B2F1F2" => ObservationOrder.BBFF,
            "F1F2B1B2" => ObservationOrder.FFBB,
            "B1F1B2F2" => ObservationOrder.BFBF,
            "F1B1F2B2" => ObservationOrder.FBFB,
            _ => ObservationOrder.Unknown
        };
    }

    public static MeasurementMode ParseMeasurementMode(string value)
    {
        string normalized = value.Trim().ToUpperInvariant();
        return normalized switch
        {
            "BF" => MeasurementMode.BF,
            "BFFB" => MeasurementMode.BFFB,
            "ABF" => MeasurementMode.AlternatingBF,
            "ABFFB" => MeasurementMode.AlternatingBFFB,
            "BBFF" => MeasurementMode.BBFF,
            _ => MeasurementMode.Unknown
        };
    }

    public static RawObservation CreateDoubleReadingObservation(
        long id,
        int sequence,
        string fromPoint,
        string toPoint,
        MeasurementMode measurementMode,
        ObservationOrder order,
        double b1,
        double b2,
        double f1,
        double f2,
        double db1,
        double db2,
        double df1,
        double df2,
        string sourceFileName,
        DateTimeOffset? measuredAt = null,
        string? comment = null) =>
        new()
        {
            Id = id,
            LineId = 0,
            Sequence = sequence,
            FromPoint = fromPoint,
            ToPoint = toPoint,
            B1 = b1,
            B2 = b2,
            F1 = f1,
            F2 = f2,
            DistanceB1 = db1,
            DistanceB2 = db2,
            DistanceF1 = df1,
            DistanceF2 = df2,
            MeasurementMode = measurementMode,
            ObservationOrder = order,
            IsValid = true,
            SourceFileName = sourceFileName,
            MeasuredAt = measuredAt,
            Comment = comment
        };
}
