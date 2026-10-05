using System.Globalization;
using System.Text;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.Application.Export;

public sealed class OutFileExporter
{
    public async Task ExportAsync(
        string filePath,
        IReadOnlyList<LevelDifference> differences,
        IReadOnlyList<KnownHeight> knownHeights,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);
        ArgumentNullException.ThrowIfNull(differences);
        ArgumentNullException.ThrowIfNull(knownHeights);

        var lines = new List<string>(differences.Count + knownHeights.Count + 2)
        {
            "From\tTo\tHei_Diff\tDistance\tStation"
        };

        foreach (LevelDifference difference in differences)
        {
            lines.Add(string.Join(
                '\t',
                difference.FromPoint,
                difference.ToPoint,
                difference.HeightDifference.ToString("0.00000", CultureInfo.InvariantCulture),
                difference.DistanceMeters.ToString("0.0000", CultureInfo.InvariantCulture),
                difference.StationCount.ToString(CultureInfo.InvariantCulture)));
        }

        lines.Add("Height");

        foreach (KnownHeight knownHeight in knownHeights)
        {
            lines.Add(string.Join(
                '\t',
                knownHeight.PointName,
                knownHeight.Height.ToString("0.00000", CultureInfo.InvariantCulture)));
        }

        await File.WriteAllLinesAsync(
            filePath,
            lines,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            cancellationToken);
    }
}
