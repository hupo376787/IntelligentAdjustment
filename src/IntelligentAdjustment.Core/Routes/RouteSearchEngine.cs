using IntelligentAdjustment.Domain;

namespace IntelligentAdjustment.Core.Routes;

public sealed class RouteSearchEngine
{
    public IReadOnlyList<NetworkRoute> Search(
        IReadOnlyList<LevelDifference> observations,
        IReadOnlyList<KnownHeight> knownHeights,
        ProjectSettings settings)
    {
        ArgumentNullException.ThrowIfNull(observations);
        ArgumentNullException.ThrowIfNull(knownHeights);
        ArgumentNullException.ThrowIfNull(settings);

        var edges = observations.Select((x, index) => new GraphEdge(index, x)).ToArray();
        var known = knownHeights
            .GroupBy(x => x.PointName, StringComparer.Ordinal)
            .ToDictionary(x => x.Key, x => x.Single().Height, StringComparer.Ordinal);

        var routes = new List<NetworkRoute>();
        var closed = FindCycleBasis(edges, settings);
        routes.AddRange(closed);

        var attached = FindAttachedRoutes(edges, known, settings);
        routes.AddRange(attached);

        return routes
            .Select((route, index) => route with { Index = index + 1 })
            .ToArray();
    }

    public IReadOnlyList<NetworkRoute> FindClosedLoops(
        IReadOnlyList<LevelDifference> observations,
        ProjectSettings settings)
    {
        var edges = observations.Select((x, index) => new GraphEdge(index, x)).ToArray();
        return FindCycleBasis(edges, settings)
            .Select((route, index) => route with { Index = index + 1 })
            .ToArray();
    }

    private static IReadOnlyList<NetworkRoute> FindCycleBasis(
        IReadOnlyList<GraphEdge> edges,
        ProjectSettings settings)
    {
        if (edges.Count == 0)
        {
            return Array.Empty<NetworkRoute>();
        }

        int targetRank = CyclomaticRank(edges);
        if (targetRank <= 0)
        {
            return Array.Empty<NetworkRoute>();
        }

        var candidates = new Dictionary<string, PathCandidate>(StringComparer.Ordinal);
        foreach (var excluded in edges)
        {
            var path = ShortestPath(edges, excluded.From, excluded.To, excluded.Index);
            if (path is null)
            {
                continue;
            }

            var steps = new List<GraphStep>(path.Steps)
            {
                excluded.Step(excluded.To, excluded.From)
            };

            var candidate = CanonicalizeClosedCycle(new PathCandidate(steps));
            string key = EdgeSetKey(candidate);
            candidates.TryAdd(key, candidate);
        }

        AddFundamentalCyclesIfNeeded(edges, candidates, targetRank);

        var sorted = candidates.Values
            .OrderBy(x => x.Steps.Count)
            .ThenBy(RoutePointKey, StringComparer.Ordinal)
            .ToArray();

        var basis = new SparseGf2Basis();
        var accepted = new List<PathCandidate>();
        foreach (var candidate in sorted)
        {
            var vector = candidate.Steps.Select(x => x.Edge.Index).ToHashSet();
            if (basis.TryAdd(vector))
            {
                accepted.Add(candidate);
                if (accepted.Count == targetRank)
                {
                    break;
                }
            }
        }

        return accepted.Select(x => ToRoute(x, RouteType.ClosedLoop, knownHeights: null, settings)).ToArray();
    }

    private static IReadOnlyList<NetworkRoute> FindAttachedRoutes(
        IReadOnlyList<GraphEdge> edges,
        IReadOnlyDictionary<string, double> knownHeights,
        ProjectSettings settings)
    {
        var knownInNetwork = knownHeights.Keys
            .Where(point => edges.Any(e => e.From == point || e.To == point))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToArray();

        if (knownInNetwork.Length < 2)
        {
            return Array.Empty<NetworkRoute>();
        }

        var knownSet = knownInNetwork.ToHashSet(StringComparer.Ordinal);
        var candidates = new Dictionary<string, PathCandidate>(StringComparer.Ordinal);

        for (int i = 0; i < knownInNetwork.Length; i++)
        {
            for (int j = i + 1; j < knownInNetwork.Length; j++)
            {
                string start = knownInNetwork[i];
                string end = knownInNetwork[j];
                foreach (var path in EnumerateSimplePathsBetweenKnownPoints(edges, start, end, knownSet))
                {
                    var canonical = CanonicalizeOpenPath(path);
                    string key = OrderedEdgeKey(canonical);
                    candidates.TryAdd(key, canonical);
                }
            }
        }

        var sorted = candidates.Values
            .OrderBy(x => x.Steps.Count)
            .ThenBy(RoutePointKey, StringComparer.Ordinal)
            .ToArray();

        var coveredEdges = new HashSet<int>();
        var accepted = new List<PathCandidate>();
        foreach (var candidate in sorted)
        {
            bool addsCoverage = candidate.Steps.Any(step => !coveredEdges.Contains(step.Edge.Index));
            if (!addsCoverage)
            {
                continue;
            }

            accepted.Add(candidate);
            foreach (var step in candidate.Steps)
            {
                coveredEdges.Add(step.Edge.Index);
            }
        }

        return accepted
            .Select(x => ToRoute(x, RouteType.Attached, knownHeights, settings))
            .ToArray();
    }

