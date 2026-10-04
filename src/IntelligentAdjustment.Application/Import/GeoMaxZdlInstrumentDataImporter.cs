using System.Globalization;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.Application.Import;

public sealed class GeoMaxZdlInstrumentDataImporter : IInstrumentDataImporter
{
    private sealed record Reading(
        string PointName,
        string Kind,
        double StaffReading,
        double Distance,
        double ReducedLevel);

    public InstrumentVendor Vendor => InstrumentVendor.GeoMaxZdl;
    public string DisplayName => "GeoMax ZDL";
    public IReadOnlyList<string> SupportedExtensions { get; } = [".mdt"];

    public async Task<InstrumentImportResult> ParseAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        string[] lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
        var readings = new List<Reading>();

        foreach (string sourceLine in lines.Skip(1))
        {
            cancellationToken.ThrowIfCancellationRequested();

            string line = sourceLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            string[] fields = line.Split(
                (char[]?)null,
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            if (fields.Length < 6 || fields[0].StartsWith('@'))
            {
                continue;
            }

            string kind = fields[1].ToUpperInvariant();
            if (kind is not ("B" or "F"))
            {
                continue;
            }

            readings.Add(new Reading(
                InstrumentImportHelpers.NormalizePointName(fields[0]),
                kind,
                double.Parse(fields[2], CultureInfo.InvariantCulture),
                double.Parse(fields[3], CultureInfo.InvariantCulture),
                double.Parse(fields[5], CultureInfo.InvariantCulture)));
        }

        if (readings.Count == 0 || readings.Count % 4 != 0)
        {
            throw new FormatException("GeoMax ZDL MDT 文件未形成完整的 4 读数测站。");
        }

        var raw = new List<RawObservation>();
        var differences = new List<LevelDifference>();
        var known = new List<KnownHeight>();
        string sourceFileName = Path.GetFileName(filePath);

        for (int i = 0; i < readings.Count; i += 4)
        {
            Reading[] station = readings.Skip(i).Take(4).ToArray();
            Reading[] backs = station.Where(x => x.Kind == "B").ToArray();
            Reading[] fores = station.Where(x => x.Kind == "F").ToArray();

            if (backs.Length != 2 || fores.Length != 2)
            {
                throw new FormatException($"GeoMax ZDL 第 {i / 4 + 1} 站不是 2 后视 + 2 前视。");
            }

            if (i == 0)
            {
                known.Add(new KnownHeight(backs[0].PointName, backs[0].ReducedLevel));
            }

            string[] labels = BuildLabels(station);
            int sequence = raw.Count;

            RawObservation observation = InstrumentImportHelpers.CreateDoubleReadingObservation(
                -(sequence + 1),
                sequence,
                backs[0].PointName,
                fores[0].PointName,
                MeasurementMode.AlternatingBFFB,
                InstrumentImportHelpers.GetOrder(labels),
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

    private static string[] BuildLabels(IReadOnlyList<Reading> station)
    {
        int b = 0;
        int f = 0;
        return station.Select(x =>
        {
            if (x.Kind == "B")
            {
                return $"B{++b}";
            }

            return $"F{++f}";
        }).ToArray();
    }
}
