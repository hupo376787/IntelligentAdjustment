using System.Globalization;
using System.Text.RegularExpressions;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.Application.Import;

public sealed partial class TopconDlInstrumentDataImporter : IInstrumentDataImporter
{
    private sealed record Reading(
        string PointName,
        double Distance,
        double StaffReading,
        string? TimeText);

    public InstrumentVendor Vendor => InstrumentVendor.TopconDl;
    public string DisplayName => "Topcon DL";
    public IReadOnlyList<string> SupportedExtensions { get; } = [".dat"];

    public async Task<InstrumentImportResult> ParseAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        string[] lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
        string? startPoint = null;
        double? knownHeight = null;
        var readings = new List<Reading>();

        foreach (string sourceLine in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string line = sourceLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            Match startMatch = StartPointRegex().Match(line);
            if (startMatch.Success)
            {
                startPoint = InstrumentImportHelpers.NormalizePointName(startMatch.Groups["point"].Value);
                continue;
            }

            Match ghMatch = KnownHeightRegex().Match(line);
            if (ghMatch.Success && knownHeight is null)
            {
                knownHeight = double.Parse(
                    ghMatch.Groups["height"].Value,
                    CultureInfo.InvariantCulture);
                continue;
            }

            string[] fields = line.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (fields.Length < 3 ||
                !double.TryParse(fields[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double distance) ||
                !double.TryParse(fields[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double staffReading))
            {
                continue;
            }

            string pointName = InstrumentImportHelpers.NormalizePointName(fields[0]);
            string? time = fields.LastOrDefault(x => TimeRegex().IsMatch(x));

            readings.Add(new Reading(pointName, distance, staffReading, time));
        }

        if (readings.Count == 0 || readings.Count % 4 != 0)
        {
            throw new FormatException("Topcon DL DAT 文件未形成完整的 4 读数测站。");
        }

        var known = new List<KnownHeight>();
        if (!string.IsNullOrWhiteSpace(startPoint) && knownHeight is not null)
        {
            known.Add(new KnownHeight(startPoint, knownHeight.Value));
        }

        var raw = new List<RawObservation>();
        var differences = new List<LevelDifference>();
        string sourceFileName = Path.GetFileName(filePath);

        for (int i = 0; i < readings.Count; i += 4)
        {
            Reading[] station = readings.Skip(i).Take(4).ToArray();

            if (!string.Equals(station[0].PointName, station[3].PointName, StringComparison.Ordinal) ||
                !string.Equals(station[1].PointName, station[2].PointName, StringComparison.Ordinal))
            {
                throw new FormatException(
                    $"Topcon DL 第 {i / 4 + 1} 站不符合 B-F-F-B 点名结构。");
            }

            int sequence = raw.Count;
            RawObservation observation = InstrumentImportHelpers.CreateDoubleReadingObservation(
                -(sequence + 1),
                sequence,
                station[0].PointName,
                station[1].PointName,
                MeasurementMode.BFFB,
                ObservationOrder.BFFB,
                station[0].StaffReading,
                station[3].StaffReading,
                station[1].StaffReading,
                station[2].StaffReading,
                station[0].Distance,
                station[3].Distance,
                station[1].Distance,
                station[2].Distance,
                sourceFileName);

            raw.Add(observation);
            differences.Add(InstrumentImportHelpers.BuildDifference(observation, -(sequence + 1)));
        }

        return new InstrumentImportResult(raw, differences, known);
    }

    [GeneratedRegex(@"START\s+BM#:\s*(?<point>\S+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StartPointRegex();

    [GeneratedRegex(@"GH\(m\):\s*(?<height>[+-]?\d+(?:\.\d+)?)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex KnownHeightRegex();

    [GeneratedRegex(@"^\d{1,2}:\d{2}$", RegexOptions.CultureInvariant)]
    private static partial Regex TimeRegex();
}