    private static IEnumerable<PathCandidate> EnumerateSimplePathsBetweenKnownPoints(
        IReadOnlyList<GraphEdge> edges,
        string start,
        string end,
        IReadOnlySet<string> knownPoints)
    {
        var adjacency = BuildAdjacency(edges);
        var visitedPoints = new HashSet<string>(StringComparer.Ordinal) { start };
        var steps = new List<GraphStep>();
        var results = new List<PathCandidate>();

        void Dfs(string current)
        {
            if (results.Count >= 10_000)
            {
                return;
            }

            if (current == end)
            {
                if (steps.Count > 0)
                {
                    results.Add(new PathCandidate(steps.ToArray()));
                }

                return;
            }

            if (current != start && knownPoints.Contains(current))
            {
                return;
            }

            if (!adjacency.TryGetValue(current, out var incident))
            {
                return;
            }

            foreach (var edge in incident.OrderBy(e => e.Other(current), StringComparer.Ordinal).ThenBy(e => e.Index))
            {
                string next = edge.Other(current);
                if (visitedPoints.Contains(next))
                {
                    continue;
                }

                if (next != end && knownPoints.Contains(next))
                {
                    continue;
                }

                visitedPoints.Add(next);
                steps.Add(edge.Step(current, next));
                Dfs(next);
                steps.RemoveAt(steps.Count - 1);
                visitedPoints.Remove(next);
            }
        }

        Dfs(start);
        return results;
    }

    private static PathCandidate? ShortestPath(
        IReadOnlyList<GraphEdge> edges,
        string start,
        string end,
        int excludedEdgeIndex)
    {
        var adjacency = BuildAdjacency(edges);
        var queue = new Queue<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal) { start };
        var previous = new Dictionary<string, (string Point, GraphEdge Edge)>(StringComparer.Ordinal);
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            string current = queue.Dequeue();
            if (current == end)
            {
                break;
            }

            if (!adjacency.TryGetValue(current, out var incident))
            {
                continue;
            }

