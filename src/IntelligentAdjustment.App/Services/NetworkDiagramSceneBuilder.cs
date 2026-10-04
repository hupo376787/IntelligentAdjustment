using System.Windows;
using IntelligentAdjustment.App.ViewModels;
using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.App.Services;

public sealed record DiagramSceneNode(
    string PointName,
    Point Position,
    DiagramNodeKind Kind,
    bool HasPersistedCoordinate);

public sealed record DiagramSceneEdge(
    LevelDifferenceRowViewModel Difference,
    string FromPoint,
    string ToPoint,
    bool IsOverLimit);

public sealed record NetworkDiagramScene(
    IReadOnlyList<DiagramSceneNode> Nodes,
    IReadOnlyList<DiagramSceneEdge> Edges,
    int MissingCoordinateCount);

public static class NetworkDiagramSceneBuilder
{
    public static NetworkDiagramScene Build(
        ProjectDocumentViewModel document,
        IReadOnlyList<NetworkRoute> routes,
        ProjectSettings settings)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(routes);
        ArgumentNullException.ThrowIfNull(settings);

        string[] pointNames = document.LevelDifferences
            .SelectMany(x => new[] { x.FromPoint.Trim(), x.ToPoint.Trim() })
            .Where(x => x.Length > 0)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        var positions = document.MapPoints
            .Where(x => pointNames.Contains(x.PointName, StringComparer.Ordinal))
            .ToDictionary(
                x => x.PointName,
                x => new Point(x.X, x.Y),
                StringComparer.Ordinal);

        string[] missing = pointNames
            .Where(x => !positions.ContainsKey(x))
            .ToArray();

        if (missing.Length > 0)
        {
            double centerX = positions.Count == 0 ? 300 : positions.Values.Average(x => x.X);
            double centerY = positions.Count == 0 ? 220 : positions.Values.Average(x => x.Y);
            double radius = Math.Max(140, 30 * Math.Max(6, missing.Length));

            for (int i = 0; i < missing.Length; i++)
            {
                double angle = Math.PI * 2 * i / missing.Length - Math.PI / 2;
                positions[missing[i]] = new Point(
                    centerX + Math.Cos(angle) * radius,
                    centerY + Math.Sin(angle) * radius);
            }
        }

        var known = document.KnownHeights
            .Select(x => x.PointName.Trim())
            .Where(x => x.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        var transition = document.LevelDifferences
            .Where(x => x.ToPointRole == PointRole.TransitionPoint)
            .Select(x => x.ToPoint.Trim())
            .Where(x => x.Length > 0)
            .ToHashSet(StringComparer.Ordinal);

        var nodes = pointNames
            .Select(name => new DiagramSceneNode(
                name,
                positions[name],
                known.Contains(name)
                    ? DiagramNodeKind.Known
                    : transition.Contains(name)
                        ? DiagramNodeKind.Transition
                        : DiagramNodeKind.Normal,
                document.MapPoints.Any(x => string.Equals(x.PointName, name, StringComparison.Ordinal))))
            .ToArray();

        HashSet<string> overLimitPairs = BuildOverLimitPairs(routes, settings);

        var edges = document.LevelDifferences
            .Select(x => new DiagramSceneEdge(
                x,
                x.FromPoint.Trim(),
                x.ToPoint.Trim(),
                overLimitPairs.Contains(CanonicalPair(x.FromPoint, x.ToPoint))))
            .ToArray();

        return new NetworkDiagramScene(nodes, edges, missing.Length);
    }

    private static HashSet<string> BuildOverLimitPairs(
        IReadOnlyList<NetworkRoute> routes,
        ProjectSettings settings)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);

        foreach (NetworkRoute route in routes)
        {
            double tolerance = settings.ToleranceMode == ClosureToleranceMode.Distance
                ? route.LengthToleranceMeters
                : route.StationToleranceMeters;

            if (Math.Abs(route.ClosureMeters) <= tolerance)
            {
                continue;
            }

            for (int i = 0; i + 1 < route.Points.Count; i++)
            {
                result.Add(CanonicalPair(route.Points[i], route.Points[i + 1]));
            }
        }

        return result;
    }

    private static string CanonicalPair(string a, string b)
    {
        string left = a.Trim();
        string right = b.Trim();
        return string.CompareOrdinal(left, right) <= 0
            ? $"{left}\u001F{right}"
            : $"{right}\u001F{left}";
    }
}
