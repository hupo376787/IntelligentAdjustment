using System.Globalization;
using System.Text.RegularExpressions;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.Application.Import;

public sealed partial class TrimbleDiNiInstrumentDataImporter : IInstrumentDataImporter
{
    private sealed record Reading(
        string PointName,
        bool IsBack,
        double StaffReading,
        double Distance);

    public InstrumentVendor Vendor => InstrumentVendor.TrimbleDiNi;
    public string DisplayName => "Trimble DiNi";
    public IReadOnlyList<string> SupportedExtensions { get; } = [".dat"];

    public async Task<InstrumentImportResult> ParseAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        string[] lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
        var known = new List<KnownHeight>();
        var completed = new List<List<Reading>>();
        var current = new List<Reading>();
        bool started = false;

        foreach (string sourceLine in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string line = sourceLine.TrimEnd();

            if (line.Contains("Start-Line", StringComparison.OrdinalIgnoreCase))
            {
                started = true;
                continue;
            }

            if (!started)
            {
                continue;
            }

            if (line.Contains("Measurement repeated", StringComparison.OrdinalIgnoreCase))
            {
                if (current.Count > 0)
                {
                    current.RemoveAt(current.Count - 1);
                }

                continue;
            }

            if (line.Contains("Station repeated", StringComparison.OrdinalIgnoreCase))
            {
                if (current.Count > 0)
                {
                    current.Clear();
                }
                else if (completed.Count > 0)
                {
                    completed.RemoveAt(completed.Count - 1);
                }

                continue;
            }

            Match readingMatch = ReadingRegex().Match(line);
            if (readingMatch.Success)
            {
                string point = NormalizeTrimblePoint(readingMatch.Groups["point"].Value);
                string kind = readingMatch.Groups["kind"].Value;

                current.Add(new Reading(
                    point,
                    kind.Equals("Rb", StringComparison.OrdinalIgnoreCase),
                    double.Parse(readingMatch.Groups["reading"].Value, CultureInfo.InvariantCulture),
                    double.Parse(readingMatch.Groups["distance"].Value, CultureInfo.InvariantCulture)));

                if (current.Count == 4)
                {
                    ValidateStation(current, completed.Count + 1);
                    completed.Add(new List<Reading>(current));
                    current.Clear();
                }

                continue;
            }

            if (known.Count == 0 && completed.Count == 0 && current.Count == 0)
            {
                Match knownMatch = KnownHeightRegex().Match(line);
                if (knownMatch.Success &&
                    !knownMatch.Groups["prefix"].Value.Contains(':'))
                {
                    string point = NormalizeTrimblePoint(knownMatch.Groups["point"].Value);
                    double height = double.Parse(
                        knownMatch.Groups["height"].Value,
                        CultureInfo.InvariantCulture);
                    known.Add(new KnownHeight(point, height));
                }
            }
        }

        if (current.Count != 0)
        {
            throw new FormatException("Trimble DiNi 文件末尾存在不完整测站。");
        }

        if (completed.Count == 0)
        {
            throw new FormatException("Trimble DiNi DAT 文件中未找到可识别的 BFFB 测站。");
        }

        var raw = new List<RawObservation>();
        var differences = new List<LevelDifference>();
        string sourceFileName = Path.GetFileName(filePath);

        foreach (List<Reading> station in completed)
        {
            Reading[] backs = station.Where(x => x.IsBack).ToArray();
            Reading[] fores = station.Where(x => !x.IsBack).ToArray();
            int sequence = raw.Count;

            RawObservation observation = InstrumentImportHelpers.CreateDoubleReadingObservation(
                -(sequence + 1),
                sequence,
                backs[0].PointName,
                fores[0].PointName,
                MeasurementMode.BFFB,
                ObservationOrder.BFFB,
                backs[0].StaffReading,
                backs[1].StaffReading,
                fores[0].StaffReading,
                fores[1].StaffReading,
                backs[0].Distance,
                backs[1].Distance,
                fores[0].Distance,
                fores[1].Distance,
                sourceFileName);

            raw.Add(observation);
            differences.Add(InstrumentImportHelpers.BuildDifference(observation, -(sequence + 1)));
        }

        return new InstrumentImportResult(raw, differences, known);
    }

    private static string NormalizeTrimblePoint(string value)
    {
        string normalized = value.Replace("#", string.Empty, StringComparison.Ordinal).Trim();
        return InstrumentImportHelpers.NormalizePointName(normalized);
    }

    private static void ValidateStation(
        IReadOnlyList<Reading> station,
        int stationNumber)
    {
        if (station.Count != 4 ||
            !station[0].IsBack ||
            station[1].IsBack ||
            station[2].IsBack ||
            !station[3].IsBack)
        {
            throw new FormatException(
                $"Trimble DiNi 第 {stationNumber} 站不符合 Rb-Rf-Rf-Rb 结构。");
        }

        if (!string.Equals(station[0].PointName, station[3].PointName, StringComparison.Ordinal) ||
            !string.Equals(station[1].PointName, station[2].PointName, StringComparison.Ordinal))
        {
            throw new FormatException(
                $"Trimble DiNi 第 {stationNumber} 站同类读数点名不一致。");
        }
    }

    [GeneratedRegex(
        @"KD1(?<point>.*?)\s+\d{2}:\d{2}:\d{2}3?DC11\|(?<kind>Rb|Rf)\s+(?<reading>[+-]?\d+(?:\.\d+)?)\s+m\s+\|HD\s+(?<distance>[+-]?\d+(?:\.\d+)?)\s+m",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex ReadingRegex();

    [GeneratedRegex(
        @"KD1(?<point>.*?)(?<prefix>\s*)DC11\|.*?\|Z\s+(?<height>[+-]?\d+(?:\.\d+)?)\s+m",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KnownHeightRegex();
}
