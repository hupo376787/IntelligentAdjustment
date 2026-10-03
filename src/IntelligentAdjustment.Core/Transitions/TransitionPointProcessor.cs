using System.Globalization;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.Core.Transitions;

public sealed class TransitionPointProcessor
{
    public IReadOnlyList<LevelDifference> ToggleSelectedRoles(
        IReadOnlyList<LevelDifference> source,
        IReadOnlySet<long> selectedIds)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(selectedIds);

        return source
            .Select(x => selectedIds.Contains(x.Id)
                ? x with
                {
                    ToPointRole = x.ToPointRole == PointRole.AdjustmentPoint
                        ? PointRole.TransitionPoint
                        : PointRole.AdjustmentPoint,
                    IsRoleManuallySpecified = true
                }
                : x)
            .ToArray();
    }

    public IReadOnlyList<LevelDifference> AutoDetectTransitionPoints(
        IReadOnlyList<LevelDifference> source,
        IReadOnlySet<string> knownPointNames)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(knownPointNames);

        var incidentCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var lineUsage = new Dictionary<string, HashSet<long>>(StringComparer.Ordinal);
        foreach (var edge in source)
        {
            Increment(incidentCounts, edge.FromPoint);
            Increment(incidentCounts, edge.ToPoint);
            AddLine(lineUsage, edge.FromPoint, edge.LineId);
            AddLine(lineUsage, edge.ToPoint, edge.LineId);
        }

        return source.Select(edge =>
        {
            if (edge.IsRoleManuallySpecified)
            {
                return edge;
            }

            string candidate = edge.ToPoint;
            bool numericName = double.TryParse(candidate, NumberStyles.Integer, CultureInfo.InvariantCulture, out _);
            bool protectedKnown = knownPointNames.Contains(candidate);
            bool degreeTwo = incidentCounts.TryGetValue(candidate, out int degree) && degree == 2;
            bool singleLine = lineUsage.TryGetValue(candidate, out var lines) && lines.Count == 1;

            PointRole role = numericName && !protectedKnown && degreeTwo && singleLine
                ? PointRole.TransitionPoint
                : PointRole.AdjustmentPoint;

            return edge with { ToPointRole = role };
        }).ToArray();
    }

    public IReadOnlyList<LevelDifference> MergeSelectedAndDelete(
        IReadOnlyList<LevelDifference> source,
        IReadOnlySet<long> selectedIds)
    {
        var toggled = ToggleSelectedRoles(source, selectedIds);
        return DeleteTransitionPoints(toggled);
    }

    public IReadOnlyList<LevelDifference> DeleteTransitionPoints(IReadOnlyList<LevelDifference> source)
    {
        ArgumentNullException.ThrowIfNull(source);

        var result = new List<LevelDifference>();
        foreach (var lineGroup in source.GroupBy(x => x.LineId).OrderBy(x => x.Key))
        {
            var rows = lineGroup.OrderBy(x => x.Sequence).ToArray();
            if (rows.Length == 0)
            {
                continue;
            }

            int outputSequence = 0;
            int index = 0;
            while (index < rows.Length)
            {
                var first = rows[index];
                string from = first.FromPoint;
                string to = first.ToPoint;
                double heightDifference = first.HeightDifference;
                double distance = first.DistanceMeters;
                int stations = first.StationCount;
                long retainedId = first.Id;
                string? comment = first.Comment;

                while (rows[index].ToPointRole == PointRole.TransitionPoint && index + 1 < rows.Length)
                {
                    var next = rows[index + 1];
                    if (!string.Equals(to, next.FromPoint, StringComparison.Ordinal))
                    {
                        break;
                    }

                    index++;
                    to = next.ToPoint;
                    heightDifference += next.HeightDifference;
                    distance += next.DistanceMeters;
                    stations += next.StationCount;
                    retainedId = next.Id;
                    comment = CombineComments(comment, next.Comment);
                }

                result.Add(new LevelDifference(
                    retainedId,
                    first.LineId,
                    outputSequence++,
                    from,
                    to,
                    heightDifference,
                    distance,
                    stations,
                    PointRole.AdjustmentPoint,
                    IsRoleManuallySpecified: false,
                    comment));

                index++;
            }
        }

        return result;
    }

    private static string? CombineComments(string? first, string? second)
    {
        if (string.IsNullOrWhiteSpace(first))
        {
            return second;
        }

        if (string.IsNullOrWhiteSpace(second))
        {
            return first;
        }

        return $"{first}; {second}";
    }

    private static void Increment(Dictionary<string, int> counts, string key)
    {
        counts[key] = counts.TryGetValue(key, out int value) ? value + 1 : 1;
    }

    private static void AddLine(Dictionary<string, HashSet<long>> usage, string point, long lineId)
    {
        if (!usage.TryGetValue(point, out var lines))
        {
            lines = new HashSet<long>();
            usage[point] = lines;
        }

        lines.Add(lineId);
    }
}
