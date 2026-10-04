using System.Text.RegularExpressions;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.Application.Import;

public sealed partial class LeicaDnaInstrumentDataImporter : IInstrumentDataImporter
{
    public InstrumentVendor Vendor => InstrumentVendor.LeicaDna;
    public string DisplayName => "Leica DNA";
    public IReadOnlyList<string> SupportedExtensions { get; } = [".gsi", ".mdt"];

    public Task<InstrumentImportResult> ParseAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        string extension = Path.GetExtension(filePath);
        return extension.Equals(".gsi", StringComparison.OrdinalIgnoreCase)
            ? ParseGsiAsync(filePath, cancellationToken)
            : MdtBffbFileParser.ParseAsync(filePath, cancellationToken);
    }

    private static async Task<InstrumentImportResult> ParseGsiAsync(
        string filePath,
        CancellationToken cancellationToken)
    {
        string[] lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
        var knownHeights = new List<KnownHeight>();
        var rawObservations = new List<RawObservation>();
        var differences = new List<LevelDifference>();
        var current = new List<GsiReading>();
        string sourceFileName = Path.GetFileName(filePath);

        foreach (string line in lines)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Match knownMatch = KnownHeightRegex().Match(line);
            if (knownMatch.Success && knownHeights.Count == 0)
            {
                string point = InstrumentImportHelpers.NormalizePointName(knownMatch.Groups["point"].Value);
                double height = ParseScaledGsiValue(knownMatch.Groups["height"].Value);
                knownHeights.Add(new KnownHeight(point, height));
                continue;
            }

            Match readingMatch = ReadingRegex().Match(line);
            if (!readingMatch.Success)
            {
                continue;
            }

            string code = readingMatch.Groups["code"].Value;
            current.Add(new GsiReading(
                code,
                InstrumentImportHelpers.NormalizePointName(readingMatch.Groups["point"].Value),
                ParseScaledGsiValue(readingMatch.Groups["distance"].Value),
                ParseScaledGsiValue(readingMatch.Groups["reading"].Value)));

            if (current.Count == 4)
            {
                AddGsiStation(rawObservations, differences, current, sourceFileName);
                current.Clear();
            }
        }

        if (current.Count != 0)
        {
            throw new FormatException("Leica GSI 文件末尾存在不完整测站。");
        }

        if (rawObservations.Count == 0)
        {
            throw new FormatException("Leica GSI 文件中未找到可识别的 BFFB 测站。");
        }

        return new InstrumentImportResult(rawObservations, differences, knownHeights);
    }

    private static void AddGsiStation(
        List<RawObservation> rawObservations,
        List<LevelDifference> differences,
        IReadOnlyList<GsiReading> readings,
        string sourceFileName)
    {
        int sequence = rawObservations.Count;
        GsiReading? b1 = readings.FirstOrDefault(x => x.Code == "331");
        GsiReading? b2 = readings.FirstOrDefault(x => x.Code == "335");
        GsiReading? f1 = readings.FirstOrDefault(x => x.Code == "332");
        GsiReading? f2 = readings.FirstOrDefault(x => x.Code == "336");

        if (b1 is null || b2 is null || f1 is null || f2 is null)
        {
            throw new FormatException($"Leica GSI 第 {sequence + 1} 站的 331/332/335/336 字段不完整。");
        }

        if (!string.Equals(b1.PointName, b2.PointName, StringComparison.Ordinal) ||
            !string.Equals(f1.PointName, f2.PointName, StringComparison.Ordinal))
        {
            throw new FormatException($"Leica GSI 第 {sequence + 1} 站同类读数点名不一致。");
        }

        string[] labels = readings.Select(x => x.Code switch
        {
            "331" => "B1",
            "335" => "B2",
            "332" => "F1",
            "336" => "F2",
            _ => string.Empty
        }).ToArray();

        RawObservation raw = InstrumentImportHelpers.CreateDoubleReadingObservation(
            -(sequence + 1),
            sequence,
            b1.PointName,
            f1.PointName,
            MeasurementMode.AlternatingBFFB,
            InstrumentImportHelpers.GetOrder(labels),
            b1.Value,
            b2.Value,
            f1.Value,
            f2.Value,
            b1.Distance,
            b2.Distance,
            f1.Distance,
            f2.Distance,
            sourceFileName);

        rawObservations.Add(raw);
        differences.Add(InstrumentImportHelpers.BuildDifference(raw, -(sequence + 1)));
    }

    private static double ParseScaledGsiValue(string value)
    {
        long raw = long.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
        return raw / 10000.0;
    }

    private sealed record GsiReading(
        string Code,
        string PointName,
        double Distance,
        double Value);

    [GeneratedRegex(@"^\d{6}\+(?<point>.{8})\s+32\.\.\.6(?<distance>[+-]\d+)\s+(?<code>331|332|335|336)\.26(?<reading>[+-]\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex ReadingRegex();

    [GeneratedRegex(@"^\d{6}\+(?<point>.{8}).*?\s83\.\.\.6(?<height>[+-]\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex KnownHeightRegex();
}
