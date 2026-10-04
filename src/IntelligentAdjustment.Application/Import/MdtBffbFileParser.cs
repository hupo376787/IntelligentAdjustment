using IntelligentAdjustment.Application.Models;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.Application.Import;

internal static class MdtBffbFileParser
{
    private sealed record Reading(string Label, string PointName, double Distance, double Value);

    public static async Task<InstrumentImportResult> ParseAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        string[] lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
        if (lines.Length < 2)
        {
            throw new FormatException("MDT 文件内容为空或不完整。");
        }

        string[] header = lines[0].Split(',');
        MeasurementMode mode = header.Length > 0
            ? InstrumentImportHelpers.ParseMeasurementMode(header[0])
            : MeasurementMode.Unknown;

        var knownHeights = new List<KnownHeight>();
        var rawObservations = new List<RawObservation>();
        var differences = new List<LevelDifference>();
        var current = new List<Reading>();
        string sourceFileName = Path.GetFileName(filePath);

        foreach (string sourceLine in lines.Skip(1))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string line = sourceLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            string[] fields = line.Split(',').Select(x => x.Trim()).ToArray();
            if (fields.Length == 0)
            {
                continue;
            }

            if (fields[0].Equals("StartPt", StringComparison.OrdinalIgnoreCase))
            {
                if (fields.Length >= 3 &&
                    InstrumentImportHelpers.TryParseDouble(fields[2], out double height))
                {
                    string point = InstrumentImportHelpers.NormalizePointName(fields[1]);
                    knownHeights.Add(new KnownHeight(point, height));
                }

                continue;
            }

            if (fields[0].Equals("Results", StringComparison.OrdinalIgnoreCase))
            {
                if (current.Count > 0)
                {
                    AddStation(
                        rawObservations,
                        differences,
                        current,
                        mode,
                        sourceFileName);
                    current.Clear();
                }

                continue;
            }

            if (fields[0] is "B" or "F" or "B1" or "B2" or "F1" or "F2")
            {
                if (fields.Length < 4)
                {
                    throw new FormatException($"MDT 读数行字段不足：{line}");
                }

                current.Add(new Reading(
                    fields[0].ToUpperInvariant(),
                    InstrumentImportHelpers.NormalizePointName(fields[1]),
                    InstrumentImportHelpers.ParseDouble(fields[2]),
                    InstrumentImportHelpers.ParseDouble(fields[3])));
            }
        }

        if (current.Count > 0)
        {
            AddStation(
                rawObservations,
                differences,
                current,
                mode,
                sourceFileName);
        }

        if (rawObservations.Count == 0)
        {
            throw new FormatException("MDT 文件中未找到可识别的水准测站。");
        }

        return new InstrumentImportResult(rawObservations, differences, knownHeights);
    }

    private static void AddStation(
        List<RawObservation> rawObservations,
        List<LevelDifference> differences,
        IReadOnlyList<Reading> readings,
        MeasurementMode headerMode,
        string sourceFileName)
    {
        int sequence = rawObservations.Count;
        Reading[] backs = readings.Where(x => x.Label.StartsWith('B')).ToArray();
        Reading[] fores = readings.Where(x => x.Label.StartsWith('F')).ToArray();

        if (backs.Length == 0 || fores.Length == 0)
        {
            throw new FormatException($"第 {sequence + 1} 站缺少后视或前视读数。");
        }

        string from = backs[0].PointName;
        string to = fores[0].PointName;
        if (backs.Any(x => !string.Equals(x.PointName, from, StringComparison.Ordinal)) ||
            fores.Any(x => !string.Equals(x.PointName, to, StringComparison.Ordinal)))
        {
            throw new FormatException($"第 {sequence + 1} 站同类读数点名不一致。");
        }

        MeasurementMode mode = headerMode;
        if (mode == MeasurementMode.Unknown)
        {
            mode = backs.Length >= 2 && fores.Length >= 2
                ? MeasurementMode.BFFB
                : MeasurementMode.BF;
        }

        RawObservation raw;
        if (backs.Length >= 2 && fores.Length >= 2)
        {
            ObservationOrder order = InstrumentImportHelpers.GetOrder(
                readings.Select(x => NormalizeReadingLabel(x.Label, backs, fores)).ToArray());

            raw = InstrumentImportHelpers.CreateDoubleReadingObservation(
                -(sequence + 1),
                sequence,
                from,
                to,
                mode,
                order,
                backs[0].Value,
                backs[1].Value,
                fores[0].Value,
                fores[1].Value,
                backs[0].Distance,
                backs[1].Distance,
                fores[0].Distance,
                fores[1].Distance,
                sourceFileName);
        }
        else
        {
            raw = new RawObservation
            {
                Id = -(sequence + 1),
                LineId = 0,
                Sequence = sequence,
                FromPoint = from,
                ToPoint = to,
                B1 = backs[0].Value,
                F1 = fores[0].Value,
                DistanceB1 = backs[0].Distance,
                DistanceF1 = fores[0].Distance,
                MeasurementMode = mode,
                ObservationOrder = readings[0].Label.StartsWith('B')
                    ? ObservationOrder.BF
                    : ObservationOrder.FB,
                IsValid = true,
                SourceFileName = sourceFileName
            };
        }

        rawObservations.Add(raw);
        differences.Add(InstrumentImportHelpers.BuildDifference(raw, -(sequence + 1)));
    }

    private static string NormalizeReadingLabel(
        string label,
        IReadOnlyList<Reading> backs,
        IReadOnlyList<Reading> fores)
    {
        if (label.Length > 1)
        {
            return label;
        }

        if (label == "B")
        {
            int index = 0;
            foreach (Reading reading in backs)
            {
                if (ReferenceEquals(reading, backs[0]))
                {
                    break;
                }

                index++;
            }

            return index == 0 ? "B1" : "B2";
        }

        return "F1";
    }
}
