using System.Globalization;
using IntelligentAdjustment.Application.Models;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.Application.Import;

public sealed class OutFileImporter
{
    public async Task<OutImportResult> ParseAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(filePath);

        string[] lines = await File.ReadAllLinesAsync(filePath, cancellationToken);
        return Parse(lines);
    }

    public OutImportResult Parse(IEnumerable<string> sourceLines)
    {
        ArgumentNullException.ThrowIfNull(sourceLines);

        var differences = new List<LevelDifference>();
        var knownHeights = new List<KnownHeight>();
        bool inHeightSection = false;
        bool headerSeen = false;
        int sequence = 0;

        foreach (string raw in sourceLines)
        {
            string line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (line.Equals("Height", StringComparison.OrdinalIgnoreCase))
            {
                inHeightSection = true;
                continue;
            }

            string[] parts = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
            if (!headerSeen && !inHeightSection)
            {
                if (parts.Length >= 5 &&
                    parts[0].Equals("From", StringComparison.OrdinalIgnoreCase) &&
                    parts[1].Equals("To", StringComparison.OrdinalIgnoreCase) &&
                    parts[2].Equals("Hei_Diff", StringComparison.OrdinalIgnoreCase))
                {
                    headerSeen = true;
                    continue;
                }

                throw new FormatException("OUT 文件缺少有效的 From/To/Hei_Diff/Distance/Station 表头。");
            }

            if (!inHeightSection)
            {
                if (parts.Length != 5)
                {
                    throw new FormatException($"无效高差记录：{line}");
                }

                differences.Add(new LevelDifference(
                    Id: sequence + 1,
                    LineId: 0,
                    Sequence: sequence,
                    FromPoint: parts[0],
                    ToPoint: parts[1],
                    HeightDifference: ParseDouble(parts[2], "高差"),
                    DistanceMeters: ParseDouble(parts[3], "距离"),
                    StationCount: ParseInt(parts[4], "测站数")));
                sequence++;
            }
            else
            {
                if (parts.Length != 2)
                {
                    throw new FormatException($"无效已知高程记录：{line}");
                }

                knownHeights.Add(new KnownHeight(parts[0], ParseDouble(parts[1], "高程")));
            }
        }

        if (!headerSeen)
        {
            throw new FormatException("OUT 文件中没有高差数据表头。");
        }

        return new OutImportResult(differences, knownHeights);
    }

    private static double ParseDouble(string text, string field)
    {
        if (!double.TryParse(text, NumberStyles.Float | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out double value))
        {
            throw new FormatException($"{field}数值无效：{text}");
        }

        return value;
    }

    private static int ParseInt(string text, string field)
    {
        if (!int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int value))
        {
            throw new FormatException($"{field}数值无效：{text}");
        }

        return value;
    }
}