            foreach (var edge in incident.OrderBy(e => e.Other(current), StringComparer.Ordinal).ThenBy(e => e.Index))
            {
                if (edge.Index == excludedEdgeIndex)
                {
                    continue;
                }

                string next = edge.Other(current);
                if (!visited.Add(next))
                {
                    continue;
                }

                previous[next] = (current, edge);
                queue.Enqueue(next);
            }
        }

        if (!visited.Contains(end))
        {
            return null;
        }

        var reverse = new List<GraphStep>();
        string cursor = end;
        while (cursor != start)
        {
            var item = previous[cursor];
            reverse.Add(item.Edge.Step(item.Point, cursor));
            cursor = item.Point;
        }

        reverse.Reverse();
        return new PathCandidate(reverse);
    }

    private static void AddFundamentalCyclesIfNeeded(
        IReadOnlyList<GraphEdge> edges,
        Dictionary<string, PathCandidate> candidates,
        int targetRank)
    {
        if (candidates.Count >= targetRank)
        {
            return;
        }

        var adjacency = BuildAdjacency(edges);
        var points = edges.SelectMany(e => new[] { e.From, e.To }).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        var parentPoint = new Dictionary<string, string?>(StringComparer.Ordinal);
        var parentEdge = new Dictionary<string, GraphEdge>(StringComparer.Ordinal);
        var treeEdges = new HashSet<int>();

        foreach (string root in points)
        {
            if (!visited.Add(root))
            {
                continue;
            }

            parentPoint[root] = null;
            var queue = new Queue<string>();
            queue.Enqueue(root);
            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                foreach (var edge in adjacency[current].OrderBy(e => e.Other(current), StringComparer.Ordinal).ThenBy(e => e.Index))
                {
                    string next = edge.Other(current);
                    if (visited.Add(next))
                    {
                        parentPoint[next] = current;
                        parentEdge[next] = edge;
                        treeEdges.Add(edge.Index);
                        queue.Enqueue(next);
                    }
                }
            }
        }

        foreach (var chord in edges.Where(e => !treeEdges.Contains(e.Index)))
        {
            var treePath = TreePath(chord.From, chord.To, parentPoint, parentEdge);
            if (treePath is null)
            {
                continue;
            }

            var steps = new List<GraphStep>(treePath.Steps) { chord.Step(chord.To, chord.From) };
            var candidate = CanonicalizeClosedCycle(new PathCandidate(steps));
            candidates.TryAdd(EdgeSetKey(candidate), candidate);
        }
    }

    private static PathCandidate? TreePath(
        string start,
        string end,
        IReadOnlyDictionary<string, string?> parentPoint,
        IReadOnlyDictionary<string, GraphEdge> parentEdge)
    {
        var startAncestors = new Dictionary<string, List<GraphStep>>(StringComparer.Ordinal)
        {
            [start] = new List<GraphStep>()
        };

        string cursor = start;
        var path = new List<GraphStep>();
        while (parentPoint.TryGetValue(cursor, out string? parent) && parent is not null)
        {
            var edge = parentEdge[cursor];
            path.Add(edge.Step(cursor, parent));
            cursor = parent;
            startAncestors[cursor] = new List<GraphStep>(path);
        }

        cursor = end;
        var endToAncestor = new List<GraphStep>();
        while (!startAncestors.ContainsKey(cursor))
        {
            if (!parentPoint.TryGetValue(cursor, out string? parent) || parent is null)
            {
                return null;
            }

            var edge = parentEdge[cursor];
            endToAncestor.Add(edge.Step(cursor, parent));
            cursor = parent;
        }

        string lca = cursor;
        var startToLca = startAncestors[lca];
        var result = new List<GraphStep>(startToLca);
        for (int i = endToAncestor.Count - 1; i >= 0; i--)
        {
            result.Add(endToAncestor[i].Reverse());
        }

        return new PathCandidate(result);
    }

    private static int CyclomaticRank(IReadOnlyList<GraphEdge> edges)
    {
        var points = edges.SelectMany(e => new[] { e.From, e.To }).Distinct(StringComparer.Ordinal).ToArray();
        var adjacency = BuildAdjacency(edges);
        var visited = new HashSet<string>(StringComparer.Ordinal);
        int components = 0;

        foreach (string point in points)
        {
            if (!visited.Add(point))
            {
                continue;
            }

            components++;
            var queue = new Queue<string>();
            queue.Enqueue(point);
            while (queue.Count > 0)
            {
                string current = queue.Dequeue();
                foreach (var edge in adjacency[current])
                {
                    string next = edge.Other(current);
                    if (visited.Add(next))
                    {
                        queue.Enqueue(next);
                    }
                }
            }
        }

        return edges.Count - points.Length + components;
    }

    private static NetworkRoute ToRoute(
        PathCandidate path,
        RouteType routeType,
        IReadOnlyDictionary<string, double>? knownHeights,
        ProjectSettings settings)
    {
        var points = Points(path);
        double sum = path.Steps.Sum(x => x.SignedHeightDifference);
        double length = path.Steps.Sum(x => x.Edge.Observation.DistanceMeters);
        int stations = path.Steps.Sum(x => x.Edge.Observation.StationCount);
        double closure = sum;

        if (routeType == RouteType.Attached)
        {
            if (knownHeights is null)
            {
                throw new InvalidOperationException("Known heights are required for an attached route.");
            }

            string start = points[0];
            string end = points[^1];
            closure = sum - (knownHeights[end] - knownHeights[start]);
        }

        var routeEdges = path.Steps.Select(step => new RouteEdge(
            step.Edge.Observation.Id,
            step.From,
            step.To,
            step.SignedHeightDifference,
            step.Edge.Observation.DistanceMeters,
            step.Edge.Observation.StationCount)).ToArray();

        return new NetworkRoute(
            0,
            routeType,
            points,
            routeEdges,
            closure,
            length,
            stations,
            ClosureToleranceCalculator.ByDistanceMeters(length, settings.DistanceToleranceCoefficientMm),
            ClosureToleranceCalculator.ByStationMeters(stations, settings.StationToleranceCoefficientMm));
    }

    private static Dictionary<string, List<GraphEdge>> BuildAdjacency(IReadOnlyList<GraphEdge> edges)
    {
        var adjacency = new Dictionary<string, List<GraphEdge>>(StringComparer.Ordinal);
        foreach (var edge in edges)
        {
            if (!adjacency.TryGetValue(edge.From, out var fromList))
            {
                fromList = new List<GraphEdge>();
                adjacency[edge.From] = fromList;
            }
            if (!adjacency.TryGetValue(edge.To, out var toList))
            {
                toList = new List<GraphEdge>();
                adjacency[edge.To] = toList;
            }

            fromList.Add(edge);
            toList.Add(edge);
        }

        return adjacency;
    }

    private static PathCandidate CanonicalizeOpenPath(PathCandidate candidate)
    {
        var points = Points(candidate);
        string forward = string.Join("\u001f", points);
        var reversed = candidate.Reverse();
        string backward = string.Join("\u001f", Points(reversed));
        return string.CompareOrdinal(forward, backward) <= 0 ? candidate : reversed;
    }

    private static PathCandidate CanonicalizeClosedCycle(PathCandidate candidate)
    {
        var steps = candidate.Steps;
        if (steps.Count == 0)
        {
            return candidate;
        }

        var variants = new List<PathCandidate>();
        AddRotations(steps, variants);
        AddRotations(candidate.Reverse().Steps, variants);
        return variants.OrderBy(RoutePointKey, StringComparer.Ordinal).First();
    }

    private static void AddRotations(IReadOnlyList<GraphStep> steps, List<PathCandidate> variants)
    {
        for (int offset = 0; offset < steps.Count; offset++)
        {
            var rotated = new List<GraphStep>(steps.Count);
            for (int i = 0; i < steps.Count; i++)
            {
                rotated.Add(steps[(offset + i) % steps.Count]);
            }

            if (rotated[^1].To == rotated[0].From)
            {
                variants.Add(new PathCandidate(rotated));
            }
        }
    }

    private static IReadOnlyList<string> Points(PathCandidate path)
    {
        if (path.Steps.Count == 0)
        {
            return Array.Empty<string>();
        }

        var points = new List<string>(path.Steps.Count + 1) { path.Steps[0].From };
        points.AddRange(path.Steps.Select(x => x.To));
        return points;
    }

    private static string RoutePointKey(PathCandidate path) => string.Join("\u001f", Points(path));

    private static string EdgeSetKey(PathCandidate path) =>
        string.Join(",", path.Steps.Select(x => x.Edge.Index).OrderBy(x => x));

    private static string OrderedEdgeKey(PathCandidate path) =>
        string.Join(",", path.Steps.Select(x => x.Edge.Index));

    private sealed record GraphEdge(int Index, LevelDifference Observation)
    {
        public string From => Observation.FromPoint;
        public string To => Observation.ToPoint;

        public string Other(string point) => point == From ? To : From;

        public GraphStep Step(string from, string to)
        {
            if (from == From && to == To)
            {
                return new GraphStep(this, from, to, Observation.HeightDifference);
            }

            if (from == To && to == From)
            {
                return new GraphStep(this, from, to, -Observation.HeightDifference);
            }

            throw new InvalidOperationException("The requested traversal does not match this graph edge.");
        }
    }

    private sealed record GraphStep(GraphEdge Edge, string From, string To, double SignedHeightDifference)
    {
        public GraphStep Reverse() => new(Edge, To, From, -SignedHeightDifference);
    }

    private sealed record PathCandidate(IReadOnlyList<GraphStep> Steps)
    {
        public PathCandidate Reverse() => new(Steps.Reverse().Select(x => x.Reverse()).ToArray());
    }

    private sealed class SparseGf2Basis
    {
        private readonly Dictionary<int, HashSet<int>> _basis = new();

        public bool TryAdd(HashSet<int> vector)
        {
            var reduced = new HashSet<int>(vector);
            while (reduced.Count > 0)
            {
                int pivot = reduced.Max();
                if (_basis.TryGetValue(pivot, out var existing))
                {
                    reduced.SymmetricExceptWith(existing);
                    continue;
                }

                _basis[pivot] = reduced;
                return true;
            }

            return false;
        }
    }
}
